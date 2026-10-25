using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// Builds one yard cell's ground: a block of the terrain's colour, with a
// little per-cell colour variation, dressed with what stands on it (grass
// tufts, a tree, bramble bushes, a stump, rubble). Each cell's dressing is
// seeded from its position, so a rebuilt tile looks the same.
public sealed class GridTileBuilder
{
    static readonly Dictionary<string, (Color color, float height)> TerrainLook = new()
    {
        ["bedrock"] = (new Color(0.24f, 0.23f, 0.25f), 1.5f), ["stone"] = (new Color(0.50f, 0.50f, 0.52f), 1.2f),
        ["pillar"] = (new Color(0.55f, 0.45f, 0.35f), 1.2f), ["rubble"] = (new Color(0.45f, 0.43f, 0.40f), 0.05f),
        ["dirt"] = (new Color(0.50f, 0.38f, 0.25f), 0f), ["grass"] = (new Color(0.33f, 0.55f, 0.22f), 0f),
        ["brush"] = (new Color(0.20f, 0.36f, 0.14f), 0.6f), ["tree"] = (new Color(0.16f, 0.38f, 0.15f), 2.2f),
        ["stump"] = (new Color(0.40f, 0.28f, 0.16f), 0.15f), ["wood"] = (new Color(0.58f, 0.40f, 0.22f), 0f),
        ["pit"] = (new Color(0.30f, 0.24f, 0.18f), 0f), ["metal"] = (new Color(0.62f, 0.66f, 0.72f), 0.02f),
    };

    // Terrains drawn as a ground tile with something standing on it; their height is an invisible collider.
    static readonly HashSet<string> Dressed = new() { TerrainIds.Tree, TerrainIds.Brush, TerrainIds.Stump, TerrainIds.Rubble };

    const float TileBottom = -1.5f;

    readonly ChemGrid grid;
    readonly MaterialCache materials;
    readonly MaterialPropertyBlock tint = new();
    readonly Mesh tuftMesh = BuildTuftMesh();

    public GridTileBuilder(ChemGrid grid, MaterialCache materials)
    {
        this.grid = grid;
        this.materials = materials;
    }

    /// <summary>A terrain's base colour and how high it stands; magenta for an unknown terrain.</summary>
    public static (Color color, float height) Look(string terrain) =>
        TerrainLook.TryGetValue(terrain, out var l) ? l : (Color.magenta, 0f);

    /// <summary>True for terrains drawn as flat ground with something standing on it (tree, brush...).</summary>
    public static bool IsDressed(string terrain) => Dressed.Contains(terrain);

    float CellSize => grid.cellSize;

    /// <summary>Builds the tile for a yard cell and returns it.</summary>
    public GameObject Build(Cell c, int x, int y)
    {
        var look = Look(c.T);
        bool dressed = IsDressed(c.T);
        float top = dressed ? grid.GroundTop(c) : grid.SurfaceY(x, y);
        var center = grid.CellCenter(x, y);

        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = $"{c.T} {x},{y}";
        tile.transform.SetParent(grid.transform, false);
        tile.transform.position = new Vector3(center.x, (top + TileBottom) / 2f, center.z);
        tile.transform.localScale = new Vector3(CellSize, top - TileBottom, CellSize);
        Paint(tile.GetComponent<Renderer>(), "Ground", GroundColor(c.T, dressed, look.color), c.T == TerrainIds.Metal ? 0.85f : 0.1f, x, y);

        Dress(tile.transform, c.T, look, new Vector3(center.x, top, center.z), x, y);
        return tile;
    }

    // Dressed terrains stand on grass or dirt; anything grassy takes the scene's grass tint.
    Color GroundColor(string terrain, bool dressed, Color own)
    {
        bool grassy = terrain == TerrainIds.Grass || terrain == TerrainIds.Brush || terrain == TerrainIds.Tree;
        var color = own;
        if (dressed) color = Look(grassy ? TerrainIds.Grass : TerrainIds.Dirt).color;
        return grassy ? color * grid.grassTint : color;
    }

    void Dress(Transform tile, string terrain, (Color color, float height) look, Vector3 surface, int x, int y)
    {
        var rng = new System.Random(x * 7919 + y * 104729 + terrain.GetHashCode());
        switch (terrain)
        {
            case TerrainIds.Grass:
                for (int i = 0; i < 6; i++) Tuft(tile, surface, rng, x, y);
                break;
            case TerrainIds.Tree:
                DressTree(tile, surface, rng, look, x, y);
                break;
            case TerrainIds.Brush:
                DressBrush(tile, surface, rng, look.color, x, y);
                break;
            case TerrainIds.Stump:
                Part(tile, PrimitiveType.Cylinder, surface + Vector3.up * 0.15f, new Vector3(0.45f, 0.15f, 0.45f), "Bark", look.color);
                break;
            case TerrainIds.Rubble:
                DressRubble(tile, surface, rng, look.color);
                break;
        }
    }

    void DressTree(Transform tile, Vector3 surface, System.Random rng, (Color color, float height) look, int x, int y)
    {
        Blocker(tile, surface, look.height);
        Part(tile, PrimitiveType.Cylinder, surface + Vector3.up * 1.1f, new Vector3(0.28f, 1.1f, 0.28f), "Bark", new Color(0.36f, 0.25f, 0.16f));
        for (int i = 0; i < 3; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 2.4f + 0.5f * (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * (CellSize * 0.6f);
            off.y = 2.3f + 0.6f * (float)rng.NextDouble();
            float r = 1.1f + 0.4f * (float)rng.NextDouble();
            var leaf = Part(tile, PrimitiveType.Sphere, surface + off, new Vector3(r, r * 0.85f, r), "Leaves", look.color);
            Paint(leaf.GetComponent<Renderer>(), "Leaves", look.color, 0.1f, x + i * 13, y);
        }
        for (int i = 0; i < 3; i++) Tuft(tile, surface, rng, x, y);
    }

    void DressBrush(Transform tile, Vector3 surface, System.Random rng, Color color, int x, int y)
    {
        Blocker(tile, surface, 1.2f);
        for (int i = 0; i < 5; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.7f);
            float r = 0.55f + 0.35f * (float)rng.NextDouble();
            var bush = Part(tile, PrimitiveType.Sphere, surface + off + Vector3.up * (r * 0.35f), new Vector3(r, r * 0.7f, r), "Leaves", color);
            Paint(bush.GetComponent<Renderer>(), "Leaves", color, 0.1f, x + i * 17, y);
        }
    }

    void DressRubble(Transform tile, Vector3 surface, System.Random rng, Color color)
    {
        for (int i = 0; i < 4; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 0.1f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.8f);
            var rock = Part(tile, PrimitiveType.Cube, surface + off, Vector3.one * (0.25f + 0.2f * (float)rng.NextDouble()), "Stone", color);
            rock.transform.rotation = Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360));
        }
    }

    // An invisible box the character can't walk through, for terrain that blocks.
    void Blocker(Transform parent, Vector3 surface, float height)
    {
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, true);
        go.transform.position = surface + Vector3.up * height / 2f;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(CellSize, height, CellSize);
    }

    // A collider-less shape at a world position and world size, under a (scaled) tile.
    GameObject Part(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, string mat, Color color)
    {
        var go = Primitives.Visual(type);
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.transform.localScale = Vector3.one;
        go.transform.localScale = WorldToLocalScale(parent, scale);
        go.GetComponent<Renderer>().sharedMaterial = materials.Lit(mat, color, 0.1f);
        return go;
    }

    void Tuft(Transform parent, Vector3 surface, System.Random rng, int x, int y)
    {
        var go = new GameObject("Grass");
        go.transform.SetParent(parent, true);
        var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.9f);
        go.transform.SetPositionAndRotation(surface + off, Quaternion.Euler(0f, rng.Next(360), 0f));
        float s = 0.7f + 0.6f * (float)rng.NextDouble();
        go.transform.localScale = WorldToLocalScale(parent, Vector3.one * s);
        go.AddComponent<MeshFilter>().sharedMesh = tuftMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        Paint(r, "Blades", new Color(0.36f, 0.62f, 0.24f) * grid.grassTint, 0.15f, x * 3 + rng.Next(5), y);
    }

    static Vector3 WorldToLocalScale(Transform parent, Vector3 world) =>
        new Vector3(world.x / parent.lossyScale.x, world.y / parent.lossyScale.y, world.z / parent.lossyScale.z);

    // Shared material, with a per-cell shade so neighbouring tiles don't read as one flat sheet.
    void Paint(Renderer r, string mat, Color color, float smoothness, int x, int y)
    {
        r.sharedMaterial = materials.Lit(mat, Color.white, smoothness);
        float n = Mathf.PerlinNoise(x * 0.37f + 0.11f, y * 0.37f + 0.53f);
        float jitter = 0.86f + 0.24f * n + 0.04f * (((x * 73856093) ^ (y * 19349663)) & 7) / 7f;
        UrpMaterials.SetColor(r, tint, color * jitter);
    }

    // A tuft of five thin blades leaning outward. Each blade has a front and a
    // back face with their own vertices; shared vertices would average the two
    // normals to zero and light as NaN, which bloom then smears over the screen.
    static Mesh BuildTuftMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var side = Vector3.Cross(dir, Vector3.up) * 0.03f;
            var tip = dir * 0.1f + Vector3.up * (0.28f + 0.08f * (i % 3));
            var facing = Vector3.Cross(side, tip + side).normalized;
            foreach (var n in new[] { facing, -facing })
            {
                int b = verts.Count;
                verts.AddRange(new[] { -side, side, tip });
                normals.AddRange(new[] { n, n, n });
                if (n == facing) tris.AddRange(new[] { b, b + 1, b + 2 });
                else tris.AddRange(new[] { b + 2, b + 1, b });
            }
        }
        var mesh = new Mesh { name = "Grass tuft" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public void Destroy() => Object.Destroy(tuftMesh);
}
