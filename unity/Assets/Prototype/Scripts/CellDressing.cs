using Chem;
using UnityEngine;

// What stands on a cell, on top of the chunk terrain (the terrain itself is the
// ground: CellHeightfield shapes it, CellPaint colours it): bramble bushes, a
// charred stump, rubble, a stone block here; Earth Spires' rock spires in CellDressing.Spires.cs; the planks of a bridge, an iron
// plate and the crags along a raised wall in CellDressing.Built.cs; a boulder, a coil, a gate and a worn
// log wall in CellDressing.Machines.cs; Plant Growth's saplings and the trees they grow into in CellDressing.Growth.cs. Otherwise trees are the terrain's own trees
// (ValleyChemistry burns them); grass is the terrain's detail grass. Each
// cell's dressing is seeded from its position, so a rebuilt one looks the same.
// What's drawn comes from the terrain's look in the rules in force (a family,
// its form and parameters: rules/looks.json), never from its id, so a mod's new
// material draws with no code and no asset.
//
// A dressing is an unscaled root at the cell's ground with its parts under it,
// so ChemGrid can raise or sink the whole thing with the ground (earth shaping,
// ChemGrid.Shaping.cs) by moving the root.
public sealed partial class CellDressing
{
    // The look families this file draws (rules/looks.json), with their forms.
    const string Block = "block", Plant = "plant", Debris = "debris", Deck = "deck", PlateFamily = "plate", Machine = "machine", Wall = "wall", Ground = "ground";
    // How far a bush follows the drawn ground off its cell's height (m): further, the ground there is a wall or a cliff.
    const float MostBushDrop = 2.5f;
    const string Bush = "bush", Sapling = "sapling", Stump = "stump", Rubble = "rubble";
    const string RustedForm = "rusted", Coil = "coil", GateShut = "gate", GateOpen = "gate-open", Built = "built";
    // The smoothness of a block whose look doesn't say.
    const float BlockSmoothness = 0.1f;

    readonly ChemGrid grid;
    readonly MaterialCache materials;
    readonly MaterialPropertyBlock tint = new();

    public CellDressing(ChemGrid grid, MaterialCache materials)
    {
        this.grid = grid;
        this.materials = materials;
    }

    /// <summary>A terrain's look in the rules in force (materials.json "look"), or null.</summary>
    public static LookDef Of(Rules rules, string terrain) =>
        rules.Terrain.TryGetValue(terrain, out var t) ? t.Look : null;

    /// <summary>A terrain's base colour and how high it stands, from the rules in force; magenta for a terrain with no look.</summary>
    public static (Color color, float height) Look(Rules rules, string terrain)
    {
        var look = Of(rules, terrain);
        return look != null ? (ColorOf(look), look.Height) : (Color.magenta, 0f);
    }

    static Color ColorOf(LookDef look) => new(look.R, look.G, look.B);

    /// <summary>True for terrains drawn as ground with something standing on it (the plant and debris families).</summary>
    public static bool IsDressed(Rules rules, string terrain) => IsDressed(Of(rules, terrain));

    /// <summary>The same, by the terrain's look (a cell's, World.TerrainOf: no lookup by id).</summary>
    public static bool IsDressed(LookDef look) => look is { Family: Plant or Debris };

    /// <summary>Whether Dress draws a terrain family: the check that rules/looks.json and this file agree.</summary>
    public static bool Draws(string family) => family is Ground or Block or SpireFamily or Plant or Debris or Deck or PlateFamily or Machine or Wall;

    float CellSize => grid.cellSize;

    /// <summary>The dressing for a cell with its root at <paramref name="ground"/>, or null when nothing stands on it.</summary>
    public GameObject Build(Cell c, int x, int y, float ground)
    {
        bool wall = grid.World.IsRaisedWall(c);
        var look = Of(grid.World.Rules, c.T);
        bool parts = (HasParts(look) && !grid.IsBuiltWall(x, y, c)) || IsDrawnTree(look, x, y);
        bool stone = !wall && SteppingStone(c, x, y);
        if (!wall && !parts && !stone) return null;
        var center = grid.CellCenter(x, y);
        var surface = new Vector3(center.x, ground, center.z);
        var root = new GameObject($"{c.T} {x},{y}");
        root.transform.SetParent(grid.CellRoot(x, y), false);
        root.transform.position = surface;
        if (parts) Dress(root.transform, c.T, look, surface, x, y);
        if (wall) DressWall(root.transform, surface, c.Height, x, y);
        if (stone) DressSteppingStone(root.transform, surface, x, y);
        return root;
    }

    // Everything but flat ground, a wall its house draws and a tree the land drew.
    static bool HasParts(LookDef look) =>
        look != null && look.Family != Ground && !look.Baked && !(look.Family == Wall && look.Form == Built);

    void Dress(Transform root, string terrain, LookDef look, Vector3 surface, int x, int y)
    {
        var rng = new System.Random(x * 7919 + y * 104729 + terrain.GetHashCode());
        var color = ColorOf(look);
        switch (look.Family)
        {
            case Plant:
                DressPlant(root, look, color, surface, rng, x, y);
                break;
            case Debris when look.Form == Rubble:
                DressRubble(root, surface, rng, color);
                break;
            case Debris:
                DressDeadfall(root, surface, rng, color, x, y);
                break;
            case Deck:
                DressPlanks(root, surface, color, rng, x, y);
                break;
            case PlateFamily:
                DressPlate(root, surface, color, rng, x, y, look.Form == RustedForm);
                break;
            case Machine when look.Form == Coil:
                DressCoil(root, surface, color, x, y);
                break;
            case Machine:
                DressGate(root, surface, color, x, y, look.Form == GateOpen);
                break;
            case Wall:
                DressWornLogs(root, surface, color, rng, x, y);
                break;
            case Block or SpireFamily when grid.BoulderRestsAt(x, y):
                DressBoulder(root, surface, rng);
                break;
            case SpireFamily:
                DressSpires(root, look, color, surface, x, y);
                break;
            default:
                DressBlock(root, look, color, surface, x, y);
                break;
        }
    }

    void DressPlant(Transform root, LookDef look, Color color, Vector3 surface, System.Random rng, int x, int y)
    {
        switch (look.Form)
        {
            case Bush:
                DressBrush(root, surface, rng, color, x, y);
                break;
            case Stump:
                Part(root, PrimitiveType.Cylinder, surface + Vector3.up * 0.15f, new Vector3(0.45f, 0.15f, 0.45f), Tinted("Charred", color, StumpColor), color, 0.05f);
                break;
            default:
                DressTree(root, surface, rng, x, y, look.Form == Sapling);
                break;
        }
    }

    // A block of the look's height, colour, roughness and glow.
    void DressBlock(Transform root, LookDef look, Color color, Vector3 surface, int x, int y)
    {
        float tall = look.Height;
        var block = Part(root, PrimitiveType.Cube, surface + Vector3.up * (tall / 2f - 0.1f), new Vector3(CellSize, tall + 0.2f, CellSize), "Stone", color);
        var r = block.GetComponent<Renderer>();
        bool plain = look.Roughness < 0f && look.Glow <= 0f;
        if (plain)
        {
            Paint(r, "Stone", color, BlockSmoothness, x, y);
            return;
        }
        float smoothness = look.Roughness < 0f ? BlockSmoothness : 1f - look.Roughness;
        string name = $"Stone {smoothness:0.00}{(look.Glow > 0f ? " glow" : "")}";
        r.sharedMaterial = materials.Lit(name, Color.white, smoothness, look.Glow > 0f ? Color.black : null);
        if (look.Glow <= 0f)
        {
            Shade(r, color, x, y);
            return;
        }
        // A glowing block keeps its shade and glow in a property block: there are few of them.
        UrpMaterials.SetColor(r, tint, color * Jitter(x, y));
        r.GetPropertyBlock(tint);
        tint.SetColor(EmissionColorId, color * look.Glow);
        r.SetPropertyBlock(tint);
    }

    /// <summary>
    /// A brush cell's bushes put on a terrain made after them: on a generated map a cell can be dressed before its chunk's
    /// terrain stands, on the cell's own ground, and the terrain's smoothed heights then left bushes up to a metre off it
    /// (#220). MapTerrains calls this for each cell under a terrain it makes.
    /// </summary>
    public static void SeatBushes(GameObject dressing, Terrain t)
    {
        if (dressing == null || !dressing.name.StartsWith(BrushPrefix, System.StringComparison.Ordinal)) return;
        float baseY = t.transform.position.y, cellY = dressing.transform.position.y;
        // Each clump stands under a foot of its own, at the ground: the foot moves, the clump with it.
        foreach (Transform foot in dressing.transform)
        {
            var p = foot.position;
            float drawn = baseY + t.SampleHeight(p);
            if (Mathf.Abs(drawn - cellY) >= MostBushDrop) continue;
            p.y = drawn;
            foot.position = p;
        }
    }

    static readonly string BrushPrefix = TerrainIds.Brush + " ";

    // The ground drawn at a point: the chunk terrain's where one stands (its own sampling, as it draws), else the grid's.
    float DrawnGround(Vector3 at) =>
        MapTerrains.Instance != null && MapTerrains.Instance.TerrainAt(at) is { terrainData: not null } t
            ? t.transform.position.y + t.SampleHeight(at)
            : grid.GroundAt(at);

    // Brambles are tall grass: clumps of the terrain's own grass and ferns (its detail prototypes), not the round blobs they
    // were (Andrew, 2026-11-11: from a little way off they read as lily pads). Each clump sits on the ground drawn under
    // it, not the cell's middle: on a slope a clump half a cell off the middle floated or sank by up to a metre (#220),
    // and the drawn ground can stand off the cell's own height. Its foot is an empty at the ground (SeatBushes moves it).
    void DressBrush(Transform root, Vector3 surface, System.Random rng, Color color, int x, int y)
    {
        var grasses = Grasses();
        for (int i = 0; i < 6; i++)
        {
            var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (CellSize * 0.8f);
            float wide = 1f + 0.5f * (float)rng.NextDouble(), tall = 0.9f + 0.5f * (float)rng.NextDouble();
            var at = surface + off;
            float drawn = DrawnGround(at);
            if (Mathf.Abs(drawn - surface.y) < MostBushDrop) at.y = drawn;
            var foot = new GameObject("Clump").transform;
            foot.SetParent(root, false);
            foot.position = at;
            if (grasses.Length > 0)
            {
                var clump = Rock(foot, grasses[rng.Next(grasses.Length)], at, wide, tall, rng);
                foreach (var col in clump.GetComponentsInChildren<Collider>()) Object.Destroy(col);
                continue;
            }
            // No terrain pack: a few blades of the look's colour.
            for (int b = 0; b < 3; b++)
            {
                var blade = Part(foot, PrimitiveType.Cube, at + new Vector3(Jitter(rng, 0.15f), tall * 0.5f, Jitter(rng, 0.15f)), new Vector3(0.05f, tall, 0.12f), "Leaves", color);
                blade.transform.rotation = Quaternion.Euler(Jitter(rng, 20f), rng.Next(360), Jitter(rng, 20f));
                Paint(blade.GetComponent<Renderer>(), "Leaves", color, 0.1f, x + i * 17 + b, y);
            }
        }
    }

    // The terrain's grass and ferns (its detail prototypes' models: the pack's Grass_A..C and Fern_A), or none without the pack.
    static GameObject[] Grasses()
    {
        var template = MapTerrains.Instance != null ? MapTerrains.Instance.template : null;
        if (template == grassesOf && grasses != null) return grasses;
        grassesOf = template;
        var found = new System.Collections.Generic.List<GameObject>();
        if (template != null)
            foreach (var d in template.detailPrototypes)
                if (d.prototype != null && d.usePrototypeMesh && (d.prototype.name.StartsWith("Grass", System.StringComparison.Ordinal) || d.prototype.name.StartsWith("Fern", System.StringComparison.Ordinal)))
                    found.Add(d.prototype);
        return grasses = found.ToArray();
    }

    static GameObject[] grasses;
    static TerrainData grassesOf;

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

    // A shared material, in a per-cell shade so neighbouring parts don't read as one flat sheet.
    void Paint(Renderer r, string mat, Color color, float smoothness, int x, int y)
    {
        r.sharedMaterial = materials.Lit(mat, Color.white, smoothness);
        Shade(r, color, x, y);
    }

    // The renderer's material in the cell's shade: a shared copy (MaterialCache.Shaded), so the dressing batches.
    void Shade(Renderer r, Color color, int x, int y)
    {
        var shade = color * Jitter(x, y);
        var shaded = materials.Shaded(r.sharedMaterial, shade);
        if (shaded != null) r.sharedMaterial = shaded;
        else UrpMaterials.SetColor(r, tint, shade);
    }

    static float Jitter(int x, int y)
    {
        float n = Mathf.PerlinNoise(x * 0.37f + 0.11f, y * 0.37f + 0.53f);
        return 0.86f + 0.24f * n + 0.04f * (((x * 73856093) ^ (y * 19349663)) & 7) / 7f;
    }

    // A shared material's name for a part in a look's colour: its own name in the family's usual
    // colour, else one for that colour (the cache keeps the first colour a name is asked for).
    static string Tinted(string name, Color color, Color usual) =>
        color == usual ? name : $"{name} {ColorUtility.ToHtmlStringRGB(color)}";
}
