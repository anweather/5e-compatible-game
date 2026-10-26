using System.Collections.Generic;
using Chem;
using UnityEngine;

// The water on the cells (the pond, trenches, puddles; the lake has its own
// surface) as one surface: a mesh over the wet cells with a rounded, wobbly
// outline (ContourSurface), rebuilt when they change, and a second one for
// ice, which is solid to walk on.
//
// Each wet cell's surface is its ground plus its water depth, but never above
// the land around it:
//  - a body of water (wet cells connected to each other) stays a little under
//    the lowest bank around it, a bank being dry ground higher than the wet
//    cell beside it, so a full pond reads as a pond below the meadow and not as
//    a slab on it;
//  - where a wet cell meets dry ground no higher than its own (a puddle on flat
//    ground, water reaching the dry end of a fresh trench), it is a thin film.
// Points between cells take the blended top of the wet cells around them, so
// neighbouring cells meet without steps. The outline reaches a quarter cell
// into a bank, under the terrain; on open ground it rounds off inside the wet
// cells and fades out, so a puddle is a blob with a soft edge, not a square.
public sealed class CellWaterMesh
{
    // How far the water stays under the land around it, and a puddle's film over its ground.
    const float Inset = 0.05f, Film = 0.03f;
    // A bank's value for the outline (CellContour, iso 0.5): the edge reaches a quarter cell into
    // it (one heightmap sample on a full-detail chunk, where the bank has risen above the water).
    const float BankValue = 0.34f, ShoreValue = 0.45f, IceLift = 0.01f;
    // The outline: 4 lattice points per cell, wobbly enough that a run of cells isn't a straight shore; how far in from the edge the water
    // turns opaque (in blended value), and the texture's repeat (calmer than the lake's 10 m).
    static readonly ContourSpec Outline = new(4, 0.5f, 0.7f, 31);
    const float FadeWidth = 0.3f, RippleMetres = 16f;

    readonly ChemGrid grid;
    readonly HashSet<int> wet = new();
    readonly Dictionary<int, float> tops = new();
    bool dirty;

    readonly ContourSurface water, ice;
    readonly HashSet<int> banks = new();
    // Banks and the wet cells beside them: the outline doesn't wobble there, or it would pull
    // back off the bank's slope (a gap) or climb over it (water showing through the ground).
    readonly HashSet<int> calm = new();

    public CellWaterMesh(ChemGrid grid, Material waterMaterial, Material iceMaterial)
    {
        this.grid = grid;
        water = new ContourSurface(grid, "Water", waterMaterial, Outline, RippleMetres);
        // The water's ripples drift, as the lake's do.
        water.go.AddComponent<ScrollingTexture>().speed = new Vector2(0.004f, 0.003f);
        ice = new ContourSurface(grid, "Ice", iceMaterial, Outline, RippleMetres);
        ice.go.AddComponent<MeshCollider>();
    }

    /// <summary>A cell's water changed (or its ground did): the surfaces are rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, bool isWet)
    {
        int key = grid.World.Idx(x, y);
        if (isWet) wet.Add(key);
        else wet.Remove(key);
        dirty = true;
    }

    /// <summary>Ground moved somewhere: the land around the water may have changed.</summary>
    public void GroundChanged() => dirty |= wet.Count > 0;

    /// <summary>The water surface drawn at a wet cell after the last Rebuild, or <paramref name="fallback"/>.</summary>
    public float Top(int x, int y, float fallback) => tops.TryGetValue(grid.World.Idx(x, y), out var t) ? t : fallback;

    /// <summary>Rebuilds both surfaces if any wet cell changed since the last time.</summary>
    public void Rebuild()
    {
        if (!dirty) return;
        dirty = false;
        ComputeTops();
        FindBanks();
        Draw(water, false);
        Draw(ice, true);
        ice.go.GetComponent<MeshCollider>().sharedMesh = ice.Empty ? null : ice.mesh;
    }

    // --- heights -------------------------------------------------------------------

    void ComputeTops()
    {
        var world = grid.World;
        var cap = new Dictionary<int, float>();
        var queue = new Queue<int>();
        foreach (int key in wet)
        {
            cap[key] = LowestBank(key);
            queue.Enqueue(key);
        }
        // Each body of water takes the lowest dry ground around any of its cells.
        while (queue.Count > 0)
        {
            int key = queue.Dequeue();
            foreach (int n in WetNeighbours(key))
            {
                if (cap[n] <= cap[key]) continue;
                cap[n] = cap[key];
                queue.Enqueue(n);
            }
        }
        tops.Clear();
        float perUnit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        foreach (int key in wet)
        {
            int x = key % world.W, y = key / world.W;
            var c = world.Cells[key];
            float ground = grid.GroundTop(x, y, c);
            float top = Mathf.Min(ground + c.Water * perUnit, cap[key] - Inset);
            if (MeetsLowGround(key, ground)) top = Mathf.Min(top, ground + Film);
            // Ice on the lake floats at the lake's surface, however deep the water under it.
            tops[key] = Mathf.Max(Mathf.Max(ground + Film, top), grid.LakeLevelAt(x, y, c) + Film);
        }
    }

    // The lowest bank: a dry neighbour (of 8) higher than the cell's own ground.
    float LowestBank(int key)
    {
        float own = OwnGround(key), lowest = float.PositiveInfinity;
        foreach (float ground in DryAround(key))
            if (ground > own + Level) lowest = Mathf.Min(lowest, ground);
        return lowest;
    }

    // Whether a dry neighbour (of 8) is no higher than the cell's own ground.
    bool MeetsLowGround(int key, float own)
    {
        foreach (float ground in DryAround(key))
            if (ground <= own + Level) return true;
        return false;
    }

    // Grounds closer than this count as level.
    const float Level = 0.01f;

    float OwnGround(int key) => grid.GroundTop(key % grid.World.W, key / grid.World.W, grid.World.Cells[key]);

    // The ground of each dry neighbour (of 8).
    IEnumerable<float> DryAround(int key)
    {
        var world = grid.World;
        int x = key % world.W, y = key / world.W;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (world.InBounds(nx, ny) && !wet.Contains(world.Idx(nx, ny)))
                    yield return grid.GroundTop(nx, ny, world.Cells[world.Idx(nx, ny)]);
            }
        }
    }

    IEnumerable<int> WetNeighbours(int key)
    {
        var world = grid.World;
        int x = key % world.W, y = key / world.W;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + World.DirX(d), ny = y + World.DirY(d);
            if (world.InBounds(nx, ny) && wet.Contains(world.Idx(nx, ny))) yield return world.Idx(nx, ny);
        }
    }

    // --- the mesh --------------------------------------------------------------------

    // Dry cells beside the water that stand above its surface: the outline reaches into them.
    void FindBanks()
    {
        var world = grid.World;
        banks.Clear();
        calm.Clear();
        foreach (int key in wet)
        {
            int x = key % world.W, y = key / world.W;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Ring[d].dx, ny = y + Ring[d].dy;
                if (!world.InBounds(nx, ny)) continue;
                int n = world.Idx(nx, ny);
                if (wet.Contains(n) || grid.GroundTop(nx, ny, world.Cells[n]) <= tops[key]) continue;
                banks.Add(n);
                calm.Add(n);
                calm.Add(key);
            }
        }
    }

    static readonly (int dx, int dy)[] Ring = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    // The water (or the ice) of the wet cells: their outline, each point at the blended top of
    // the wet cells around it, fading out toward an edge on open ground.
    void Draw(ContourSurface surface, bool frozen)
    {
        var box = Box(frozen);
        if (box.width <= 0)
        {
            surface.Build(new RectInt(0, 0, 0, 0), (x, y) => 0f, (x, y) => 0f, (x, y, v) => Color.clear);
            return;
        }
        surface.Build(box, (x, y) => Value(x, y, frozen),
            (gx, gy) => ContourSurface.BlendAround(gx, gy, (x, y) => TopOf(x, y, frozen), 0f),
            (gx, gy, v) => new Color(1f, 1f, 1f, Opacity(v, frozen)),
            (x, y) => grid.World.InBounds(x, y) && calm.Contains(grid.World.Idx(x, y)) ? 0f : 1f);
    }

    // The bounding box of the wet cells that are (or aren't) frozen; empty if there are none.
    RectInt Box(bool frozen)
    {
        var world = grid.World;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int key in wet)
        {
            if (world.Cells[key].Frozen != frozen) continue;
            int x = key % world.W, y = key / world.W;
            x0 = Mathf.Min(x0, x);
            y0 = Mathf.Min(y0, y);
            x1 = Mathf.Max(x1, x);
            y1 = Mathf.Max(y1, y);
        }
        return x1 < x0 ? new RectInt(0, 0, 0, 0) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    bool Holds(int x, int y, bool frozen)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return false;
        int key = world.Idx(x, y);
        return wet.Contains(key) && world.Cells[key].Frozen == frozen;
    }

    // In, a bank or the other kind (water beside ice: they meet under each other's edge), or out.
    float Value(int x, int y, bool frozen)
    {
        if (Holds(x, y, frozen)) return 1f;
        if (Holds(x, y, !frozen)) return ShoreValue;
        return grid.World.InBounds(x, y) && banks.Contains(grid.World.Idx(x, y)) ? BankValue : 0f;
    }

    // Ice stands a hair above the water it meets, so the two never flicker where they overlap.
    float? TopOf(int x, int y, bool frozen)
    {
        if (!Holds(x, y, frozen)) return null;
        return tops[grid.World.Idx(x, y)] + (frozen ? IceLift : 0f);
    }

    // Opaque inside; toward an edge on open ground, fading out (an edge under a bank is hidden
    // by the bank anyway). Ice stays solid.
    static float Opacity(float value, bool frozen) =>
        frozen ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value));

    public void Destroy()
    {
        water.Destroy();
        ice.Destroy();
    }
}
