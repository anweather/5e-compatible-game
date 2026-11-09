using System.Collections.Generic;
using System.Threading.Tasks;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The far ring on a generated map (docs/design/world-squares.md section 4, the view half): past the generated land the
// known chunks (Chem.KnownRing) are drawn as coarse meshes from their 9 × 9 heights, painted by dominant biome, with no
// colliders; the chunk terrains draw only where no known mesh does, within the known ring (MapTerrains asks
// TerrainDraws), so nothing is drawn twice; the haze is exponential-squared, about 98% at the known ring's edge (k 1.98
// over know × 96 m, after Punchy), so the ring's edge never shows; a sea plane runs from the map's edge to the horizon,
// so the sky never shows below it; and the plan's far landmarks (FarRing.Landmarks.cs) stand past the known ring with
// their own haze. The meshes are worked out on a worker and only handed to Unity here.
//
// All view and all float: the ring and the landmarks are pure functions of the seed and the plan, and nothing shared
// reads anything here (D4). Starts itself on a generated map (PinnedValley.InGenerated); the valley alone keeps its
// skyline and its linear haze.
public sealed partial class FarRing : MonoBehaviour
{
    /// <summary>Punchy's haze constant: exponential-squared density × draw distance, ~98% at the edge.</summary>
    public const float HazeK = 1.98f;

    /// <summary>The far ring in play, or null on a map that isn't generated.</summary>
    public static FarRing Instance { get; private set; }

    /// <summary>Where the haze reaches ~98% in clear weather (m): the known ring's edge. SkyLight closes it in for rain and fog.</summary>
    public float HazeMetres { get; private set; }

    /// <summary>The last known-chunk build: how many chunks, the worker's milliseconds and the main thread's (meshes to Unity).</summary>
    public static (int chunks, double workerMs, double mainMs) LastBuild { get; private set; }

    // What the worker reads at the start: the map, its far landmarks and the ground at each.
    sealed class Loaded
    {
        public GeneratedMap Map;
        public FarLandmarks Far;
        public float[] Ground;
    }

    Task<Loaded> loading;
    GeneratedMap map;
    KnownRing ring;
    MapLand land;
    MapTerrains terrains;
    Material landMaterial, landmarkMaterial;
    int chunksW, chunksH, sight, know;
    float chunkMetres;

    // The known chunks drawn now, by ring id, with the chunk each was built from (the ring keeps a chunk's object while
    // it's known, so the same object means the same land).
    readonly Dictionary<int, (KnownChunk chunk, GameObject go)> drawn = new();
    Task<List<(KnownChunk chunk, FarMeshes.Data data)>> building;
    double buildStarted;
    int builtVersion = -1;
    readonly List<(int x, int y)> characters = new();
    readonly List<(int x, int y)> wanted = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        LastBuild = default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Begin();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Begin();

    static void Begin()
    {
        if (Instance != null || ChemGrid.Current?.Grid == null || !PinnedValley.InGenerated) return;
        new GameObject("Far ring").AddComponent<FarRing>();
    }

    void Awake()
    {
        Instance = this;
        land = MapLand.Current;
        string source = ChemGrid.MapSource;
        float water = land != null ? land.WaterLevel : 0f;
        // The plan and the generator, the landmarks and the ground they stand on, all off the main thread.
        float cell = land != null ? land.Layout.CellSize : 1.5f;
        loading = Task.Run(() => Load(source, water, cell));
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static Loaded Load(string source, float water, float cell)
    {
        var m = GeneratedMap.Load(source);
        var far = m.Far;
        var gen = m.Gen;
        var ground = new float[far.All.Count];
        var near = new Dictionary<int, KnownChunk>();
        for (int n = 0; n < far.All.Count; n++)
        {
            var l = far.All[n];
            ground[n] = Ground(gen, l, water, near, cell);
        }
        return new Loaded { Map = m, Far = far, Ground = ground };
    }

    // The ground a far landmark stands on (m): the lowest dry known sample within its radius (at least a sample's spacing)
    // of its middle, so a stamp's own rise (a volcano's cone) isn't counted twice and a tree on a hill stands on the hill;
    // just under the water's top where there's none.
    static float Ground(ChunkGen gen, FarLandmark l, float water, Dictionary<int, KnownChunk> near, float cellMetres)
    {
        int cx = l.X / ChunkGen.Size, cy = l.Y / ChunkGen.Size;
        long reach = Mathf.Max(KnownChunk.Step + 1, Mathf.CeilToInt(l.Radius / cellMetres));
        int low = int.MaxValue;
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
            {
                if (x < 0 || y < 0 || x >= gen.ChunksW || y >= gen.ChunksH) continue;
                int id = y * gen.ChunksW + x;
                if (!near.TryGetValue(id, out var k)) near[id] = k = gen.Know(x, y);
                for (int q = 0; q < k.Heights.Length; q++)
                {
                    long dx = x * ChunkGen.Size + q % KnownChunk.Side * KnownChunk.Step - l.X, dy = y * ChunkGen.Size + q / KnownChunk.Side * KnownChunk.Step - l.Y;
                    if (k.Water[q] == KnownChunk.Dry && dx * dx + dy * dy <= reach * reach) low = Mathf.Min(low, k.Heights[q]);
                }
            }
        float metres = low == int.MaxValue ? water : low * (float)MapFile.LandUnitMetres;
        return Mathf.Max(metres, water - 1f);
    }

    void Update()
    {
        if (map == null && !Ready()) return;
        float clear = HazeK / Mathf.Max(1f, HazeMetres);
        Shader.SetGlobalFloat(HazeDensityId, RenderSettings.fogMode == FogMode.ExponentialSquared ? RenderSettings.fogDensity : clear);
        Shader.SetGlobalFloat(HazeClearId, clear);
        Shader.SetGlobalFloat(SightMetresId, sight * chunkMetres);
        var horizon = RenderSettings.fogColor;
        var zenith = Shader.GetGlobalColor(PunchyZenithId);
        Shader.SetGlobalColor(SkyHorizonId, horizon);
        Shader.SetGlobalColor(SkyZenithId, zenith.maxColorComponent > 0f ? zenith : horizon);

        if (FollowCharacters()) ring.Want(characters);
        ring.Handover();
        if (building == null && ring.Version != builtVersion) StartBuild();
        if (building != null && building.IsCompleted) FinishBuild();
        UpdateLandmarks();
    }

    // The first frame the worker's load is done: the ring, the haze, the materials, the sea plane and the landmarks.
    bool Ready()
    {
        var rules = ChemGrid.Current?.Rules;
        if (!loading.IsCompleted || rules == null || land == null) return false;
        if (loading.IsFaulted)
        {
            Debug.LogError($"FAR RING: the map can't be read for the far ring: {loading.Exception?.GetBaseException().Message}");
            enabled = false;
            return false;
        }
        var loaded = loading.Result;
        map = loaded.Map;
        chunksW = map.Gen.ChunksW;
        chunksH = map.Gen.ChunksH;
        sight = rules.Sim.Streaming.Sight;
        know = rules.Sim.Streaming.Know;
        chunkMetres = land.Layout.ChunkSize;
        ring = map.KnownRing(rules.Sim.Streaming);
        terrains = FindFirstObjectByType<MapTerrains>();

        HazeMetres = know * chunkMetres;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = HazeK / HazeMetres;

        var grid = ChemGrid.Current.Grid;
        var shader = grid.farShader != null ? grid.farShader : Shader.Find("Prototype/FarLand");
        landMaterial = new Material(shader) { name = "Far land" };
        landmarkMaterial = new Material(shader) { name = "Far landmarks" };
        landmarkMaterial.SetFloat(OwnHazeId, 1f);
        MakeSea();
        MakeLandmarks(loaded);
        Debug.Log($"FAR RING: know {know} (haze 98% at {HazeMetres:0} m, density {RenderSettings.fogDensity:0.00000}/m), {loaded.Far.All.Count} far landmarks");
        return true;
    }

    static readonly int HazeDensityId = Shader.PropertyToID("_FarHazeDensity"), HazeClearId = Shader.PropertyToID("_FarHazeClear"), SightMetresId = Shader.PropertyToID("_FarSightMetres"),
        SkyHorizonId = Shader.PropertyToID("_FarSkyHorizon"), SkyZenithId = Shader.PropertyToID("_FarSkyZenith"),
        PunchyZenithId = Shader.PropertyToID("_PunchySkyZenith"), OwnHazeId = Shader.PropertyToID("_OwnHaze");

    // The chunks the characters stand in (the camera's, with none); true when they changed.
    bool FollowCharacters()
    {
        wanted.Clear();
        var grid = ChemGrid.Current?.Grid;
        if (grid != null && grid.World != null)
            foreach (var (x, y) in grid.PlayerCells()) AddChunkOf(x, y);
        if (wanted.Count == 0 && Camera.main != null)
        {
            var p = Camera.main.transform.position;
            var (x, y) = land.Layout.CellAt(p.x, p.z);
            AddChunkOf(x, y);
        }
        if (wanted.Count == 0) return false;
        bool same = wanted.Count == characters.Count;
        for (int n = 0; same && n < wanted.Count; n++) same = wanted[n] == characters[n];
        if (same) return false;
        characters.Clear();
        characters.AddRange(wanted);
        if (terrains != null) terrains.ReachNow();
        return true;
    }

    void AddChunkOf(int x, int y)
    {
        var c = (Mathf.Clamp(x / ChunkGen.Size, 0, chunksW - 1), Mathf.Clamp(y / ChunkGen.Size, 0, chunksH - 1));
        if (!wanted.Contains(c)) wanted.Add(c);
    }

    // The known chunks not drawn yet, built on a worker.
    void StartBuild()
    {
        builtVersion = ring.Version;
        var fresh = new List<KnownChunk>();
        foreach (var k in ring.Chunks)
            if (!drawn.TryGetValue(ring.Id(k.X, k.Y), out var d) || d.chunk != k) fresh.Add(k);
        float cell = land.Layout.CellSize;
        buildStarted = Time.realtimeSinceStartupAsDouble;
        building = Task.Run(() =>
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var made = new List<(KnownChunk, FarMeshes.Data)>(fresh.Count);
            foreach (var k in fresh) made.Add((k, FarMeshes.Known(k, cell)));
            workerMs = timer.Elapsed.TotalMilliseconds;
            return made;
        });
    }

    double workerMs;

    // The worker's meshes to Unity, the chunks no longer known taken away, and the terrains told what's drawn now.
    void FinishBuild()
    {
        var job = building;
        building = null;
        if (job.IsFaulted)
        {
            Debug.LogError($"FAR RING: a known chunk's mesh failed: {job.Exception?.GetBaseException().Message}");
            return;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (k, data) in job.Result)
        {
            int id = ring.Id(k.X, k.Y);
            if (drawn.TryGetValue(id, out var old)) Destroy(old.go);
            drawn[id] = (k, Show(k, data));
        }
        var gone = new List<int>();
        foreach (var p in drawn)
            if (!ring.TryGet(p.Key % chunksW, p.Key / chunksW, out var now) || now != p.Value.chunk) gone.Add(p.Key);
        foreach (int id in gone)
        {
            if (drawn[id].go != null)
            {
                Destroy(drawn[id].go.GetComponent<MeshFilter>().sharedMesh);
                Destroy(drawn[id].go);
            }
            drawn.Remove(id);
        }
        if (terrains != null) terrains.ReachNow();
        LastBuild = (job.Result.Count, workerMs, timer.Elapsed.TotalMilliseconds);
        if (job.Result.Count > 0)
            Debug.Log($"FAR RING: {job.Result.Count} known chunks built ({workerMs:0.0} ms on a worker, {LastBuild.mainMs:0.0} ms to hand to Unity, " +
                      $"{(Time.realtimeSinceStartupAsDouble - buildStarted) * 1000:0} ms in all), {drawn.Count} drawn");
    }

    GameObject Show(KnownChunk k, FarMeshes.Data data)
    {
        var go = new GameObject($"Known {k.X},{k.Y}");
        go.transform.SetParent(transform, false);
        go.transform.position = Corner(k.X, k.Y);
        go.AddComponent<MeshFilter>().sharedMesh = data.ToMesh(go.name);
        var r = go.AddComponent<MeshRenderer>();
        Quiet(r, landMaterial);
        return go;
    }

    // No shadows, no probes, and on the far ring's own layer (past the Default layer's cull distance).
    static void Quiet(MeshRenderer r, Material material)
    {
        r.gameObject.layer = Layer;
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // World x, z of map chunk (cx, cy)'s north-west corner: the line between cells, as the chunk terrains' edges run.
    Vector3 Corner(int cx, int cy)
    {
        var l = land.Layout;
        int x = cx * ChunkGen.Size, y = cy * ChunkGen.Size;
        return new Vector3(l.OriginX + (x + l.U0) * l.CellSize, 0f, l.OriginZ + (l.V0 + 1 - y) * l.CellSize);
    }

    // Chebyshev chunks from map chunk (cx, cy) to the nearest character.
    int Nearest(int cx, int cy)
    {
        int near = int.MaxValue;
        foreach (var (x, y) in characters) near = Mathf.Min(near, Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(y - cy)));
        return near;
    }

    /// <summary>Whether map chunk (cx, cy) is drawn as its chunk terrain: within the known ring and not as a known mesh.</summary>
    public bool LandIsTerrain(int cx, int cy) =>
        ring == null || characters.Count == 0 || (Nearest(cx, cy) <= know && !drawn.ContainsKey(ring.Id(cx, cy)));

    /// <summary>Whether chunk terrain (i, j) draws (MapTerrains): everywhere until the ring is ready, then as LandIsTerrain.</summary>
    public bool TerrainDraws(int i, int j)
    {
        if (ring == null) return true;
        var l = land.Layout;
        // The terrain's north-west cell, in map cells (the terrains run north to south the other way).
        var (x, y) = l.GridCell(i * TerrainGrid.ChunkCells, j * TerrainGrid.ChunkCells + TerrainGrid.ChunkCells - 1);
        if (x < 0 || y < 0) return true;
        int cx = x / ChunkGen.Size, cy = y / ChunkGen.Size;
        return cx >= chunksW || cy >= chunksH || LandIsTerrain(cx, cy);
    }

    // From the map's edge to the horizon at the water's level (a little under it, so the map's own sea draws over it).
    void MakeSea()
    {
        var l = land.Layout;
        float x0 = l.OriginX, z0 = l.OriginZ, x1 = x0 + l.ChunksX * l.ChunkSize, z1 = z0 + l.ChunksZ * l.ChunkSize;
        var data = FarMeshes.SeaFrame(x0, z0, x1, z1, 30000f, land.WaterLevel - 0.4f, new Color(0.08f, 0.2f, 0.27f, 0f));
        var go = new GameObject("Sea to the horizon");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = data.ToMesh(go.name);
        Quiet(go.AddComponent<MeshRenderer>(), landMaterial);
    }
}
