using System;
using System.Collections.Generic;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// The trees, every species, drawn by distance (docs/design/tree-impostors.md, J-090, J-091), on unless turned off
// (-no-tree-impostors, or TreeImpostors.On in the Editor). On, the chunk terrains draw no trees (a terrain's tree distance
// does nothing to LOD-grouped trees but at 0) and this draws them all: within Near of the camera each tree is its species'
// own prefab, a GameObject with its LOD group and SpeedTree's wind, from a pool the trees left behind go back to, nearest
// first and a few a frame; from Near out to Far one quad a tree, an octahedral impostor (Prototype/TreeImpostor, from the
// atlases Prototype > Bake Tree Impostors makes), instanced a chunk and a species a call, casting shadows only within
// ShadowFar (the drawn tier).
//
// It's view only and decides nothing (D4). The trees come from the view: a chunk with a terrain made has its terrain's
// trees (the ones drawn there, burnt and felled ones gone); a pinned map's chunk (the nine's valley) without one has its
// own ground's trees, but those whose cell has stopped being a tree; any other chunk has a tree in each tree cell, placed
// as the ground would place it (GroundFromLand.Spot), standing on the map's land. Cells are read on the main thread,
// through the chunk accessors, and a packed chunk is never unpacked to read it (D17): its cells are the world's source's
// with its changes laid over (#213), read from its save record, which reads it where it lies. Round a world that wraps
// (the hills' torus) the chunks run on over the seams to those they fold to, and each chunk's trees stand at the copy of
// its place nearest the camera (ViewWrap), moved there when that copy changes.
public sealed class TreeImpostors : MonoBehaviour
{
    /// <summary>The Resources folder the baker writes the species' materials to (a material a species, named as its prefab).</summary>
    public const string ResourceFolder = "TreeImpostors";

    /// <summary>Whether the impostors draw (on unless turned off).</summary>
    public static bool On { get; set; } = true;
    /// <summary>Nearer than this (m) a tree is its prefab, with wind; from here out the impostor.</summary>
    public static float Near { get; set; } = DefaultNear;
    /// <summary>How far out the impostors draw (m) on a map without the far ring's haze, or once set (-tree-impostors-far, a probe).</summary>
    public static float Far
    {
        get => farMetres;
        set
        {
            farMetres = value;
            farSet = true;
        }
    }
    // Unset, on a generated map the impostors draw out to where the haze hides them (#220).
    static float farMetres = 500f;
    static bool farSet;

    /// <summary>
    /// How near the prefabs draw and how far the impostors draw this frame (m): Near and FarNow, or nearer where the forest
    /// is dense (TreeBudget, from the trees round the camera last frame).
    /// </summary>
    public static float PrefabsWithin { get; private set; } = DefaultNear;
    public static float ImpostorsWithin { get; private set; } = 500f;
    /// <summary>The trees within Near (and Keep) and those within FarNow, every way, round the camera last frame (TreeBudget's counts).</summary>
    public static int NearAround { get; private set; }
    public static int AllAround { get; private set; }
    /// <summary>Whether the tree budget is kept (off with -no-tree-budget, to compare).</summary>
    public static bool Budget { get; set; } = true;
    /// <summary>The budget's counts (TreeBudget.MostNear, MostAround), or a run's (-tree-budget near around).</summary>
    public static int MostNear { get; set; } = TreeBudget.MostNear;
    public static int MostAround { get; set; } = TreeBudget.MostAround;
    /// <summary>The budget's floor for the impostors' reach (TreeBudget.FarFloor, or a run's -tree-budget-floor M).</summary>
    public static float FarFloor { get; set; } = TreeBudget.FarFloor;

    /// <summary>How far out the impostors draw now (m).</summary>
    public static float FarNow => !farSet && FarRing.Instance is { HazeMetres: > 0f } ring ? ring.HazeHides : Far;
    /// <summary>How far out the impostors cast shadows (m): the drawn tier (J-091).</summary>
    public static float ShadowFar { get; set; } = 256f;
    /// <summary>Trees this says yes to aren't drawn (a probe clears a view with it); null for none.</summary>
    public static Func<Vector3, bool> Skip { get; set; }
    /// <summary>Whether they cast shadows at all.</summary>
    public static bool Shadows { get; set; } = true;

    /// <summary>The trees drawn as impostors in the last frame, and the instanced calls that drew them.</summary>
    public static int Drawn { get; private set; }
    public static int Calls { get; private set; }
    /// <summary>The trees standing as their prefab now, and how many of those were ever made (the pool's size).</summary>
    public static int NearDrawn { get; private set; }
    /// <summary>The trees within the prefabs' distance still drawn as impostors, waiting for their prefab (a still waits them out).</summary>
    public static int NearWaiting { get; private set; }
    public static int NearMade { get; private set; }
    /// <summary>The chunks' trees read so far, and what reading them cost (ms) all told.</summary>
    public static int ChunksRead { get; private set; }
    public static double ReadMs { get; private set; }
    /// <summary>Of the chunks read, the packed ones with changes (their changes read from their save record), and the pinned ones read from their ground.</summary>
    public static int ChangedRead { get; private set; }
    public static int PinnedRead { get; private set; }

    public static TreeImpostors Instance { get; private set; }

    // Cell-made chunks read a frame, terrain chunks re-read a frame (a burnt tree drops out within a second or so).
    const int ReadsPerFrame = 4, RefreshesPerFrame = 2, Batch = 1023;
    const float TerrainTreeDistance = 900f;
    // Out to here a tree is its prefab (#220: at 60 m the swap showed in a sparse wood, where nothing in front hides it;
    // SpeedTree's own LODs are meshes to past 500 m for the grown trees, so 150 m is still a full tree, not its billboard).
    const float DefaultNear = 150f;
    // A near tree keeps its prefab out to Near + Keep, so one on the edge doesn't swap each step.
    const float Keep = 8f;
    // About this many ms a frame go to making near trees' prefabs (at least one a frame); all of them when none stands.
    const double SpawnBudgetMs = 1.0;

    // Play mode without a domain reload keeps statics: every play starts as made.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        On = true;
        Near = DefaultNear;
        farMetres = 500f;
        farSet = false;
        ShadowFar = 256f;
        FlatFrom = 100f;
        Shadows = true;
        Skip = null;
        Budget = true;
        MostNear = TreeBudget.MostNear;
        MostAround = TreeBudget.MostAround;
        FarFloor = TreeBudget.FarFloor;
        PrefabsWithin = DefaultNear;
        ImpostorsWithin = 500f;
        MapTerrains.SetTreeDistance(TerrainTreeDistance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        ReadArgs(Environment.GetCommandLineArgs());
        if (Instance != null) return;
        var go = new GameObject("TreeImpostors");
        DontDestroyOnLoad(go);
        go.AddComponent<TreeImpostors>();
        Debug.Log(On ? $"TREE IMPOSTORS: on, prefabs within {Near} m, impostors to {ImpostorsTo()}, their shadows {ShadowsTo()}, budget {BudgetSaid()}" : "TREE IMPOSTORS: off");
    }

    static string BudgetSaid() => Budget ? $"{MostNear} prefabs, {MostAround} trees round" : "off";

    // The command line's tree switches.
    static void ReadArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            float m = 0f;
            bool metres = i + 1 < args.Length && TryMetres(args[i + 1], out m);
            switch (args[i])
            {
                case "-no-tree-impostors": On = false; break;
                case "-tree-impostors":
                    On = true;
                    if (metres) Near = m;
                    break;
                case "-tree-impostors-no-shadows": Shadows = false; break;
                case "-no-tree-budget": Budget = false; break;
                case "-tree-budget": Counts(args, i); break;
                default:
                    if (metres) Metres(args[i], m);
                    break;
            }
        }
    }

    // A switch that takes metres: -tree-impostors-far, -tree-impostors-flat-from, -tree-budget-floor, -tree-impostors-shadow-far.
    static void Metres(string flag, float m)
    {
        if (flag == "-tree-impostors-far") Far = m;
        else if (flag == "-tree-impostors-flat-from") FlatFrom = m;
        else if (flag == "-tree-budget-floor") FarFloor = m;
        else if (flag == "-tree-impostors-shadow-far") ShadowFar = m;
    }

    // -tree-budget near around: the budget's counts.
    static void Counts(string[] args, int i)
    {
        if (i + 2 >= args.Length || !int.TryParse(args[i + 1], out int most) || !int.TryParse(args[i + 2], out int all)) return;
        MostNear = most;
        MostAround = all;
    }

    static string ImpostorsTo() => farSet ? $"{Far} m" : "the haze";

    static string ShadowsTo() => Shadows ? $"within {ShadowFar} m" : "off";

    static bool TryMetres(string s, out float m) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m);

    // One terrain chunk's trees, by species, as instance matrices.
    sealed class Block
    {
        public Matrix4x4[][] ByKind;
        public int[] Counts;
        public Bounds Bounds;
        // How far its trees stand from their chunk's own place (a whole number of the world's periods): on a world that
        // wraps, a chunk's trees are moved to the copy nearest the camera (ViewWrap) when it moves.
        public Vector2 Shift;
        public Terrain Terrain; // the terrain they were read from; null when made from the cells or the pinned ground
        public bool Any;
    }

    // A near tree standing as its prefab.
    sealed class Standing
    {
        public GameObject Tree;
        public int Kind;
        public int Seen;
    }

    readonly Dictionary<int, Block> blocks = new();
    readonly List<int> wanted = new(), stale = new();
    readonly Plane[] planes = new Plane[6];
    readonly Matrix4x4[] scratch = new Matrix4x4[Batch];
    Material[] materials; // by the template's tree kind
    Material[] flat; // the same, keeping the quad's own depth (_IMPOSTOR_FLAT): for chunks wholly past FlatFrom
    GameObject[] prefabs; // by kind
    string[] kinds;
    Mesh quad;
    World world;
    bool applied;
    int refreshAt, frame;
    readonly Cell made = new();

    // The near trees: those standing by where they stand, those wanted this frame and not standing yet, and the pool.
    readonly Dictionary<long, Standing> standing = new();
    readonly List<(long key, int kind, Matrix4x4 m, float d2)> toStand = new();
    readonly List<long> leaving = new();
    Stack<GameObject>[] pool;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (quad != null) Destroy(quad);
        DropFlat();
    }

    void Update()
    {
        Drawn = 0;
        Calls = 0;
        frame++;
        if (!On)
        {
            if (applied) MapTerrains.SetTreeDistance(TerrainTreeDistance);
            applied = false;
            if (standing.Count > 0) LetAllGo();
            return;
        }
        var terrains = MapTerrains.Instance;
        var layout = MapLand.Current?.Layout;
        var cam = Camera.main;
        if (!Ready(terrains, layout, cam)) return;
        var chunks = terrains.Chunks;
        // The terrains are made for the map the scene started on: a joiner that takes the host's map has the new map's
        // layout before its terrains, and the old map's chunks can't be read by the new layout's numbers.
        if (chunks.Length != layout.ChunksX * layout.ChunksZ) return;
        var eye = cam.transform.position;
        wanted.Clear();
        stale.Clear();
        toStand.Clear();
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        nearD2.Clear();
        around.Clear();
        float reach = FarNow + layout.ChunkSize;
        var (i0, i1) = Span(eye.x, reach, layout.OriginX, layout.ChunkSize, layout.ChunksX, ViewWrap.EastWest);
        var (j0, j1) = Span(eye.z, reach, layout.OriginZ, layout.ChunkSize, layout.ChunksZ, ViewWrap.NorthSouth);
        pending.Clear();
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++) Visit(layout, chunks, i, j, eye);
        // Nearest first, so the nearer trees' depth hides the ones behind them before those are shaded.
        pending.Sort(NearestFirst);
        foreach (var d in pending) Draw(d.b, eye, d.whole, d.shadows, d.flat, ImpostorsWithin * ImpostorsWithin);
        StandNear();
        // The next frame's distances from this frame's trees round the camera (counted out to Near and FarNow, so how far
        // they draw never feeds back into the counts).
        NearAround = nearD2.Count;
        AllAround = 0;
        foreach (var c in around) AllAround += c.trees;
        PrefabsWithin = Budget ? TreeBudget.Near(nearD2, Near, MostNear) : Near;
        ImpostorsWithin = Budget ? TreeBudget.Far(around, FarNow, MostAround, FarFloor) : FarNow;
        Read(wanted, layout, chunks, eye, ReadsPerFrame);
        Refresh(chunks);
        Forget(layout, eye, reach);
    }

    // The chunks along one axis within reach of the eye: round a world that wraps that way they run on over the seam (to
    // those they fold to, ViewWrap), else within the map.
    static (int lo, int hi) Span(float eye, float reach, float origin, float size, int chunks, bool wraps)
    {
        int lo = Mathf.FloorToInt((eye - reach - origin) / size), hi = Mathf.FloorToInt((eye + reach - origin) / size);
        return wraps ? (lo, hi) : (Mathf.Max(0, lo), Mathf.Min(chunks - 1, hi));
    }

    // The chunks' trees held past Far and LetGoPast chunks more are let go, every LetGoEvery frames: kept, every chunk
    // flown past stayed held (a crossing of the island held thousands, hundreds of MB). The margin is so turning back
    // reads none again at once; one coming back within Far is read again, nearest first, as at the start.
    void Forget(TerrainGrid layout, Vector3 eye, float reach)
    {
        if (frame % LetGoEvery != 0 || blocks.Count == 0) return;
        float size = layout.ChunkSize, past = reach + LetGoPast * size, pastSq = past * past;
        gone.Clear();
        foreach (int k in blocks.Keys)
        {
            var (cx, cz) = NearCorner(layout, k);
            float dx = Mathf.Max(cx - eye.x, 0f, eye.x - (cx + size)), dz = Mathf.Max(cz - eye.z, 0f, eye.z - (cz + size));
            if (dx * dx + dz * dz > pastSq) gone.Add(k);
        }
        foreach (int k in gone) blocks.Remove(k);
    }

    readonly List<int> gone = new();
    const int LetGoEvery = 30, LetGoPast = 2;
    // This frame's counts for the tree budget: the squared distance of each tree within Near and Keep, and each chunk's
    // nearest squared distance and trees within FarNow.
    readonly List<float> nearD2 = new();
    readonly List<(float d2, int trees)> around = new();

    // Whether there's a world, a map, a camera and a baked species to draw with; the terrains' trees off once there are.
    bool Ready(MapTerrains terrains, TerrainGrid layout, Camera cam)
    {
        var w = ChemGrid.Current?.World;
        if (terrains == null || w == null || layout == null || cam == null || terrains.template == null) return false;
        if (w != world)
        {
            world = w;
            blocks.Clear();
        }
        if (materials == null && !Load(terrains.template)) return false;
        if (!applied || MapTerrains.TreeDistance != 0f) MapTerrains.SetTreeDistance(0f);
        applied = true;
        if (quad == null) quad = Quad();
        return true;
    }

    // Chunk (i, j): its trees drawn if it's within Far and they've been read; asked to be read (or read again) if not.
    // Chunk k's corner at the copy nearest the camera (its own place on a world that doesn't wrap).
    static (float x, float z) NearCorner(TerrainGrid layout, int k)
    {
        var (cx, cz) = layout.ChunkCorner(k % layout.ChunksX, k / layout.ChunksX);
        float half = layout.ChunkSize * 0.5f;
        return (ViewWrap.NearX(cx + half) - half, ViewWrap.NearZ(cz + half) - half);
    }

    // A block's trees moved to stand at shift from their chunk's own place.
    static void MoveTo(Block b, Vector2 shift)
    {
        var d = shift - b.Shift;
        if (d == Vector2.zero) return;
        for (int kind = 0; kind < b.ByKind.Length; kind++)
        {
            var list = b.ByKind[kind];
            for (int t = 0; t < b.Counts[kind]; t++)
            {
                list[t].m03 += d.x;
                list[t].m23 += d.y;
            }
        }
        b.Bounds.center += new Vector3(d.x, 0f, d.y);
        b.Shift = shift;
    }

    // Chunk k's trees as read, asked to be read (or read again: its terrain came or went, or in turn); null if never read.
    Block Known(Terrain[] chunks, int k)
    {
        var t = chunks[k];
        bool hasTerrain = t != null && t.terrainData != null;
        bool known = blocks.TryGetValue(k, out var b);
        if (!known || b.Terrain != (hasTerrain ? t : null)) wanted.Add(k);
        else if (hasTerrain) stale.Add(k);
        return known ? b : null;
    }

    void Visit(TerrainGrid layout, Terrain[] chunks, int ui, int uj, Vector3 eye)
    {
        var (i, j) = ViewWrap.FoldChunk((ui, uj), layout.ChunksX, layout.ChunksZ);
        float size = layout.ChunkSize, keepSq = (Near + Keep) * (Near + Keep), farSq = FarNow * FarNow, drawSq = ImpostorsWithin * ImpostorsWithin;
        int k = j * layout.ChunksX + i;
        var (cx, cz) = layout.ChunkCorner(ui, uj);
        var (ox, oz) = layout.ChunkCorner(i, j);
        float dx = Mathf.Max(cx - eye.x, 0f, eye.x - (cx + size)), dz = Mathf.Max(cz - eye.z, 0f, eye.z - (cz + size));
        float nearestSq = dx * dx + dz * dz;
        if (nearestSq > farSq) return;
        var b = Known(chunks, k);
        if (b == null || !b.Any) return;
        MoveTo(b, new Vector2(cx - ox, cz - oz));
        int trees = 0;
        foreach (int n in b.Counts) trees += n;
        around.Add((nearestSq, trees));
        if (nearestSq > drawSq) return;
        bool reachesNear = nearestSq < keepSq;
        // A chunk reaching into Near sorts its trees even out of sight: its near trees stand as they are.
        if (!reachesNear && !GeometryUtility.TestPlanesAABB(planes, b.Bounds)) return;
        bool whole = !reachesNear && Far2(eye, cx, cz, size) <= drawSq;
        bool shadows = Shadows && nearestSq < ShadowFar * ShadowFar;
        pending.Add((nearestSq, b, whole, shadows, !reachesNear && nearestSq > FlatFrom * FlatFrom));
    }

    // This frame's chunks to draw: how near, its trees, whether whole (all within the impostors' reach, none near), with
    // shadows, and flat (wholly past FlatFrom).
    readonly List<(float d2, Block b, bool whole, bool shadows, bool flat)> pending = new();
    static readonly Comparison<(float d2, Block b, bool whole, bool shadows, bool flat)> NearestFirst = (a, c) => a.d2.CompareTo(c.d2);

    /// <summary>
    /// Past this (m) a chunk's impostors keep their quads' own depth (the flat material): far off the depth the impostor
    /// works out for each pixel changes nothing that shows, and writing it stopped the GPU testing depth before shading, so
    /// in a dense wood every tree behind another was shaded too (the hills' woods, 10 ms of a frame at a wood's heart).
    /// </summary>
    public static float FlatFrom { get; set; } = 100f;

    // The terrain chunks whose trees changed (ValleyGround.TreesChanged: a fire, a felling) read again, a few a frame; and
    // in turn, one every RefreshEvery frames, the rest (for a writer that doesn't say). Every chunk's twice a frame read
    // all its trees into new arrays: a fifth of the flyover's garbage.
    void Refresh(Terrain[] chunks)
    {
        int read = 0;
        while (read < RefreshesPerFrame && changed.Count > 0)
        {
            int k = changed[changed.Count - 1];
            changed.RemoveAt(changed.Count - 1);
            if (k < 0 || k >= chunks.Length || chunks[k] == null || chunks[k].terrainData == null || !blocks.TryGetValue(k, out var was)) continue;
            blocks[k] = FromTerrain(k, chunks[k], was);
            read++;
        }
        if (stale.Count == 0 || frame % RefreshEvery != 0) return;
        int s = stale[refreshAt % stale.Count];
        blocks[s] = FromTerrain(s, chunks[s], ReadBefore(s));
        refreshAt = (refreshAt + 1) % Math.Max(1, stale.Count);
    }

    readonly List<int> changed = new();
    const int RefreshEvery = 8;

    void OnEnable() => ValleyGround.TreesChanged += TreesChanged;

    void OnDisable() => ValleyGround.TreesChanged -= TreesChanged;

    void TreesChanged((int i, int j) at)
    {
        var layout = MapLand.Current?.Layout;
        if (layout == null) return;
        int k = at.j * layout.ChunksX + at.i;
        if (!changed.Contains(k)) changed.Add(k);
    }

    /// <summary>Every chunk's trees read again (a probe that changed the terrains' trees itself, or Skip).</summary>
    public void ReadAgain() => blocks.Clear();

    // The squared distance from the eye to a chunk's farthest corner (in plan).
    static float Far2(Vector3 eye, float cx, float cz, float size)
    {
        float fx = Mathf.Max(Mathf.Abs(cx - eye.x), Mathf.Abs(cx + size - eye.x)), fz = Mathf.Max(Mathf.Abs(cz - eye.z), Mathf.Abs(cz + size - eye.z));
        return fx * fx + fz * fz;
    }

    void Draw(Block b, Vector3 eye, bool whole, bool shadows, bool flatDepth, float farSq)
    {
        for (int kind = 0; kind < b.ByKind.Length; kind++)
        {
            int count = b.Counts[kind];
            var mat = flatDepth && flat[kind] != null ? flat[kind] : materials[kind];
            if (count == 0 || mat == null) continue;
            var rp = new RenderParams(mat)
            {
                worldBounds = b.Bounds,
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
            };
            if (whole)
                for (int start = 0; start < count; start += Batch) Instanced(rp, b.ByKind[kind], start, Math.Min(Batch, count - start));
            else Sorted(rp, kind, b.ByKind[kind], count, eye, farSq);
        }
    }

    // A chunk's trees of one species that reaches within Near: those nearer stand as their prefab (or wait to, drawn as
    // impostors meanwhile), the rest out to Far are drawn as impostors.
    void Sorted(RenderParams rp, int kind, Matrix4x4[] all, int count, Vector3 eye, float farSq)
    {
        float near = PrefabsWithin, nearSq = near * near, keepSq = (near + Keep) * (near + Keep), countSq = (Near + Keep) * (Near + Keep);
        bool prefab = prefabs[kind] != null;
        int n = 0;
        for (int t = 0; t < count; t++)
        {
            var p = all[t].GetColumn(3);
            float dx = p.x - eye.x, dz = p.z - eye.z, d2 = dx * dx + dz * dz;
            if (prefab && d2 < countSq) nearD2.Add(d2);
            if (d2 > farSq) continue;
            if (prefab && d2 < keepSq && Stands(kind, all[t], d2, nearSq)) continue;
            scratch[n++] = all[t];
            if (n < Batch) continue;
            Instanced(rp, scratch, 0, n);
            n = 0;
        }
        if (n > 0) Instanced(rp, scratch, 0, n);
    }

    // A tree within Near (and Keep): true if it stands as its prefab now; if not, and it's within Near, it's wanted to.
    bool Stands(int kind, Matrix4x4 m, float d2, float nearSq)
    {
        long key = Key(m.GetColumn(3));
        if (standing.TryGetValue(key, out var s) && s.Kind == kind)
        {
            s.Seen = frame;
            return true;
        }
        if (d2 < nearSq) toStand.Add((key, kind, m, d2));
        return false;
    }

    // A tree by where it stands (to the centimetre).
    static long Key(Vector4 p) => ((long)Mathf.RoundToInt(p.x * 100f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 100f);

    void Instanced(RenderParams rp, Matrix4x4[] list, int start, int count)
    {
        Graphics.RenderMeshInstanced(rp, quad, 0, list, count, start);
        Calls++;
        Drawn += count;
    }

    // The near trees: those not seen this frame go back to the pool (or straight to a tree that wants one), and the wanted
    // ones stand, nearest first, as many as the frame's budget makes (all when none stands, as play starts).
    void StandNear()
    {
        leaving.Clear();
        foreach (var (key, s) in standing)
            if (s.Seen != frame) leaving.Add(key);
        var free = freed;
        free.Clear();
        foreach (long key in leaving)
        {
            free.Add(standing[key]);
            standing.Remove(key);
        }
        bool all = standing.Count == 0;
        toStand.Sort(ByDistance);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int waiting = 0;
        foreach (var (key, kind, m, _) in toStand)
        {
            var go = Take(kind, free, all || watch.Elapsed.TotalMilliseconds <= SpawnBudgetMs);
            if (go == null)
            {
                waiting++;
                continue;
            }
            go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            go.transform.localScale = m.lossyScale;
            standing[key] = new Standing { Tree = go, Kind = kind, Seen = frame };
        }
        foreach (var f in free)
        {
            f.Tree.SetActive(false);
            pool[f.Kind].Push(f.Tree);
        }
        NearDrawn = standing.Count;
        NearWaiting = waiting;
    }

    readonly List<Standing> freed = new();
    static readonly Comparison<(long key, int kind, Matrix4x4 m, float d2)> ByDistance = (a, b) => a.d2.CompareTo(b.d2);

    // A tree of a species for a near tree: one just let go, else one from the pool, else a new one if it may be made.
    GameObject Take(int kind, List<Standing> free, bool make)
    {
        int at = -1;
        for (int n = 0; n < free.Count && at < 0; n++)
            if (free[n].Kind == kind) at = n;
        if (at >= 0)
        {
            var go = free[at].Tree;
            free.RemoveAt(at);
            return go;
        }
        if (pool[kind].Count > 0)
        {
            var go = pool[kind].Pop();
            go.SetActive(true);
            return go;
        }
        return make ? Make(kind) : null;
    }

    // A species' prefab, made for the pool: its LOD group and wind as the terrain drew it, without its colliders (the
    // terrain's tree colliders stand there already).
    GameObject Make(int kind)
    {
        var go = Instantiate(prefabs[kind], transform);
        go.name = kinds[kind];
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        NearMade++;
        return go;
    }

    void LetAllGo()
    {
        foreach (var s in standing.Values)
        {
            s.Tree.SetActive(false);
            pool[s.Kind].Push(s.Tree);
        }
        standing.Clear();
        NearDrawn = 0;
    }

    // Reads the nearest of the wanted chunks, up to budget of those made from cells (a terrain's trees are cheap to read).
    void Read(List<int> list, TerrainGrid layout, Terrain[] chunks, Vector3 eye, int budget)
    {
        if (list.Count == 0) return;
        list.Sort((a, b) => Dist(layout, a, eye).CompareTo(Dist(layout, b, eye)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var pinned = MapTerrains.Instance.Ground?.Builder?.Pinned;
        foreach (int k in list)
        {
            var t = chunks[k];
            if (t != null && t.terrainData != null) blocks[k] = FromTerrain(k, t, ReadBefore(k));
            else
            {
                if (budget-- <= 0) continue;
                var at = (k % layout.ChunksX, k / layout.ChunksX);
                if (pinned != null && pinned.TryGetValue(at, out var own))
                {
                    blocks[k] = FromPinned(layout, own);
                    PinnedRead++;
                }
                else if (NotMadeYet(layout, at.Item1, at.Item2)) continue;
                else blocks[k] = FromCells(layout, at.Item1, at.Item2);
            }
            ChunksRead++;
        }
        ReadMs += watch.Elapsed.TotalMilliseconds;
    }

    static float Dist(TerrainGrid layout, int k, Vector3 eye)
    {
        var (cx, cz) = NearCorner(layout, k);
        float h = layout.ChunkSize * 0.5f, dx = cx + h - eye.x, dz = cz + h - eye.z;
        return dx * dx + dz * dz;
    }

    // A terrain's trees, into <paramref name="reuse"/>'s arrays when there's one (read again: no new garbage).
    Block FromTerrain(int k, Terrain t, Block reuse = null)
    {
        var data = t.terrainData;
        var map = KindsOf(data.treePrototypes);
        var b = reuse != null && reuse.ByKind.Length == kinds.Length ? Emptied(reuse) : NewBlock();
        b.Terrain = t;
        var origin = t.transform.position;
        // The terrain stands at a copy of its chunk's place (MapTerrains.Corner): its trees are read there.
        var layout = MapLand.Current?.Layout;
        if (k >= 0 && layout != null)
        {
            var (ox, oz) = layout.ChunkCorner(k % layout.ChunksX, k / layout.ChunksX);
            b.Shift = new Vector2(origin.x - ox, origin.z - oz);
        }
        else b.Shift = Vector2.zero;
        var scale = data.size;
        int n = data.treeInstanceCount;
        for (int i = 0; i < n; i++)
        {
            var tree = data.GetTreeInstance(i);
            int kind = tree.prototypeIndex >= 0 && tree.prototypeIndex < map.Length ? map[tree.prototypeIndex] : -1;
            if (kind < 0 || tree.widthScale <= 0f || tree.heightScale <= 0f) continue;
            Add(b, kind, origin + Vector3.Scale(tree.position, scale), tree.rotation, tree.widthScale, tree.heightScale);
        }
        return b;
    }

    // On a map made from its source (the Wide World), whether the chunk under a terrain square isn't made yet: reading its
    // cells and land here would make it on this thread (milliseconds a chunk), so it's asked for on the worker and read once
    // it's made.
    bool NotMadeYet(TerrainGrid layout, int i, int j)
    {
        if (world.Source is not GeneratedSource source) return false;
        var (x0, z0) = layout.ChunkCorner(i, j);
        var (x, y) = layout.CellAt(x0 + layout.ChunkSize * 0.5f, z0 + layout.ChunkSize * 0.5f);
        if (!world.InBounds(x, y)) return false;
        int cx = Chunks.ChunkOf(x), cy = Chunks.ChunkOf(y);
        if (source.IsMade(cx, cy)) return false;
        source.Ahead(new[] { (cx, cy) });
        return true;
    }

    Block FromCells(TerrainGrid layout, int i, int j)
    {
        var b = NewBlock();
        int seed = unchecked((int)world.Seed);
        // The ground from the map's land, not the drawn ground: a chunk with no terrain has its ground made from its cells,
        // which would unpack it (and those round it).
        var land = MapLand.Current?.Land;
        float unit = ChemGrid.Current.Grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        var cells = new CellsNow(this);
        foreach (var (x, y) in layout.CellsIn(i, j))
        {
            if (!world.InBounds(x, y) || cells.Terrain(x, y) != TerrainIds.Tree) continue;
            var spot = GroundFromLand.Spot(seed, x, y, kinds.Length);
            var (u, v) = layout.WorldCell(x, y);
            float wx = layout.OriginX + (u + spot.Inset) * layout.CellSize, wz = layout.OriginZ + (v + spot.Across) * layout.CellSize;
            var at = new Vector3(wx, land != null && x < land.W && y < land.H ? GroundFromLand.LandUnits(land, x, y) * unit : 0f, wz);
            Add(b, spot.Kind, at, spot.Rotation, spot.Size, spot.Size);
        }
        return b;
    }

    // A pinned chunk with no terrain made: its own ground's trees, as its terrain will draw them (#213), standing on its
    // ground; but a tree whose cell was a tree as the world started and isn't now (burnt, felled) is gone, as the terrain
    // would have it.
    Block FromPinned(TerrainGrid layout, GroundChunk chunk)
    {
        var b = NewBlock();
        var builder = MapTerrains.Instance.Ground.Builder;
        var protos = builder.TreePrototypes;
        float bottom = builder.Bottom, span = builder.Top - builder.Bottom, size = layout.ChunkSize;
        var (cx, cz) = layout.ChunkCorner(chunk.I, chunk.J);
        var cells = new CellsNow(this);
        int res = chunk.Resolution;
        foreach (var t in chunk.Trees)
        {
            if (t.Prototype < 0 || t.Prototype >= protos.Length || protos[t.Prototype].prefab == null) continue;
            int kind = Array.IndexOf(kinds, protos[t.Prototype].prefab.name);
            float wx = cx + t.X * size, wz = cz + t.Z * size;
            var (x, y) = layout.CellAt(wx, wz);
            if (world.InBounds(x, y) && cells.Terrain(x, y) != TerrainIds.Tree && cells.StartTerrain(x, y) == TerrainIds.Tree) continue;
            int sx = Mathf.Clamp(Mathf.RoundToInt(t.X * (res - 1)), 0, res - 1), sz = Mathf.Clamp(Mathf.RoundToInt(t.Z * (res - 1)), 0, res - 1);
            float wy = bottom + span * chunk.Heights[sz * res + sx] / (float)GroundPack.HeightSteps;
            Add(b, kind, new Vector3(wx, wy, wz), t.Rotation, t.Width, t.Height);
        }
        return b;
    }

    // The cells' terrain now, a chunk at a time, read where it lies: an unpacked chunk's own cells, a packed one's from the
    // world's source with its changes laid over (from its save record, which reads a packed chunk without unpacking it).
    struct CellsNow
    {
        readonly TreeImpostors owner;
        int cx, cy;
        bool packed;
        Dictionary<int, string> changes;

        public CellsNow(TreeImpostors owner)
        {
            this.owner = owner;
            cx = cy = int.MinValue;
            packed = false;
            changes = null;
        }

        public string Terrain(int x, int y)
        {
            var w = owner.world;
            int ncx = x >> Chunks.Shift, ncy = y >> Chunks.Shift;
            if (ncx != cx || ncy != cy)
            {
                cx = ncx;
                cy = ncy;
                packed = w.Source != null && w.IsPacked(cx, cy);
                changes = packed ? ChangesOf(w, cx, cy) : null;
            }
            int local = ((y & (Chunks.Size - 1)) << Chunks.Shift) | (x & (Chunks.Size - 1));
            if (!packed)
            {
                var cells = w.ChunkCells(cx, cy);
                return cells.IsEmpty ? null : cells[local]?.T;
            }
            if (changes != null && changes.TryGetValue(local, out var t)) return t;
            return StartTerrain(x, y);
        }

        // The terrain the world's source makes at a cell: how it started.
        public string StartTerrain(int x, int y)
        {
            var w = owner.world;
            if (w.Source == null) return null;
            owner.made.Reset();
            w.Source.Make(x, y, owner.made);
            return owner.made.T;
        }

        // A packed chunk's changed cells' terrains by local index; null when it has none.
        static Dictionary<int, string> ChangesOf(World w, int cx, int cy)
        {
            var p = w.PackedOf(cx, cy);
            if (p == null || p.Count == 0) return null;
            ChangedRead++;
            var changes = new Dictionary<int, string>(p.Count);
            if (w.SaveChunkOf(cx, cy, w.Source)["cells"] is JArray cells)
                foreach (var c in cells) changes[(int)c[0]] = (string)c[1];
            return changes;
        }
    }

    int[] protoMap = Array.Empty<int>();

    // Chunk k's block as read last (its arrays to read into again), or null.
    Block ReadBefore(int k) => blocks.TryGetValue(k, out var b) ? b : null;

    // A terrain's tree prototypes as indices into kinds (-1: none), in a kept array.
    int[] KindsOf(TreePrototype[] protos)
    {
        if (protoMap.Length != protos.Length) protoMap = new int[protos.Length];
        for (int p = 0; p < protos.Length; p++) protoMap[p] = protos[p].prefab != null ? Array.IndexOf(kinds, protos[p].prefab.name) : -1;
        return protoMap;
    }

    static Block Emptied(Block b)
    {
        Array.Clear(b.Counts, 0, b.Counts.Length);
        b.Any = false;
        b.Bounds = default;
        return b;
    }

    Block NewBlock()
    {
        var b = new Block { ByKind = new Matrix4x4[kinds.Length][], Counts = new int[kinds.Length] };
        for (int k = 0; k < kinds.Length; k++) b.ByKind[k] = Array.Empty<Matrix4x4>();
        return b;
    }

    void Add(Block b, int kind, Vector3 at, float rotation, float width, float height)
    {
        if (kind < 0 || kind >= kinds.Length || materials[kind] == null || (Skip != null && Skip(at))) return;
        ref var list = ref b.ByKind[kind];
        int n = b.Counts[kind];
        if (n == list.Length) Array.Resize(ref list, Math.Max(16, n * 2));
        list[n] = Matrix4x4.TRS(at, Quaternion.Euler(0f, rotation * Mathf.Rad2Deg, 0f), new Vector3(width, height, width));
        b.Counts[kind] = n + 1;
        var mat = materials[kind];
        float r = mat.GetFloat(RadiusId) * Mathf.Max(width, height) * 1.1f;
        var c = at + new Vector3(0f, mat.GetVector(CentreId).y * height, 0f);
        var box = new Bounds(c, Vector3.one * (2f * r));
        if (!b.Any) b.Bounds = box;
        else b.Bounds.Encapsulate(box);
        b.Any = true;
    }

    static readonly int RadiusId = Shader.PropertyToID("_Radius"), CentreId = Shader.PropertyToID("_Centre");

    // The species' materials by the template's tree kinds; false (and off) when none was baked.
    bool Load(TerrainData template)
    {
        var protos = template.treePrototypes;
        kinds = new string[protos.Length];
        materials = new Material[protos.Length];
        DropFlat();
        flat = new Material[protos.Length];
        prefabs = new GameObject[protos.Length];
        if (pool == null || pool.Length != protos.Length)
        {
            pool = new Stack<GameObject>[protos.Length];
            for (int k = 0; k < protos.Length; k++) pool[k] = new Stack<GameObject>();
        }
        int found = 0;
        for (int k = 0; k < protos.Length; k++)
        {
            kinds[k] = protos[k].prefab != null ? protos[k].prefab.name : "";
            materials[k] = Resources.Load<Material>($"{ResourceFolder}/{kinds[k]}");
            prefabs[k] = protos[k].prefab;
            if (materials[k] == null) continue;
            found++;
            flat[k] = new Material(materials[k]) { name = materials[k].name + " (flat)" };
            flat[k].EnableKeyword("_IMPOSTOR_FLAT");
        }
        Debug.Log($"TREE IMPOSTORS: {found} of {protos.Length} species baked ({string.Join(", ", kinds)})");
        if (found > 0) return true;
        Debug.LogWarning("TREE IMPOSTORS: none baked (Prototype > Bake Tree Impostors); off");
        On = false;
        materials = null;
        return false;
    }

    void DropFlat()
    {
        if (flat == null) return;
        foreach (var m in flat)
            if (m != null) Destroy(m);
        flat = null;
    }

    // A quad of corners (-1, -1) to (1, 1), its bounds left wide: the shader stands it up and sizes it.
    static Mesh Quad()
    {
        var m = new Mesh { name = "Tree impostor" };
        m.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
        m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        m.bounds = new Bounds(new Vector3(0, 15, 0), new Vector3(40, 40, 40));
        return m;
    }

    /// <summary>The trees read so far by species name (every chunk read, near or far), for a count.</summary>
    public Dictionary<string, int> Totals()
    {
        var totals = new Dictionary<string, int>();
        if (kinds == null) return totals;
        foreach (var b in blocks.Values)
            for (int k = 0; k < kinds.Length; k++)
                totals[kinds[k]] = (totals.TryGetValue(kinds[k], out int n) ? n : 0) + b.Counts[k];
        return totals;
    }

    /// <summary>How many chunks' trees are held now.</summary>
    public int Held => blocks.Count;

    /// <summary>Whether a tree stands within a metre of a point in the trees held now (a probe's check), and as which species.</summary>
    public bool TreeNear(Vector3 p, float metres, out string kind)
    {
        kind = null;
        if (kinds == null) return false;
        float m2 = metres * metres;
        foreach (var b in blocks.Values)
            for (int k = 0; k < kinds.Length; k++)
                for (int t = 0; t < b.Counts[k]; t++)
                {
                    var q = b.ByKind[k][t].GetColumn(3);
                    float dx = q.x - p.x, dz = q.z - p.z;
                    if (dx * dx + dz * dz > m2) continue;
                    kind = kinds[k];
                    return true;
                }
        return false;
    }

    /// <summary>A tree held now between min and max metres (in plan) from a point, in a chunk with no terrain made (a probe's far tree); null if none.</summary>
    public Vector3? FarTree(Vector3 from, float min, float max)
    {
        if (kinds == null) return null;
        var chunks = MapTerrains.Instance.Chunks;
        foreach (var (k, b) in blocks)
        {
            if (b.Terrain != null || (chunks[k] != null && chunks[k].terrainData != null)) continue;
            for (int kind = 0; kind < kinds.Length; kind++)
                for (int t = 0; t < b.Counts[kind]; t++)
                {
                    Vector3 p = b.ByKind[kind][t].GetColumn(3);
                    float d = new Vector2(p.x - from.x, p.z - from.z).magnitude;
                    if (d >= min && d <= max) return p;
                }
        }
        return null;
    }

    /// <summary>
    /// For a probe (#213): a pinned chunk with its terrain made, its trees as the pinned ground has them (what it draws far
    /// off, before its terrain is made) against the terrain's: how many the terrain has, and how many of those the pinned
    /// read has within 5 cm, of the same species. False for any other chunk.
    /// </summary>
    public bool PinnedAgainstTerrain(int k, out int terrainTrees, out int matched)
    {
        terrainTrees = matched = 0;
        var layout = MapLand.Current?.Layout;
        var terrains = MapTerrains.Instance;
        var pinned = terrains != null ? terrains.Ground?.Builder?.Pinned : null;
        if (kinds == null || layout == null || pinned == null || world == null) return false;
        var t = MapTerrains.Instance.Chunks[k];
        if (t == null || t.terrainData == null || !pinned.TryGetValue((k % layout.ChunksX, k / layout.ChunksX), out var own)) return false;
        var seen = FromTerrain(k, t);
        var far = FromPinned(layout, own);
        for (int kind = 0; kind < kinds.Length; kind++)
        {
            for (int n = 0; n < seen.Counts[kind]; n++)
            {
                terrainTrees++;
                Vector3 p = seen.ByKind[kind][n].GetColumn(3);
                if (Within(far, kind, p, 0.05f)) matched++;
            }
        }
        return true;
    }

    static bool Within(Block b, int kind, Vector3 p, float metres)
    {
        for (int n = 0; n < b.Counts[kind]; n++)
        {
            Vector3 q = b.ByKind[kind][n].GetColumn(3);
            if ((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) <= metres * metres) return true;
        }
        return false;
    }

    /// <summary>Forgets every chunk's trees, so they're read again (after a bake, or to time the reads).</summary>
    public void Forget()
    {
        blocks.Clear();
        materials = null;
        if (standing.Count > 0 && pool != null) LetAllGo();
        ChunksRead = 0;
        ChangedRead = 0;
        PinnedRead = 0;
        ReadMs = 0;
    }
}
