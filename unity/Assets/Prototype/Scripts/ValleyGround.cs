using System.Collections.Generic;
using Chem;
using UnityEngine;

// The ground of the chunk terrains under the chemistry's cells, as Unity
// TerrainData: the cells' heights (CellHeightfield: baked ground plus each
// cell's offset, eased) and their paint (CellPaint: soft-edged paint laid over
// the saved valley's own, with the detail grass cleared off bare ground).
//
// Only cells whose paint differs from how they were baked paint anything: the
// rest keep the saved paint (the meadow, the old yard's soft-edged pad and
// gully), so a burnt patch fades into whatever was there before.
//
// Works on runtime copies of the chunks the cells change, made the first time
// one changes, so playing never changes the saved valley; Bind drops them (a new
// world starts from the saved ground). The baked heights already hold the starting cells' offsets
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
        // The saved paint, [z, x, terrain layer], read the first time the chunk is painted.
        public float[,,] baseAlpha;
        public bool heightsChanged;
    }

    readonly TerrainGrid layout;
    readonly Dictionary<(int i, int j), Chunk> chunks = new();
    readonly HashSet<(int x, int y)> repaint = new();
    readonly List<(int x, int y)> reshaped = new();
    CellHeightfield heightfield;
    World world;
    float[] cellBase;
    // Each cell's paint as baked (CellPaint.Mix of the starting world); a cell still painting
    // this keeps the saved paint.
    IReadOnlyList<float>[] bakedPaint;

    public ValleyGround(TerrainGrid layout, Terrain[] terrains)
    {
        this.layout = layout;
        foreach (var (i, j) in layout.GridChunks())
        {
            var terrain = terrains[j * layout.ChunksX + i];
            if (terrain != null) chunks[(i, j)] = new Chunk { terrain = terrain, original = terrain.terrainData };
        }
    }

    /// <summary>The chunks the cells reach as drawn now: a runtime copy once one changed, else the saved one.</summary>
    public IEnumerable<((int i, int j) at, TerrainData data)> Chunks()
    {
        foreach (var kv in chunks) yield return (kv.Key, Shown(kv.Value));
    }

    /// <summary>One of the chunks the cells reach, as drawn now.</summary>
    public TerrainData Data((int i, int j) at) => Shown(chunks[at]);

    /// <summary>One of the chunks the cells reach, to change: its runtime copy, made now if it has none yet.</summary>
    public TerrainData WritableData((int i, int j) at) => Writable(chunks[at]);

    static TerrainData Shown(Chunk ch) => ch.data != null ? ch.data : ch.original;

    // A chunk is copied the first time the cells change it, so the saved valley never changes and a
    // valley of many chunks doesn't copy them all up front.
    static TerrainData Writable(Chunk ch)
    {
        if (ch.data != null) return ch.data;
        ch.data = Object.Instantiate(ch.original);
        Use(ch, ch.data);
        return ch.data;
    }

    static void Use(Chunk ch, TerrainData data)
    {
        ch.terrain.terrainData = data;
        if (ch.terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = data;
    }

    // Back to the saved chunk, dropping the copy (the terrain may be gone already, as the scene unloads).
    static void Restore(Chunk ch)
    {
        if (ch.data == null) return;
        if (ch.terrain != null) Use(ch, ch.original);
        Object.Destroy(ch.data);
        ch.data = null;
        ch.heightsChanged = false;
    }

    /// <summary>True while reshaped ground is still easing to its new height.</summary>
    public bool Shaping => heightfield != null && !heightfield.Settled;

    /// <summary>Starts over on a world: the saved chunks again (copied when first changed), their base heights recovered.</summary>
    public void Bind(World w, float stepHeight, float shapeSpeed)
    {
        world = w;
        heightfield = new CellHeightfield(layout, stepHeight, shapeSpeed);
        heightfield.Reset(w);
        repaint.Clear();
        reshaped.Clear();
        foreach (var kv in chunks) Recover(kv.Key, kv.Value);
        cellBase = new float[w.W * w.H];
        bakedPaint = new IReadOnlyList<float>[w.W * w.H];
        for (int y = 0; y < w.H; y++)
        {
            for (int x = 0; x < w.W; x++)
            {
                cellBase[w.Idx(x, y)] = SampleBase(x, y);
                bakedPaint[w.Idx(x, y)] = CellPaint.Mix(w, w.At(x, y));
            }
        }
    }

    void Recover((int i, int j) at, Chunk ch)
    {
        Restore(ch);
        int res = ch.original.heightmapResolution;
        var heights = ch.original.GetHeights(0, 0, res, res);
        var offsets = heightfield.Current(at.i, at.j);
        float size = ch.original.size.y;
        ch.baseHeights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) ch.baseHeights[z, x] = heights[z, x] * size - offsets[z, x];
        ch.layers = LayerIndices(ch.original.terrainLayers);
        ch.baseAlpha = null;
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

    /// <summary>The terrain's ground drawn now at a world point over the cells (0 off them).</summary>
    public float GroundAt(float wx, float wz)
    {
        var (x, y) = layout.CellAt(wx, wz);
        if (!chunks.TryGetValue(layout.ChunkOfCell(x, y), out var ch)) return 0f;
        var data = Shown(ch);
        var p = ch.terrain.transform.position;
        return p.y + data.GetInterpolatedHeight((wx - p.x) / data.size.x, (wz - p.z) / data.size.z);
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
        var data = Writable(ch);
        float size = data.size.y;
        var heights = new float[r.Height, r.Width];
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) heights[z, x] = (ch.baseHeights[r.Z + z, r.X + x] + offsets[r.Z + z, r.X + x]) / size;
        data.SetHeightsDelayLOD(r.X, r.Z, heights);
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
        // A cell's paint reaches its neighbours' texels: repaint them too, as strips of cells.
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => PaintOf(x, y, scorched(x, y));
        foreach (var r in CellPaint.Runs(layout, CellPaint.Around(layout, repaint))) Paint(r, paintOf);
        foreach (var (x, y) in repaint) ClearDetailIfBare(x, y, scorched(x, y));
        repaint.Clear();
    }

    // What a cell paints over the saved paint: nothing while it paints as it was baked.
    IReadOnlyList<float> PaintOf(int x, int y, bool scorched)
    {
        var mix = CellPaint.Mix(world, world.At(x, y), scorched);
        return ReferenceEquals(mix, bakedPaint[world.Idx(x, y)]) ? null : mix;
    }

    void Paint(ChunkRect r, System.Func<int, int, IReadOnlyList<float>> paintOf)
    {
        if (!chunks.TryGetValue((r.I, r.J), out var ch)) return;
        var data = Writable(ch);
        int res = data.alphamapResolution;
        ch.baseAlpha ??= data.GetAlphamaps(0, 0, res, res);
        data.SetAlphamaps(r.X, r.Z, Compose(CellPaint.Block(layout, r, paintOf), ch.layers, ch.baseAlpha));
    }

    void ClearDetailIfBare(int x, int y, bool scorched)
    {
        if (CellPaint.IsGrassy(CellPaint.Mix(world, world.At(x, y), scorched))) return;
        var r = layout.TexelRect(x, y);
        if (chunks.TryGetValue((r.I, r.J), out var ch)) ClearDetail(Writable(ch), r);
    }

    /// <summary>
    /// A CellPaint block laid over a terrain's paint (<paramref name="baseAlpha"/>, the whole
    /// alphamap, [z, x, layer]) for the block's rectangle, spread over all of the terrain's layers
    /// (<paramref name="layers"/> from LayerIndices).
    /// </summary>
    public static float[,,] Compose(PaintBlock block, int[] layers, float[,,] baseAlpha)
    {
        var r = block.Rect;
        var full = new float[r.Height, r.Width, baseAlpha.GetLength(2)];
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) ComposeTexel(block, layers, baseAlpha, full, x, z);
        return full;
    }

    static void ComposeTexel(PaintBlock block, int[] layers, float[,,] baseAlpha, float[,,] full, int x, int z)
    {
        var r = block.Rect;
        float a = block.Cover[z, x];
        for (int n = 0; n < full.GetLength(2); n++) full[z, x, n] = baseAlpha[r.Z + z, r.X + x, n] * (1f - a);
        if (a <= 0f) return;
        for (int k = 0; k < layers.Length; k++)
            if (layers[k] >= 0) full[z, x, layers[k]] += a * block.Mix[z, x, k];
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

    /// <summary>Frees the runtime copies, the saved chunks back on the terrains.</summary>
    public void Destroy()
    {
        foreach (var ch in chunks.Values) Restore(ch);
    }
}
