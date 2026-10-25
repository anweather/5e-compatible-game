using System.Collections.Generic;
using Chem;
using UnityEngine;

// The ground of the chunk terrains under the chemistry's cells, as Unity
// TerrainData: the cells' heights (CellHeightfield: baked ground plus each
// cell's offset, eased) and their paint (CellPaint: a block of alphamap texels
// per cell, with the detail grass cleared off bare ground).
//
// Works on runtime copies of the chunks the cells reach, so playing never
// changes the saved valley; Bind makes fresh copies (a new world starts from the
// saved ground). The baked heights already hold the starting cells' offsets
// (ValleyBuilder bakes them with the same CellHeightfield), so the base under
// each sample is what was baked minus that offset.
public sealed class ValleyGround
{
    sealed class Chunk
    {
        public Terrain terrain;
        public TerrainData original, data;
        // Heights in metres above the terrain's position, before the cells' offsets: [z, x].
        public float[,] baseHeights;
        // CellPaint.Layers as indices into the terrain's layers (-1: the terrain lacks it).
        public int[] layers;
        public bool heightsChanged;
    }

    readonly TerrainGrid layout;
    readonly Dictionary<(int i, int j), Chunk> chunks = new();
    readonly HashSet<(int x, int y)> repaint = new();
    readonly List<(int x, int y)> reshaped = new();
    CellHeightfield heightfield;
    World world;
    float[] cellBase;

    public ValleyGround(TerrainGrid layout, Terrain[] terrains)
    {
        this.layout = layout;
        foreach (var (i, j) in layout.GridChunks())
        {
            var terrain = terrains[j * layout.ChunksX + i];
            if (terrain != null) chunks[(i, j)] = new Chunk { terrain = terrain, original = terrain.terrainData };
        }
    }

    /// <summary>The runtime copies of the chunks the cells reach, by chunk.</summary>
    public IEnumerable<((int i, int j) at, TerrainData data)> Chunks()
    {
        foreach (var kv in chunks) yield return (kv.Key, kv.Value.data);
    }

    /// <summary>The runtime copy of one of the chunks the cells reach.</summary>
    public TerrainData Data((int i, int j) at) => chunks[at].data;

    /// <summary>True while reshaped ground is still easing to its new height.</summary>
    public bool Shaping => heightfield != null && !heightfield.Settled;

    /// <summary>Starts over on a world: fresh copies of the saved chunks, their base heights recovered.</summary>
    public void Bind(World w, float stepHeight, float shapeSpeed)
    {
        world = w;
        heightfield = new CellHeightfield(layout, stepHeight, shapeSpeed);
        heightfield.Reset(w);
        repaint.Clear();
        reshaped.Clear();
        foreach (var kv in chunks) Copy(kv.Key, kv.Value);
        cellBase = new float[w.W * w.H];
        for (int y = 0; y < w.H; y++)
            for (int x = 0; x < w.W; x++) cellBase[w.Idx(x, y)] = SampleBase(x, y);
    }

    void Copy((int i, int j) at, Chunk ch)
    {
        if (ch.data != null) Object.Destroy(ch.data);
        ch.data = Object.Instantiate(ch.original);
        ch.terrain.terrainData = ch.data;
        if (ch.terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = ch.data;
        int res = ch.data.heightmapResolution;
        var heights = ch.data.GetHeights(0, 0, res, res);
        var offsets = heightfield.Current(at.i, at.j);
        float size = ch.data.size.y;
        ch.baseHeights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) ch.baseHeights[z, x] = heights[z, x] * size - offsets[z, x];
        ch.layers = LayerIndices(ch.data.terrainLayers);
    }

    /// <summary>CellPaint's layers as indices into a terrain's layers, by name ("&lt;name&gt;_TerrainLayer...", as ValleyBuilder names them); -1 where missing.</summary>
    public static int[] LayerIndices(TerrainLayer[] layers)
    {
        var map = new int[CellPaint.Layers.Count];
        for (int k = 0; k < map.Length; k++)
        {
            map[k] = -1;
            for (int n = 0; n < layers.Length; n++)
                if (layers[n] != null && layers[n].name.StartsWith(CellPaint.Layers[k] + "_TerrainLayer", System.StringComparison.Ordinal)) map[k] = n;
        }
        return map;
    }

    float SampleBase(int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        return chunks.TryGetValue((i, j), out var ch) ? ch.terrain.transform.position.y + ch.baseHeights[sz, sx] : 0f;
    }

    /// <summary>The valley's own ground at a cell, before the cell's offset.</summary>
    public float BaseHeight(int x, int y) => cellBase != null && world.InBounds(x, y) ? cellBase[world.Idx(x, y)] : 0f;

    /// <summary>The ground drawn at a cell's centre now (it lags its target while easing).</summary>
    public float ShownGround(int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        return BaseHeight(x, y) + (heightfield != null && heightfield.Shapes(i, j) ? heightfield.Current(i, j)[sz, sx] : 0f);
    }

    /// <summary>A cell's elevation or height changed: its ground eases there (see Advance).</summary>
    public void Reshape(int x, int y) => reshaped.Add((x, y));

    /// <summary>A cell's terrain or cover changed: its texels are repainted at the next Flush.</summary>
    public void Repaint(int x, int y) => repaint.Add((x, y));

    /// <summary>Eases reshaped ground by <paramref name="dt"/> seconds and hands the moved samples to the terrains.</summary>
    public void Advance(float dt)
    {
        if (heightfield == null) return;
        if (reshaped.Count > 0)
        {
            heightfield.Update(world, reshaped);
            reshaped.Clear();
        }
        foreach (var r in heightfield.Advance(dt)) WriteHeights(r);
    }

    void WriteHeights(ChunkRect r)
    {
        if (!chunks.TryGetValue((r.I, r.J), out var ch)) return;
        var offsets = heightfield.Current(r.I, r.J);
        float size = ch.data.size.y;
        var heights = new float[r.Height, r.Width];
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) heights[z, x] = (ch.baseHeights[r.Z + z, r.X + x] + offsets[r.Z + z, r.X + x]) / size;
        ch.data.SetHeightsDelayLOD(r.X, r.Z, heights);
        ch.heightsChanged = true;
    }

    /// <summary>Once a frame: the batched paint, and the terrains' LOD and colliders for moved ground.</summary>
    public void Flush(System.Func<int, int, bool> scorched)
    {
        foreach (var ch in chunks.Values)
        {
            if (!ch.heightsChanged) continue;
            ch.data.SyncHeightmap();
            ch.heightsChanged = false;
        }
        if (repaint.Count == 0) return;
        foreach (var (x, y) in repaint) Paint(x, y, scorched);
        repaint.Clear();
    }

    // One cell's texels (a rectangle per cell, so cells around it keep the valley's own paint),
    // and its detail grass cleared if it is no longer grassy.
    void Paint(int x, int y, System.Func<int, int, bool> scorched)
    {
        var r = layout.TexelRect(x, y);
        if (!chunks.TryGetValue((r.I, r.J), out var ch)) return;
        var block = Expand(CellPaint.Block(layout, world, r, scorched), ch.layers, ch.data.alphamapLayers);
        ch.data.SetAlphamaps(r.X, r.Z, block);
        var w = CellPaint.Weights(world.At(x, y), scorched(x, y));
        if (IsGrassy(w)) return;
        ClearDetail(ch.data, r);
    }

    /// <summary>Whether layer weights (CellPaint.Layers order) read as grass that detail grass grows on.</summary>
    public static bool IsGrassy(IReadOnlyList<float> w) => w[0] + w[1] >= 0.5f;

    /// <summary>A CellPaint block spread over all of a terrain's layers (<paramref name="layers"/> from LayerIndices).</summary>
    public static float[,,] Expand(float[,,] block, int[] layers, int layerCount)
    {
        int h = block.GetLength(0), w = block.GetLength(1);
        var full = new float[h, w, layerCount];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                for (int k = 0; k < layers.Length; k++)
                    if (layers[k] >= 0) full[z, x, layers[k]] += block[z, x, k];
        return full;
    }

    /// <summary>Clears the detail grass under a texel rectangle (detail and alphamap resolutions may differ).</summary>
    public static void ClearDetail(TerrainData data, ChunkRect r)
    {
        float k = data.detailResolution / (float)data.alphamapResolution;
        int x0 = Mathf.FloorToInt(r.X * k), z0 = Mathf.FloorToInt(r.Z * k);
        int x1 = Mathf.Max(x0, Mathf.CeilToInt((r.XMax + 1) * k) - 1), z1 = Mathf.Max(z0, Mathf.CeilToInt((r.ZMax + 1) * k) - 1);
        var none = new int[z1 - z0 + 1, x1 - x0 + 1];
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++) data.SetDetailLayer(x0, z0, layer, none);
    }

    /// <summary>Frees the runtime copies.</summary>
    public void Destroy()
    {
        foreach (var ch in chunks.Values)
            if (ch.data != null) Object.Destroy(ch.data);
    }
}
