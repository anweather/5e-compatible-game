using System.Collections.Generic;
using Chem;
using UnityEngine;

// Oil and grease on the cells as one glossy film lying on the ground: a mesh
// over the covered cells with a rounded, wobbly outline (ContourSurface) that
// fades out at its edge, oil near black, grease a dull yellow. Mud and glass
// ground wear the same film, thinner: mud a wet, dark brown sheen over its
// darker paint (CellPaint), glass a pale, glassy glaze over sand. A cover's
// film wins over its ground's. Rebuilt when a cell's cover or terrain changes.
// Ash is not a film: it is the terrain's charred paint. Ground acid has eaten
// (ChemGrid.WatchAcid) wears a stain in the same film, a dark, sour green, for
// a while (Eaten, Expire); a slick there wins over it. A falling star's target
// mark (Cell.Star) is a hot glow in the same film, over everything else.
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
    static readonly Color StarColor = new(1f, 0.45f, 0.08f, 0.7f);
    const string StarFilm = "star";

    readonly ChemGrid grid;
    readonly Dictionary<int, string> slicks = new();
    // Cells acid ate, and until when their stain shows.
    readonly Dictionary<int, float> eaten = new();
    readonly List<int> expired = new();
    readonly ContourSurface surface;
    bool dirty;
    // ColorAt's channels, made once (Shade asks for each at every point of the film).
    readonly System.Func<int, int, float?> redAt, greenAt, blueAt, alphaAt;

    public CellSlickMesh(ChemGrid grid, Material material)
    {
        this.grid = grid;
        surface = new ContourSurface(grid, "Slicks", material, Outline, 4f);
        redAt = (x, y) => ColorAt(x, y)?.r;
        greenAt = (x, y) => ColorAt(x, y)?.g;
        blueAt = (x, y) => ColorAt(x, y)?.b;
        alphaAt = (x, y) => ColorAt(x, y)?.a;
    }

    /// <summary>True for a cover drawn as a film (oil, grease).</summary>
    public static bool IsSlick(string cover) => cover == CoverIds.Oil || cover == CoverIds.Grease;

    /// <summary>True for a terrain drawn with a film (mud, glass).</summary>
    public static bool IsGlazed(string terrain) => terrain == TerrainIds.Mud || terrain == TerrainIds.Glass;

    /// <summary>The film a cell wears: its cover's (oil, grease), else its ground's (mud, glass), else null.</summary>
    public static string FilmOf(Cell c)
    {
        if (c.Star > 0) return StarFilm;
        if (IsSlick(c.Cover)) return c.Cover;
        return IsGlazed(c.T) ? c.T : null;
    }

    /// <summary>A cell's cover or terrain changed: the film is rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, Cell c)
    {
        int key = grid.World.Idx(x, y);
        var film = FilmOf(c);
        bool had = slicks.TryGetValue(key, out var was);
        if (film != null) slicks[key] = film;
        else slicks.Remove(key);
        dirty |= had ? was != film : film != null;
    }

    /// <summary>Ground moved somewhere: the film follows it at the next Rebuild.</summary>
    public void GroundChanged() => dirty |= slicks.Count > 0 || eaten.Count > 0;

    /// <summary>Acid ate something at a cell: it wears the eaten stain until <paramref name="until"/> (Time.time).</summary>
    public void Eaten(int x, int y, float until)
    {
        eaten[grid.World.Idx(x, y)] = until;
        dirty = true;
    }

    /// <summary>Stains past their time go, at the next Rebuild.</summary>
    public void Expire(float now)
    {
        if (eaten.Count == 0) return;
        expired.Clear();
        foreach (var kv in eaten)
            if (kv.Value <= now) expired.Add(kv.Key);
        foreach (int key in expired) eaten.Remove(key);
        dirty |= expired.Count > 0;
    }

    /// <summary>Rebuilds the film if any cell's slick changed since the last time.</summary>
    public void Rebuild()
    {
        if (!dirty) return;
        dirty = false;
        surface.Build(Box(), (x, y) => Has(x, y) ? 1f : 0f,
            Height, Shade, (x, y) => BesideStep(x, y) ? 0f : 1f);
    }

    // On the ground, but never dipping below the slick cells' own level: past a pit's lip the
    // film stays level (and faded, see Shade) instead of bending down, which would catch the
    // light along the bend.
    float Height(float gx, float gy)
    {
        float ground = grid.GroundAt(surface.Flat(gx, gy));
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

    RectInt Box()
    {
        int w = grid.World.W, x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int key in Keys())
        {
            x0 = Mathf.Min(x0, key % w);
            y0 = Mathf.Min(y0, key / w);
            x1 = Mathf.Max(x1, key % w);
            y1 = Mathf.Max(y1, key / w);
        }
        return x1 < x0 ? new RectInt(0, 0, 0, 0) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    IEnumerable<int> Keys()
    {
        foreach (int key in slicks.Keys) yield return key;
        foreach (int key in eaten.Keys) yield return key;
    }

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
        float ex = grid.GroundAt(surface.Flat(gx + d, gy)) - grid.GroundAt(surface.Flat(gx - d, gy));
        float ey = grid.GroundAt(surface.Flat(gx, gy + d)) - grid.GroundAt(surface.Flat(gx, gy - d));
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
            StarFilm => StarColor,
            _ => OilColor,
        };
    }

    public void Destroy() => surface.Destroy();
}
