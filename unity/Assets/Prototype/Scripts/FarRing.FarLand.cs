using System.Collections.Generic;
using System.Threading.Tasks;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The far ring's far land (docs/design/hills.md "One air"): a hills map drawn whole, coarse, past the known ring out to
// the horizon, so its air can be clear for kilometres (the boat game's one air, B166/B167) and the massif stands on
// land, not over a band of haze. Every known chunk of the map (ChunkGen.Know, as the known meshes are made) gives its
// heights every FarStep cells, painted as the known meshes paint them (FarMeshes.LandColour, the high massif's rock
// too), sunk FarSink under them; the map is cut into tiles, and each tile is drawn instanced at every copy of the torus
// within the air's reach. It isn't drawn nearer than the known ring's edge (FarLand.shader's _NearClip): there the
// chunk terrains and known meshes are the land. Made on a worker once the far ring is up. All view: nothing shared
// reads it.
public sealed partial class FarRing
{
    // The far land's spacing in cells (two known samples), how far under the known land it stands (m), and its tiles in
    // cells (a hills map's size divides by them: 4 × 5 on hills-12x10).
    const int FarStep = 16, TileW = 1728, TileH = 1152;
    const float FarSink = 3f;
    // The massif's rock: from RockFrom land units over the meadow, all rock by RockAll.
    const int RockFrom = 250, RockAll = 900;
    // How much the far land pales toward the sky's colour far off (Air.hlsl's DulledFar).
    const float FarDulls = 0.5f;

    /// <summary>A hills map's air (m): its clear air's reach, its gaussian's 98% in clear air and in rain, and where all gives way.</summary>
    public const float HillsReach = 15000f, HillsClear = 25000f, HillsRainy = 1500f, HillsFarFrom = 12500f, HillsFarTo = 14500f;

    Task<(FarMeshes.Data[] tiles, double ms)> farLandWork;
    Mesh[] farTiles;
    Vector2[] farCorners;
    Material farMaterial;
    bool hasFarLand;
    float periodX, periodZ, tileX, tileZ;
    readonly List<Matrix4x4> farCopies = new();
    System.Func<int, int, float> rockAt;

    /// <summary>
    /// The air's dials on this map (AirInPlay): its clear air's reach (m, 0 for none), its gaussian's ~98% in clear air and
    /// in rain (m), and where everything gives way (m, 0 for nowhere). A hills map draws its far land, so its air is
    /// clear for kilometres; a map with no far land closes its haze at the known ring's edge, so the edge never shows.
    /// </summary>
    public (float reach, float clear, float rainy, float farFrom, float farTo) AirDials => hasFarLand
        ? (HillsReach, HillsClear, HillsRainy, HillsFarFrom, HillsFarTo)
        : (0f, HazeMetres, HazeMetres * 0.6f, 0f, 0f);

    /// <summary>A hills map's land at map cell (x, y) in metres, from its generator (no chunk is made), or null on another map.</summary>
    public float? HillsLandAt(int x, int y) => map?.Hills is { } h ? h.At((x % map.Gen.CellsW + map.Gen.CellsW) % map.Gen.CellsW, (y % map.Gen.CellsH + map.Gen.CellsH) % map.Gen.CellsH) * (float)MapFile.LandUnitMetres : null;

    /// <summary>Whether a hills map's far land is drawn now (the probe and the stills read it).</summary>
    public bool FarLandShown => farTiles != null;

    /// <summary>The far land's vertices, all tiles (one copy of the map).</summary>
    public int FarLandVertices { get; private set; }

    void StartFarLand(GeneratedMap m)
    {
        var hills = m.Hills;
        if (hills == null) return;
        hasFarLand = true;
        var mm = hills.Massif;
        var gen = m.Gen;
        int w = gen.CellsW, ht = gen.CellsH;
        if (mm != null) rockAt = (x, y) => Mathf.InverseLerp(mm.MeadowHeight + RockFrom, mm.MeadowHeight + RockAll, mm.At((x % w + w) % w, (y % ht + ht) % ht));
        var colours = palette;
        var rock = rockAt;
        float cell = land.Layout.CellSize;
        farLandWork = Task.Run(() =>
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var tiles = FarLandTiles(gen, cell, colours, rock);
            return (tiles, timer.Elapsed.TotalMilliseconds);
        });
        var l = land.Layout;
        periodX = gen.CellsW * l.CellSize;
        periodZ = gen.CellsH * l.CellSize;
        tileX = TileW * l.CellSize;
        tileZ = TileH * l.CellSize;
    }

    /// <summary>
    /// A hills map's far land as tiles of TileW × TileH cells, row by row from the north-west: each sample every FarStep
    /// cells at its known height less FarSink, painted as the known meshes are. Safe off the main thread.
    /// </summary>
    public static FarMeshes.Data[] FarLandTiles(ChunkGen gen, float cell, FarMeshes.Palette colours, System.Func<int, int, float> rockAt)
    {
        colours ??= FarMeshes.Palette.Plain;
        int per = ChunkGen.Size / FarStep, gw = gen.CellsW / FarStep, gh = gen.CellsH / FarStep, every = FarStep / KnownChunk.Step;
        float unit = (float)MapFile.LandUnitMetres, step = FarStep * cell;
        var h = new float[gw * gh];
        var water = new byte[gw * gh];
        var biome = new string[gw * gh];
        for (int cy = 0; cy < gen.ChunksH; cy++)
            for (int cx = 0; cx < gen.ChunksW; cx++)
            {
                var k = gen.Know(cx, cy);
                for (int j = 0; j < per; j++)
                    for (int i = 0; i < per; i++)
                    {
                        int q = j * every * KnownChunk.Side + i * every, g = (cy * per + j) * gw + cx * per + i;
                        water[g] = k.Water[q];
                        h[g] = k.Heights[q] * unit - (k.Water[q] is KnownChunk.Lake or KnownChunk.River ? 0.4f : 0f) - FarSink;
                        biome[g] = k.Biomes[q] ?? k.Biome;
                    }
            }
        float H(int i, int j) => h[(j % gh + gh) % gh * gw + (i % gw + gw) % gw];
        int tw = TileW / FarStep, th = TileH / FarStep, tilesX = gen.CellsW / TileW, tilesY = gen.CellsH / TileH;
        var tiles = new FarMeshes.Data[tilesX * tilesY];
        for (int ty = 0; ty < tilesY; ty++)
            for (int tx = 0; tx < tilesX; tx++)
            {
                var d = new FarMeshes.Data();
                for (int j = 0; j <= th; j++)
                    for (int i = 0; i <= tw; i++)
                    {
                        int gi = tx * tw + i, gj = ty * th + j, g = gj % gh * gw + gi % gw;
                        float dx = (H(gi + 1, gj) - H(gi - 1, gj)) / (2f * step), dz = (H(gi, gj - 1) - H(gi, gj + 1)) / (2f * step);
                        var normal = new Vector3(-dx, 1f, -dz).normalized;
                        int x = gi % gw * FarStep, y = gj % gh * FarStep;
                        var c = FarMeshes.LandColour(colours.Of(biome[g]), colours.Rock, normal, water[g], x, y, rockAt?.Invoke(x, y) ?? 0f);
                        d.Vertex(new Vector3(i * step, H(gi, gj), -j * step), normal, c);
                    }
                for (int j = 0; j < th; j++)
                    for (int i = 0; i < tw; i++)
                    {
                        int a = j * (tw + 1) + i;
                        d.Quad(a, a + 1, a + tw + 1, a + tw + 2);
                    }
                tiles[ty * tilesX + tx] = d;
            }
        return tiles;
    }

    // The worker's tiles to Unity when they're done; then, every frame, each tile drawn at its copies within the air's reach.
    void UpdateFarLand()
    {
        if (farLandWork != null && farLandWork.IsCompleted) FinishFarLand();
        if (farTiles == null) return;
        farMaterial.SetFloat(NearClipId, HazeMetres);
        var cam = Camera.main;
        if (cam == null) return;
        var eye = cam.transform.position;
        var rp = new RenderParams(farMaterial)
        {
            layer = Layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            worldBounds = new Bounds(new Vector3(eye.x, 0f, eye.z), new Vector3(2f * HillsFarTo + 2f * tileX, 4000f, 2f * HillsFarTo + 2f * tileZ)),
        };
        for (int t = 0; t < farTiles.Length; t++)
        {
            farCopies.Clear();
            var c = farCorners[t];
            // The copy of the tile's middle nearest the eye, then the copies round it within reach.
            float mx = c.x + tileX * 0.5f, mz = c.y - tileZ * 0.5f;
            mx += Mathf.Round((eye.x - mx) / periodX) * periodX;
            mz += Mathf.Round((eye.z - mz) / periodZ) * periodZ;
            for (int a = -2; a <= 2; a++)
                for (int b = -2; b <= 2; b++)
                {
                    float x = mx + a * periodX, z = mz + b * periodZ;
                    float nx = Mathf.Max(0f, Mathf.Abs(eye.x - x) - tileX * 0.5f), nz = Mathf.Max(0f, Mathf.Abs(eye.z - z) - tileZ * 0.5f);
                    if (nx * nx + nz * nz > HillsFarTo * HillsFarTo) continue;
                    farCopies.Add(Matrix4x4.Translate(new Vector3(x - tileX * 0.5f, 0f, z + tileZ * 0.5f)));
                }
            if (farCopies.Count > 0) Graphics.RenderMeshInstanced(rp, farTiles[t], 0, farCopies);
        }
    }

    void FinishFarLand()
    {
        var job = farLandWork;
        farLandWork = null;
        if (job.IsFaulted)
        {
            Debug.LogError($"FAR RING: the far land failed: {job.Exception?.GetBaseException().Message}");
            return;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var (tiles, ms) = job.Result;
        var l = land.Layout;
        int tilesX = map.Gen.CellsW / TileW;
        farTiles = new Mesh[tiles.Length];
        farCorners = new Vector2[tiles.Length];
        int vertices = 0;
        for (int t = 0; t < tiles.Length; t++)
        {
            farTiles[t] = tiles[t].ToMesh($"Far land {t}");
            vertices += tiles[t].Vertices.Count;
            int x = t % tilesX * TileW, y = t / tilesX * TileH;
            farCorners[t] = new Vector2(l.OriginX + (x + l.U0) * l.CellSize, l.OriginZ + (l.V0 + 1 - y) * l.CellSize);
        }
        FarLandVertices = vertices;
        farMaterial = new Material(landMaterial) { name = "Far land", enableInstancing = true, renderQueue = (int)RenderQueue.AlphaTest };
        farMaterial.SetFloat(DullsId, FarDulls);
        Debug.Log($"FAR RING: the far land, {tiles.Length} tiles, {vertices} vertices ({ms:0} ms on a worker, {timer.Elapsed.TotalMilliseconds:0} ms to hand to Unity)");
    }

    void DropFarLand()
    {
        if (farTiles == null) return;
        foreach (var m in farTiles) Destroy(m);
        farTiles = null;
    }

    static readonly int NearClipId = Shader.PropertyToID("_NearClip"), DullsId = Shader.PropertyToID("_Dulls");
}
