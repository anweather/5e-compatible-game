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

    /// <summary>
    /// Where the clear haze reaches 99% (m), at a depth from the eye as URP's fog measures it: past here nothing shows, so
    /// it's where the Default layer stops drawing and the tree impostors end (#220: at the old 500 m the haze was ~89% and
    /// the coast's dock popped in and out from the mountain by the falls). Rain and fog only close the haze in.
    /// </summary>
    public float HazeHides => HazeMetres * HideK / HazeK;

    // sqrt(ln 100): exponential-squared haze is 99% where density × depth is this.
    const float HideK = 2.146f;

    /// <summary>The last known-chunk build: how many chunks, the worker's milliseconds and the main thread's (meshes to Unity).</summary>
    public static (int chunks, double workerMs, double mainMs) LastBuild { get; private set; }

    // What the worker reads at the start: the map, its far landmarks and the ground at each.
    sealed class Loaded
    {
        public GeneratedMap Map;
        public FarLandmarks Far;
        public float[] Ground;
        public Dictionary<string, FarMeshes.FarLook> Looks;
        public BiomeGround Biomes;
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
        ViewDrawn = ViewKnown = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        DialsFromArgs();
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
        var m = LoadedMaps.Get(source);
        var far = m.Far;
        var gen = m.Gen;
        var ground = new float[far.All.Count];
        var near = new Dictionary<int, KnownChunk>();
        for (int n = 0; n < far.All.Count; n++)
        {
            var l = far.All[n];
            ground[n] = Ground(gen, l, water, near, cell);
        }
        var looks = FarMeshes.FarLooks(System.IO.File.ReadAllText(System.IO.Path.Combine(m.WorldDir, HorizonTable.FileName)));
        return new Loaded { Map = m, Far = far, Ground = ground, Looks = looks, Biomes = BiomeGround.Of(m) };
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

    static readonly Unity.Profiling.ProfilerMarker DIAG_UpdateMarker = new("FarRing.Update");

    void Update()
    {

        using var diagMarked = DIAG_UpdateMarker.Auto();
        if (map == null && !Ready()) return;
        // A rule switch can move streaming sight or know (the dials unset follow it); nothing changes while they hold.
        var rules = ChemGrid.Current?.Rules;
        if (rules != null) Dial(rules.Sim.Streaming);
        float clear = HazeK / Mathf.Max(1f, HazeMetres);
        Shader.SetGlobalFloat(HazeDensityId, RenderSettings.fogMode == FogMode.ExponentialSquared ? RenderSettings.fogDensity : clear);
        Shader.SetGlobalFloat(HazeClearId, clear);
        Shader.SetGlobalFloat(SightMetresId, sight * chunkMetres);
        Shader.SetGlobalFloat(KnownMetresId, HazeMetres);
        Shader.SetGlobalFloat(StretchFromId, StretchFrom);
        Shader.SetGlobalFloat(StretchToId, StretchFrom + StretchOverChunks * chunkMetres);
        var horizon = RenderSettings.fogColor;
        var zenith = Shader.GetGlobalColor(PunchyZenithId);
        Shader.SetGlobalColor(SkyHorizonId, horizon);
        Shader.SetGlobalColor(SkyZenithId, zenith.maxColorComponent > 0f ? zenith : horizon);

        if (FollowCharacters()) ring.Want(characters);
        ring.Handover();
        if (building == null && ring.Version != builtVersion) StartBuild();
        if (building != null && building.IsCompleted) FinishBuild();
        LetGo();
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
        chunkMetres = land.Layout.ChunkSize;
        terrains = FindFirstObjectByType<MapTerrains>();
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        Dial(rules.Sim.Streaming);

        var grid = ChemGrid.Current.Grid;
        var shader = grid.farShader != null ? grid.farShader : Shader.Find("Prototype/FarLand");
        landMaterial = new Material(shader) { name = "Far land" };
        landmarkMaterial = new Material(shader) { name = "Far landmarks" };
        landmarkMaterial.SetFloat(OwnHazeId, 1f);
        MakeSea();
        MakeLandmarks(loaded);
        palette = Palette(loaded.Biomes, grid.World, terrains);
        StartupTimeline.Mark("far ring map read, landmarks made");
        Debug.Log($"FAR RING: know {know} (haze 98% at {HazeMetres:0} m, density {RenderSettings.fogDensity:0.00000}/m), {loaded.Far.All.Count} far landmarks");
        return true;
    }

    static readonly int HazeDensityId = Shader.PropertyToID("_FarHazeDensity"), HazeClearId = Shader.PropertyToID("_FarHazeClear"), SightMetresId = Shader.PropertyToID("_FarSightMetres"),
        SkyHorizonId = Shader.PropertyToID("_FarSkyHorizon"), KnownMetresId = Shader.PropertyToID("_FarKnownMetres"), SkyZenithId = Shader.PropertyToID("_FarSkyZenith"),
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
        var colours = palette;
        buildStarted = Time.realtimeSinceStartupAsDouble;
        building = Task.Run(() =>
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var made = new List<(KnownChunk, FarMeshes.Data)>(fresh.Count);
            foreach (var k in fresh) made.Add((k, FarMeshes.Known(k, cell, colours)));
            workerMs = timer.Elapsed.TotalMilliseconds;
            return made;
        });
    }

    double workerMs;
    FarMeshes.Palette palette = FarMeshes.Palette.Plain;

    // The known chunks' colours from what the near ground draws (#191): each biome's ground as the map's cells paint it,
    // in the colours of the chunk terrains' own layers. Once, as the ring starts.
    static FarMeshes.Palette Palette(BiomeGround biomes, World world, MapTerrains terrains)
    {
        var layers = terrains != null && terrains.template != null ? terrains.template.terrainLayers : null;
        if (biomes == null || world == null || layers == null) return FarMeshes.Palette.Plain;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var colours = new Dictionary<string, Color>();
        foreach (string name in CellPaint.Layers)
            foreach (var layer in layers)
                if (layer != null && layer.diffuseTexture != null && layer.name.StartsWith(name, System.StringComparison.Ordinal))
                {
                    var tint = layer.diffuseRemapMax;
                    var mean = MeanColour(layer.diffuseTexture);
                    colours[name] = new Color(mean.r * tint.x, mean.g * tint.y, mean.b * tint.z);
                    break;
                }
        var palette = FarMeshes.Palette.Drawn(biomes.Grounds(world), colours);
        StartupTimeline.Mark("far ring colours");
        Debug.Log($"FAR RING: the land's colours from the near ground's paint in {timer.Elapsed.TotalMilliseconds:0} ms");
        return palette;
    }

    // A texture's mean colour, linear as the terrain's shader mixes and lights it (FarLand lights a vertex colour as it is),
    // from a small copy on the GPU: the terrain's textures can't be read here.
    static Color MeanColour(Texture texture)
    {
        const int Side = 64;
        var small = RenderTexture.GetTemporary(Side, Side, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var was = RenderTexture.active;
        Graphics.Blit(texture, small);
        RenderTexture.active = small;
        var read = new Texture2D(Side, Side, TextureFormat.RGBAHalf, false, true);
        read.ReadPixels(new Rect(0, 0, Side, Side), 0, 0);
        RenderTexture.active = was;
        RenderTexture.ReleaseTemporary(small);
        Color sum = Color.black;
        var pixels = read.GetPixels();
        foreach (var p in pixels) sum += p;
        Destroy(read);
        return sum / pixels.Length;
    }

    // The worker's meshes to Unity, the chunks no longer known taken away, and the terrains told what's drawn now.
    void FinishBuild()
    {
        using var marked = FinishMarker.Auto();
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
            if (drawn.TryGetValue(id, out var old)) Drop(old.go);
            if (leaving.Remove(id, out var was)) Drop(was.go);
            drawn[id] = (k, Show(k, data));
        }
        var gone = new List<int>();
        foreach (var p in drawn)
            if (!ring.TryGet(p.Key % chunksW, p.Key / chunksW, out var now) || now != p.Value.chunk) gone.Add(p.Key);
        foreach (int id in gone)
        {
            // Land come within sight is the terrains' now: its mesh stays until they draw there (LetGo), so the hand-off
            // shows no hole while they're made and switched on.
            if (terrains != null && Nearest(id % chunksW, id / chunksW) <= know) leaving[id] = (drawn[id].go, Time.unscaledTime);
            else Drop(drawn[id].go);
            drawn.Remove(id);
        }
        if (terrains != null) terrains.ReachNow();
        StartupTimeline.Mark("far ring known chunks built");
        LastBuild = (job.Result.Count, workerMs, timer.Elapsed.TotalMilliseconds);
        if (job.Result.Count > 0)
            Debug.Log($"FAR RING: {job.Result.Count} known chunks built ({workerMs:0.0} ms on a worker, {LastBuild.mainMs:0.0} ms to hand to Unity, " +
                      $"{(Time.realtimeSinceStartupAsDouble - buildStarted) * 1000:0} ms in all), {drawn.Count} drawn");
    }

    static readonly Unity.Profiling.ProfilerMarker FinishMarker = new("FarRing.FinishBuild");

    // A known chunk's object and its mesh (the object doesn't own the mesh: destroying it alone leaked the mesh).
    static void Drop(GameObject go)
    {
        if (go == null) return;
        Destroy(go.GetComponent<MeshFilter>().sharedMesh);
        Destroy(go);
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

    // Known meshes of land now within sight, kept showing (and when they began to) until the chunk terrains draw there.
    readonly Dictionary<int, (GameObject go, float since)> leaving = new();
    readonly List<int> letGo = new();
    // A leaving mesh goes after this long whatever the terrains do (one whose terrain stays off for another chunk's mesh).
    const float MostLeavingSeconds = 6f;

    // The leaving meshes whose land the terrains draw now, or that are past sight again, or kept too long, dropped.
    void LetGo()
    {
        if (leaving.Count == 0) return;
        letGo.Clear();
        foreach (var p in leaving)
        {
            int cx = p.Key % chunksW, cy = p.Key / chunksW;
            if (Nearest(cx, cy) > know || Time.unscaledTime - p.Value.since > MostLeavingSeconds || TerrainsDraw(cx, cy)) letGo.Add(p.Key);
        }
        foreach (int id in letGo)
        {
            Drop(leaving[id].go);
            leaving.Remove(id);
        }
    }

    // Whether every chunk terrain under map chunk (cx, cy) stands and draws (the terrains are offset from the map's
    // chunks, so up to four meet it): its four corner cells' terrains.
    bool TerrainsDraw(int cx, int cy)
    {
        var l = land.Layout;
        int x0 = cx * ChunkGen.Size, y0 = cy * ChunkGen.Size, x1 = x0 + ChunkGen.Size - 1, y1 = y0 + ChunkGen.Size - 1;
        return TerrainDrawsAt(l, x0, y0) && TerrainDrawsAt(l, x1, y0) && TerrainDrawsAt(l, x0, y1) && TerrainDrawsAt(l, x1, y1);
    }

    bool TerrainDrawsAt(TerrainGrid l, int x, int y)
    {
        var (i, j) = l.ChunkOfCell(x, y);
        if (!l.HasChunk(i, j)) return true;
        var t = terrains.Chunks[j * l.ChunksX + i];
        return t != null && t.terrainData != null && t.isActiveAndEnabled && t.drawHeightmap;
    }

    /// <summary>For the flyover probe's holes: what the ring says of map chunk (cx, cy).</summary>
    public string Explain(int cx, int cy) =>
        ring == null ? "no ring" : $"nearest {Nearest(cx, cy)} know {know} sight {sight} drawn {drawn.ContainsKey(ring.Id(cx, cy))} leaving {leaving.ContainsKey(ring.Id(cx, cy))} known {ring.TryGet(cx, cy, out _)}";

    /// <summary>Whether map chunk (cx, cy) shows a known mesh now: one of the ring's, or one kept until its terrains draw.</summary>
    public bool MeshShows(int cx, int cy) => ring != null && (drawn.ContainsKey(ring.Id(cx, cy)) || leaving.ContainsKey(ring.Id(cx, cy)));

    /// <summary>How many known chunks are drawn as meshes now.</summary>
    public int DrawnCount => drawn.Count;

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

    // From the map's edge to the horizon at the water's level (a little under it, so the map's own sea draws over it), and
    // under the map itself past the known ring (FarMeshes.SeaFrame).
    void MakeSea()
    {
        var l = land.Layout;
        float x0 = l.OriginX, z0 = l.OriginZ, x1 = x0 + l.ChunksX * l.ChunkSize, z1 = z0 + l.ChunksZ * l.ChunkSize;
        var data = FarMeshes.SeaFrame(x0, z0, x1, z1, 30000f, land.WaterLevel - 0.4f, new Color(0.08f, 0.2f, 0.27f, 0f));
        var go = new GameObject("Sea to the horizon");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = data.ToMesh(go.name);
        Quiet(go.AddComponent<MeshRenderer>(), SeaMaterial());
    }

    // The far land's look, drawn after the sky and going clear past SeaClearFrom so the sky shows through toward the
    // horizon (FarLand.shader): fogged wholly it's the haze's colour, and the sky just over the horizon is brighter.
    Material SeaMaterial()
    {
        var sea = new Material(landMaterial) { name = "Far sea", renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent };
        sea.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        sea.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        sea.SetFloat("_ZWrite", 0f);
        sea.SetFloat("_SkyFadeFrom", SeaClearFrom);
        sea.SetFloat("_SkyFadeTo", SeeMetres * 0.9f);
        return sea;
    }

    /// <summary>Where the sea plane starts to give way to the sky (m): past the known ring, where the haze is whole.</summary>
    public const float SeaClearFrom = 1500f;
}
