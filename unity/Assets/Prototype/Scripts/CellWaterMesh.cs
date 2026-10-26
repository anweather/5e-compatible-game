using System.Collections.Generic;
using Chem;
using UnityEngine;

// The water on the cells (the pond, the river, trenches, puddles; the lake
// has its own surface): a mesh over the wet cells with a rounded, wobbly
// outline (ContourSurface), and a second one for ice, which is solid to walk
// on. Both are drawn in tiles of TileCells × TileCells cells, each its own
// mesh (and, for ice, its own collider), and only the tiles near a change
// are rebuilt: the river runs the length of the valley, and one mesh over all
// of it took over 100 ms to rebuild for a single puddle.
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

    /// <summary>Cells a tile side.</summary>
    public const int TileCells = 32;

    readonly ChemGrid grid;
    readonly Material waterMaterial, iceMaterial;
    readonly HashSet<int> wet = new();
    readonly Dictionary<int, float> tops = new();
    // The tops before the last ComputeTops: a body of water whose level moved redraws where it did.
    readonly Dictionary<int, float> oldTops = new();
    // Tiles to redraw at the next Rebuild (a tile key: ty * tilesX + tx), and each tile's surfaces.
    readonly HashSet<int> dirtyTiles = new();
    readonly Dictionary<int, (ContourSurface water, ContourSurface ice)> tiles = new();
    readonly int tilesX;
    readonly GameObject root;
    readonly HashSet<int> banks = new();
    // Banks and the wet cells beside them: the outline doesn't wobble there, or it would pull
    // back off the bank's slope (a gap) or climb over it (water showing through the ground).
    readonly HashSet<int> calm = new();

    public CellWaterMesh(ChemGrid grid, Material waterMaterial, Material iceMaterial)
    {
        this.grid = grid;
        // One instance of the water material for every tile, so the ripples drift together as the lake's do.
        this.waterMaterial = new Material(waterMaterial) { name = waterMaterial.name };
        this.iceMaterial = iceMaterial;
        tilesX = (grid.World.W + TileCells - 1) / TileCells;
        root = new GameObject("Water and ice");
        root.transform.SetParent(grid.transform, false);
    }

    /// <summary>A cell's water changed (or its ground did): the tiles round it are redrawn at the next Rebuild.</summary>
    public void Changed(int x, int y, bool isWet)
    {
        int key = grid.World.Idx(x, y);
        if (isWet) wet.Add(key);
        else wet.Remove(key);
        MarkAround(x, y);
    }

    /// <summary>Ground moved at a cell: the water beside it may sit differently against its banks.</summary>
    public void GroundChanged(int x, int y)
    {
        if (wet.Count > 0) MarkAround(x, y);
    }

    // A cell's change reaches two cells round it (its outline reads one ring out, a bank's another):
    // the tiles under that square are redrawn.
    void MarkAround(int x, int y)
    {
        int tx0 = Mathf.Max(0, (x - 2) / TileCells), tx1 = Mathf.Min(tilesX - 1, (x + 2) / TileCells);
        int ty0 = Mathf.Max(0, (y - 2) / TileCells), ty1 = (y + 2) / TileCells;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++) dirtyTiles.Add(ty * tilesX + tx);
    }

    /// <summary>The water surface drawn at a wet cell after the last Rebuild, or <paramref name="fallback"/>.</summary>
    public float Top(int x, int y, float fallback) => tops.TryGetValue(grid.World.Idx(x, y), out var t) ? t : fallback;

    /// <summary>Drifts the water's ripples, as the lake's do (once a frame).</summary>
    public void Scroll(float time) => waterMaterial.SetTextureOffset("_BaseMap", new Vector2(0.004f, 0.003f) * time);

    /// <summary>Redraws the tiles near any change since the last time.</summary>
    public void Rebuild()
    {
        if (dirtyTiles.Count == 0) return;
        oldTops.Clear();
        foreach (var kv in tops) oldTops[kv.Key] = kv.Value;
        ComputeTops();
        // Where a body of water's level moved, far from the cell that changed, redraw there too.
        var world = grid.World;
        foreach (var kv in tops)
            if (!oldTops.TryGetValue(kv.Key, out var was) || !Mathf.Approximately(was, kv.Value)) MarkAround(kv.Key % world.W, kv.Key / world.W);
        FindBanks();
        foreach (int tile in dirtyTiles) DrawTile(tile);
        dirtyTiles.Clear();
    }

    // A tile's water and ice: made the first time it has any, emptied when it has none.
    void DrawTile(int tile)
    {
        int tx = tile % tilesX, ty = tile / tilesX;
        var world = grid.World;
        int x0 = tx * TileCells, y0 = ty * TileCells;
        var box = new RectInt(x0, y0, Mathf.Min(TileCells, world.W - x0), Mathf.Min(TileCells, world.H - y0));
        if (box.width <= 0 || box.height <= 0) return;
        if (!tiles.TryGetValue(tile, out var t))
        {
            if (!AnyWetNear(box)) return;
            t = (new ContourSurface(grid, "Water", waterMaterial, Outline, RippleMetres, tiled: true),
                 new ContourSurface(grid, "Ice", iceMaterial, Outline, RippleMetres, tiled: true));
            t.water.go.transform.SetParent(root.transform, true);
            t.ice.go.transform.SetParent(root.transform, true);
            t.ice.go.AddComponent<MeshCollider>();
            tiles[tile] = t;
        }
        Draw(t.water, box, false);
        Draw(t.ice, box, true);
        t.ice.go.GetComponent<MeshCollider>().sharedMesh = t.ice.Empty ? null : t.ice.mesh;
    }

    // Whether any cell in the box, or the ring round it, holds water (a tile also draws the edge of
    // water lying just past it).
    bool AnyWetNear(RectInt box)
    {
        var world = grid.World;
        for (int y = box.yMin - 1; y <= box.yMax; y++)
            for (int x = box.xMin - 1; x <= box.xMax; x++)
                if (world.InBounds(x, y) && wet.Contains(world.Idx(x, y))) return true;
        return false;
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

    // The water (or the ice) of one tile's wet cells: their outline, each point at the blended top
    // of the wet cells around it, fading out toward an edge on open ground.
    void Draw(ContourSurface surface, RectInt box, bool frozen)
    {
        surface.Build(box, (x, y) => Value(x, y, frozen),
            (gx, gy) => ContourSurface.BlendAround(gx, gy, (x, y) => TopOf(x, y, frozen), 0f),
            (gx, gy, v) => new Color(1f, 1f, 1f, Opacity(v, frozen)),
            (x, y) => grid.World.InBounds(x, y) && calm.Contains(grid.World.Idx(x, y)) ? 0f : 1f);
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
        foreach (var t in tiles.Values)
        {
            t.water.Destroy();
            t.ice.Destroy();
        }
        tiles.Clear();
        Object.Destroy(waterMaterial);
        Object.Destroy(root);
    }
}
