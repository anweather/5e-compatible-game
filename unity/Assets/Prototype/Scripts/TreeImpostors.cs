using System;
using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The tree impostors' trial (docs/design/tree-impostors.md, J-090), off unless asked for (-tree-impostors [near metres],
// -tree-impostors-far <metres>, or TreeImpostors.On in the Editor). On, the chunk terrains draw their trees only within
// Near of the camera, as full meshes, and this draws every tree from Near out to Far as one quad, an octahedral impostor
// (Prototype/TreeImpostor, from the atlases Prototype > Bake Tree Impostor makes), instanced a chunk and a species a call.
//
// It's view only and decides nothing (D4). The trees come from the view: a chunk with a terrain made has its terrain's
// trees (the ones drawn there, burnt and felled ones gone); a chunk without one has a tree in each tree cell, placed as the
// ground would place it (GroundFromLand.Spot), standing on the map's land. Cells are read on the main thread, through the
// chunk accessors, and a packed chunk is never unpacked to read it (D17): its cells are made from the world's source, as
// it started.
public sealed class TreeImpostors : MonoBehaviour
{
    /// <summary>The Resources folder the baker writes the species' materials to (a material a species, named as its prefab).</summary>
    public const string ResourceFolder = "TreeImpostors";

    /// <summary>Whether the impostors draw (off by default).</summary>
    public static bool On { get; set; }
    /// <summary>Nearer than this (m) the terrain draws the full tree; from here out the impostor does.</summary>
    public static float Near { get; set; } = 60f;
    /// <summary>How far out the impostors draw (m).</summary>
    public static float Far { get; set; } = 500f;
    /// <summary>Trees this says yes to aren't drawn (a probe clears a view with it); null for none.</summary>
    public static Func<Vector3, bool> Skip { get; set; }
    /// <summary>Whether they cast shadows.</summary>
    public static bool Shadows { get; set; } = true;

    /// <summary>The trees drawn as impostors in the last frame, and the instanced calls that drew them.</summary>
    public static int Drawn { get; private set; }
    public static int Calls { get; private set; }
    /// <summary>The chunks' trees read so far, and what reading them cost (ms) all told.</summary>
    public static int ChunksRead { get; private set; }
    public static double ReadMs { get; private set; }

    public static TreeImpostors Instance { get; private set; }

    // Cell-made chunks read a frame, terrain chunks re-read a frame (a burnt tree drops out within a second or so).
    const int ReadsPerFrame = 4, RefreshesPerFrame = 2, Batch = 1023;
    const float TerrainTreeDistance = 900f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-tree-impostors")
            {
                On = true;
                if (i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float near)) Near = near;
            }
            else if (args[i] == "-tree-impostors-far" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float far)) Far = far;
            else if (args[i] == "-tree-impostors-no-shadows") Shadows = false;
        }
        if (Instance != null) return;
        var go = new GameObject("TreeImpostors");
        DontDestroyOnLoad(go);
        go.AddComponent<TreeImpostors>();
        if (On) Debug.Log($"TREE IMPOSTORS: on, from {Near} m to {Far} m, shadows {(Shadows ? "on" : "off")}");
    }

    // One terrain chunk's trees, by species, as instance matrices.
    sealed class Block
    {
        public Matrix4x4[][] ByKind;
        public int[] Counts;
        public Bounds Bounds;
        public Terrain Terrain; // the terrain they were read from; null when made from the cells
        public bool Any;
    }

    readonly Dictionary<int, Block> blocks = new();
    readonly List<int> wanted = new(), stale = new();
    readonly Plane[] planes = new Plane[6];
    Matrix4x4[] scratch = new Matrix4x4[Batch];
    Material[] materials; // by the template's tree kind
    string[] kinds;
    Mesh quad;
    World world;
    bool applied;
    int refreshAt;
    readonly Cell made = new();

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
        if (!On)
        {
            if (applied) MapTerrains.SetTreeDistance(TerrainTreeDistance);
            applied = false;
            return;
        }
        var terrains = MapTerrains.Instance;
        var w = ChemGrid.Current?.World;
        var layout = MapLand.Current?.Layout;
        var cam = Camera.main;
        if (terrains == null || w == null || layout == null || cam == null || terrains.template == null) return;
        if (w != world)
        {
            world = w;
            blocks.Clear();
        }
        if (materials == null && !Load(terrains.template)) return;
        if (!applied || !Mathf.Approximately(MapTerrains.TreeDistance, Near)) MapTerrains.SetTreeDistance(Near);
        applied = true;
        if (quad == null) quad = Quad();

        var eye = cam.transform.position;
        float size = layout.ChunkSize, reach = Far + size;
        int i0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - reach - layout.OriginX) / size)), i1 = Mathf.Min(layout.ChunksX - 1, Mathf.FloorToInt((eye.x + reach - layout.OriginX) / size));
        int j0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - reach - layout.OriginZ) / size)), j1 = Mathf.Min(layout.ChunksZ - 1, Mathf.FloorToInt((eye.z + reach - layout.OriginZ) / size));
        var chunks = terrains.Chunks;
        wanted.Clear();
        stale.Clear();
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        float nearSq = Near * Near, farSq = Far * Far;
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                int k = j * layout.ChunksX + i;
                var (cx, cz) = layout.ChunkCorner(i, j);
                float dx = Mathf.Max(cx - eye.x, 0f, eye.x - (cx + size)), dz = Mathf.Max(cz - eye.z, 0f, eye.z - (cz + size));
                if (dx * dx + dz * dz > farSq) continue;
                var t = chunks[k];
                bool hasTerrain = t != null && t.terrainData != null;
                if (!blocks.TryGetValue(k, out var b) || (hasTerrain ? b.Terrain != t : b.Terrain != null))
                {
                    wanted.Add(k);
                    if (b == null) continue;
                }
                else if (hasTerrain) stale.Add(k);
                if (!b.Any || !GeometryUtility.TestPlanesAABB(planes, b.Bounds)) continue;
                // The whole chunk past Near draws as it is; one that reaches nearer draws only its trees past Near.
                bool whole = dx * dx + dz * dz >= nearSq && Far >= Mathf.Sqrt(Far2(eye, cx, cz, size));
                Draw(b, eye, whole, nearSq, farSq);
            }
        }
        Read(wanted, layout, chunks, eye, ReadsPerFrame);
        if (stale.Count > 0)
        {
            for (int n = 0; n < RefreshesPerFrame && n < stale.Count; n++)
            {
                int k = stale[(refreshAt + n) % stale.Count];
                blocks[k] = FromTerrain(chunks[k]);
            }
            refreshAt = (refreshAt + RefreshesPerFrame) % Math.Max(1, stale.Count);
        }
    }

    // The squared distance from the eye to a chunk's farthest corner (in plan).
    static float Far2(Vector3 eye, float cx, float cz, float size)
    {
        float fx = Mathf.Max(Mathf.Abs(cx - eye.x), Mathf.Abs(cx + size - eye.x)), fz = Mathf.Max(Mathf.Abs(cz - eye.z), Mathf.Abs(cz + size - eye.z));
        return fx * fx + fz * fz;
    }

    void Draw(Block b, Vector3 eye, bool whole, float nearSq, float farSq)
    {
        for (int kind = 0; kind < b.ByKind.Length; kind++)
        {
            int count = b.Counts[kind];
            var mat = materials[kind];
            if (count == 0 || mat == null) continue;
            var rp = new RenderParams(mat)
            {
                worldBounds = b.Bounds,
                shadowCastingMode = Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
            };
            var all = b.ByKind[kind];
            if (whole)
            {
                for (int start = 0; start < count; start += Batch)
                {
                    Graphics.RenderMeshInstanced(rp, quad, 0, all, Math.Min(Batch, count - start), start);
                    Calls++;
                }
                Drawn += count;
                continue;
            }
            int n = 0;
            for (int t = 0; t < count; t++)
            {
                var p = all[t].GetColumn(3);
                float dx = p.x - eye.x, dz = p.z - eye.z, d2 = dx * dx + dz * dz;
                if (d2 < nearSq || d2 > farSq) continue;
                scratch[n++] = all[t];
                if (n < Batch) continue;
                Graphics.RenderMeshInstanced(rp, quad, 0, scratch, n);
                Calls++;
                Drawn += n;
                n = 0;
            }
            if (n == 0) continue;
            Graphics.RenderMeshInstanced(rp, quad, 0, scratch, n);
            Calls++;
            Drawn += n;
        }
    }

    // Reads the nearest of the wanted chunks, up to budget of those made from cells (a terrain's trees are cheap to read).
    void Read(List<int> list, TerrainGrid layout, Terrain[] chunks, Vector3 eye, int budget)
    {
        if (list.Count == 0) return;
        list.Sort((a, b) => Dist(layout, a, eye).CompareTo(Dist(layout, b, eye)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (int k in list)
        {
            var t = chunks[k];
            if (t != null && t.terrainData != null) blocks[k] = FromTerrain(t);
            else
            {
                if (budget-- <= 0) continue;
                blocks[k] = FromCells(layout, k % layout.ChunksX, k / layout.ChunksX);
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
        int lastCx = int.MinValue, lastCy = int.MinValue;
        bool packed = false;
        ReadOnlySpan<Cell> cells = default;
        foreach (var (x, y) in layout.CellsIn(i, j))
        {
            if (!world.InBounds(x, y)) continue;
            int cx = x >> Chunks.Shift, cy = y >> Chunks.Shift;
            if (cx != lastCx || cy != lastCy)
            {
                lastCx = cx;
                lastCy = cy;
                packed = world.Source != null && world.IsPacked(cx, cy);
                cells = packed ? default : world.ChunkCells(cx, cy);
            }
            Cell c;
            if (packed)
            {
                made.Reset();
                world.Source.Make(x, y, made);
                c = made;
            }
            else
            {
                if (cells.IsEmpty) continue;
                c = cells[((y & (Chunks.Size - 1)) << Chunks.Shift) | (x & (Chunks.Size - 1))];
            }
            if (c == null || c.T != TerrainIds.Tree) continue;
            var spot = GroundFromLand.Spot(seed, x, y, kinds.Length);
            var (u, v) = layout.WorldCell(x, y);
            float wx = layout.OriginX + (u + spot.Inset) * layout.CellSize, wz = layout.OriginZ + (v + spot.Across) * layout.CellSize;
            int idx = y * layout.GridW + x;
            var at = new Vector3(wx, land != null && idx < land.Length ? land[idx] * unit : 0f, wz);
            Add(b, spot.Kind, at, spot.Rotation, spot.Size, spot.Size);
        }
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
        int found = 0;
        for (int k = 0; k < protos.Length; k++)
        {
            kinds[k] = protos[k].prefab != null ? protos[k].prefab.name : "";
            materials[k] = Resources.Load<Material>($"{ResourceFolder}/{kinds[k]}");
            if (materials[k] != null) found++;
        }
        Debug.Log($"TREE IMPOSTORS: {found} of {protos.Length} species baked ({string.Join(", ", kinds)})");
        if (found > 0) return true;
        Debug.LogWarning("TREE IMPOSTORS: none baked (Prototype > Bake Tree Impostor); off");
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

    /// <summary>Forgets every chunk's trees, so they're read again (after a bake, or to time the reads).</summary>
    public void Forget()
    {
        blocks.Clear();
        materials = null;
        ChunksRead = 0;
        ReadMs = 0;
    }
}
