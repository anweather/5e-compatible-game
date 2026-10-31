using System.Collections.Generic;
using System.IO;
using Chem;
using UnityEngine;

// The 3D view of a Chem.World. The rules come from ../rules/*.json, the
// same files the web sandbox runs on; this side only ticks the world and
// draws it (see ChemGrid.Visuals.cs). North (y-1) is +z.
//
// Every cell is drawn on the valley's chunk terrains (ValleyChemistry): the
// terrain is the ground, shaped and painted by the cells, with dressing,
// blockers, water and effects on top. The yard is only a coordinate frame for
// maps and demos: yard cell (0, 0) sits at world (0, 0, (yard height - 1) * cellSize).
public partial class ChemGrid : MonoBehaviour
{
    [TextArea(8, 20)] public string map;
    public uint seed = 1;
    [Tooltip("World height of one step of elevation (a pit is one step deep).")]
    public float stepHeight = 0.5f;
    [Tooltip("World width of one cell. A 5 ft square is about 1.5 m.")]
    public float cellSize = 1.5f;

    [Tooltip("The yard's cells within the map; empty means the whole map is the yard.")]
    public RectInt yardCells;
    [Tooltip("Optional: the valley's terrains, which the cells shape and paint. Without it the ground is flat at 0.")]
    public ValleyChemistry valley;

    [Header("Tuning for this scene (0 keeps rules/sim.json); applied as part of the base rules")]
    [Tooltip("Fire weakens as it spreads and dies out after about this many cells (see FireParams.FadeCells).")]
    public int fireFadeCells;
    [Tooltip("How many connected conductive cells one lightning strike can reach.")]
    public int conductLimit;

    public World World { get; private set; }
    /// <summary>The world's props and items (world/things.json) with their stable ids; ThingSpawner spawns them.</summary>
    public WorldThings Things { get; private set; } = WorldThings.Empty;
    public Vector2Int PlayerStart { get; private set; }

    /// <summary>When true, something else (NetChem in multiplayer) steps the world; the grid only draws it.</summary>
    public bool ExternalTicks { get; set; }

    /// <summary>A whole tick has run, solo or online (a tick's World.CombosFired last until the next).</summary>
    public event System.Action TickDone;

    /// <summary>Raises TickDone; NetChem calls it for the ticks it runs.</summary>
    public void NoteTickDone() => TickDone?.Invoke();

    /// <summary>
    /// Simulate only the chunks around the players (ChunkStreaming). Solo the grid applies the plan
    /// before each tick; online NetChem's host stamps it into its batches.
    /// </summary>
    public bool streamChunks = true;

    /// <summary>Plans the chunks solo, lingering what is still live (ChunkPlanner); online the host keeps its own. New with each world.</summary>
    public ChunkPlanner ChunkPlanner { get; private set; } = new();

    /// <summary>
    /// A chunk of the current world activated or deactivated (ChunkSwitch: CX, CY, Active), after
    /// it caught up; World.ChunkSwitched passed on, so it keeps working across ResetWorld. Solo and
    /// online alike: switches reach the world only as commands (the solo plan, or the host's tick
    /// batches on every peer), so every peer hears the same switches at the same tick. Chunk
    /// coordinates are world cells / 64 (ChunkAt, ThingId's chunk). For the view only: a handler
    /// mustn't change the world.
    /// </summary>
    public event System.Action<ChunkSwitch> ChunkSwitched;

    /// <summary>A new world was built (ResetWorld, or going online): every chunk starts active again, with no ChunkSwitched for it.</summary>
    public event System.Action WorldReset;

    /// <summary>The chunk a scene position is in (world chunk coordinates, as ChunkSwitched and ThingId use).</summary>
    public Vector2Int ChunkAt(Vector3 p)
    {
        var cell = CellAt(p);
        return new Vector2Int(Chunks.ChunkOf(cell.x), Chunks.ChunkOf(cell.y));
    }

    /// <summary>Whether a chunk is simulated now; false for one outside the world.</summary>
    public bool IsChunkActive(int cx, int cy) => World != null && World.HasChunk(cx, cy) && World.IsActive(cx, cy);

    readonly List<(int x, int y)> playerCells = new();
    readonly System.Diagnostics.Stopwatch timer = new();

    /// <summary>Milliseconds the last frame spent stepping the world, drawing its cells, and placing the fire lights (the probe's wildfire scenario reads them).</summary>
    public double StepMs { get; private set; }
    public double RenderMs { get; private set; }

    static readonly Unity.Profiling.ProfilerMarker StepMarker = new("ChemGrid.Step"), RenderMarker = new("ChemGrid.Render");
    public double LightsMs { get; private set; }
    SpellCaster[] wizards = System.Array.Empty<SpellCaster>();
    float nextWizardLookup;

    // One character per cell, the same legend as the web sandbox's scenarios.
    static readonly Dictionary<char, (string t, int water, bool frozen, string cover)> Legend = new()
    {
        ['%'] = ("bedrock", 0, false, null), ['#'] = ("stone", 0, false, null),
        ['.'] = ("dirt", 0, false, null), [','] = ("grass", 0, false, null),
        ['"'] = ("brush", 0, false, null), ['T'] = ("tree", 0, false, null),
        ['='] = ("wood", 0, false, null), ['_'] = ("pit", 0, false, null),
        ['w'] = ("pit", 4, false, null), ['*'] = ("pit", 4, true, null), ['r'] = ("river", 4, false, null),
        ['~'] = ("dirt", 2, false, null), ['m'] = ("metal", 0, false, null),
        ['o'] = ("dirt", 0, false, "oil"), ['@'] = ("grass", 0, false, null),
        ['L'] = ("logwall", 0, false, null), ['P'] = ("pane", 0, false, null),
    };

    float tickTimer;
    int lastLitTick = -1;
    LogEntry lastLog;
    int lastLogCount;

    void Awake()
    {
        BuildWorld();
        LoadThings();
        // Live rules: the mods panel and, in the Editor and dev builds, the rules/ file watcher.
        if (!TryGetComponent<RuleSwitcher>(out _)) gameObject.AddComponent<RuleSwitcher>();
    }

    // The things file is read once: it is the same for every world this scene starts (and online
    // its fingerprint is part of the checksum, so every machine must have read the same one).
    void LoadThings()
    {
        try
        {
            Things = WorldThings.Load(FindWorldDir());
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{WorldThings.FileName} can't be read, so no props or items: {e.Message}");
            Things = WorldThings.Empty;
        }
        foreach (var problem in Things.Check(World.Rules)) Debug.LogWarning(problem);
    }

    /// <summary>Throws away the current world and its visuals and starts again from the map.</summary>
    public void ResetWorld()
    {
        foreach (Transform child in transform) Destroy(child.gameObject);
        lastLog = null;
        tickTimer = 0f;
        BuildWorld();
    }

    void BuildWorld()
    {
        var rules = StartRules();

        World = MapWorld(rules, map, seed, out var start);
        World.ChunkSwitched += s => ChunkSwitched?.Invoke(s);
        ChunkPlanner = new ChunkPlanner();
        PlayerStart = start;
        if (valley != null) valley.Bind(World);

        InitVisuals();
        RenderAll();
        WorldReset?.Invoke();
    }

    /// <summary>
    /// A new world from a map (one Legend character per cell, rows top to bottom). The editor's
    /// ValleyBuilder bakes the terrain from the same starting world.
    /// </summary>
    public static World MapWorld(Rules rules, string map, uint seed, out Vector2Int start)
    {
        var rows = map.Replace("\r", "").Trim('\n').Split('\n');
        int h = rows.Length, w = 0;
        foreach (var r in rows) w = Mathf.Max(w, r.Length);
        var world = new World(rules, w, h, seed);
        start = Vector2Int.zero;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char ch = x < rows[y].Length ? rows[y][x] : '.';
                if (ch == '@') start = new Vector2Int(x, y);
                PaintCell(world, x, y, ch);
            }
        }
        LayPuzzles(world);
        LayGolf(world);
        return world;
    }

    // Spell golf's hole, the ball on its tee from the start on every machine alike (GolfCourse draws it).
    static void LayGolf(World world)
    {
        try
        {
            if (GolfCommand.Load(FindWorldDir()) is { } hole) world.ApplyGolf(hole);
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{GolfCommand.FileName} can't be read, so no golf: {e.Message}");
        }
    }

    // The puzzles of world/puzzles.json (a coil and a gate by the golf course), over the map's cells:
    // only in a world big enough to hold them (the valley, not a small test yard).
    static void LayPuzzles(World world)
    {
        try
        {
            var puzzles = WorldPuzzles.Load(FindWorldDir());
            foreach (var problem in puzzles.Check(world.Rules)) Debug.LogWarning(problem);
            puzzles.LayInto(world);
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{WorldPuzzles.FileName} can't be read, so no puzzles: {e.Message}");
        }
    }

    // Sets up one cell from its map character (see Legend); unknown characters are dirt.
    static void PaintCell(World world, int x, int y, char ch)
    {
        if (!Legend.TryGetValue(ch, out var spec)) spec = Legend['.'];
        world.Paint(x, y, spec.t);
        if (spec.water > 0) world.AddWater(x, y, spec.water);
        if (spec.frozen) world.At(x, y).Frozen = true;
        if (spec.cover != null) world.SetCover(x, y, spec.cover);
    }

    void Update()
    {
        UpdateDetailChunks();
        if (ExternalTicks)
        {
            Render();
            if (World.Tick != lastLitTick) { fireLights.Place(); lastLitTick = World.Tick; }
            fireLights.Flicker();
            PumpLog();
            return;
        }

        tickTimer += Time.deltaTime;
        timer.Restart();
        stepBudget.Start();
        bool stepped;
        using (StepMarker.Auto()) stepped = StepWithinBudget();
        StepMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        using (RenderMarker.Auto()) Render();
        RenderMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        if (stepped) fireLights.Place();
        fireLights.Flicker();
        LightsMs = timer.Elapsed.TotalMilliseconds;
        PumpLog();
    }

    // The rules: packed into a build's StreamingAssets/rules (see PrototypeBuild), or in the
    // Editor the repo's rules/ folder, found by walking up from the project. A fixed
    // "../../rules" breaks for Multiplayer Play Mode's virtual players, whose project copies
    // live deeper (unity/Library/VP/<id>/).
    internal static string FindRulesDir()
    {
        var packed = Path.Combine(Application.streamingAssetsPath, "rules");
        if (!Application.isEditor && File.Exists(Path.Combine(packed, "materials.json"))) return packed;
        var dir = new DirectoryInfo(Application.dataPath);
        for (; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "rules");
            if (File.Exists(Path.Combine(candidate, "materials.json"))) return candidate;
        }
        throw new DirectoryNotFoundException($"No rules/ folder with materials.json above {Application.dataPath}");
    }

    // world/ (things.json) sits next to rules/: the repo's in the Editor, StreamingAssets/world in a build.
    public static string FindWorldDir() => Path.Combine(Path.GetDirectoryName(FindRulesDir()), "world");

    // Milliseconds of a frame the solo grid may spend on chemistry ticks: a tick that needs longer
    // runs in slices over the next frames (World.StepSome) and the clock catches up after, rather
    // than one frame stalling for the whole tick. Meanwhile the view may show a tick half done for
    // a frame or two (fire spread this tick, say, but water not yet moved).
    const double StepBudgetMs = 6.0;
    // In a playtest run, slices instead (ExactFrames): a quiet valley tick is a hundred or so.
    const int ExactStepSlices = 256;
    readonly FrameBudget stepBudget = new(StepBudgetMs, ExactStepSlices);

    // Runs the ticks that are due, a tick in progress first, within the frame's budget: rule
    // switches and the chunk plan apply only between whole ticks. True if a tick finished.
    bool StepWithinBudget()
    {
        float tick = 1f / Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
        bool Over() => stepBudget.Over();
        int guard = 4;
        bool stepped = false;
        while (true)
        {
            if (!World.TickInProgress)
            {
                if (tickTimer < tick || guard-- <= 0 || Over()) return stepped;
                tickTimer -= tick;
                ApplyQueuedRules();
                if (streamChunks) ChunkStreaming.Apply(World, ChunkPlanner.Plan(World, PlayerCells()));
            }
            if (!World.StepSome(Over)) return stepped;
            stepped = true;
            NoteTickDone();
        }
    }

    /// <summary>The cells every wizard in the scene stands on (the solo one, or every player's online), for ChunkStreaming.</summary>
    public IReadOnlyList<(int x, int y)> PlayerCells()
    {
        if (ChoreClock >= nextWizardLookup)
        {
            wizards = FindObjectsByType<SpellCaster>(FindObjectsSortMode.InstanceID);
            nextWizardLookup = ChoreClock + 1f;
        }
        playerCells.Clear();
        foreach (var w in wizards)
        {
            if (w == null) continue;
            var cell = CellAt(w.transform.position);
            if (World.InBounds(cell.x, cell.y)) playerCells.Add((cell.x, cell.y));
        }
        return playerCells;
    }

    // --- coordinates -----------------------------------------------------

    RectInt Yard => yardCells.width > 0 ? yardCells : new RectInt(0, 0, World.W, World.H);

    /// <summary>World cell of a yard cell (yard maps and demos use yard coordinates).</summary>
    public Vector2Int YardCell(int x, int y) => new Vector2Int(x + Yard.x, y + Yard.y);

    /// <summary>Centre of a yard cell, given in yard coordinates (as the demo scripts are).</summary>
    public Vector3 YardCellCenter(int x, int y)
    {
        var cell = YardCell(x, y);
        return CellCenter(cell.x, cell.y);
    }

    public Vector3 CellCenter(int x, int y)
    {
        var yard = Yard;
        return new Vector3((x - yard.x) * cellSize, 0f, (yard.y + yard.height - 1 - y) * cellSize);
    }

    public Vector2Int CellAt(Vector3 p)
    {
        var yard = Yard;
        return new Vector2Int(Mathf.RoundToInt(p.x / cellSize) + yard.x, yard.y + yard.height - 1 - Mathf.RoundToInt(p.z / cellSize));
    }

    /// <summary>Top of what stands on a cell: its ground, plus a stone block's or a tree's height.</summary>
    public float SurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        return c == null ? 0f : GroundTop(x, y, c) + CellDressing.Look(c.T).height;
    }

    /// <summary>
    /// A cell's ground, before anything standing on it: the valley's own ground (0 without a
    /// valley) plus its elevation and height, the same offset CellHeightfield shapes the terrain with.
    /// </summary>
    public float GroundTop(int x, int y, Cell c)
    {
        float baseY = valley != null ? valley.BaseHeight(x, y) : 0f;
        return baseY + World.ElevationUnits(c) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;
    }

    /// <summary>The ground drawn at a world point: the terrain's (between cell centres too), or its cell's without a valley.</summary>
    public float GroundAt(Vector3 p)
    {
        if (valley != null) return valley.GroundAt(p);
        var cell = CellAt(p);
        var c = World.At(cell.x, cell.y);
        return c == null ? 0f : GroundTop(cell.x, cell.y, c);
    }

    public bool IsSlipperyAt(Vector3 p)
    {
        var cell = CellAt(p);
        var c = World.At(cell.x, cell.y);
        return c != null && World.IsSlippery(c);
    }

    // --- log ---------------------------------------------------------------

    // Forward new entries from the world's log to the on-screen log.
    void PumpLog()
    {
        var log = World.Log;
        if (log.Count == 0) return;
        int start = 0;
        if (lastLog != null)
        {
            int at = -1;
            for (int i = log.Count - 1; i >= 0; i--)
                if (ReferenceEquals(log[i], lastLog)) { at = i; break; }
            if (at >= 0)
            {
                if (lastLog.Count != lastLogCount) EventLog.Add($"{lastLog.Text} (×{lastLog.Count})", lastLog.Text);
                start = at + 1;
            }
        }
        for (int i = start; i < log.Count; i++) EventLog.Add(log[i].Count > 1 ? $"{log[i].Text} (×{log[i].Count})" : log[i].Text, log[i].Text);
        lastLog = log[log.Count - 1];
        lastLogCount = lastLog.Count;
    }
}
