using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The sea on a generated map (#214, J-086): what it costs follows the drawn tier, never the map's size. One mesh at
// fineStep over the whole map was the nine's 670 thousand vertices and a fifth of a second, and on the Scale Island
// (6.9 x 5.8 km) gigabytes before its first frame. So the fine sea is laid in tiles, one a chunk terrain's square,
// round each character and the camera as far as the terrains are made (MapTerrains.Sight plus a chunk), off the map's
// edge too: nearest first, a few a frame within TileBudgetMs, each from a pool the tiles left behind go back to, and
// each measured on the terrain under it (built only once that terrain stands, so its shoals and foam are right). Past
// them, a coarse sea: the same grid as the valley's sea but CoarseStep apart, with no swell, CoarseBelow under the
// level and drawn before the tiles, so it's hidden under a tile (by the tile where the water is deep, where the tile
// is opaque, and by the ground where it's shallow) and shows only where no tile is. The valley keeps its one mesh: 81
// chunks, all drawn.
public partial class Ocean
{
    /// <summary>The coarse sea's spacing past the tiles (m), and how far under the level it lies.</summary>
    public const float CoarseStep = 32f, CoarseBelow = 3.5f;

    // About this many ms a frame go to laying tiles in play (at least one a frame while any is wanted).
    const double TileBudgetMs = 1.5;
    // A tile is laid within the terrains' sight plus Margin of a focus and goes back to the pool past it plus KeepMargin.
    const int Margin = 1, KeepMargin = 2;
    const float PlanEvery = 0.25f;

    sealed class Tile
    {
        public GameObject go;
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    bool tiled;
    GameObject coarse;
    Material coarseMaterial;
    MapLand coarseFor;
    readonly Dictionary<(int i, int j), Tile> tiles = new();
    readonly Stack<Tile> tilePool = new();
    readonly List<(int i, int j)> tileFocus = new(), tilesWanted = new(), tilesLeaving = new();
    float nextTilePlan;
    Color tint = Color.white;

    /// <summary>Whether the sea is laid in tiles round the drawn tier (a generated map's in play), not as one mesh.</summary>
    public bool Tiled => tiled;

    /// <summary>The fine tiles laid now, and the vertices in them and in the coarse sea (the perf lines, the probe).</summary>
    public (int tiles, int tileVertices, int coarseVertices) Counts
    {
        get
        {
            int n = 0, v = 0;
            foreach (var t in tiles.Values)
                if (t != null)
                {
                    n++;
                    v += t.mesh.vertexCount;
                }
            int c = coarse != null && coarse.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null ? f.sharedMesh.vertexCount : 0;
            return (n, v, c);
        }
    }

    static bool WantsTiles() => Application.isPlaying && MapTerrains.Instance != null && MapTerrains.Instance.Lazy;

    // The coarse sea (once a map: it reads no terrain), then the tiles round the focus, all of them now.
    void BuildTiled()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        ReleaseTiles();
        if (coarse == null || coarseFor != builtFor) LayCoarse();
        PlanTiles();
        LayTiles(whole: true);
        var (n, v, c) = Counts;
        StartupTimeline.Mark("sea tiles laid");
        Debug.Log($"SEA for the drawn tier: {n} tiles ({v} vertices; {tiles.Count - n} dry, {tilesWanted.Count} waiting for their " +
                  $"terrains, round {tileFocus.Count} focus) and the coarse sea ({c} vertices) in {timer.Elapsed.TotalMilliseconds:0} ms");
    }

    void LayCoarse()
    {
        ClearCoarse();
        coarseFor = builtFor;
        var (us, vs) = Grid(CoarseStep);
        var m = Lay(us, vs, (_, outward) => Shelf(outward), "Sea far");
        if (m == null) return;
        mesh = m;
        coarseMaterial = new Material(material) { name = "Sea far", renderQueue = material.renderQueue - 1, hideFlags = HideFlags.DontSave };
        coarse = Surface("Sea far", m, coarseMaterial, swell: false);
        coarse.transform.localPosition = Vector3.down * CoarseBelow;
    }

    void ClearCoarse()
    {
        if (coarse != null) DestroyNow(coarse);
        if (mesh != null) DestroyNow(mesh);
        if (coarseMaterial != null) DestroyNow(coarseMaterial);
        coarse = null;
        mesh = null;
        coarseMaterial = null;
        coarseFor = null;
    }

    // Every tile back to the pool (to be measured again), or destroyed with the pool (the sea is going).
    void ReleaseTiles(bool destroy = false)
    {
        foreach (var t in tiles.Values)
            if (t != null) Pool(t);
        tiles.Clear();
        if (!destroy) return;
        foreach (var t in tilePool)
        {
            DestroyNow(t.go);
            DestroyNow(t.mesh);
        }
        tilePool.Clear();
    }

    void Pool(Tile t)
    {
        t.mesh.Clear();
        t.go.SetActive(false);
        tilePool.Push(t);
    }

    void StreamTiles()
    {
        if (Time.unscaledTime >= nextTilePlan) PlanTiles();
        LayTiles(whole: false);
    }

    // Which tiles are wanted: round each character and the camera. Those past the keep distance go back to the pool now;
    // those wanted and not laid wait, nearest first.
    void PlanTiles()
    {
        nextTilePlan = Time.unscaledTime + PlanEvery;
        tilesWanted.Clear();
        var layout = builtFor?.Layout;
        if (layout == null) return;
        tileFocus.Clear();
        var grid = ChemGrid.Current?.Grid;
        if (grid != null && grid.World != null)
            foreach (var (x, y) in grid.PlayerCells()) AddTileFocus(layout.ChunkOfCell(x, y));
        var cam = Camera.main;
        if (cam != null)
        {
            var p = cam.transform.position;
            AddTileFocus((Mathf.FloorToInt((p.x - layout.OriginX) / layout.ChunkSize), Mathf.FloorToInt((p.z - layout.OriginZ) / layout.ChunkSize)));
        }
        int sight = MapTerrains.Sight(), make = sight + Margin, keep = sight + KeepMargin;
        tilesLeaving.Clear();
        foreach (var at in tiles.Keys)
            if (TileDistance(at) > keep) tilesLeaving.Add(at);
        foreach (var at in tilesLeaving)
        {
            if (tiles[at] != null) Pool(tiles[at]);
            tiles.Remove(at);
        }
        foreach (var (fi, fj) in tileFocus)
            for (int j = fj - make; j <= fj + make; j++)
                for (int i = fi - make; i <= fi + make; i++)
                    if (!tiles.ContainsKey((i, j)) && !tilesWanted.Contains((i, j))) tilesWanted.Add((i, j));
        tilesWanted.Sort((a, b) => TileDistance(a).CompareTo(TileDistance(b)));
    }

    void AddTileFocus((int i, int j) at)
    {
        if (!tileFocus.Contains(at)) tileFocus.Add(at);
    }

    int TileDistance((int i, int j) at)
    {
        int best = int.MaxValue;
        foreach (var (fi, fj) in tileFocus) best = Mathf.Min(best, Mathf.Max(Mathf.Abs(at.i - fi), Mathf.Abs(at.j - fj)));
        return best;
    }

    // The waiting tiles, nearest first: all of them that can be (before the first frame), or as many as fit the frame's
    // budget. A tile over the map waits for its terrain, so it's measured on the ground as drawn.
    void LayTiles(bool whole)
    {
        if (tilesWanted.Count == 0) return;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var layout = builtFor.Layout;
        var terrains = MapTerrains.Instance;
        bool any = false;
        for (int n = 0; n < tilesWanted.Count; n++)
        {
            if (!whole && any && timer.Elapsed.TotalMilliseconds >= TileBudgetMs) break;
            var at = tilesWanted[n];
            var (x0, z0) = layout.ChunkCorner(at.i, at.j);
            if (layout.HasChunk(at.i, at.j) && terrains != null
                && terrains.TerrainAt(new Vector3(x0 + layout.ChunkSize * 0.5f, 0f, z0 + layout.ChunkSize * 0.5f)) == null)
                continue;
            tiles[at] = LayTile(at, x0, z0, layout.ChunkSize);
            tilesWanted.RemoveAt(n--);
            any = true;
        }
    }

    // One chunk's square of fine sea, from the pool; null where none of it is over the water.
    Tile LayTile((int i, int j) at, float x0, float z0, float size)
    {
        int cells = Mathf.CeilToInt(size / fineStep);
        float step = size / cells;
        var us = new List<float>(cells + 1);
        for (int k = 0; k <= cells; k++) us.Add(x0 + k * step);
        var vs = new List<float>(cells + 1);
        for (int k = 0; k <= cells; k++) vs.Add(z0 + k * step);
        // Measured on this tile's own terrain: its edge a hair inside it, so the tile never waits on a neighbour's.
        float lo = 0.01f, hi = size - 0.01f;
        Tile t = tilePool.Count > 0 ? tilePool.Pop() : null;
        var m = Lay(us, vs, (xz, outward) =>
        {
            var p = new Vector2(Mathf.Clamp(xz.x, x0 + lo, x0 + hi), Mathf.Clamp(xz.y, z0 + lo, z0 + hi));
            return Depth(p, outward);
        }, $"Sea tile {at.i},{at.j}", t?.mesh, square: true);
        if (m == null)
        {
            if (t != null) tilePool.Push(t);
            return null;
        }
        if (t == null)
        {
            var go = Surface("Sea tile", m, material, swell: true);
            t = new Tile { go = go, mesh = m, renderer = go.GetComponent<MeshRenderer>() };
        }
        else
        {
            t.go.SetActive(true);
            Look(t.renderer, swell: true);
        }
        t.go.name = $"Sea tile {at.i},{at.j}";
        return t;
    }
}
