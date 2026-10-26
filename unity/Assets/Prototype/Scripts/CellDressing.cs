using System.Collections.Generic;
using Chem;
using UnityEngine;

// What stands on a cell, on top of the chunk terrain (the terrain itself is the
// ground: CellHeightfield shapes it, CellPaint colours it): bramble bushes, a
// charred stump, rubble, a stone block here; the planks of a bridge, an iron
// plate and the crags along a raised wall in CellDressing.Built.cs. Trees are the terrain's own trees
// (ValleyChemistry burns them); grass is the terrain's detail grass. Each
// cell's dressing is seeded from its position, so a rebuilt one looks the same.
//
// A dressing is an unscaled root at the cell's ground with its parts under it,
// so ChemGrid can raise or sink the whole thing with the ground (earth shaping,
// ChemGrid.Shaping.cs) by moving the root.
public sealed partial class CellDressing
{
    static readonly Dictionary<string, (Color color, float height)> TerrainLook = new()
    {
        ["bedrock"] = (new Color(0.24f, 0.23f, 0.25f), 1.5f), ["stone"] = (new Color(0.50f, 0.50f, 0.52f), 1.2f),
        ["pillar"] = (new Color(0.55f, 0.45f, 0.35f), 1.2f), ["rubble"] = (new Color(0.45f, 0.43f, 0.40f), 0.05f),
        ["dirt"] = (new Color(0.50f, 0.38f, 0.25f), 0f), ["grass"] = (new Color(0.33f, 0.55f, 0.22f), 0f),
        ["brush"] = (new Color(0.20f, 0.36f, 0.14f), 0.6f), ["tree"] = (new Color(0.16f, 0.38f, 0.15f), 2.2f),
        ["stump"] = (new Color(0.13f, 0.10f, 0.08f), 0.15f), ["wood"] = (new Color(0.58f, 0.40f, 0.22f), 0f),
        ["deadfall"] = (new Color(0.32f, 0.23f, 0.15f), 0.4f),
        ["pit"] = (new Color(0.30f, 0.24f, 0.18f), 0f), ["metal"] = (new Color(0.30f, 0.29f, 0.28f), 0.06f),
        ["rock"] = (new Color(0.47f, 0.45f, 0.42f), 0f),
    };

    // Terrains drawn as ground with something standing on it; effects sit on their ground, not on top.
    static readonly HashSet<string> Dressed = new() { TerrainIds.Tree, TerrainIds.Brush, TerrainIds.Stump, TerrainIds.Rubble, TerrainIds.Deadfall };
    // Terrains that stand up as a solid block of their own height.
    static readonly HashSet<string> Blocks = new() { "bedrock", "stone", "pillar" };

    readonly ChemGrid grid;
    readonly MaterialCache materials;
    readonly MaterialPropertyBlock tint = new();

    public CellDressing(ChemGrid grid, MaterialCache materials)
    {
        this.grid = grid;
        this.materials = materials;
    }

    /// <summary>A terrain's base colour and how high it stands; magenta for an unknown terrain.</summary>
    public static (Color color, float height) Look(string terrain) =>
        TerrainLook.TryGetValue(terrain, out var l) ? l : (Color.magenta, 0f);

    /// <summary>True for terrains drawn as ground with something standing on it (tree, brush...).</summary>
    public static bool IsDressed(string terrain) => Dressed.Contains(terrain);

    float CellSize => grid.cellSize;

    /// <summary>The dressing for a cell with its root at <paramref name="ground"/>, or null when nothing stands on it.</summary>
    public GameObject Build(Cell c, int x, int y, float ground)
    {
        bool wall = grid.World.IsRaisedWall(c);
        if (!wall && !HasParts(c.T)) return null;
        var center = grid.CellCenter(x, y);
        var surface = new Vector3(center.x, ground, center.z);
        var root = new GameObject($"{c.T} {x},{y}");
        root.transform.SetParent(grid.transform, false);
        root.transform.position = surface;
        if (HasParts(c.T)) Dress(root.transform, c.T, Look(c.T), surface, x, y);
        if (wall) DressWall(root.transform, surface, c.Height, x, y);
        return root;
    }

    static bool HasParts(string terrain) =>
        terrain == TerrainIds.Brush || terrain == TerrainIds.Stump || terrain == TerrainIds.Rubble || terrain == TerrainIds.Deadfall ||
        terrain == TerrainIds.Wood || terrain == TerrainIds.Metal || Blocks.Contains(terrain);

    void Dress(Transform root, string terrain, (Color color, float height) look, Vector3 surface, int x, int y)
    {
        var rng = new System.Random(x * 7919 + y * 104729 + terrain.GetHashCode());
        switch (terrain)
        {
            case TerrainIds.Brush:
                DressBrush(root, surface, rng, look.color, x, y);
                break;
            case TerrainIds.Stump:
                Part(root, PrimitiveType.Cylinder, surface + Vector3.up * 0.15f, new Vector3(0.45f, 0.15f, 0.45f), "Charred", look.color, 0.05f);
                break;
            case TerrainIds.Rubble:
                DressRubble(root, surface, rng, look.color);
                break;
            case TerrainIds.Deadfall:
                DressDeadfall(root, surface, rng, look.color, x, y);
                break;
            case TerrainIds.Wood:
                DressPlanks(root, surface, look.color, rng, x, y);
                break;
            case TerrainIds.Metal:
                DressPlate(root, surface, rng, x, y);
                break;
            default:
                var block = Part(root, PrimitiveType.Cube, surface + Vector3.up * (look.height / 2f - 0.1f), new Vector3(CellSize, look.height + 0.2f, CellSize), "Stone", look.color);
                Paint(block.GetComponent<Renderer>(), "Stone", look.color, 0.1f, x, y);
                break;
        }
    }

    void DressBrush(Transform root, Vector3 surface, System.Random rng, Color color, int x, int y)
    {
        for (int i = 0; i < 5; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.7f);
            float r = 0.55f + 0.35f * (float)rng.NextDouble();
            var bush = Part(root, PrimitiveType.Sphere, surface + off + Vector3.up * (r * 0.35f), new Vector3(r, r * 0.7f, r), "Leaves", color);
            Paint(bush.GetComponent<Renderer>(), "Leaves", color, 0.1f, x + i * 17, y);
        }
    }

    // Broken stone: a few of the terrain pack's small stones when the grid has them (ChemGrid.wallStones), else rough cubes.
    void DressRubble(Transform root, Vector3 surface, System.Random rng, Color color)
    {
        var stones = grid.wallStones;
        if (stones != null && stones.Length > 0 && stones[0] != null)
        {
            for (int i = 0; i < 3; i++)
            {
                var at = surface + new Vector3(Jitter(rng, 0.35f), -0.05f, Jitter(rng, 0.35f)) * CellSize;
                Rock(root, stones[rng.Next(stones.Length)], at, CellSize * (0.3f + 0.2f * (float)rng.NextDouble()), 0.15f + 0.15f * (float)rng.NextDouble(), rng);
            }
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 0.1f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.8f);
            var rock = Part(root, PrimitiveType.Cube, surface + off, Vector3.one * (0.25f + 0.2f * (float)rng.NextDouble()), "Stone", color);
            rock.transform.rotation = Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360));
        }
    }

    // A collider-less shape at a world position and world size, under the (unscaled) root.
    GameObject Part(Transform root, PrimitiveType type, Vector3 at, Vector3 scale, string mat, Color color, float smoothness = 0.1f)
    {
        var go = Primitives.Visual(type);
        go.transform.SetParent(root, true);
        go.transform.position = at;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = materials.Lit(mat, color, smoothness);
        return go;
    }

    // Shared material, with a per-cell shade so neighbouring parts don't read as one flat sheet.
    void Paint(Renderer r, string mat, Color color, float smoothness, int x, int y)
    {
        r.sharedMaterial = materials.Lit(mat, Color.white, smoothness);
        float n = Mathf.PerlinNoise(x * 0.37f + 0.11f, y * 0.37f + 0.53f);
        float jitter = 0.86f + 0.24f * n + 0.04f * (((x * 73856093) ^ (y * 19349663)) & 7) / 7f;
        UrpMaterials.SetColor(r, tint, color * jitter);
    }
}
