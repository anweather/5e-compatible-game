using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The water on the cells (the pond, trenches, puddles; the lake has its own
// surface) as one surface: a mesh over the wet cells, rebuilt when they
// change, and a second one for ice, which is solid to walk on.
//
// Each wet cell's surface is its ground plus its water depth, but never above
// the land around it:
//  - a body of water (wet cells connected to each other) stays a little under
//    the lowest bank around it, a bank being dry ground higher than the wet
//    cell beside it, so a full pond reads as a pond below the meadow and not as
//    a slab on it;
//  - where a wet cell meets dry ground no higher than its own (a puddle on flat
//    ground, water reaching the dry end of a fresh trench), it is a thin film. Corners take the mean of the wet cells that share them,
// so neighbouring cells meet without steps, and the edge facing dry ground
// reaches into the bank, under the terrain.
public sealed class CellWaterMesh
{
    // How far the water stays under the land around it, a puddle's film over its ground, and
    // how far an edge reaches into the bank: one heightmap sample on a full-detail chunk (a
    // quarter cell), where the bank has risen to the dry cell's ground, above the water.
    const float Inset = 0.05f, Film = 0.03f, Tuck = 0.375f;

    readonly ChemGrid grid;
    readonly HashSet<int> wet = new();
    readonly Dictionary<int, float> tops = new();
    readonly Surface water, ice;
    bool dirty;

    sealed class Surface
    {
        public readonly Mesh mesh = new() { name = "Cell water" };
        public readonly GameObject go;
        public readonly List<Vector3> vertices = new();
        public readonly List<Vector2> uvs = new();
        public readonly List<int> triangles = new();

        public Surface(Transform parent, string name, Material material)
        {
            mesh.MarkDynamic();
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Clear()
        {
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        public void Apply()
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    public CellWaterMesh(ChemGrid grid, Material waterMaterial, Material iceMaterial)
    {
        this.grid = grid;
        water = new Surface(grid.transform, "Water", waterMaterial);
        // The lake material's own ripples drift, as the lake's do.
        water.go.AddComponent<ScrollingTexture>().speed = new Vector2(0.004f, 0.003f);
        ice = new Surface(grid.transform, "Ice", iceMaterial);
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
        water.Clear();
        ice.Clear();
        foreach (int key in wet) AddCell(key);
        water.Apply();
        ice.Apply();
        ice.go.GetComponent<MeshCollider>().sharedMesh = ice.triangles.Count > 0 ? ice.mesh : null;
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
            tops[key] = Mathf.Max(ground + Film, top);
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

    // One quad per wet cell, its corners at the mean top of the wet cells sharing them, pushed
    // out into the bank on sides that face dry ground.
    void AddCell(int key)
    {
        var world = grid.World;
        int x = key % world.W, y = key / world.W;
        var surface = world.Cells[key].Frozen ? ice : water;
        var center = grid.CellCenter(x, y);
        float half = grid.cellSize / 2f;
        // Grid y grows south, world z north: the cell's north side is y - 1.
        float west = Reach(x - 1, y), east = Reach(x + 1, y), north = Reach(x, y - 1), south = Reach(x, y + 1);
        int b = surface.vertices.Count;
        AddCorner(surface, center + new Vector3(-half - west, 0f, -half - south), x, y, -1, 1);
        AddCorner(surface, center + new Vector3(half + east, 0f, -half - south), x, y, 1, 1);
        AddCorner(surface, center + new Vector3(half + east, 0f, half + north), x, y, 1, -1);
        AddCorner(surface, center + new Vector3(-half - west, 0f, half + north), x, y, -1, -1);
        surface.triangles.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
    }

    float Reach(int x, int y) => grid.World.InBounds(x, y) && wet.Contains(grid.World.Idx(x, y)) ? 0f : Tuck;

    // A corner between cell (x, y) and its neighbours toward (dx, dy): the mean top of the wet ones.
    void AddCorner(Surface surface, Vector3 at, int x, int y, int dx, int dy)
    {
        var world = grid.World;
        float sum = 0f;
        int n = 0;
        foreach (var (cx, cy) in new[] { (x, y), (x + dx, y), (x, y + dy), (x + dx, y + dy) })
        {
            if (!world.InBounds(cx, cy) || !tops.TryGetValue(world.Idx(cx, cy), out var t)) continue;
            sum += t;
            n++;
        }
        at.y = sum / n;
        surface.vertices.Add(at);
        // The lake's tiling: its texture repeats every 10 m (a 240 m × 160 m plane tiled 24 × 16).
        surface.uvs.Add(new Vector2(at.x / 240f, at.z / 160f));
    }

    public void Destroy()
    {
        Object.Destroy(water.mesh);
        Object.Destroy(ice.mesh);
    }
}
