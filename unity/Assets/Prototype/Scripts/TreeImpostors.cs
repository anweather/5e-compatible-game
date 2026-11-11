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
// with its changes laid over (#213), read from its save record, which reads it where it lies.
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
        Shadows = true;
        Skip = null;
        MapTerrains.SetTreeDistance(TerrainTreeDistance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-no-tree-impostors") On = false;
            else if (args[i] == "-tree-impostors")
            {
                On = true;
                if (i + 1 < args.Length && TryMetres(args[i + 1], out float near)) Near = near;
            }
            else if (args[i] == "-tree-impostors-far" && i + 1 < args.Length && TryMetres(args[i + 1], out float far)) Far = far;
            else if (args[i] == "-tree-impostors-no-shadows") Shadows = false;
        }
        if (Instance != null) return;
        var go = new GameObject("TreeImpostors");
        DontDestroyOnLoad(go);
        go.AddComponent<TreeImpostors>();
        Debug.Log(On ? $"TREE IMPOSTORS: on, prefabs within {Near} m, impostors to {ImpostorsTo()}, their shadows {ShadowsTo()}" : "TREE IMPOSTORS: off");
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
        float size = layout.ChunkSize, reach = FarNow + size;
        int i0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - reach - layout.OriginX) / size)), i1 = Mathf.Min(layout.ChunksX - 1, Mathf.FloorToInt((eye.x + reach - layout.OriginX) / size));
        int j0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - reach - layout.OriginZ) / size)), j1 = Mathf.Min(layout.ChunksZ - 1, Mathf.FloorToInt((eye.z + reach - layout.OriginZ) / size));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++) Visit(layout, chunks, i, j, eye);
        StandNear();
        Read(wanted, layout, chunks, eye, ReadsPerFrame);
        Refresh(chunks);
    }

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
    void Visit(TerrainGrid layout, Terrain[] chunks, int i, int j, Vector3 eye)
    {
        float size = layout.ChunkSize, keepSq = (Near + Keep) * (Near + Keep), farSq = FarNow * FarNow;
        int k = j * layout.ChunksX + i;
        var (cx, cz) = layout.ChunkCorner(i, j);
        float dx = Mathf.Max(cx - eye.x, 0f, eye.x - (cx + size)), dz = Mathf.Max(cz - eye.z, 0f, eye.z - (cz + size));
        float nearestSq = dx * dx + dz * dz;
        if (nearestSq > farSq) return;
        var t = chunks[k];
        bool hasTerrain = t != null && t.terrainData != null;
        bool known = blocks.TryGetValue(k, out var b);
        if (!known || b.Terrain != (hasTerrain ? t : null))
        {
            wanted.Add(k);
            if (!known) return;
        }
        else if (hasTerrain) stale.Add(k);
        if (!b.Any) return;
        bool reachesNear = nearestSq < keepSq;
        // A chunk reaching into Near sorts its trees even out of sight: its near trees stand as they are.
        if (!reachesNear && !GeometryUtility.TestPlanesAABB(planes, b.Bounds)) return;
        bool whole = !reachesNear && Far2(eye, cx, cz, size) <= farSq;
        bool shadows = Shadows && nearestSq < ShadowFar * ShadowFar;
        Draw(b, eye, whole, shadows, farSq);
    }

    // A few of the terrain chunks' trees read again, in turn.
    void Refresh(Terrain[] chunks)
    {
        if (stale.Count == 0) return;
        for (int n = 0; n < RefreshesPerFrame && n < stale.Count; n++)
        {
            int k = stale[(refreshAt + n) % stale.Count];
            blocks[k] = FromTerrain(chunks[k]);
        }
        refreshAt = (refreshAt + RefreshesPerFrame) % Math.Max(1, stale.Count);
    }

    // The squared distance from the eye to a chunk's farthest corner (in plan).
    static float Far2(Vector3 eye, float cx, float cz, float size)
    {
        float fx = Mathf.Max(Mathf.Abs(cx - eye.x), Mathf.Abs(cx + size - eye.x)), fz = Mathf.Max(Mathf.Abs(cz - eye.z), Mathf.Abs(cz + size - eye.z));
        return fx * fx + fz * fz;
    }

    void Draw(Block b, Vector3 eye, bool whole, bool shadows, float farSq)
    {
        for (int kind = 0; kind < b.ByKind.Length; kind++)
        {
            int count = b.Counts[kind];
            var mat = materials[kind];
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
        float nearSq = Near * Near, keepSq = (Near + Keep) * (Near + Keep);
        bool prefab = prefabs[kind] != null;
        int n = 0;
        for (int t = 0; t < count; t++)
        {
            var p = all[t].GetColumn(3);
            float dx = p.x - eye.x, dz = p.z - eye.z, d2 = dx * dx + dz * dz;
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
        var free = new List<Standing>();
        foreach (long key in leaving)
        {
            free.Add(standing[key]);
            standing.Remove(key);
        }
        bool all = standing.Count == 0;
        toStand.Sort((a, b) => a.d2.CompareTo(b.d2));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (key, kind, m, _) in toStand)
        {
            var go = Take(kind, free, all || watch.Elapsed.TotalMilliseconds <= SpawnBudgetMs);
            if (go == null) continue;
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
    }

    // A tree of a species for a near tree: one just let go, else one from the pool, else a new one if it may be made.
    GameObject Take(int kind, List<Standing> free, bool make)
    {
        int at = free.FindIndex(f => f.Kind == kind);
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
            if (t != null && t.terrainData != null) blocks[k] = FromTerrain(t);
            else
            {
                if (budget-- <= 0) continue;
                var at = (k % layout.ChunksX, k / layout.ChunksX);
                if (pinned != null && pinned.TryGetValue(at, out var own))
                {
                    blocks[k] = FromPinned(layout, own);
                    PinnedRead++;
                }
                else blocks[k] = FromCells(layout, at.Item1, at.Item2);
            }
            ChunksRead++;
        }
        ReadMs += watch.Elapsed.TotalMilliseconds;
    }

    static float Dist(TerrainGrid layout, int k, Vector3 eye)
    {
        var (cx, cz) = layout.ChunkCorner(k % layout.ChunksX, k / layout.ChunksX);
        float h = layout.ChunkSize * 0.5f, dx = cx + h - eye.x, dz = cz + h - eye.z;
        return dx * dx + dz * dz;
    }

    Block FromTerrain(Terrain t)
    {
        var data = t.terrainData;
        var protos = data.treePrototypes;
        var map = new int[protos.Length];
        for (int p = 0; p < protos.Length; p++) map[p] = protos[p].prefab != null ? Array.IndexOf(kinds, protos[p].prefab.name) : -1;
        var b = NewBlock();
        b.Terrain = t;
        var origin = t.transform.position;
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
            int idx = y * layout.GridW + x;
            var at = new Vector3(wx, land != null && idx < land.Length ? land[idx] * unit : 0f, wz);
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
            if (materials[k] != null) found++;
        }
        Debug.Log($"TREE IMPOSTORS: {found} of {protos.Length} species baked ({string.Join(", ", kinds)})");
        if (found > 0) return true;
        Debug.LogWarning("TREE IMPOSTORS: none baked (Prototype > Bake Tree Impostors); off");
        On = false;
        materials = null;
        return false;
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
        var seen = FromTerrain(t);
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
