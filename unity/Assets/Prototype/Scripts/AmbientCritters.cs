using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// Fireflies at night and butterflies by day, around the camera. Adapted from
// Punchy's CritterSystem (Zachary Franklin, ThirdParty/Punchy): each tile of
// TileSize metres gets a fixed set of home spots, picked from the cells under
// it (butterflies over dry grass and brush, fireflies there too and thicker by
// water; none where snow lies), and the shaders fly each one around its home, so nothing moves on
// the CPU. How many are out comes from the clock and the weather (SkyLight,
// Chem.Sky): each critter has its own threshold, so they come and go
// gradually. Presentation only; a tile's homes are picked when it streams in.
[RequireComponent(typeof(SkyLight))]
public class AmbientCritters : MonoBehaviour
{
    public Material fireflyMaterial;
    public Material butterflyMaterial;
    public float tileSize = 24f;
    /// <summary>Critters are drawn this far from the camera (the shaders fade them out by here).</summary>
    public float radius = 70f;
    /// <summary>Candidate homes of each kind per 100 m²; each is kept with its spot's density over this.</summary>
    public float maxDensity = 8f;

    const int FireflyVertices = 6;
    const int ButterflyVertices = 12;
    const int TilesPerFrame = 4;

    // Must match Critters.hlsl's Critter.
    [StructLayout(LayoutKind.Sequential)]
    struct Critter
    {
        public Vector3 position;   // home, on the ground
        public float random;
        public Vector4 color;
    }

    sealed class Tile
    {
        public GraphicsBuffer fireflies, butterflies;
        public int fireflyCount, butterflyCount;
        public Bounds bounds;
        public MaterialPropertyBlock fireflyProps, butterflyProps;

        public void Dispose()
        {
            fireflies?.Dispose();
            butterflies?.Dispose();
        }
    }

    static readonly int CrittersBufferId = Shader.PropertyToID("_CritterBuffer");
    static readonly int CrittersId = Shader.PropertyToID("_PunchyCritters");

    static readonly Color[] Wings =
    {
        new Color(1f, 0.42f, 0.04f),     // monarch orange
        new Color(0.95f, 0.95f, 0.9f),   // cabbage white
        new Color(1f, 0.85f, 0.18f),     // brimstone yellow
        new Color(0.25f, 0.45f, 0.95f),  // blue
        new Color(0.75f, 0.3f, 0.45f),   // pinkish
    };

    readonly Dictionary<Vector2Int, Tile> tiles = new();
    readonly List<Vector2Int> scratch = new();
    readonly List<Critter> fireflies = new(), butterflies = new();
    ChemGrid grid;
    SkyLight skyLight;
    Vector2 shown;   // x fireflies, y butterflies: how many are out, 0..1

    /// <summary>How many fireflies and butterflies are out (0..1 each), for the probe.</summary>
    public Vector2 Shown => shown;

    /// <summary>Fireflies and butterflies with homes in the tiles drawn now, for the probe.</summary>
    public (int fireflies, int butterflies) Homes()
    {
        int f = 0, b = 0;
        foreach (var t in tiles.Values)
        {
            f += t.fireflyCount;
            b += t.butterflyCount;
        }
        return (f, b);
    }

    void Start()
    {
        grid = FindAnyObjectByType<ChemGrid>();
        skyLight = GetComponent<SkyLight>();
    }

    void OnDisable()
    {
        foreach (var t in tiles.Values) t.Dispose();
        tiles.Clear();
        Shader.SetGlobalVector(CrittersId, Vector4.zero);
    }

    void Update()
    {
        var cam = Camera.main;
        var world = grid != null ? grid.World : null;
        if (cam == null || world == null || fireflyMaterial == null || butterflyMaterial == null) return;
        shown = Vector2.MoveTowards(shown, Wanted(world), Time.deltaTime * 0.15f);
        Shader.SetGlobalVector(CrittersId, new Vector4(shown.x, shown.y, 0f, 0f));
        if (shown.x <= 0f && shown.y <= 0f) return;
        var camXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
        Stream(world, camXZ);
        Draw();
    }

    // Fireflies come out as the light goes, butterflies in full day; rain and snow keep both in, wind most of them.
    Vector2 Wanted(World world)
    {
        float day = skyLight.Daylight;
        var now = world.Weather;
        float calm = (1f - now.Rain / 100f) * (1f - now.Snow / 100f) * (1f - now.Fog / 200f) * (world.WindDir >= 0 ? 0.4f : 1f);
        return new Vector2(Mathf.InverseLerp(0.45f, 0.1f, day), Mathf.InverseLerp(0.5f, 0.9f, day)) * calm;
    }

    Vector2 TileCentre(Vector2Int c) => ((Vector2)c + Vector2.one * 0.5f) * tileSize;

    // Drops tiles that fell out of range and builds the nearest few missing ones.
    void Stream(World world, Vector2 camXZ)
    {
        scratch.Clear();
        foreach (var kv in tiles)
            if (Vector2.Distance(TileCentre(kv.Key), camXZ) > radius + tileSize * 1.5f) scratch.Add(kv.Key);
        foreach (var key in scratch)
        {
            tiles[key].Dispose();
            tiles.Remove(key);
        }

        scratch.Clear();
        int r = Mathf.CeilToInt(radius / tileSize) + 1;
        var centre = Vector2Int.FloorToInt(camXZ / tileSize);
        for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                var c = centre + new Vector2Int(x, z);
                var min = (Vector2)c * tileSize;
                var nearest = new Vector2(Mathf.Clamp(camXZ.x, min.x, min.x + tileSize), Mathf.Clamp(camXZ.y, min.y, min.y + tileSize));
                if (!tiles.ContainsKey(c) && Vector2.Distance(nearest, camXZ) < radius) scratch.Add(c);
            }
        if (scratch.Count == 0) return;
        scratch.Sort((a, b) => (TileCentre(a) - camXZ).sqrMagnitude.CompareTo((TileCentre(b) - camXZ).sqrMagnitude));
        for (int i = 0; i < Mathf.Min(scratch.Count, TilesPerFrame); i++) tiles[scratch[i]] = Build(world, scratch[i]);
    }

    Tile Build(World world, Vector2Int coord)
    {
        fireflies.Clear();
        butterflies.Clear();
        int perTile = (int)(tileSize * tileSize * maxDensity / 100f);
        // Seeded by the tile, so a tile has the same homes each time it streams in (as far as its cells allow).
        var rng = new System.Random(coord.x * 73856093 ^ coord.y * 19349663);
        for (int i = 0; i < perTile * 2; i++)
        {
            bool butterfly = i >= perTile;
            var xz = ((Vector2)coord + new Vector2((float)rng.NextDouble(), (float)rng.NextDouble())) * tileSize;
            float keep = (float)rng.NextDouble(), random = (float)rng.NextDouble(), pick = (float)rng.NextDouble();
            if (keep * maxDensity >= Density(world, xz, butterfly, out float y)) continue;
            var color = butterfly ? Wings[(int)(pick * Wings.Length) % Wings.Length]
                                  : Color.Lerp(new Color(0.75f, 1f, 0.25f), new Color(1f, 0.85f, 0.3f), pick);
            (butterfly ? butterflies : fireflies).Add(new Critter { position = new Vector3(xz.x, y, xz.y), random = random, color = color });
        }

        var centre = TileCentre(coord);
        var tile = new Tile
        {
            fireflyCount = fireflies.Count,
            butterflyCount = butterflies.Count,
            fireflyProps = new MaterialPropertyBlock(),
            butterflyProps = new MaterialPropertyBlock(),
            bounds = new Bounds(new Vector3(centre.x, grid.GroundAt(new Vector3(centre.x, 0f, centre.y)), centre.y),
                                new Vector3(tileSize + 12f, 80f, tileSize + 12f)),
        };
        tile.fireflies = Upload(fireflies, tile.fireflyProps);
        tile.butterflies = Upload(butterflies, tile.butterflyProps);
        return tile;
    }

    static GraphicsBuffer Upload(List<Critter> critters, MaterialPropertyBlock props)
    {
        if (critters.Count == 0) return null;
        var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, critters.Count, Marshal.SizeOf<Critter>());
        buffer.SetData(critters);
        props.SetBuffer(CrittersBufferId, buffer);
        return buffer;
    }

    // Critters of a kind per 100 m² at a spot, from its cell, and the height they hover from
    // (the water's surface over water). Nothing lives on bare dirt, rock, walls or fire.
    float Density(World world, Vector2 xz, bool butterfly, out float y)
    {
        var p = new Vector3(xz.x, 0f, xz.y);
        var cell = grid.CellAt(p);
        var c = world.At(cell.x, cell.y);
        y = grid.GroundAt(p);
        if (c == null || c.Fire > 0 || CellPaint.IsSnowy(c)) return 0f;
        bool wet = c.Water > 0;
        if (wet) y = Mathf.Max(y, grid.WaterSurfaceY(cell.x, cell.y));
        bool green = c.T == TerrainIds.Grass || c.T == TerrainIds.Brush || c.T == TerrainIds.Tree;
        if (butterfly)
        {
            if (!green || wet) return 0f;
            return c.T == TerrainIds.Brush ? 8f : 5f;
        }
        if (wet || NearWater(world, cell)) return 6f;
        return green ? 2f : 0f;
    }

    static bool NearWater(World world, Vector2Int cell)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var c = world.At(cell.x + dx, cell.y + dy);
                if (c != null && c.Water > 0) return true;
            }
        return false;
    }

    void Draw()
    {
        foreach (var tile in tiles.Values)
        {
            if (shown.x > 0f && tile.fireflyCount > 0)
                Graphics.RenderPrimitives(Params(fireflyMaterial, tile.fireflyProps, tile.bounds), MeshTopology.Triangles, FireflyVertices, tile.fireflyCount);
            if (shown.y > 0f && tile.butterflyCount > 0)
                Graphics.RenderPrimitives(Params(butterflyMaterial, tile.butterflyProps, tile.bounds), MeshTopology.Triangles, ButterflyVertices, tile.butterflyCount);
        }
    }

    static RenderParams Params(Material material, MaterialPropertyBlock props, Bounds bounds) => new(material)
    {
        worldBounds = bounds,
        matProps = props,
        shadowCastingMode = ShadowCastingMode.Off,
        receiveShadows = false,
    };
}
