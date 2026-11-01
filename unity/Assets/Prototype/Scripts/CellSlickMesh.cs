using System.Collections.Generic;
using Chem;
using UnityEngine;

// Oil and grease on the cells as one glossy film lying on the ground: a mesh
// over the covered cells with a rounded, wobbly outline (ContourSurface) that
// fades out at its edge, oil near black, grease a dull yellow. Mud and glass
// ground wear the same film, thinner: mud a wet, dark brown sheen over its
// darker paint (CellPaint), glass a pale, glassy glaze over sand, brick a
// baked red (its courses are decals, CellCrackMesh). A cover's film wins over its ground's. Rebuilt when a cell's cover or terrain changes.
// Ash is not a film: it is the terrain's charred paint. Ground acid has eaten
// (ChemGrid.WatchAcid) wears a stain in the same film, a dark, sour green, for
// a while (Eaten, Expire); a slick there wins over it. A falling star's target
// mark (Cell.Star) is a hot glow in the same film, over everything else. Frost
// is a pale glaze in it, and the rock lava leaves (Crust) a dark, cindery crust.
// In tiles, as the water is (CellWaterMesh): a change redraws the tiles round it, nearest the
// camera first, within a few milliseconds a frame, so a storm's mud never stalls one.
public sealed class CellSlickMesh
{
    // How far the film floats over the terrain, how far in from its edge it turns opaque (in
    // blended value), and the outline: 4 lattice points per cell, more wobble than water.
    const float Lift = 0.025f, FadeWidth = 0.35f;
    // A step in the ground (metres) beside a cell that holds the outline still there: a wobble
    // over a pit's lip would drape the film down its side.
    const float Step = 0.1f;
    // Ground steeper than this (rise over run) sheds the film: none clings to a pit's side.
    const float FlatGrade = 0.12f, SteepGrade = 0.35f;
    static readonly ContourSpec Outline = new(4, 0.5f, 0.8f, 47);
    static readonly Color OilColor = new(0.03f, 0.028f, 0.03f, 0.93f);
    static readonly Color GreaseColor = new(0.62f, 0.52f, 0.2f, 0.85f);
    static readonly Color MudColor = new(0.17f, 0.11f, 0.06f, 0.5f);
    static readonly Color GlassColor = new(0.2f, 0.31f, 0.35f, 0.42f);
    static readonly Color EatenColor = new(0.16f, 0.22f, 0.07f, 0.55f);
    static readonly Color StarColor = new(0.95f, 0.2f, 0.03f, 0.8f);
    static readonly Color BrickColor = new(0.27f, 0.095f, 0.06f, 0.95f);
    static readonly Color FrostColor = new(0.8f, 0.9f, 0.97f, 0.62f);
    static readonly Color CrustColor = new(0.06f, 0.05f, 0.05f, 0.82f);
    const string StarFilm = "star", CrustFilm = "crust";

    readonly ChemGrid grid;
    readonly Dictionary<int, string> slicks = new();
    // Rock cells that were lava (a look only: the core's cell is plain rock).
    readonly HashSet<int> crusts = new();
    // Cells acid ate, and until when their stain shows.
    readonly Dictionary<int, float> eaten = new();
    readonly List<int> expired = new();
    const int TileCells = 32;
    const double DrawBudgetMs = 2.0;
    const int ExactDrawTiles = 2;
    readonly Material material;
    readonly int tilesX;
    readonly GameObject root;
    readonly Dictionary<int, ContourSurface> tiles = new();
    readonly HashSet<int> dirtyTiles = new();
    readonly List<int> drawOrder = new();
    readonly ChemGrid.FrameBudget drawBudget = new(DrawBudgetMs, ExactDrawTiles);
    // What a tile's Build asks, made once.
    readonly System.Func<int, int, float> filmAt, wobbleAt;
    readonly System.Func<float, float, float> heightAt;
    readonly System.Func<float, float, float, Color> shadeAt;
    // ColorAt's channels, made once (Shade asks for each at every point of the film).
    readonly System.Func<int, int, float?> redAt, greenAt, blueAt, alphaAt;

    public CellSlickMesh(ChemGrid grid, Material material)
    {
        this.grid = grid;
        this.material = material;
        tilesX = (grid.World.W + TileCells - 1) / TileCells;
        root = new GameObject("Slicks");
        root.transform.SetParent(grid.transform, false);
        filmAt = (x, y) => Has(x, y) ? 1f : 0f;
        wobbleAt = (x, y) => BesideStep(x, y) ? 0f : 1f;
        heightAt = Height;
        shadeAt = Shade;
        redAt = (x, y) => ColorAt(x, y)?.r;
        greenAt = (x, y) => ColorAt(x, y)?.g;
        blueAt = (x, y) => ColorAt(x, y)?.b;
        alphaAt = (x, y) => ColorAt(x, y)?.a;
    }

    /// <summary>True for a cover drawn as a film (oil, grease).</summary>
    public static bool IsSlick(string cover) => cover == CoverIds.Oil || cover == CoverIds.Grease;

    /// <summary>A cell was redrawn: rock that <paramref name="cooled"/> from lava wears a crust; anything else loses it.</summary>
    public void Crust(int x, int y, Cell c, bool cooled)
    {
        int key = grid.World.Idx(x, y);
        if (cooled && c.T == TerrainIds.Rock) crusts.Add(key);
        else if (c.T != TerrainIds.Rock) crusts.Remove(key);
    }

    /// <summary>True for a terrain drawn with a film (mud, glass, brick).</summary>
    public static bool IsGlazed(string terrain) => terrain == TerrainIds.Mud || terrain == TerrainIds.Glass || terrain == TerrainIds.Brick;

    /// <summary>The film a cell wears: its cover's (oil, grease), else its ground's (mud, glass), else null.</summary>
    public static string FilmOf(Cell c)
    {
        if (c.Star > 0) return StarFilm;
        if (IsSlick(c.Cover) || c.Cover == CoverIds.Frost) return c.Cover;
        return IsGlazed(c.T) ? c.T : null;
    }

    /// <summary>A cell's cover or terrain changed: the film is rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, Cell c)
    {
        int key = grid.World.Idx(x, y);
        var film = FilmOf(c) ?? (crusts.Contains(key) ? CrustFilm : null);
        bool had = slicks.TryGetValue(key, out var was);
        if (film != null) slicks[key] = film;
        else slicks.Remove(key);
        if (PerfCounts.On && (had ? was != film : film != null)) PerfCounts.Add("slick.changed." + (film ?? "none_was_" + was));
        if (had ? was != film : film != null) MarkAround(x, y);
    }

    /// <summary>Ground moved at a cell: the film near it follows at the next Rebuild.</summary>
    public void GroundChanged(int x, int y)
    {
        PerfCounts.Add("slick.ground_changed");
        if (slicks.Count > 0 || eaten.Count > 0) MarkAround(x, y);
    }

    // A cell's change reaches two cells round it (the outline reads one ring out, the wobble another).
    void MarkAround(int x, int y)
    {
        int tx0 = Mathf.Max(0, (x - 2) / TileCells), tx1 = Mathf.Min(tilesX - 1, (x + 2) / TileCells);
        int ty0 = Mathf.Max(0, (y - 2) / TileCells), ty1 = (y + 2) / TileCells;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++) dirtyTiles.Add(ty * tilesX + tx);
    }

    /// <summary>Acid ate something at a cell: it wears the eaten stain until <paramref name="until"/> (Time.time).</summary>
    public void Eaten(int x, int y, float until)
    {
        eaten[grid.World.Idx(x, y)] = until;
        MarkAround(x, y);
    }

    /// <summary>Stains past their time go, at the next Rebuild.</summary>
    public void Expire(float now)
    {
        if (eaten.Count == 0) return;
        expired.Clear();
        foreach (var kv in eaten)
            if (kv.Value <= now) expired.Add(kv.Key);
        foreach (int key in expired)
        {
            eaten.Remove(key);
            MarkAround(key % grid.World.W, key / grid.World.W);
        }
    }

    /// <summary>Redraws the tiles near any change since the last time, nearest the camera first, as far as the frame's budget goes.</summary>
    public void Rebuild()
    {
        if (dirtyTiles.Count == 0) return;
        PerfCounts.Add("slick.rebuilds");
        PerfCounts.Peak("slick.film_cells", slicks.Count + eaten.Count);
        drawBudget.Start();
        drawOrder.Clear();
        drawOrder.AddRange(dirtyTiles);
        var cam = Camera.main;
        if (cam != null && drawOrder.Count > 1)
        {
            var eye = cam.transform.position;
            drawOrder.Sort((a, b) => TileDistance(a, eye).CompareTo(TileDistance(b, eye)));
        }
        foreach (int tile in drawOrder)
        {
            if (drawBudget.Over()) break;
            DrawTile(tile);
            dirtyTiles.Remove(tile);
        }
    }

    // Squared distance on the ground from a point to a tile's middle.
    float TileDistance(int tile, Vector3 eye)
    {
        int cx = (tile % tilesX) * TileCells + TileCells / 2, cy = (tile / tilesX) * TileCells + TileCells / 2;
        var mid = grid.CellCenter(Mathf.Min(cx, grid.World.W - 1), Mathf.Min(cy, grid.World.H - 1));
        float dx = mid.x - eye.x, dz = mid.z - eye.z;
        return dx * dx + dz * dz;
    }

    // A tile's film: made the first time it has any, emptied when it has none.
    void DrawTile(int tile)
    {
        var world = grid.World;
        int x0 = tile % tilesX * TileCells, y0 = tile / tilesX * TileCells;
        var box = new RectInt(x0, y0, Mathf.Min(TileCells, world.W - x0), Mathf.Min(TileCells, world.H - y0));
        if (box.width <= 0 || box.height <= 0) return;
        if (!tiles.TryGetValue(tile, out var surface))
        {
            if (!AnyNear(box)) return;
            surface = new ContourSurface(grid, "Slicks", material, Outline, 4f, tiled: true);
            surface.go.transform.SetParent(root.transform, true);
            tiles[tile] = surface;
        }
        surface.Build(box, filmAt, heightAt, shadeAt, wobbleAt);
    }

    // Whether any cell in the box, or the ring round it, wears a film (a tile also draws the edge of one lying just past it).
    bool AnyNear(RectInt box)
    {
        for (int y = box.yMin - 1; y <= box.yMax; y++)
            for (int x = box.xMin - 1; x <= box.xMax; x++)
                if (Has(x, y)) return true;
        return false;
    }

    // On the ground, but never dipping below the slick cells' own level: past a pit's lip the
    // film stays level (and faded, see Shade) instead of bending down, which would catch the
    // light along the bend.
    float Height(float gx, float gy)
    {
        float ground = grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy));
        float level = ContourSurface.BlendAround(gx, gy, (x, y) => Has(x, y) ? grid.GroundTop(x, y, grid.World.At(x, y)) : null, ground);
        return Mathf.Max(ground, level) + Lift;
    }

    // Whether the ground beside a cell (its 8 neighbours) is a step up or down from it.
    bool BesideStep(int x, int y)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return false;
        float top = grid.GroundTop(x, y, world.At(x, y));
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (world.InBounds(x + dx, y + dy) && Mathf.Abs(grid.GroundTop(x + dx, y + dy, world.At(x + dx, y + dy)) - top) > Step)
                    return true;
        return false;
    }

    bool Has(int x, int y) => grid.World.InBounds(x, y) && (slicks.ContainsKey(grid.World.Idx(x, y)) || eaten.ContainsKey(grid.World.Idx(x, y)));

    // The films' colours (blended where they meet), fading out toward the edge and on steep ground.
    Color Shade(float gx, float gy, float value)
    {
        var c = new Color(
            ContourSurface.BlendAround(gx, gy, redAt, 0f),
            ContourSurface.BlendAround(gx, gy, greenAt, 0f),
            ContourSurface.BlendAround(gx, gy, blueAt, 0f),
            ContourSurface.BlendAround(gx, gy, alphaAt, 0f));
        c.a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value));
        c.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatGrade, SteepGrade, Grade(gx, gy)));
        return c;
    }

    // How steep the ground is at a point (grid units): the larger rise over run across a quarter cell.
    float Grade(float gx, float gy)
    {
        const float d = 0.125f;
        float run = 2f * d * grid.cellSize;
        float ex = grid.GroundAt(ContourSurface.FlatOn(grid, gx + d, gy)) - grid.GroundAt(ContourSurface.FlatOn(grid, gx - d, gy));
        float ey = grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy + d)) - grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy - d));
        return Mathf.Max(Mathf.Abs(ex), Mathf.Abs(ey)) / run;
    }

    // A cell's film colour, or null for a cell without a film.
    Color? ColorAt(int x, int y)
    {
        if (!Has(x, y)) return null;
        if (!slicks.TryGetValue(grid.World.Idx(x, y), out var film)) return EatenColor;
        return film switch
        {
            CoverIds.Grease => GreaseColor,
            TerrainIds.Mud => MudColor,
            TerrainIds.Glass => GlassColor,
            TerrainIds.Brick => BrickColor,
            StarFilm => StarColor,
            CoverIds.Frost => FrostColor,
            CrustFilm => CrustColor,
            _ => OilColor,
        };
    }

    public void Destroy()
    {
        foreach (var surface in tiles.Values) surface.Destroy();
        Object.Destroy(root);
    }
}
