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

    public World World
    {
        get => played;
        private set
        {
            played = value;
            ViewWrap.Set(value, cellSize);
        }
    }

    World played;
    public WorldThings Things { get; private set; } = WorldThings.Empty;
    public Vector2Int PlayerStart { get; private set; }

    public bool ExternalTicks { get; set; }

    public event System.Action TickDone;

    public void NoteTickDone() => TickDone?.Invoke();

    public bool streamChunks = true;

    public ChunkPlanner ChunkPlanner { get; private set; } = new();

    public event System.Action<ChunkSwitch> ChunkSwitched;

    public event System.Action WorldReset;

    public event System.Action<ChunkFresh> ChunkFreshened;

    public Vector2Int ChunkAt(Vector3 p)
    {
        var cell = CellAt(p);
        return new Vector2Int(Chunks.ChunkOf(cell.x), Chunks.ChunkOf(cell.y));
    }

    public bool IsChunkActive(int cx, int cy) => World != null && World.HasChunk(cx, cy) && World.IsActive(cx, cy);

    readonly List<(int x, int y)> playerCells = new();
    readonly System.Diagnostics.Stopwatch timer = new();

    public float OwedSeconds => tickTimer;
    public double StepMs { get; set; }
    public double RenderMs { get; private set; }

    static readonly Unity.Profiling.ProfilerMarker StepMarker = new("ChemGrid.Step"), RenderMarker = new("ChemGrid.Render"),
        ChunkPlanMarker = new("ChemGrid.ChunkPlan"), ChunkApplyMarker = new("ChemGrid.ChunkApply"), ChunkEventMarker = new("ChemGrid.ChunkEvent"),
        DetailChunksMarker = new("ChemGrid.DetailChunks");

    static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("ChemGrid.BuildWorld"), BuildRulesMarker = new("ChemGrid.BuildWorld.Rules"),
        BuildCellsMarker = new("ChemGrid.BuildWorld.Cells"), BuildValleyMarker = new("ChemGrid.BuildWorld.Valley"),
        BuildDrawMarker = new("ChemGrid.BuildWorld.Draw"), BuildListenersMarker = new("ChemGrid.BuildWorld.Listeners");
    public double LightsMs { get; private set; }

    readonly List<ChunkFresh> freshChunks = new();

    public void SummonDragon() => World.ApplyDragon(new DragonCommand());
    SpellCaster[] wizards = System.Array.Empty<SpellCaster>();
    float nextWizardLookup;

    float tickTimer;
    int lastLitTick = -1;
    LogEntry lastLog;
    int lastLogCount;

    void Awake()
    {
        TakeCurrent();
        StartupTimeline.Mark("grid awake (start of the world build)");
        BuildWorld();
        StandAtSpawn();
        StartupTimeline.Mark("stood at spawn");
        LoadThings();
        StartupTimeline.Mark("things loaded");
        if (!TryGetComponent<RuleSwitcher>(out _)) gameObject.AddComponent<RuleSwitcher>();
        GridAddOns.Add(gameObject, GridStage.Grid);
        StartupTimeline.Mark("grid's parts added (rule switcher, mods, row houses, holds, waystones)");
    }

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

    public void ResetWorld()
    {
        DropWorldView();
        BuildWorld();
    }

    public void LoadSnapshot(JoinSnapshot snapshot)
    {
        var (world, rules) = snapshot.Join(CurrentRules.Base, World.Source, MapLandUnits(), Budgets);
        world.SetLandRange(MapFile.Load(Folders.World).LandRange);
        SetHold(world);
        DropWorldView();
        WorldLoaded = true;
        using var built = BuildMarker.Auto();
        queuedRules = null;
        TakeYard();
        World = world;
        HearRoom();
        CurrentRules = world.Room != null ? world.RoomRules : rules;
        RecolourWorld();
        PlayerStart = SpawnPoints.StartCell(mapStart);
        BindWorld(0);
        RulesChanged?.Invoke();
    }

    public bool WorldLoaded { get; private set; }

    int[] MapLandUnits()
    {
        if (World?.Source is IGeneratedSource) return null;
        var land = valley != null ? ValleyChemistry.LandOf(World) : null;
        if (land == null) return null;
        var units = new int[land.W * land.H];
        var band = new int[Chunks.Size * land.W];
        for (int cy = 0; cy * Chunks.Size < land.H; cy++)
        {
            land.Band(cy, band);
            int rows = System.Math.Min(Chunks.Size, land.H - cy * Chunks.Size);
            System.Array.Copy(band, 0, units, cy * Chunks.Size * land.W, rows * land.W);
        }
        return units;
    }

    void DropWorldView()
    {
        foreach (Transform child in transform)
            if (blockers == null || child != blockers.Parent) Destroy(child.gameObject);
        ForgetCellRoots();
        freshChunks.Clear();
        lastLog = null;
        tickTimer = 0f;
        views = null;
    }

    Vector2Int mapStart;

    void BuildWorld()
    {
        using var built = BuildMarker.Auto();
        Rules rules;
        using (BuildRulesMarker.Auto()) rules = StartRules();
        StartupTimeline.Mark("rules loaded and composed");
        var roomWas = World?.Room;

        CheckMap();
        StartupTimeline.Mark("map checked");
        TakeYard();
        Vector2Int start;
        var phase = System.Diagnostics.Stopwatch.StartNew();
        using (BuildCellsMarker.Auto()) World = StartWorld(rules, seed, out start);
        WorldLoaded = false;
        StartupTimeline.Mark("world cells built (incl. keep start)");
        double cellsMs = phase.Elapsed.TotalMilliseconds;
        using (BuildRulesMarker.Auto()) OpenRoom(roomWas);
        StartupTimeline.Mark("room and mods");
        mapStart = start;
        PlayerStart = SpawnPoints.StartCell(start);
        if (streamChunks) ChunkStreaming.Apply(World, ChunkStreaming.StartPlan(World, new[] { (PlayerStart.x, PlayerStart.y) }));
        StartupTimeline.Mark("start reach active");
        BindWorld(cellsMs);
    }

    void BindWorld(double cellsMs)
    {
        World.ChunkSwitched += OnChunkSwitched;
        World.LandMoved += (x, y, reach) =>
        {
            if (valley == null) return;
            valley.LandMoved(x, y, reach);
            LandShaped(x, y, reach);
        };
        World.ChunkFreshened += freshChunks.Add;
        ChunkPlanner = new ChunkPlanner();
        var phase = System.Diagnostics.Stopwatch.StartNew();
        if (valley != null)
            using (BuildValleyMarker.Auto()) valley.Bind(World);
        StartupTimeline.Mark("valley bound");
        double valleyMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        double initMs;
        using (BuildDrawMarker.Auto())
        {
            InitVisuals();
            StartupTimeline.Mark("visuals made");
            initMs = phase.Elapsed.TotalMilliseconds;
            RenderAll();
            StartupTimeline.Mark("near cells drawn");
        }
        double drawMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        using (BuildListenersMarker.Auto()) WorldReset?.Invoke();
        StartupTimeline.Mark("world-reset listeners");
        Debug.Log($"WORLD BUILT {World.W} x {World.H}: cells {cellsMs:0} ms (keep start {KeepStartMs:0}), valley {valleyMs:0} ms, draw {drawMs:0} ms (visuals {initMs:0}, near {drawMs - initMs:0}), listeners {phase.Elapsed.TotalMilliseconds:0} ms");
    }

    void TakeYard()
    {
        var land = MapLand.Current;
        if (land == null || valley == null) return;
        var (x, y, w, h) = land.Map.Yard;
        yardCells = new RectInt(x, y, w, h);
    }

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

    public static double KeepStartMs { get; private set; }

    public static World StartWorld(Rules rules, string map, uint seed, out Vector2Int start) => Kept(MapWorld(rules, map, seed, out start));

    public static World StartWorld(Rules rules, uint seed, out Vector2Int start) =>
        Kept(GeneratedMap.IsRowsOnly(Folders.World) ? SourceWorld(rules, seed, out start) : MapWorld(rules, CellMap(), seed, out start));

    static World Kept(World world)
    {
        var kept = System.Diagnostics.Stopwatch.StartNew();
        world.KeepStart();
        KeepStartMs = kept.Elapsed.TotalMilliseconds;
        return world;
    }

    public static World SourceWorld(Rules rules, uint seed, out Vector2Int start)
    {
        var source = PlayedSource;
        var map = MapFile.Load(Folders.World);
        var world = new World(rules, source.W, source.H, seed, wrapX: map.WrapX, wrapY: map.WrapY);
        world.SetLandRange(map.LandRange);
        world.MakeFrom(source);
        world.LandFromSource();
        start = new Vector2Int(source.Start.x, source.Start.y);
        LayPuzzles(world);
        LayStamps(world);
        LayRows(world);
        LayGolf(world);
        SetHold(world);
        return world;
    }

    static GeneratedSource playedSource;
    static string playedSourceOf;

    public static GeneratedSource PlayedSource
    {
        get
        {
            string dir = Folders.World;
            if (playedSourceOf == dir) return playedSource;
            playedSourceOf = dir;
            playedSource = GeneratedMap.IsRowsOnly(dir) ? new GeneratedSource(LoadedMaps.Get(MapSource), Rules.Load(Folders.Rules)) : null;
            return playedSource;
        }
    }

    public static World MapWorld(Rules rules, string map, uint seed, out Vector2Int start)
    {
        var source = BakedSource.FromText(map, rules);
        var world = new World(rules, source.W, source.H, seed);
        world.SetLandRange(MapFile.Load(Folders.World).LandRange);
        world.MakeFrom(source);
        start = new Vector2Int(source.Start.x, source.Start.y);
        LayPuzzles(world);
        LayStamps(world);
        LayRows(world);
        LayGolf(world);
        SetHold(world);
        return world;
    }

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

    static readonly System.Diagnostics.Stopwatch SincePlay = new();
    static bool readyLogged;

    void Update()
    {
        if (!readyLogged)
        {
            readyLogged = true;
            Debug.Log($"PLAY READY {World.W} x {World.H}: first frame {SincePlay.Elapsed.TotalMilliseconds:0} ms after Play started" +
                      (World.Source is GeneratedSource g ? $"; {g.Made} chunks made ({g.MadeOnRead} on a read, {g.MadeAhead} ahead)" : ""));
            StartupTimeline.Done("first frame (PLAY READY)");
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
        drawBudgetMs = System.Math.Clamp(DrawFrameMs - StepMs, LeastDrawMs, DrawBudgetMs);
        timer.Restart();
        using (RenderMarker.Auto()) Render(stepped);
        RenderMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        if (stepped) fireLights.Place();
        fireLights.Flicker();
        LightsMs = timer.Elapsed.TotalMilliseconds;
        PumpLog();
    }

    static GameFolders folders;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetFolders()
    {
        SincePlay.Restart();
        readyLogged = false;
        folders = null;
        checkedMap = null;
        mapHashOf = null;
        chosenMap = null;
        playedSource = null;
        playedSourceOf = null;
        ValleyChemistry.ForgetLand();
    }

    public static string CellMap() => MapFile.ReadCells(Folders.World);

    static string chosenMap;

    public static void PlayMap(string id)
    {
        chosenMap = id;
        SwitchFolders(id);
        Debug.Log($"MAP: on to {MapId}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    public static (string id, string name)[] Maps()
    {
        var game = NewFolders(GameFolders.DefaultMap);
        if (!Directory.Exists(game.Maps)) return new[] { (GameFolders.DefaultMap, "The Valley") };
        var maps = new List<(string id, string name)>();
        foreach (var dir in Directory.GetDirectories(game.Maps))
        {
            if (!File.Exists(Path.Combine(dir, MapFile.FileName))) continue;
            string id = Path.GetFileName(dir);
            maps.Add((id, MapFile.Load(dir).Name ?? id));
        }
        maps.Sort((a, b) => (a.id != GameFolders.DefaultMap).CompareTo(b.id != GameFolders.DefaultMap) is var first && first != 0 ? first : string.CompareOrdinal(a.id, b.id));
        return maps.ToArray();
    }

    static string OwnMap
    {
        get
        {
            if (testMap != null) return testMap;
            if (chosenMap != null) return chosenMap;
            var args = System.Environment.GetCommandLineArgs();
            if (!Application.isEditor && System.Array.IndexOf(args, "-map") < 0) return SoloMap.Start(args);
            if (System.Array.IndexOf(args, "-map") >= 0 || !Application.isEditor || !File.Exists(EditorMapFile)) return MapIdentity.Named(args);
            string named = File.ReadAllText(EditorMapFile).Trim();
            return named.Length > 0 ? named : GameFolders.DefaultMap;
        }
    }

    static string testMap;

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

    public const string EditorMapFile = "Temp/map";

    static string mapHash, mapHashOf;

    public static string MapRefusal(string offered)
    {
        if (mapHashOf != Folders.World)
        {
            mapHash = MapIdentity.Hash(MapSource);
            mapHashOf = Folders.World;
        }
        return MapIdentity.Refusal(MapId, MapFile.Load(Folders.World).Name, mapHash, offered);
    }

    public static void BackToOwnMap()
    {
        if (OwnMap != MapId) SwitchFolders(OwnMap);
    }

    static void SwitchFolders(string id)
    {
        folders = FoldersFor(id);
        ValleyChemistry.ForgetLand();
    }

    public static string MapId => PlayedMaps.TryGetValue(Folders.World, out var m) ? m.id : Folders.Map;

    public static string MapSource => PlayedMaps.TryGetValue(Folders.World, out var m) ? m.source : Folders.World;

    static readonly Dictionary<string, (string id, string source)> PlayedMaps = new();
    static readonly HashSet<string> NotMaps = new();

    public static string PlayFolder(string id) => FoldersFor(id).World;

    public static string PlayFolder(string id, bool playing) => FoldersFor(id, playing).World;

    static GameFolders FoldersFor(string named) => FoldersFor(named, Application.isPlaying);

    static GameFolders FoldersFor(string named, bool playing)
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
        string played = playing ? Baked(dir, id, game) : dir;
        if (played != Path.Combine(game.Maps, id)) PlayedMaps[played] = (id, dir);
        return game.WithMap(played == Path.Combine(game.Maps, id) ? id : played);
    }

    static string Baked(string dir, string id, GameFolders game)
    {
        if (!GeneratedMap.IsGenerated(dir)) return dir;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string into = dir;
        bool rowsOnly = !BakesWhole(dir);
        if (!Application.isEditor && game.PlayerData != null) into = Path.Combine(game.PlayerData, "worlds", GeneratedMap.InputHash(dir), id);
        bool baked;
        if (rowsOnly) baked = MapBake.EnsureRows(dir, into);
        else baked = into == dir ? MapBake.EnsureBaked(dir) : MapBake.EnsureBakedInto(dir, into);
        StartupTimeline.Mark("map baked or found");
        Debug.Log($"MAP: {id} {(rowsOnly ? "its rows " : "")}{(baked ? "baked" : "already baked")} in {into} ({timer.Elapsed.TotalMilliseconds:0} ms)");
        return into;
    }

    public static bool BakesWhole(string dir) =>
        Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, MapFile.FileName)))[GeneratedMap.Key]?["bake"]?.ToObject<bool>() != false;

    static string checkedMap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMapStatics()
    {
        folders = null;
        mapHash = null;
        mapHashOf = null;
        checkedMap = null;
        KeepStartMs = 0;
    }

    static void CheckMap()
    {
        if (checkedMap == Folders.World) return;
        var errors = MapCheck.Errors(Folders.World, Application.isEditor || Folders.PlayerData == null ? null : Path.Combine(Folders.PlayerData, "map-checks", Path.GetFileName(Folders.World) + ".check"));
        if (errors.Count > 0) throw new InvalidDataException($"{Folders.World} can't be played:\n{string.Join("\n", errors)}");
        checkedMap = Folders.World;
    }

    public static GameFolders Folders => Application.isEditor && !Application.isPlaying ? FoldersFor(OwnMap) : folders ??= FoldersFor(OwnMap);

    static GameFolders NewFolders(string map) =>
        GameFolders.ForGame(Application.streamingAssetsPath, Application.dataPath, Application.isEditor, Application.persistentDataPath, map);

    const double StepBudgetMs = 6.0;
    public const float MostOwedSeconds = 1f;
    const int ExactStepSlices = 256;
    readonly FrameBudget stepBudget = new(StepBudgetMs, ExactStepSlices);

    public void SetStepBudget(double ms, int exactSlices) => stepBudget.Set(ms, exactSlices);

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

    public IReadOnlyList<(int x, int y)> PlayerCells()
    {
        if (ChoreClock >= nextWizardLookup)
        {
            wizards = SpellCaster.AllByInstanceId;
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

    RectInt Yard => yardCells.width > 0 ? yardCells : new RectInt(0, 0, World.W, World.H);

    public Vector2Int YardCell(int x, int y) => new Vector2Int(x + Yard.x, y + Yard.y);

    public Vector3 YardCellCenter(int x, int y)
    {
        var cell = YardCell(x, y);
        return CellCenter(cell.x, cell.y);
    }

    public Vector3 CellCenter(int x, int y) => ViewWrap.Near(CellCenterUnwrapped(x, y));

    public Vector3 CellCenterUnwrapped(int x, int y)
    {
        var yard = Yard;
        return new Vector3((x - yard.x) * cellSize, 0f, (yard.y + yard.height - 1 - y) * cellSize);
    }

    public Vector2Int CellAt(Vector3 p)
    {
        var yard = Yard;
        return new Vector2Int(ViewWrap.FoldX(Mathf.RoundToInt(p.x / cellSize) + yard.x), ViewWrap.FoldY(yard.y + yard.height - 1 - Mathf.RoundToInt(p.z / cellSize)));
    }

    public float SurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        return c == null ? 0f : GroundTop(x, y, c) + CellDressing.Look(World.Rules, c.T).height;
    }

    public float GroundTop(int x, int y, Cell c)
    {
        float baseY = valley != null ? valley.BaseHeight(x, y) : 0f;
        return baseY + World.ElevationUnits(c) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;
    }

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
