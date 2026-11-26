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
    public const float DetailMetres = 80f;

    public static float TreeDistance { get; private set; } = 900f;

    public static void SetTreeDistance(float metres)
    {
        TreeDistance = metres;
        if (Instance == null || Instance.chunks == null) return;
        foreach (var t in Instance.chunks)
            if (t != null) t.treeDistance = metres;
    }

    public static MapTerrains Instance { get; private set; }

    [Tooltip("The prototypes every chunk is built with: terrain layers, trees, grass (Generated/Ground/Template.asset).")]
    public TerrainData template;
    [Tooltip("The terrains' material: URP's terrain with rock mapped from the sides (TerrainRock.shader).")]
    public Material material;
    [Tooltip("The terrain pack's own tuft meshes, which the template's grass is made lighter from (LightTufts); for -perf-look-pair-tufts and -perf-pack-tufts only.")]
    public Mesh[] packTufts;

    TerrainGrid layout;
    Terrain[] chunks;

    public GroundBuilder Builder
    {
        get
        {
            if (chunks == null) Make();
            return builder;
        }
    }

    GroundBuilder builder;

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
        var far = FarRing.Instance;
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
            if (far != null && t.enabled != draw) t.enabled = draw;
            if (t.drawHeightmap == draw) continue;
            t.drawHeightmap = draw;
            t.drawTreesAndFoliage = draw;
        }
        Drawing = drawn.Count;
        HintDrawn();
    }

    readonly System.Collections.Generic.HashSet<int> drawn = new(), visit = new();
    bool checkedOnce, withFar;

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

    void CellChunksUnder(World world, int i, int j, int ring, HashSet<(int cx, int cy)> into)
    {
        int last = Chem.Chunks.Size - 1;
        int x0 = Mathf.Max(0, i * Chem.Chunks.Size - layout.U0), x1 = Mathf.Min(world.W - 1, i * Chem.Chunks.Size + last - layout.U0);
        int y0 = Mathf.Max(0, layout.V0 - j * Chem.Chunks.Size - last), y1 = Mathf.Min(world.H - 1, layout.V0 - j * Chem.Chunks.Size);
        if (x0 > x1 || y0 > y1) return;
        int cxMax = (world.W - 1) >> Chem.Chunks.Shift, cyMax = (world.H - 1) >> Chem.Chunks.Shift;
        int cy0 = (y0 >> Chem.Chunks.Shift) - ring, cy1 = (y1 >> Chem.Chunks.Shift) + ring, cx0 = (x0 >> Chem.Chunks.Shift) - ring, cx1 = (x1 >> Chem.Chunks.Shift) + ring;
        if (!world.WrapY) (cy0, cy1) = (Mathf.Max(0, cy0), Mathf.Min(cyMax, cy1));
        if (!world.WrapX) (cx0, cx1) = (Mathf.Max(0, cx0), Mathf.Min(cxMax, cx1));
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++) into.Add((world.FoldChunkX(cx), world.FoldChunkY(cy)));
    }

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

    List<(int cx, int cy)> Unmade(IEnumerable<(int i, int j)> terrains)
    {
        var world = ground?.World;
        if (world?.Source is not Chem.GeneratedSource source) return null;
        unmadeScratch.Clear();
        foreach (var (i, j) in terrains) CellChunksUnder(world, i, j, 1, unmadeScratch);
        var unmade = new List<(int cx, int cy)>();
        foreach (var (cx, cy) in unmadeScratch)
            if (world.IsPacked(cx, cy) && !source.IsMade(cx, cy)) unmade.Add((cx, cy));
        return unmade;
    }

    readonly HashSet<(int cx, int cy)> unmadeScratch = new();

    void MakeFirst(List<(int i, int j)> terrains)
    {
        if (Unmade(terrains) is { Count: > 0 } unmade) ground.World.MakeNow(unmade);
    }

    void Unpin()
    {
        if (pinned.Count == 0) return;
        pinned.Clear();
        HintDrawn(now: true);
    }

    public ValleyGround Ground => ground;

    public int Drawing { get; private set; }

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
        lazy = builder == null && Application.isPlaying && GeneratedMap.IsGenerated(ChemGrid.MapSource);
        chunks = lazy ? new Terrain[layout.ChunksX * layout.ChunksZ] : Place(transform, layout, builder, material);
        StartupTimeline.Mark("ground terrains placed");
        Debug.Log(lazy
            ? $"GROUND TERRAINS for the drawn tier: none of {chunks.Length} chunks made yet, they're made as they're wanted"
            : $"GROUND TERRAINS {(builder != null ? "built" : "waiting for the land")}: {chunks.Length} chunks in {timer.Elapsed.TotalMilliseconds:0} ms");
    }

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

    const int Margin = 1, KeepMargin = 2;
    const double MakeBudgetMs = 4.0;
    const float BusyFrameSeconds = 0.022f;
    const int MostWaits = 3;
    int waited;
    const int MostAtOnce = 8;

    public bool Lazy => lazy;

    public (int standing, int pooled, int created) Counts => lazy ? (made.Count, pool.Count, createdEver) : (chunks?.Length ?? 0, 0, chunks?.Length ?? 0);

    public double ArraysMs { get; private set; }

    public int Waiting => toMake.Count;

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

    public static int Sight()
    {
        var far = FarRing.Instance;
        int sight = far != null ? far.Dials.drawn : 0;
        return sight > 0 ? sight : ChemGrid.Current?.Rules?.Sim.Streaming.Sight ?? 4;
    }

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
        if (ViewWrap.EastWest) Replace();
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
        at = Fold(at);
        if (!focus.Contains(at)) focus.Add(at);
    }

    (int i, int j) Fold((int i, int j) at) => ViewWrap.FoldChunk(at, layout.ChunksX, layout.ChunksZ);

    Vector3 Corner((int i, int j) at)
    {
        var (cx, cz) = layout.ChunkCorner(at.i, at.j);
        float half = layout.ChunkSize * 0.5f;
        return new Vector3(ViewWrap.NearX(cx + half) - half, 0f, ViewWrap.NearZ(cz + half) - half);
    }

    void Replace()
    {
        foreach (int k in made)
        {
            var t = chunks[k];
            if (t == null) continue;
            var to = Corner((k % layout.ChunksX, k / layout.ChunksX));
            var p = t.transform.position;
            if (Mathf.Abs(p.x - to.x) > 1f || Mathf.Abs(p.z - to.z) > 1f) t.transform.position = new Vector3(to.x, p.y, to.z);
        }
    }

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
        at = Fold(at);
        if (layout.HasChunk(at.i, at.j) && chunks[at.j * layout.ChunksX + at.i] == null && ground.HasChunk(at)) wantedNow.Add(at);
    }

    int Distance((int i, int j) at)
    {
        if (activeNow.Contains(at)) return 0;
        int best = int.MaxValue;
        foreach (var (fi, fj) in focus)
            best = Mathf.Min(best, Mathf.Max(ViewWrap.ChunkDistance(at.i, fi, layout.ChunksX, true), ViewWrap.ChunkDistance(at.j, fj, layout.ChunksZ, false)));
        return best;
    }

    void MakeSome(bool whole)
    {
        using var marked = MakeMarker.Auto();
        TakeArrays();
        if (toMake.Count == 0) return;
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
            MakeFirst(toMake);
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
        if (toMake.Count > 0 && !whole) StartArrays(0);
        if (!any) return;
        nextReach = 0f;
    }

    void PrepareHeights()
    {
        var ready = new List<(int i, int j)>();
        for (int n = 0; n < toMake.Count && ready.Count < HeightsAFrame; n++)
        {
            if (!ground.Builder.Holds(toMake[n])) break;
            if (ready.Count > 0 && Mathf.Max(Mathf.Abs(toMake[n].i - ready[0].i), Mathf.Abs(toMake[n].j - ready[0].j)) > NearBox) continue;
            ready.Add(toMake[n]);
        }
        if (ready.Count < 2) return;
        MakeFirst(BoxOf(ready));
        ground.PrepareHeights(ready);
    }

    const int NearBox = 6;

    static List<(int i, int j)> BoxOf(List<(int i, int j)> terrains)
    {
        int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
        foreach (var (i, j) in terrains)
        {
            i0 = Mathf.Min(i0, i);
            j0 = Mathf.Min(j0, j);
            i1 = Mathf.Max(i1, i);
            j1 = Mathf.Max(j1, j);
        }
        if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > Chem.CellHeightfield.MostCachedChunks) return terrains;
        var box = new List<(int i, int j)>();
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++) box.Add((i, j));
        return box;
    }

    const int HeightsAFrame = 8;

    void StartArrays(int from)
    {
        if (arrays != null) return;
        var missing = ground.Builder.Missing(toMake.Skip(from).Take(MostAtOnce * 2));
        if (missing.Count > MostAtOnce) missing.RemoveRange(MostAtOnce, missing.Count - MostAtOnce);
        if (missing.Count == 0) return;
        if (Unmade(missing) is { Count: > 0 } unmade)
        {
            ground.World.MakeAhead(unmade);
            return;
        }
        var builder = ground.Builder;
        arraysFor = missing;
        Warm(missing);
        arrays = System.Threading.Tasks.Task.Run(() => builder.MakeArrays(missing));
    }

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
        t.transform.position = Corner(at);
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

    Terrain Standing((int i, int j) at)
    {
        at = Fold(at);
        return layout.HasChunk(at.i, at.j) && chunks[at.j * layout.ChunksX + at.i] is { } t && t.terrainData != null ? t : null;
    }

    Terrain Alike((int i, int j) at, bool high) => Standing(at) is { } t && layout.IsHighRes(at.i, at.j) == high ? t : null;

    public Terrain TerrainAt(Vector3 p)
    {
        if (chunks == null) return null;
        var (i, j) = Fold((Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize)));
        return layout.HasChunk(i, j) ? chunks[j * layout.ChunksX + i] : null;
    }

    public float? HeightAt(Vector3 p)
    {
        var t = TerrainAt(p);
        if (t != null && t.terrainData != null) return t.SampleHeight(p) + t.transform.position.y;
        int i = Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), j = Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize);
        if (ground == null || !layout.HasChunk(i, j)) return null;
        return ground.GroundAt(p.x, p.z);
    }

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
