using System.Collections.Generic;
using System.Threading.Tasks;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The far ring on a generated map (docs/design/world-squares.md section 4, the view half): past the generated land the
// known chunks (Chem.KnownRing) are drawn as coarse meshes from their 9 × 9 heights, painted by dominant biome, with no
// colliders; the chunk terrains draw only where no known mesh does, within the known ring (MapTerrains asks
// TerrainDraws), so nothing is drawn twice; on a hills map the whole map is drawn coarse past the known ring to the
// horizon (FarRing.FarLand.cs); a sea plane runs from the map's edge to the horizon, so the sky never shows below it;
// and the plan's far landmarks (FarRing.Landmarks.cs) stand past the known ring. The air over all of it is one
// (AirInPlay, Air.hlsl), from the dials here (AirDials): on a hills map clear air reaches kilometres, as the far land is
// drawn; on a map with no far land the haze is exponential-squared, about 98% at the known ring's edge (k 1.98 over
// know × 96 m, after Punchy), so the ring's edge never shows. The meshes are worked out on a worker and only handed to
// Unity here.
//
// All view and all float: the ring and the landmarks are pure functions of the seed and the plan, and nothing shared
// reads anything here (D4). Starts itself on a generated map (PinnedValley.InGenerated); the valley alone keeps its
// skyline and its linear haze.
public sealed partial class FarRing : MonoBehaviour
{
    public const float HazeK = Air.HazeK;

    public static FarRing Instance { get; private set; }

    public float HazeMetres { get; private set; }

    public float HazeHides => HazeMetres * HideK / HazeK;

    const float HideK = 2.146f;

    public static (int chunks, double workerMs, double mainMs) LastBuild { get; private set; }

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
        float cell = land != null ? land.Layout.CellSize : 1.5f;
        loading = Task.Run(() => Load(source, water, cell));
    }

    void OnDestroy()
    {
        DropFarLand();
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
        var rules = ChemGrid.Current?.Rules;
        if (rules != null) Dial(rules.Sim.Streaming);
        Shader.SetGlobalFloat(SightMetresId, sight * chunkMetres);
        Shader.SetGlobalFloat(KnownMetresId, HazeMetres);
        Shader.SetGlobalFloat(StretchFromId, StretchFrom);
        Shader.SetGlobalFloat(StretchToId, StretchFrom + StretchOverChunks * chunkMetres);

        if (FollowCharacters()) ring.Want(characters);
        ring.Handover();
        if (building == null && ring.Version != builtVersion) StartBuild();
        if (building != null && building.IsCompleted) FinishBuild();
        LetGo();
        UpdateLandmarks();
        UpdateFarLand();
    }

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
        Dial(rules.Sim.Streaming);

        var grid = ChemGrid.Current.Grid;
        var shader = grid.farShader != null ? grid.farShader : Shader.Find("Prototype/FarLand");
        landMaterial = new Material(shader) { name = "Far land" };
        landmarkMaterial = new Material(shader) { name = "Far landmarks" };
        landmarkMaterial.SetFloat(OwnHazeId, 1f);
        if (!(ViewWrap.EastWest && ViewWrap.NorthSouth)) MakeSea();
        MakeLandmarks(loaded);
        palette = Palette(loaded.Biomes, grid.World, terrains, map.Hills?.Biome);
        StartFarLand(map);
        StartupTimeline.Mark("far ring map read, landmarks made");
        var d = AirDials;
        Debug.Log($"FAR RING: know {know} ({HazeMetres:0} m), air: clear reach {d.reach:0} m, haze 98% at {d.clear:0} m, {loaded.Far.All.Count} far landmarks");
        return true;
    }

    static readonly int SightMetresId = Shader.PropertyToID("_FarSightMetres"), KnownMetresId = Shader.PropertyToID("_FarKnownMetres"),
        OwnHazeId = Shader.PropertyToID("_OwnHaze");

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
            AddChunkOf(ViewWrap.FoldX(x), ViewWrap.FoldY(y));
        }
        if (wanted.Count == 0) return false;
        bool same = wanted.Count == characters.Count;
        for (int n = 0; same && n < wanted.Count; n++) same = wanted[n] == characters[n];
        if (same) return false;
        characters.Clear();
        characters.AddRange(wanted);
        if (terrains != null) terrains.ReachNow();
        if (ViewWrap.EastWest) Replace();
        return true;
    }

    void AddChunkOf(int x, int y)
    {
        var c = (Mathf.Clamp(x / ChunkGen.Size, 0, chunksW - 1), Mathf.Clamp(y / ChunkGen.Size, 0, chunksH - 1));
        if (!wanted.Contains(c)) wanted.Add(c);
    }

    void StartBuild()
    {
        builtVersion = ring.Version;
        var fresh = new List<KnownChunk>();
        foreach (var k in ring.Chunks)
            if (!drawn.TryGetValue(ring.Id(k.X, k.Y), out var d) || d.chunk != k) fresh.Add(k);
        float cell = land.Layout.CellSize;
        var colours = palette;
        var rock = rockAt;
        buildStarted = Time.realtimeSinceStartupAsDouble;
        building = Task.Run(() =>
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var made = new List<(KnownChunk, FarMeshes.Data)>(fresh.Count);
            foreach (var k in fresh) made.Add((k, FarMeshes.Known(k, cell, colours, rock)));
            workerMs = timer.Elapsed.TotalMilliseconds;
            return made;
        });
    }

    double workerMs;
    FarMeshes.Palette palette = FarMeshes.Palette.Plain;

    static FarMeshes.Palette Palette(BiomeGround biomes, World world, MapTerrains terrains, string allOneBiome)
    {
        var layers = terrains != null && terrains.template != null ? terrains.template.terrainLayers : null;
        if (world == null || layers == null) return FarMeshes.Palette.Plain;
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
        var grounds = biomes != null ? biomes.Grounds(world) : new Dictionary<string, float[]>();
        if (biomes == null && allOneBiome != null && MadeGround(world) is { } made) grounds[allOneBiome] = made;
        var palette = FarMeshes.Palette.Drawn(grounds, colours);
        StartupTimeline.Mark("far ring colours");
        var layersSaid = string.Join(", ", System.Linq.Enumerable.Select(colours, c => $"{c.Key} {c.Value.r:0.000},{c.Value.g:0.000},{c.Value.b:0.000}"));
        Debug.Log($"FAR RING: the land's colours from the near ground's paint in {timer.Elapsed.TotalMilliseconds:0} ms: {layersSaid}; grass {palette.Grass}{(allOneBiome != null ? $", {allOneBiome} {palette.Of(allOneBiome)}" : "")}");
        return palette;
    }

    static float[] MadeGround(World world)
    {
        const int Every = 4;
        var sum = new float[CellPaint.Layers.Count];
        int n = 0;
        for (int cy = 0; cy < world.H / ChunkGen.Size; cy++)
            for (int cx = 0; cx < world.W / ChunkGen.Size; cx++)
            {
                int x0 = cx * ChunkGen.Size, y0 = cy * ChunkGen.Size;
                if (BiomeGround.CellAsIs(world, x0, y0) == null) continue;
                for (int y = y0; y < y0 + ChunkGen.Size; y += Every)
                    for (int x = x0; x < x0 + ChunkGen.Size; x += Every)
                    {
                        var c = BiomeGround.CellAsIs(world, x, y);
                        if (c == null || c.Water > 0) continue;
                        var mix = CellPaint.Mix(world, x, y, c);
                        for (int k = 0; k < sum.Length && k < mix.Count; k++) sum[k] += mix[k];
                        n++;
                    }
            }
        if (n == 0) return null;
        for (int k = 0; k < sum.Length; k++) sum[k] /= n;
        return sum;
    }

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

    static void Quiet(MeshRenderer r, Material material)
    {
        r.gameObject.layer = Layer;
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    Vector3 Corner(int cx, int cy)
    {
        var l = land.Layout;
        int x = cx * ChunkGen.Size, y = cy * ChunkGen.Size;
        float half = l.ChunkSize * 0.5f, wx = l.OriginX + (x + l.U0) * l.CellSize, wz = l.OriginZ + (l.V0 + 1 - y) * l.CellSize;
        return new Vector3(ViewWrap.NearX(wx + half) - half, 0f, ViewWrap.NearZ(wz - half) + half);
    }

    void Replace()
    {
        foreach (var p in drawn) Place(p.Value.go, p.Key);
        foreach (var p in leaving) Place(p.Value.go, p.Key);
    }

    void Place(GameObject go, int id)
    {
        if (go == null) return;
        var to = Corner(id % chunksW, id / chunksW);
        var at = go.transform.position;
        if (Mathf.Abs(at.x - to.x) > 1f || Mathf.Abs(at.z - to.z) > 1f) go.transform.position = new Vector3(to.x, at.y, to.z);
    }

    int Nearest(int cx, int cy)
    {
        int near = int.MaxValue;
        foreach (var (x, y) in characters)
            near = Mathf.Min(near, Mathf.Max(ViewWrap.ChunkDistance(x, cx, chunksW, true), ViewWrap.ChunkDistance(y, cy, chunksH, false)));
        return near;
    }

    readonly Dictionary<int, (GameObject go, float since)> leaving = new();
    readonly List<int> letGo = new();
    const float MostLeavingSeconds = 6f;

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

    public string Explain(int cx, int cy) =>
        ring == null ? "no ring" : $"nearest {Nearest(cx, cy)} know {know} sight {sight} drawn {drawn.ContainsKey(ring.Id(cx, cy))} leaving {leaving.ContainsKey(ring.Id(cx, cy))} known {ring.TryGet(cx, cy, out _)}";

    public bool MeshShows(int cx, int cy) => ring != null && (drawn.ContainsKey(ring.Id(cx, cy)) || leaving.ContainsKey(ring.Id(cx, cy)));

    public int DrawnCount => drawn.Count;

    public bool LandIsTerrain(int cx, int cy) =>
        ring == null || characters.Count == 0 || (Nearest(cx, cy) <= know && !drawn.ContainsKey(ring.Id(cx, cy)));

    public bool TerrainDraws(int i, int j)
    {
        if (ring == null) return true;
        var l = land.Layout;
        var (x, y) = l.GridCell(i * TerrainGrid.ChunkCells, j * TerrainGrid.ChunkCells + TerrainGrid.ChunkCells - 1);
        if (x < 0 || y < 0) return true;
        int cx = x / ChunkGen.Size, cy = y / ChunkGen.Size;
        return cx >= chunksW || cy >= chunksH || LandIsTerrain(cx, cy);
    }

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

    public const float SeaClearFrom = 1500f;
}
