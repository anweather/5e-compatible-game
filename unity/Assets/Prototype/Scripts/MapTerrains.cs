using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The chunk terrains, made when the scene starts from the map's folder (docs/design/maps-as-data.md, phase 6): the scene
// holds no ground of its own. With the map's ground/ (GroundSwitch on, the default) every chunk is built here from its
// arrays (GroundBuilder); in land mode, or for a map with no ground/, the chunks wait blank until the first world says
// what its land is (ValleyChemistry.Bind). What can't be data stays an asset: the template (the prototypes: layers,
// trees, grass) and the rock material, both made by Build Chemistry Yard. Early, so what reads the terrains as it
// starts (the sea's depth, the coast's life) finds them.
//
// A generated map's terrains are made only for the drawn tier (#205, J-086): the chunks within the far ring's sight (its
// drawn dial) of a character plus a chunk, and those under the active chunks, nearest first and a few a frame, from a pool
// the chunks left behind go back to. The far ring's known meshes draw the land past them. Nothing is made for the rest of
// the map, so what the terrains cost follows the drawn tier, never the map's size. The valley keeps all of its 81.
[DefaultExecutionOrder(-1000)]
public sealed class MapTerrains : MonoBehaviour
{
    /// <summary>How far a chunk terrain draws its details (grass), in metres, as made.</summary>
    public const float DetailMetres = 120f;

    /// <summary>How far a chunk terrain draws its trees, in metres (900 as made; the tree impostors' trial draws them nearer, TreeImpostors).</summary>
    public static float TreeDistance { get; private set; } = 900f;

    /// <summary>Sets TreeDistance, on every terrain made now and on those made after.</summary>
    public static void SetTreeDistance(float metres)
    {
        TreeDistance = metres;
        if (Instance == null || Instance.chunks == null) return;
        foreach (var t in Instance.chunks)
            if (t != null) t.treeDistance = metres;
    }

    /// <summary>The scene's chunk terrains (null before one starts).</summary>
    public static MapTerrains Instance { get; private set; }

    [Tooltip("The prototypes every chunk is built with: terrain layers, trees, grass (Generated/Ground/Template.asset).")]
    public TerrainData template;
    [Tooltip("The terrains' material: URP's terrain with rock mapped from the sides (TerrainRock.shader).")]
    public Material material;

    TerrainGrid layout;
    Terrain[] chunks;

    /// <summary>The builder the chunks were built with at the start; null when they wait for a world (land mode).</summary>
    public GroundBuilder Builder
    {
        get
        {
            if (chunks == null) Make();
            return builder;
        }
    }

    GroundBuilder builder;

    /// <summary>The chunk terrains, chunk (i, j) at j * ChunksX + i; made now if the scene hasn't started them yet.</summary>
    public Terrain[] Chunks
    {
        get
        {
            if (chunks == null) Make();
            return chunks;
        }
    }

    void Awake()
    {
        Instance = this;
        if (chunks == null) Make();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // A chunk past the camera's far plane isn't drawn, but Unity still culls every terrain each frame: on the nine (729)
    // that was 9 ms. Those beyond it stop drawing (they stay terrains, for heights and collisions) and draw again as the
    // camera nears; a few times a second, as the camera moves less than a chunk in that time.
    const float ReachEvery = 0.25f;
    float nextReach;

    void Update()
    {
        if (lazy && ground != null) Stream();
        StartupTimeline.Mark("first frame: ground terrains streamed", 20);
        if (!Application.isPlaying || chunks == null || Time.unscaledTime < nextReach) return;
        using var marked = ReachMarker.Auto();
        nextReach = Time.unscaledTime + ReachEvery;
        var cam = Camera.main;
        if (cam == null) return;
        float reach = cam.farClipPlane + layout.ChunkSize, reachSq = reach * reach;
        var eye = cam.transform.position;
        float half = layout.ChunkSize * 0.5f;
        // On a generated map the far ring says which draw: none past the known ring, and none it draws as a known chunk.
        var far = FarRing.Instance;
        // Only the chunks that can change are visited (#208): those that drew at the last check, and those in the square
        // round the camera's reach. Any other was off and stays off. The first check visits them all, as they start on, and
        // so does the first with the far ring (which switches the off ones off, not only hidden).
        visit.Clear();
        if (lazy) visit.UnionWith(made);
        else if (!checkedOnce || withFar != (far != null))
        {
            for (int k = 0; k < chunks.Length; k++) visit.Add(k);
            checkedOnce = true;
            withFar = far != null;
        }
        else
        {
            visit.UnionWith(drawn);
            var (ox, oz) = layout.ChunkCorner(0, 0);
            float size = layout.ChunkSize;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - reach - ox) / size)), i1 = Mathf.Min(layout.ChunksX - 1, Mathf.FloorToInt((eye.x + reach - ox) / size));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - reach - oz) / size)), j1 = Mathf.Min(layout.ChunksZ - 1, Mathf.FloorToInt((eye.z + reach - oz) / size));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++) visit.Add(j * layout.ChunksX + i);
        }
        drawn.Clear();
        foreach (int k in visit)
        {
            var t = chunks[k];
            if (t == null) continue;
            var p = t.transform.position;
            float dx = p.x + half - eye.x, dz = p.z + half - eye.z;
            bool draw = dx * dx + dz * dz <= reachSq && (far == null || far.TerrainDraws(k % layout.ChunksX, k / layout.ChunksX));
            if (draw) drawn.Add(k);
            // On a generated map a chunk that doesn't draw is switched off, not only hidden: Unity culls every enabled
            // Terrain each frame whether it draws or not (729 on the nine, 8 ms of TerrainManager.CullAllTerrains in the
            // Editor, #182). Its collider stays on, and SampleHeight still reads it. The valley's 81 stay as they were.
            if (far != null && t.enabled != draw) t.enabled = draw;
            if (t.drawHeightmap == draw) continue;
            t.drawHeightmap = draw;
            t.drawTreesAndFoliage = draw;
        }
        Drawing = drawn.Count;
        HintDrawn();
    }

    // The chunks that drew at the last check, and those this check visits.
    readonly System.Collections.Generic.HashSet<int> drawn = new(), visit = new();
    bool checkedOnce, withFar;

    // The world's drawn hint (World.SetDrawn, D17): the chemistry chunks under the drawn terrains, so the core doesn't pack
    // what the view draws. Given again only when the drawn terrains or the world change. The terrains are offset from the
    // chemistry's chunks and run north to south the other way, so each terrain meets up to four of them.
    readonly HashSet<int> hinted = new();
    readonly HashSet<(int cx, int cy)> drawnCells = new();
    World hintedWorld;

    void HintDrawn(bool now = false)
    {
        var world = ChemGrid.Current?.Grid?.World;
        if (world == null || (!now && world == hintedWorld && hinted.SetEquals(drawn))) return;
        hintedWorld = world;
        hinted.Clear();
        hinted.UnionWith(drawn);
        drawnCells.Clear();
        foreach (int k in drawn) CellChunksUnder(world, k % layout.ChunksX, k / layout.ChunksX, 0, drawnCells);
        if (world == pinnedWorld) drawnCells.UnionWith(pinned);
        world.SetDrawn(drawnCells);
    }

    // The chemistry chunks under chunk terrain (i, j), and `ring` chunks round them.
    void CellChunksUnder(World world, int i, int j, int ring, HashSet<(int cx, int cy)> into)
    {
        int last = Chem.Chunks.Size - 1;
        int x0 = Mathf.Max(0, i * Chem.Chunks.Size - layout.U0), x1 = Mathf.Min(world.W - 1, i * Chem.Chunks.Size + last - layout.U0);
        int y0 = Mathf.Max(0, layout.V0 - j * Chem.Chunks.Size - last), y1 = Mathf.Min(world.H - 1, layout.V0 - j * Chem.Chunks.Size);
        if (x0 > x1 || y0 > y1) return;
        int cxMax = (world.W - 1) >> Chem.Chunks.Shift, cyMax = (world.H - 1) >> Chem.Chunks.Shift;
        for (int cy = Mathf.Max(0, (y0 >> Chem.Chunks.Shift) - ring); cy <= Mathf.Min(cyMax, (y1 >> Chem.Chunks.Shift) + ring); cy++)
            for (int cx = Mathf.Max(0, (x0 >> Chem.Chunks.Shift) - ring); cx <= Mathf.Min(cxMax, (x1 >> Chem.Chunks.Shift) + ring); cx++) into.Add((cx, cy));
    }

    // The ground's arrays and heights are made side by side (Parallel.For, a worker), and they read the world's cells. A
    // read of a packed chunk unpacks it (D17), which changes the world's lists and its pool of blocks: never safe off the
    // main thread. So the chunks they read (those under the terrains and one round them, for the paint's and heights'
    // neighbours) are unpacked here first, on the main thread, and held in the drawn hint until the work is taken.
    readonly HashSet<(int cx, int cy)> pinned = new();
    World pinnedWorld;

    void Warm(IEnumerable<(int i, int j)> terrains)
    {
        var world = ground?.World;
        if (world == null) return;
        if (world != pinnedWorld)
        {
            pinned.Clear();
            pinnedWorld = world;
        }
        int before = pinned.Count;
        foreach (var (i, j) in terrains) CellChunksUnder(world, i, j, 1, pinned);
        if (pinned.Count != before) HintDrawn(now: true);
        foreach (var (cx, cy) in pinned) world.At(cx << Chem.Chunks.Shift, cy << Chem.Chunks.Shift);
    }

    void Unpin()
    {
        if (pinned.Count == 0) return;
        pinned.Clear();
        HintDrawn(now: true);
    }

    /// <summary>A generated map's ground, once it's served (null before, and for a map whose terrains are all made at the start).</summary>
    public ValleyGround Ground => ground;

    /// <summary>How many chunk terrains drew at the last check (the drawn tier, for the tiersweep probe).</summary>
    public int Drawing { get; private set; }

    /// <summary>Works out which terrains draw at the next frame, not at the next quarter second (the far ring changed).</summary>
    public void ReachNow()
    {
        nextReach = 0f;
        planNow = true;
    }

    void Make()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string dir = ChemGrid.Folders.World;
        layout = MapLand.Current?.Layout ?? TerrainGrid.Valley();
        builder = GroundSwitch.Mode == GroundSwitch.On && GroundPack.In(dir)
            ? new GroundBuilder(GroundPack.Load(dir), template, layout.ChunkSize)
            : null;
        // A generated map (no ground/: its ground is made from its land) makes its terrains as they're wanted, once the
        // ground is served (ValleyChemistry's first Bind), none now.
        lazy = builder == null && Application.isPlaying && GeneratedMap.IsGenerated(ChemGrid.MapSource);
        chunks = lazy ? new Terrain[layout.ChunksX * layout.ChunksZ] : Place(transform, layout, builder, material);
        StartupTimeline.Mark("ground terrains placed");
        Debug.Log(lazy
            ? $"GROUND TERRAINS for the drawn tier: none of {chunks.Length} chunks made yet, they're made as they're wanted"
            : $"GROUND TERRAINS {(builder != null ? "built" : "waiting for the land")}: {chunks.Length} chunks in {timer.Elapsed.TotalMilliseconds:0} ms");
    }

    // ---------- the drawn tier's terrains (#205) ----------

    bool lazy;
    ValleyGround ground;
    readonly Stack<Terrain> pool = new();
    readonly HashSet<int> made = new();
    readonly List<(int i, int j)> toMake = new();
    readonly HashSet<(int i, int j)> wantedNow = new(), activeNow = new();
    readonly List<(int i, int j)> focus = new();
    readonly List<int> leaving = new();
    float nextPlan;
    bool planNow;
    int createdEver;

    // A chunk terrain is made within the drawn dial plus Margin of a character, and goes back to the pool past it plus
    // KeepMargin (the two apart so a character on a chunk's edge doesn't make and drop the same row each step).
    const int Margin = 1, KeepMargin = 2;
    // About this many ms a frame go to making terrains in play (at least one a frame while any is wanted and its arrays are
    // made); the arrays of up to MostAtOnce chunks are made side by side on a worker.
    const double MakeBudgetMs = 4.0;
    const float BusyFrameSeconds = 0.022f;
    const int MostWaits = 3;
    int waited;
    const int MostAtOnce = 8;

    /// <summary>Whether the terrains are made only for the drawn tier (a generated map's), not one for every chunk.</summary>
    public bool Lazy => lazy;

    /// <summary>How many chunk terrains stand now, how many wait in the pool, and how many were ever made (the pool's size).</summary>
    public (int standing, int pooled, int created) Counts => lazy ? (made.Count, pool.Count, createdEver) : (chunks?.Length ?? 0, 0, chunks?.Length ?? 0);

    /// <summary>What the first frame's terrains' arrays took (ms), made side by side.</summary>
    public double ArraysMs { get; private set; }

    /// <summary>How many chunk terrains are wanted and not made yet.</summary>
    public int Waiting => toMake.Count;

    /// <summary>
    /// A generated map's ground is ready (ValleyChemistry's first Bind): the terrains the drawn tier wants are made now,
    /// whole, before the first frame; then as the characters move, a few a frame.
    /// </summary>
    public void Serve(ValleyGround served)
    {
        if (!lazy) return;
        ground = served;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Plan();
        MakeSome(whole: true);
        StartupTimeline.Mark("ground terrains for the drawn tier made");
        Debug.Log($"GROUND TERRAINS for the drawn tier: {made.Count} of {chunks.Length} chunks made in {timer.Elapsed.TotalMilliseconds:0} ms " +
                  $"({ArraysMs:0} ms of it their arrays and height offsets, side by side; sight {Sight()} + {Margin})");
    }

    void Stream()
    {
        if (planNow || Time.unscaledTime >= nextPlan) Plan();
        MakeSome(whole: false);
    }

    /// <summary>The far ring's drawn dial in chunks (sim.json's streaming sight until it's read): how far round a character the terrains, and the sea's tiles, are made.</summary>
    public static int Sight()
    {
        var far = FarRing.Instance;
        int sight = far != null ? far.Dials.drawn : 0;
        return sight > 0 ? sight : ChemGrid.Current?.Rules?.Sim.Streaming.Sight ?? 4;
    }

    // Which terrains are wanted: round each character (and the camera), and under the active chunks. Those no longer
    // wanted go back to the pool now; those wanted and not made wait, nearest first.
    void Plan()
    {
        using var marked = PlanMarker.Auto();
        planNow = false;
        nextPlan = Time.unscaledTime + ReachEvery;
        focus.Clear();
        var grid = ChemGrid.Current?.Grid;
        if (grid != null && grid.World != null)
            foreach (var (x, y) in grid.PlayerCells()) AddFocus(layout.ChunkOfCell(x, y));
        var cam = Camera.main;
        if (cam != null)
        {
            var p = cam.transform.position;
            AddFocus((Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize)));
        }
        activeNow.Clear();
        if (grid != null && grid.World != null)
            foreach (var (cx, cy) in grid.World.ActiveChunks) AddActive(grid.World, cx, cy);
        int sight = Sight(), make = sight + Margin, keep = sight + KeepMargin;
        leaving.Clear();
        foreach (int k in made)
        {
            var at = (k % layout.ChunksX, k / layout.ChunksX);
            if (Distance(at) > keep && !activeNow.Contains(at)) leaving.Add(k);
        }
        foreach (int k in leaving) Release(k);
        ForgetFar(keep);
        wantedNow.Clear();
        foreach (var (fi, fj) in focus)
            for (int j = fj - make; j <= fj + make; j++)
                for (int i = fi - make; i <= fi + make; i++) Want((i, j));
        foreach (var at in activeNow) Want(at);
        toMake.Clear();
        toMake.AddRange(wantedNow);
        toMake.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
    }

    // Every so often, what the ground holds for chunks with no terrain far past the drawn cells (the known radius, where
    // the cells' dressings stop, and a margin) is let go (ValleyGround.ForgetFar): their height offsets and arrays.
    void ForgetFar(int keep)
    {
        if (Time.unscaledTime < nextForget || arrays != null) return;
        nextForget = Time.unscaledTime + ForgetEvery;
        var far = FarRing.Instance;
        int past = Mathf.Max(keep, far != null ? far.Dials.known : keep) + ForgetMargin;
        ground.ForgetFar(at => Distance(at) > past);
    }

    float nextForget;
    const float ForgetEvery = 2f;
    const int ForgetMargin = 3;

    void AddFocus((int i, int j) at)
    {
        if (!focus.Contains(at)) focus.Add(at);
    }

    // The terrains under an active chunk of cells (cx, cy): the cells' chunks and the terrains' are offset.
    void AddActive(World world, int cx, int cy)
    {
        int x0 = cx << Chem.Chunks.Shift, y0 = cy << Chem.Chunks.Shift;
        int x1 = Mathf.Min(world.W - 1, x0 + Chem.Chunks.Size - 1), y1 = Mathf.Min(world.H - 1, y0 + Chem.Chunks.Size - 1);
        var (ia, ja) = layout.ChunkOfCell(x0, y0);
        var (ib, jb) = layout.ChunkOfCell(x1, y1);
        for (int j = Mathf.Min(ja, jb); j <= Mathf.Max(ja, jb); j++)
            for (int i = Mathf.Min(ia, ib); i <= Mathf.Max(ia, ib); i++)
                if (layout.HasChunk(i, j)) activeNow.Add((i, j));
    }

    void Want((int i, int j) at)
    {
        if (layout.HasChunk(at.i, at.j) && chunks[at.j * layout.ChunksX + at.i] == null && ground.HasChunk(at)) wantedNow.Add(at);
    }

    // Chebyshev chunks to the nearest focus (0 for an active chunk, which must stand under what moves there).
    int Distance((int i, int j) at)
    {
        if (activeNow.Contains(at)) return 0;
        int best = int.MaxValue;
        foreach (var (fi, fj) in focus) best = Mathf.Min(best, Mathf.Max(Mathf.Abs(at.i - fi), Mathf.Abs(at.j - fj)));
        return best;
    }

    // The waiting terrains, nearest first: all of them (before the first frame), or as many as fit the frame's budget. In
    // play their arrays are made on a worker, a few chunks side by side, while the frames go on: only a terrain whose
    // arrays are made is made in a frame (or one under an active chunk, which waits for nothing).
    void MakeSome(bool whole)
    {
        using var marked = MakeMarker.Auto();
        TakeArrays();
        if (toMake.Count == 0) return;
        // After a slow frame a terrain not about to be stood on waits a frame (a few at most): making one is a few ms, and
        // the far ring's mesh shows there until it's drawn (FarRing.LetGo).
        if (!whole && Time.unscaledDeltaTime > BusyFrameSeconds && waited < MostWaits && Distance(toMake[0]) > 1)
        {
            waited++;
            StartArrays(0);
            return;
        }
        waited = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (whole)
        {
            Warm(toMake);
            ground.Prepare(toMake);
            if (arrays == null) Unpin();
            ArraysMs = timer.Elapsed.TotalMilliseconds;
        }
        else PrepareHeights();
        int next = 0;
        bool any = false;
        while (next < toMake.Count && (whole || !any || timer.Elapsed.TotalMilliseconds < MakeBudgetMs))
        {
            var at = toMake[next];
            if (!ground.Builder.Holds(at))
            {
                if (activeNow.Contains(at)) ground.Builder.Prepare(new[] { at });
                else
                {
                    StartArrays(next);
                    break;
                }
            }
            Attach(at);
            any = true;
            next++;
        }
        toMake.RemoveRange(0, next);
        // The next ones' arrays start now, so they're likely made by the time their turn comes.
        if (toMake.Count > 0 && !whole) StartArrays(0);
        if (!any) return;
        nextReach = 0f;
    }

    // In play, the height offsets of the next few chunks whose arrays are made, side by side: worked out one at a time as
    // each terrain is made, they were a few milliseconds a terrain on the main thread (#206).
    void PrepareHeights()
    {
        var ready = new List<(int i, int j)>();
        for (int n = 0; n < toMake.Count && ready.Count < HeightsAFrame; n++)
        {
            if (!ground.Builder.Holds(toMake[n])) break;
            ready.Add(toMake[n]);
        }
        if (ready.Count > 1) ground.PrepareHeights(ready);
    }

    const int HeightsAFrame = 8;

    // The next chunks' arrays, made on a worker (one batch at a time).
    void StartArrays(int from)
    {
        if (arrays != null) return;
        var missing = ground.Builder.Missing(toMake.Skip(from).Take(MostAtOnce * 2));
        if (missing.Count > MostAtOnce) missing.RemoveRange(MostAtOnce, missing.Count - MostAtOnce);
        if (missing.Count == 0) return;
        var builder = ground.Builder;
        arraysFor = missing;
        Warm(missing);
        arrays = System.Threading.Tasks.Task.Run(() => builder.MakeArrays(missing));
    }

    // A worker's arrays, kept on the main thread once they're done.
    void TakeArrays()
    {
        if (arrays == null || !arrays.IsCompleted) return;
        var job = arrays;
        arrays = null;
        Unpin();
        if (job.IsFaulted)
        {
            Debug.LogError($"GROUND TERRAINS: a chunk's arrays failed: {job.Exception?.GetBaseException().Message}");
            return;
        }
        ground.Builder.Keep(arraysFor, job.Result);
    }

    System.Threading.Tasks.Task<GroundChunk[]> arrays;
    List<(int i, int j)> arraysFor;

    // A terrain from the pool (or a new one) for chunk (i, j), given the chunk's ground.
    void Attach((int i, int j) at)
    {
        using var marked = AttachMarker.Auto();
        Terrain t;
        if (pool.Count > 0)
        {
            t = pool.Pop();
            t.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("Chunk", typeof(Terrain), typeof(TerrainCollider));
            go.transform.SetParent(transform, false);
            t = go.GetComponent<Terrain>();
            createdEver++;
        }
        t.name = $"Chunk {at.i},{at.j}";
        var (cx, cz) = layout.ChunkCorner(at.i, at.j);
        t.transform.position = new Vector3(cx, 0f, cz);
        Configure(t, layout.IsHighRes(at.i, at.j), material);
        t.allowAutoConnect = false;
        t.enabled = true;
        t.drawHeightmap = true;
        t.drawTreesAndFoliage = true;
        using (AttachGroundMarker.Auto()) ground.Attach(at, t);
        int k = at.j * layout.ChunksX + at.i;
        chunks[k] = t;
        made.Add(k);
        Connect(at);
        using (BushesMarker.Auto()) SeatBushes(at, t);
    }

    static readonly Unity.Profiling.ProfilerMarker ReachMarker = new("MapTerrains.Reach"), PlanMarker = new("MapTerrains.Plan"),
        MakeMarker = new("MapTerrains.MakeSome"), AttachMarker = new("MapTerrains.Attach"), AttachGroundMarker = new("MapTerrains.Attach.Ground"),
        BushesMarker = new("MapTerrains.Attach.Bushes"), ReleaseMarker = new("MapTerrains.Release");

    // The bushes of the cells under a terrain just made, put on it (CellDressing.SeatBushes).
    void SeatBushes((int i, int j) at, Terrain t)
    {
        var grid = ChemGrid.Current?.Grid;
        if (grid == null || t.terrainData == null) return;
        for (int b = 0; b < TerrainGrid.ChunkCells; b++)
            for (int a = 0; a < TerrainGrid.ChunkCells; a++)
            {
                var (x, y) = layout.GridCell(at.i * TerrainGrid.ChunkCells + a, at.j * TerrainGrid.ChunkCells + b);
                CellDressing.SeatBushes(grid.DressingAt(x, y), t);
            }
    }

    // Chunk k's terrain back to the pool (its ground keeps what was changed on it).
    void Release(int k)
    {
        using var marked = ReleaseMarker.Auto();
        var t = chunks[k];
        chunks[k] = null;
        made.Remove(k);
        drawn.Remove(k);
        ground.Release((k % layout.ChunksX, k / layout.ChunksX));
        if (t == null) return;
        Connect((k % layout.ChunksX, k / layout.ChunksX));
        t.SetNeighbors(null, null, null, null);
        t.terrainData = null;
        if (t.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = null;
        t.gameObject.SetActive(false);
        pool.Push(t);
    }

    // A terrain made or let go: it and the four round it told their neighbours (those standing, of the same detail, which
    // Unity stitches). Done here rather than by Terrain.SetConnectivityDirty, whose AutoConnect went over every terrain
    // and made garbage each time one was made, a few a second in flight.
    void Connect((int i, int j) at)
    {
        Neighbours(at);
        Neighbours((at.i - 1, at.j));
        Neighbours((at.i + 1, at.j));
        Neighbours((at.i, at.j - 1));
        Neighbours((at.i, at.j + 1));
    }

    void Neighbours((int i, int j) at)
    {
        var t = Standing(at);
        if (t == null) return;
        bool high = layout.IsHighRes(at.i, at.j);
        t.SetNeighbors(Alike((at.i - 1, at.j), high), Alike((at.i, at.j + 1), high), Alike((at.i + 1, at.j), high), Alike((at.i, at.j - 1), high));
    }

    Terrain Standing((int i, int j) at) => layout.HasChunk(at.i, at.j) && chunks[at.j * layout.ChunksX + at.i] is { } t && t.terrainData != null ? t : null;

    Terrain Alike((int i, int j) at, bool high) => Standing(at) is { } t && layout.IsHighRes(at.i, at.j) == high ? t : null;

    /// <summary>The chunk terrain standing under a world point, or null (none made there, or off the map).</summary>
    public Terrain TerrainAt(Vector3 p)
    {
        if (chunks == null) return null;
        int i = Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), j = Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize);
        return layout.HasChunk(i, j) ? chunks[j * layout.ChunksX + i] : null;
    }

    /// <summary>
    /// The drawn ground's height under a point: its terrain's where one stands, else the ground's (the land, for a chunk
    /// with no terrain made); null off the map.
    /// </summary>
    public float? HeightAt(Vector3 p)
    {
        var t = TerrainAt(p);
        if (t != null && t.terrainData != null) return t.SampleHeight(p) + t.transform.position.y;
        int i = Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), j = Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize);
        if (ground == null || !layout.HasChunk(i, j)) return null;
        return ground.GroundAt(p.x, p.z);
    }

    /// <summary>
    /// The chunks as Terrains under <paramref name="parent"/>, each from <paramref name="builder"/>'s arrays (or blank,
    /// without one), standing where its step 0 is. Build Chemistry Yard uses the same to stand things on the ground.
    /// </summary>
    public static Terrain[] Place(Transform parent, TerrainGrid layout, GroundBuilder builder, Material material)
    {
        var terrains = new Terrain[layout.ChunksX * layout.ChunksZ];
        for (int j = 0; j < layout.ChunksZ; j++)
        {
            for (int i = 0; i < layout.ChunksX; i++)
            {
                var data = builder != null && builder.Has((i, j)) ? builder.Build((i, j)) : Blank(layout, i, j);
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = $"Chunk {i},{j}";
                go.transform.SetParent(parent, false);
                var (cx, cz) = layout.ChunkCorner(i, j);
                go.transform.position = new Vector3(cx, builder != null ? builder.Bottom : 0f, cz);
                terrains[j * layout.ChunksX + i] = Configure(go.GetComponent<Terrain>(), layout.IsHighRes(i, j), material);
            }
        }
        Terrain.SetConnectivityDirty();
        return terrains;
    }

    static TerrainData Blank(TerrainGrid layout, int i, int j) => new TerrainData
    {
        name = $"Blank chunk {i},{j}",
        heightmapResolution = 33,
        size = new Vector3(layout.ChunkSize, 1f, layout.ChunkSize),
    };

    // Terrains connect to neighbours of the same group for LOD stitching, and Unity only stitches neighbours of the same
    // resolution, so full-detail and coarse chunks are two groups (their shared edges match by construction).
    static Terrain Configure(Terrain terrain, bool fullDetail, Material material)
    {
        terrain.treeDistance = TreeDistance;
        terrain.treeBillboardDistance = 160f;
        terrain.detailObjectDistance = DetailMetres;
        terrain.heightmapPixelError = 4f;
        terrain.drawInstanced = true;
        terrain.materialTemplate = material;
        terrain.allowAutoConnect = true;
        terrain.groupingID = fullDetail ? 1 : 0;
        return terrain;
    }
}
