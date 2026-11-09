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

    // A world built (start-up, hosting, a reset): its parts, which the play log names when a rebuild stalls a frame.
    static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("ChemGrid.BuildWorld"), BuildRulesMarker = new("ChemGrid.BuildWorld.Rules"),
        BuildCellsMarker = new("ChemGrid.BuildWorld.Cells"), BuildValleyMarker = new("ChemGrid.BuildWorld.Valley"),
        BuildDrawMarker = new("ChemGrid.BuildWorld.Draw"), BuildListenersMarker = new("ChemGrid.BuildWorld.Listeners");
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
        StandAtSpawn();
        LoadThings();
        // Live rules: the mods panel and, in the Editor and dev builds, the rules/ file watcher.
        if (!TryGetComponent<RuleSwitcher>(out _)) gameObject.AddComponent<RuleSwitcher>();
        if (!TryGetComponent<HoldShow>(out _)) gameObject.AddComponent<HoldShow>();
        if (!TryGetComponent<ProgressKeeper>(out _)) gameObject.AddComponent<ProgressKeeper>();
        if (!TryGetComponent<HoldBarricades>(out _)) gameObject.AddComponent<HoldBarricades>();
        if (!TryGetComponent<HoldMoments>(out _)) gameObject.AddComponent<HoldMoments>();
        if (!TryGetComponent<Waystones>(out _)) gameObject.AddComponent<Waystones>();
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
        // The blockers stay, for the new world to take again (CellBlockers.ReleaseAll).
        foreach (Transform child in transform)
            if (blockers == null || child != blockers.Parent) Destroy(child.gameObject);
        freshChunks.Clear();
        lastLog = null;
        tickTimer = 0f;
        // The old world's views go before the new one is built, so both worlds' views aren't held at once at the peak.
        views = null;
        BuildWorld();
    }

    void BuildWorld()
    {
        using var built = BuildMarker.Auto();
        Rules rules;
        using (BuildRulesMarker.Auto()) rules = StartRules();
        var roomWas = World?.Room;

        CheckMap();
        TakeYard();
        Vector2Int start;
        var phase = System.Diagnostics.Stopwatch.StartNew();
        using (BuildCellsMarker.Auto()) World = StartWorld(rules, CellMap(), seed, out start);
        double cellsMs = phase.Elapsed.TotalMilliseconds;
        using (BuildRulesMarker.Auto()) OpenRoom(roomWas);
        World.ChunkSwitched += OnChunkSwitched;
        World.LandMoved += (x, y, reach) =>
        {
            if (valley == null) return;
            valley.LandMoved(x, y, reach);
            LandShaped(x, y, reach);
        };
        World.ChunkFreshened += freshChunks.Add;
        ChunkPlanner = new ChunkPlanner();
        // The spawn the host picked (SpawnPoints), else the map's own.
        PlayerStart = SpawnPoints.StartCell(start);
        phase.Restart();
        if (valley != null)
            using (BuildValleyMarker.Auto()) valley.Bind(World);
        double valleyMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        using (BuildDrawMarker.Auto())
        {
            InitVisuals();
            RenderAll();
        }
        double drawMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        using (BuildListenersMarker.Auto()) WorldReset?.Invoke();
        // What a world costs to build, by part (cell-memory: a big map's start and every reset pay it).
        Debug.Log($"WORLD BUILT {World.W} x {World.H}: cells {cellsMs:0} ms, valley {valleyMs:0} ms, draw {drawMs:0} ms, listeners {phase.Elapsed.TotalMilliseconds:0} ms");
    }

    // Where the map's cells stand in the world (map.json's yard: its south-west cell at world 0, 0), when this grid plays a
    // map (the yard scene's, with its valley's ground); a test's own small yard keeps its own.
    void TakeYard()
    {
        var land = MapLand.Current;
        if (land == null || valley == null) return;
        var (x, y, w, h) = land.Map.Yard;
        yardCells = new RectInt(x, y, w, h);
    }

    // The solo wizard is built standing at the valley's start; on another map it starts at that map's, on its ground.
    void StandAtSpawn()
    {
        var wizard = WizardRig.FindLocal();
        if (wizard == null || valley == null) return;
        var at = CellCenter(PlayerStart.x, PlayerStart.y);
        var was = wizard.transform.position;
        if (new Vector2(was.x - at.x, was.z - at.z).magnitude < 2f) return;
        at.y = GroundAt(at) + 0.1f;
        bool body = wizard.TryGetComponent<CharacterController>(out var controller) && controller.enabled;
        if (body) controller.enabled = false;
        wizard.transform.position = at;
        if (body) controller.enabled = true;
        Debug.Log($"SPAWN: on {MapId} at {PlayerStart}");
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
        // The map's stamps before its rows: a row's pad is cut level on the stamped land (a village up a volcano's slope).
        LayStamps(world);
        LayRows(world);
        LayGolf(world);
        SetHold(world);
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

    // The map's rows its cells don't hold, laid over them: only in a world big enough (the valley, not a small test yard).
    static void LayRows(World world)
    {
        try
        {
            WorldBuildings.Load(Folders.World).LayInto(world);
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{WorldBuildings.FileName} can't be read, so its laid rows aren't: {e.Message}");
        }
    }

    // The map's own land stamps (stamps.json), before its rows, which are laid on the stamped land: only on the map's whole world.
    static void LayStamps(World world)
    {
        var map = MapLand.Current?.Map;
        if (map == null || world.W != map.W || world.H != map.H) return;
        try
        {
            foreach (var why in MapStamps.Load(Folders.World).LayInto(world)) Debug.LogError(why);
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{MapStamps.FileName} can't be read, so its land stamps aren't laid: {e.Message}");
        }
    }

    // The map's Hold, only in a world big enough to hold it (the valley, not a small test yard).
    static void SetHold(World world)
    {
        try
        {
            var site = WorldHold.Load(Folders.World);
            if (site == null || !world.InBounds(site.Heart.x, site.Heart.y)) return;
            foreach (var problem in site.Check(world.Rules)) Debug.LogWarning(problem);
            world.SetHold(site);
        }
        catch (System.Exception e) when (e is System.FormatException || e is IOException)
        {
            Debug.LogError($"world/{WorldHold.FileName} can't be read, so no Hold: {e.Message}");
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

    // From the start of Play to the grid's first frame: what a start-up costs (view-rebuild's target).
    static readonly System.Diagnostics.Stopwatch SincePlay = new();
    static bool readyLogged;

    void Update()
    {
        if (!readyLogged)
        {
            readyLogged = true;
            Debug.Log($"PLAY READY {World.W} x {World.H}: first frame {SincePlay.Elapsed.TotalMilliseconds:0} ms after Play started");
        }
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

    // Play mode without a domain reload keeps statics: each play starts on this copy's own map again (Temp/map may name
    // another since).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetFolders()
    {
        SincePlay.Restart();
        readyLogged = false;
        folders = null;
        checkedMap = null;
        mapHashOf = null;
        chosenMap = null;
        ValleyChemistry.ForgetLand();
    }

    /// <summary>The map's cells, one Legend character each (cells.txt in its folder).</summary>
    public static string CellMap() => MapFile.ReadCells(Folders.World);

    // A map picked in play (the Esc menu's map to host, or the host's map a joiner goes to): it outlives the scene's reload.
    static string chosenMap;

    /// <summary>
    /// Plays another map from here on: its folders (baked first, for a generated one) and the scene loaded again on it, as
    /// the scene's terrains, places and sea are built for the map it starts on.
    /// </summary>
    public static void PlayMap(string id)
    {
        chosenMap = id;
        SwitchFolders(id);
        Debug.Log($"MAP: on to {MapId}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>The maps this copy has (world/maps), by id and name, the valley first.</summary>
    public static (string id, string name)[] Maps()
    {
        var game = NewFolders(GameFolders.DefaultMap);
        if (!Directory.Exists(game.Maps)) return new[] { (GameFolders.DefaultMap, "The Valley") };
        var maps = new List<(string id, string name)>();
        foreach (var dir in Directory.GetDirectories(game.Maps))
        {
            if (!File.Exists(Path.Combine(dir, MapFile.FileName))) continue;
            string id = Path.GetFileName(dir);
            // A generated map too big to bake whole (world-12x10, "bake": false) can't be played yet: hosting it would throw.
            if (GeneratedMap.IsGenerated(dir) && Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, MapFile.FileName)))[GeneratedMap.Key]?["bake"]?.ToObject<bool>() == false) continue;
            maps.Add((id, MapFile.Load(dir).Name ?? id));
        }
        // The valley first, then the rest by id.
        maps.Sort((a, b) => (a.id != GameFolders.DefaultMap).CompareTo(b.id != GameFolders.DefaultMap) is var first && first != 0 ? first : string.CompareOrdinal(a.id, b.id));
        return maps.ToArray();
    }

    // The map this copy starts on, its own pick: the host's map when it hosts. A player takes -map <id>; the open Editor,
    // which a shell can't pass a flag to, reads Temp/map (a probe on a second map). One picked in play comes first.
    static string OwnMap
    {
        get
        {
            if (testMap != null) return testMap;
            if (chosenMap != null) return chosenMap;
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-map") >= 0 || !Application.isEditor || !File.Exists(EditorMapFile)) return MapIdentity.Named(args);
            string named = File.ReadAllText(EditorMapFile).Trim();
            return named.Length > 0 ? named : GameFolders.DefaultMap;
        }
    }

    // An EditMode fixture's map in place of the Editor's own (OnMap): one that reads the valley's data says so.
    static string testMap;

    /// <summary>
    /// For an EditMode fixture (OnTheValleyAttribute): the Editor's folders are this map's until the scope ends, whatever
    /// Temp/map names, so a test of the valley's data passes with the Editor pointed at another map.
    /// </summary>
    public static System.IDisposable OnMap(string id)
    {
        var before = testMap;
        testMap = id;
        ValleyChemistry.ForgetLand();
        return new MapScope(before);
    }

    sealed class MapScope : System.IDisposable
    {
        readonly string before;
        bool ended;
        public MapScope(string before) => this.before = before;

        public void Dispose()
        {
            if (ended) return;
            ended = true;
            testMap = before;
            ValleyChemistry.ForgetLand();
        }
    }

    /// <summary>The file that names the open Editor's map (its id), when there is one.</summary>
    public const string EditorMapFile = "Temp/map";

    static string mapHash, mapHashOf;

    /// <summary>Why the host turns away a copy offering these maps, or null when it has this one.</summary>
    public static string MapRefusal(string offered)
    {
        if (mapHashOf != Folders.World)
        {
            // A generated map's hash is of its inputs, read from the folder it's made from (a build's bake is elsewhere).
            mapHash = MapIdentity.Hash(MapSource);
            mapHashOf = Folders.World;
        }
        return MapIdentity.Refusal(MapId, MapFile.Load(Folders.World).Name, mapHash, offered);
    }

    /// <summary>Back from a host's map to this copy's own, before the scene starts over.</summary>
    public static void BackToOwnMap()
    {
        if (OwnMap != MapId) SwitchFolders(OwnMap);
    }

    static void SwitchFolders(string id)
    {
        folders = FoldersFor(id);
        ValleyChemistry.ForgetLand();
    }

    /// <summary>The id of the map being played (world/maps/&lt;id&gt;, or a map folder's name), what peers know it by.</summary>
    public static string MapId => PlayedMaps.TryGetValue(Folders.World, out var m) ? m.id : Folders.Map;

    /// <summary>The folder the map is made from: its own, unless it's a build's bake of a generated map.</summary>
    public static string MapSource => PlayedMaps.TryGetValue(Folders.World, out var m) ? m.source : Folders.World;

    // Each played folder's map id and the folder it was made from, for a folder that isn't world/maps/<id> itself.
    static readonly Dictionary<string, (string id, string source)> PlayedMaps = new();
    // Names said once not to be maps, so the Editor's every look at its folders doesn't say it again.
    static readonly HashSet<string> NotMaps = new();

    /// <summary>The folder a map by this id is played from (a generated one baked first when playing).</summary>
    public static string PlayFolder(string id) => FoldersFor(id).World;

    // A map's folders from its name in -map, the menu or Temp/map: an id under world/maps, or a map folder's path (absolute,
    // or from the repo's root: test-fixtures/maps/flat-1728, #169). A generated map (the nine) is baked first when playing:
    // into its own folder in the Editor (out of git), and into the player's data folder in a build (worlds/<inputs>/<id>).
    static GameFolders FoldersFor(string named)
    {
        var game = NewFolders(GameFolders.DefaultMap);
        if (string.IsNullOrEmpty(named) || named == GameFolders.DefaultMap) return game;
        bool path = Path.IsPathRooted(named) || named.IndexOf('/') >= 0 || named.IndexOf('\\') >= 0;
        string dir = path ? Path.GetFullPath(Path.Combine(game.Repo, named)) : Path.Combine(game.Maps, named);
        string id = path ? Path.GetFileName(dir.TrimEnd('/', '\\')) : named;
        if (!File.Exists(Path.Combine(dir, MapFile.FileName)))
        {
            if (NotMaps.Add(named)) Debug.LogError($"MAP: {named} isn't a map ({dir} has no {MapFile.FileName}), so this is {GameFolders.DefaultMap}");
            return game;
        }
        string played = Application.isPlaying ? Baked(dir, id, game) : dir;
        if (played != Path.Combine(game.Maps, id)) PlayedMaps[played] = (id, dir);
        return game.WithMap(played == Path.Combine(game.Maps, id) ? id : played);
    }

    // The folder to play a map from: its own, or a generated map's bake (made now if there's none of its inputs yet).
    static string Baked(string dir, string id, GameFolders game)
    {
        if (!GeneratedMap.IsGenerated(dir)) return dir;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string into = dir;
        bool baked;
        if (Application.isEditor || game.PlayerData == null) baked = GeneratedMap.EnsureBaked(dir);
        else
        {
            // The folder is named for the map (MapCheck holds a map to its folder's name), under its inputs.
            into = Path.Combine(game.PlayerData, "worlds", GeneratedMap.InputHash(dir), id);
            baked = GeneratedMap.EnsureBakedInto(dir, into);
        }
        Debug.Log($"MAP: {id} {(baked ? "baked" : "already baked")} in {into} ({timer.Elapsed.TotalMilliseconds:0} ms)");
        return into;
    }

    static string checkedMap;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMapStatics()
    {
        folders = null;
        mapHash = null;
        mapHashOf = null;
        checkedMap = null;
        KeepStartMs = 0;
    }

    // A folder that's wrong stops the game here, by name, rather than start on the wrong land: once, not at every reset.
    static void CheckMap()
    {
        if (checkedMap == Folders.World) return;
        var errors = MapCheck.Errors(Folders.World);
        if (errors.Count > 0) throw new InvalidDataException($"{Folders.World} can't be played:\n{string.Join("\n", errors)}");
        checkedMap = Folders.World;
    }

    // The game's folders (rules/, world/, the player's data folder and mods), from the roots only Unity knows.
    // Outside Play mode the Editor's map is read afresh each time: Temp/map may have named another since a play.
    public static GameFolders Folders => Application.isEditor && !Application.isPlaying ? FoldersFor(OwnMap) : folders ??= FoldersFor(OwnMap);

    static GameFolders NewFolders(string map) =>
        GameFolders.ForGame(Application.streamingAssetsPath, Application.dataPath, Application.isEditor, Application.persistentDataPath, map);

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
