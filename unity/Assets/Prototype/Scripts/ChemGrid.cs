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

    /// <summary>A whole tick has run, solo or online; not one finished from outside (World.FinishTick), so casts are read through FiredCastsHeard.</summary>
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

    /// <summary>
    /// The dragon's breath put a chunk back as it started, and it has been redrawn (the frame after the tick that did it,
    /// on every peer, a replaying late joiner too). World.ChunkFreshened passed on, so it keeps working across ResetWorld.
    /// For the view only: a handler mustn't change the world.
    /// </summary>
    public event System.Action<ChunkFresh> ChunkFreshened;

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
    /// <summary>The time the solo world owes in seconds: past a tick's worth, it's behind.</summary>
    public float OwedSeconds => tickTimer;
    public double StepMs { get; set; }
    public double RenderMs { get; private set; }

    static readonly Unity.Profiling.ProfilerMarker StepMarker = new("ChemGrid.Step"), RenderMarker = new("ChemGrid.Render"),
        ChunkPlanMarker = new("ChemGrid.ChunkPlan"), ChunkApplyMarker = new("ChemGrid.ChunkApply"), ChunkEventMarker = new("ChemGrid.ChunkEvent"),
        DetailChunksMarker = new("ChemGrid.DetailChunks");
    public double LightsMs { get; private set; }

    // The chunks the dragon's breath has reset since the last frame (World.ChunkFreshened).
    readonly List<ChunkFresh> freshChunks = new();

    /// <summary>Summons the dragon (the Reset, solo); online it goes through NetChem.SummonDragon, on every peer's same tick.</summary>
    public void SummonDragon() => World.ApplyDragon(new DragonCommand());
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
        TakeCurrent();
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
            Things = WorldThings.Load(Folders.World);
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
        freshChunks.Clear();
        lastLog = null;
        tickTimer = 0f;
        BuildWorld();
    }

    void BuildWorld()
    {
        var rules = StartRules();
        var roomWas = World?.Room;

        CheckMap();
        World = StartWorld(rules, CellMap(), seed, out var start);
        OpenRoom(roomWas);
        World.ChunkSwitched += OnChunkSwitched;
        World.LandMoved += (x, y, reach) =>
        {
            if (valley == null) return;
            valley.LandMoved(x, y, reach);
            LandShaped(x, y, reach);
        };
        World.ChunkFreshened += freshChunks.Add;
        ChunkPlanner = new ChunkPlanner();
        PlayerStart = start;
        if (valley != null) valley.Bind(World);

        InitVisuals();
        RenderAll();
        WorldReset?.Invoke();
    }

    void OnChunkSwitched(ChunkSwitch s)
    {
        PerfCounts.Add(s.Active ? "chunk.on" : "chunk.off");
        if (s.Active) PerfCounts.Add("chunk.caught_up_ticks", World.CaughtUp(s.CX, s.CY));
        using (ChunkEventMarker.Auto()) ChunkSwitched?.Invoke(s);
    }

    /// <summary>How long the last world took to keep its starting state, in milliseconds.</summary>
    public static double KeepStartMs { get; private set; }

    /// <summary>
    /// A world to play in: MapWorld, then its starting state kept for the dragon to put back. Every peer builds one this
    /// way, a late joiner and an online reset too. Whatever lays cells at the start belongs in MapWorld (the puzzles, the
    /// Gauntlet's booths among them, and golf), or the dragon wipes it: nothing may write cells between here and the first tick.
    /// </summary>
    public static World StartWorld(Rules rules, string map, uint seed, out Vector2Int start)
    {
        var world = MapWorld(rules, map, seed, out start);
        var kept = System.Diagnostics.Stopwatch.StartNew();
        world.KeepStart();
        KeepStartMs = kept.Elapsed.TotalMilliseconds;
        return world;
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
        world.SetLandRange(MapFile.Load(Folders.World).LandRange);
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
            if (GolfCommand.Load(Folders.World) is { } hole) world.ApplyGolf(hole);
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
            var puzzles = WorldPuzzles.Load(Folders.World);
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
        using (DetailChunksMarker.Auto()) UpdateDetailChunks();
        FollowLooks();
        if (ExternalTicks)
        {
            Render();
            if (World.Tick != lastLitTick) { fireLights.Place(); lastLitTick = World.Tick; }
            fireLights.Flicker();
            PumpLog();
            return;
        }

        tickTimer = Mathf.Min(tickTimer + Time.deltaTime, MostOwedSeconds);
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

    static GameFolders folders;

    /// <summary>The map's cells, one Legend character each (cells.txt in its folder).</summary>
    public static string CellMap() => MapFile.ReadCells(Folders.World);

    // The map this copy starts on, its own pick: the host's map when it hosts.
    static string OwnMap => MapIdentity.Named(System.Environment.GetCommandLineArgs());

    static string mapHash, mapHashOf;

    /// <summary>Why the host turns away a copy offering these maps, or null when it has this one.</summary>
    public static string MapRefusal(string offered)
    {
        if (mapHashOf != Folders.World)
        {
            mapHash = MapIdentity.Hash(Folders.World);
            mapHashOf = Folders.World;
        }
        return MapIdentity.Refusal(Folders.Map, MapFile.Load(Folders.World).Name, mapHash, offered);
    }

    /// <summary>The host's map, for a world about to be built: what's read from the map's folder is read again.</summary>
    public void UseMap(string id)
    {
        if (string.IsNullOrEmpty(id) || id == Folders.Map) return;
        Debug.Log($"NET: on the host's map, {id}");
        SwitchFolders(id);
        budgets = null;
        LoadThings();
    }

    /// <summary>Back from a host's map to this copy's own, before the scene starts over.</summary>
    public static void BackToOwnMap()
    {
        if (OwnMap != Folders.Map) SwitchFolders(OwnMap);
    }

    static void SwitchFolders(string id)
    {
        folders = Folders.WithMap(id);
        ValleyChemistry.ForgetLand();
    }

    static string checkedMap;

    // A folder that's wrong stops the game here, by name, rather than start on the wrong land: once, not at every reset.
    static void CheckMap()
    {
        if (checkedMap == Folders.World) return;
        var errors = MapCheck.Errors(Folders.World);
        if (errors.Count > 0) throw new InvalidDataException($"{Folders.World} can't be played:\n{string.Join("\n", errors)}");
        checkedMap = Folders.World;
    }

    // The game's folders (rules/, world/, the player's data folder and mods), from the roots only Unity knows.
    public static GameFolders Folders => folders ??= GameFolders.ForGame(Application.streamingAssetsPath, Application.dataPath, Application.isEditor, Application.persistentDataPath, OwnMap);

    // Milliseconds of a frame the solo grid may spend on chemistry ticks: a tick that needs longer
    // runs in slices over the next frames (World.StepSome) and the clock catches up after, rather
    // than one frame stalling for the whole tick. Meanwhile the view may show a tick half done for
    // a frame or two (fire spread this tick, say, but water not yet moved).
    const double StepBudgetMs = 6.0;
    /// <summary>The most time the world may owe: a slower world just runs slow, and doesn't race to catch up after.</summary>
    public const float MostOwedSeconds = 1f;
    // In a playtest run, slices instead (ExactFrames): a quiet valley tick is a hundred or so.
    const int ExactStepSlices = 256;
    readonly FrameBudget stepBudget = new(StepBudgetMs, ExactStepSlices);

    // Runs the ticks that are due, a tick in progress first, within the frame's budget: rule
    // switches and the chunk plan apply only between whole ticks. True if a tick finished.
    bool StepWithinBudget()
    {
        float tick = 1f / Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
        bool Over()
        {
            PerfCounts.TickSlice(World.TickStageNumber);
            return stepBudget.Over();
        }
        int guard = 4;
        bool stepped = false;
        while (true)
        {
            if (!World.TickInProgress)
            {
                if (tickTimer < tick || guard-- <= 0 || Over())
                {
                    PerfCounts.Peak("tick.owed_ms", (long)(tickTimer * 1000f));
                    return stepped;
                }
                tickTimer -= tick;
                ApplyQueuedRules();
                if (streamChunks) PlanChunks();
            }
            PerfCounts.SliceStart(World.TickStageNumber);
            bool finished = World.StepSome(Over);
            PerfCounts.SliceStop();
            if (!finished)
            {
                PerfCounts.Add("tick.budget_ran_out");
                PerfCounts.Peak("tick.owed_ms", (long)(tickTimer * 1000f));
                return stepped;
            }
            PerfCounts.Add("tick.done");
            stepped = true;
            NoteTickDone();
        }
    }

    void PlanChunks()
    {
        System.Collections.Generic.List<ChunkSwitch> plan;
        using (ChunkPlanMarker.Auto()) plan = ChunkPlanner.Plan(World, PlayerCells());
        using (ChunkApplyMarker.Auto()) ChunkStreaming.Apply(World, plan);
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
        return c == null ? 0f : GroundTop(x, y, c) + CellDressing.Look(World.Rules, c.T).height;
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
