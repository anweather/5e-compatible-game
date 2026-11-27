using System.Threading.Tasks;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The far ring's far land (docs/design/hills.md "One air"): a hills map drawn whole, coarse, past the known ring out to
// the horizon, so its air can be clear for kilometres (the boat game's one air, B166/B167) and the massif stands on
// land, not over a band of haze. Every known chunk of the map (ChunkGen.Know, as the known meshes are made) gives its
// heights every FarStep cells, painted as the known meshes paint them (FarMeshes.LandColour, the high massif's rock
// too), sunk FarSink under them; the map is cut into tiles, and each tile is drawn at every copy of the torus within
// the air's reach (an object a copy: an instanced draw's variant is stripped from a player, as no material asks for it). It isn't drawn nearer than the known ring's edge (FarLand.shader's _NearClip): there the
// chunk terrains and known meshes are the land. Made on a worker once the far ring is up. All view: nothing shared
// reads it.
public sealed partial class FarRing
{
    const int FarStep = 16, TileW = 1728, TileH = 1152;
    const float FarSink = 3f;
    const int RockFrom = 250, RockAll = 900;
    const float FarDulls = 0.5f;

    public const float HillsReach = 15000f, HillsClear = 25000f, HillsRainy = 1500f, HillsFarFrom = 12500f, HillsFarTo = 14500f;

    Task<(FarMeshes.Data[] tiles, double ms)> farLandWork;
    Mesh[] farTiles;
    Vector2[] farCorners;
    Material farMaterial;
    float periodX, periodZ, tileX, tileZ;
    GameObject[] farCopies;
    const int Copies = 2;
    System.Func<int, int, float> rockAt;

    public (float reach, float clear, float rainy, float farFrom, float farTo) AirDials => (HillsReach, HillsClear, HillsRainy, HillsFarFrom, HillsFarTo);

    public float? HillsLandAt(int x, int y) => map?.Hills is { } h ? h.At((x % map.Gen.CellsW + map.Gen.CellsW) % map.Gen.CellsW, (y % map.Gen.CellsH + map.Gen.CellsH) % map.Gen.CellsH) * (float)MapFile.LandUnitMetres : null;

    public bool FarLandShown => farTiles != null;

    public int FarLandVertices { get; private set; }

    void StartFarLand(GeneratedMap m)
    {
        var hills = m.Hills;
        if (hills == null) return;
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

    public static FarMeshes.Data[] FarLandTiles(ChunkGen gen, float cell, FarMeshes.Palette colours, System.Func<int, int, float> rockAt)
    {
        colours ??= FarMeshes.Palette.Plain;
        int per = ChunkGen.Size / FarStep, gw = gen.CellsW / FarStep, gh = gen.CellsH / FarStep, every = FarStep / KnownChunk.Step;
        float unit = (float)MapFile.LandUnitMetres, step = FarStep * cell;
        var h = new float[gw * gh];
        var water = new byte[gw * gh];
        var biome = new string[gw * gh];
        var wood = new byte[gw * gh];
        int fw = gen.CellsW / KnownChunk.Step, fh = gen.CellsH / KnownChunk.Step, fine = KnownChunk.Side - 1;
        var f = new float[fw * fh];
        for (int cy = 0; cy < gen.ChunksH; cy++)
            for (int cx = 0; cx < gen.ChunksW; cx++)
            {
                var k = gen.Know(cx, cy);
                for (int j = 0; j < fine; j++)
                    for (int i = 0; i < fine; i++)
                    {
                        int q = j * KnownChunk.Side + i;
                        f[(cy * fine + j) * fw + cx * fine + i] = k.Heights[q] * unit - (k.Water[q] is KnownChunk.Lake or KnownChunk.River ? 0.4f : 0f);
                    }
                for (int j = 0; j < per; j++)
                    for (int i = 0; i < per; i++)
                    {
                        int q = j * every * KnownChunk.Side + i * every, g = (cy * per + j) * gw + cx * per + i;
                        water[g] = k.Water[q];
                        h[g] = k.Heights[q] * unit - (k.Water[q] is KnownChunk.Lake or KnownChunk.River ? 0.4f : 0f) - FarSink;
                        biome[g] = k.Biomes[q] ?? k.Biome;
                        wood[g] = k.Wood[q];
                    }
            }
        float H(int i, int j) => h[(j % gh + gh) % gh * gw + (i % gw + gw) % gw];
        float F(int i, int j) => f[(j % fh + fh) % fh * fw + (i % fw + fw) % fw];
        float fineStep = KnownChunk.Step * cell;
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
                        int fi = gi * every, fj = gj * every;
                        float dx = (F(fi + 1, fj) - F(fi - 1, fj)) / (2f * fineStep), dz = (F(fi, fj - 1) - F(fi, fj + 1)) / (2f * fineStep);
                        var normal = new Vector3(-dx, 1f, -dz).normalized;
                        int x = gi % gw * FarStep, y = gj % gh * FarStep;
                        var c = FarMeshes.LandColour(colours.Wooded(colours.Of(biome[g]), wood[g]), colours.Rock, normal, water[g], x, y, rockAt?.Invoke(x, y) ?? 0f);
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

    void UpdateFarLand()
    {
        if (farLandWork != null && farLandWork.IsCompleted) FinishFarLand();
        if (farTiles == null) return;
        farMaterial.SetFloat(NearClipId, HazeMetres);
        var cam = Camera.main;
        if (cam == null) return;
        var eye = cam.transform.position;
        int side = 2 * Copies + 1;
        for (int t = 0; t < farTiles.Length; t++)
        {
            var c = farCorners[t];
            float mx = c.x + tileX * 0.5f, mz = c.y - tileZ * 0.5f;
            mx += Mathf.Round((eye.x - mx) / periodX) * periodX;
            mz += Mathf.Round((eye.z - mz) / periodZ) * periodZ;
            for (int a = -Copies; a <= Copies; a++)
                for (int b = -Copies; b <= Copies; b++)
                {
                    var go = farCopies[(t * side + a + Copies) * side + b + Copies];
                    float x = mx + a * periodX, z = mz + b * periodZ;
                    float nx = Mathf.Max(0f, Mathf.Abs(eye.x - x) - tileX * 0.5f), nz = Mathf.Max(0f, Mathf.Abs(eye.z - z) - tileZ * 0.5f);
                    bool show = nx * nx + nz * nz <= HillsFarTo * HillsFarTo;
                    if (go.activeSelf != show) go.SetActive(show);
                    if (!show) continue;
                    var at = new Vector3(x - tileX * 0.5f, 0f, z + tileZ * 0.5f);
                    if ((go.transform.position - at).sqrMagnitude > 1f) go.transform.position = at;
                }
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
        farMaterial = new Material(landMaterial) { name = "Far land", renderQueue = (int)RenderQueue.AlphaTest };
        farMaterial.SetFloat(DullsId, FarDulls);
        int side = 2 * Copies + 1;
        farCopies = new GameObject[tiles.Length * side * side];
        for (int i = 0; i < farCopies.Length; i++)
        {
            var go = new GameObject($"Far land {i / (side * side)} copy {i % (side * side)}");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = farTiles[i / (side * side)];
            Quiet(go.AddComponent<MeshRenderer>(), farMaterial);
            go.SetActive(false);
            farCopies[i] = go;
        }
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
