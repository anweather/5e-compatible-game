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
// Works on runtime copies of the chunks, made when a world starts (Bind), so
// playing never changes the saved valley. They are made up front, not when a
// chunk first changes: swapping a terrain's collider under a prop resting on it
// drops the contact, and the prop falls through the ground. The baked heights already hold the starting cells' offsets
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
        // This frame's painted texels, [z, x, terrain layer] over the whole alphamap, and the box
        // round them (Flush writes it to the terrain once).
        public float[,,] frameAlpha;
        public int dx0 = int.MaxValue, dz0 = int.MaxValue, dx1 = -1, dz1 = -1;
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

    // The chunk's runtime copy (Bind makes them all; this only covers a chunk Bind didn't reach).
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
    public void Bind(World w, float stepHeight, float shapeSpeed, bool flatUnderWalls = false)
    {
        world = w;
        heightfield = new CellHeightfield(layout, stepHeight, shapeSpeed) { FlatUnderWalls = flatUnderWalls };
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
        Writable(ch);
        int res = ch.original.heightmapResolution;
        var heights = ch.original.GetHeights(0, 0, res, res);
        var offsets = heightfield.Current(at.i, at.j);
        float size = ch.original.size.y;
        ch.baseHeights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) ch.baseHeights[z, x] = heights[z, x] * size - offsets[z, x];
        ch.layers = LayerIndices(ch.original.terrainLayers);
        ch.baseAlpha = null;
        ch.frameAlpha = null;
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

    /// <summary>
    /// Counts some cells' ground from higher than the terrain is drawn there (after Bind): the river's
    /// cells over its channel, whose water stands at the banks' level. <paramref name="cells"/> are
    /// world cell indices, <paramref name="metres"/> how much higher each.
    /// </summary>
    public void RaiseBase(int[] cells, float[] metres)
    {
        if (cellBase == null || cells == null || metres == null) return;
        int n = Mathf.Min(cells.Length, metres.Length);
        for (int k = 0; k < n; k++)
            if (cells[k] >= 0 && cells[k] < cellBase.Length) cellBase[cells[k]] += metres[k];
    }

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
        using var marked = FlushMarker.Auto();
        foreach (var ch in chunks.Values)
        {
            if (!ch.heightsChanged) continue;
            ch.data.SyncHeightmap();
            ch.heightsChanged = false;
        }
        if (repaint.Count == 0)
        {
            // The paint composed in earlier frames still goes out, a chunk a frame.
            WriteOnePaint();
            return;
        }
        // A big fire scorches hundreds of cells a tick: repaint at most so many a frame, the rest
        // over the next frames, so the terrain's paint never stalls a frame.
        batch.Clear();
        foreach (var cell in repaint)
        {
            batch.Add(cell);
            if (batch.Count == RepaintPerFrame) break;
        }
        foreach (var cell in batch) repaint.Remove(cell);
        // A cell's paint reaches its neighbours' texels: repaint them too, as strips of cells.
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => PaintOf(x, y, scorched(x, y));
        using (PaintMarker.Auto())
        {
            foreach (var r in CellPaint.Runs(layout, CellPaint.Around(layout, batch))) Paint(r, paintOf);
            WriteOnePaint();
        }
        using (DetailMarker.Auto()) ClearBareDetail(scorched);
    }

    // Profiler markers (Window > Analysis > Profiler) round the terrain's per-frame work.
    static readonly Unity.Profiling.ProfilerMarker FlushMarker = new("ValleyGround.Flush"),
        PaintMarker = new("ValleyGround.Paint"), DetailMarker = new("ValleyGround.ClearGrass"), UploadMarker = new("ValleyGround.UploadPaint");

    const int RepaintPerFrame = 48;
    readonly List<(int x, int y)> batch = new();

    // What a cell paints over the saved paint: nothing while it paints as it was baked.
    IReadOnlyList<float> PaintOf(int x, int y, bool scorched)
    {
        var mix = CellPaint.Mix(world, world.At(x, y), scorched);
        return ReferenceEquals(mix, bakedPaint[world.Idx(x, y)]) ? null : mix;
    }

    // Composes a strip of cells' paint into its chunk's frame copy; WritePaint sends it to the terrain.
    void Paint(ChunkRect r, System.Func<int, int, IReadOnlyList<float>> paintOf)
    {
        if (!chunks.TryGetValue((r.I, r.J), out var ch)) return;
        var data = Writable(ch);
        int res = data.alphamapResolution;
        ch.baseAlpha ??= data.GetAlphamaps(0, 0, res, res);
        ch.frameAlpha ??= (float[,,])ch.baseAlpha.Clone();
        var block = CellPaint.Block(layout, r, paintOf);
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) ComposeTexel(block, (ch.layers, ch.baseAlpha), ch.frameAlpha, x, z, (r.X, r.Z));
        ch.dx0 = Mathf.Min(ch.dx0, r.X);
        ch.dz0 = Mathf.Min(ch.dz0, r.Z);
        ch.dx1 = Mathf.Max(ch.dx1, r.XMax);
        ch.dz1 = Mathf.Max(ch.dz1, r.ZMax);
    }

    // Uploads one chunk's waiting paint a frame, taking the chunks in turn, so a fire across several
    // chunks spreads its uploads over frames rather than stacking them in one.
    void WriteOnePaint()
    {
        if (paintOrder.Count != chunks.Count) paintOrder = new List<Chunk>(chunks.Values);
        for (int k = 0; k < paintOrder.Count; k++)
        {
            nextPaint = (nextPaint + 1) % paintOrder.Count;
            if (paintOrder[nextPaint].dx1 < 0) continue;
            WritePaint(paintOrder[nextPaint]);
            return;
        }
    }

    // One paint texture's texels in the chunk's box, written in place through GetPixelData (no copy,
    // nothing for the garbage collector); <paramref name="first"/> is the layer in its red channel.
    static void WriteTexture(Texture2D tex, int first, Chunk ch)
    {
        var pixels = tex.GetPixelData<Color32>(0);
        int width = tex.width, n = ch.frameAlpha.GetLength(2);
        for (int z = ch.dz0; z <= ch.dz1; z++)
            for (int x = ch.dx0; x <= ch.dx1; x++)
            {
                var c = new Color32();
                for (int channel = 0; channel < 4 && first + channel < n; channel++)
                    c[channel] = (byte)Mathf.RoundToInt(Mathf.Clamp01(ch.frameAlpha[z, x, first + channel]) * 255f);
                pixels[z * width + x] = c;
            }
    }

    List<Chunk> paintOrder = new();
    int nextPaint;

    // Writes the box round a chunk's painted strips into the terrain's paint textures (its
    // alphamapTextures, four layers to a texture, one per channel: the paint's own storage, so
    // GetAlphamaps reads it back) and uploads them with their mip levels. TerrainData.SetAlphamaps
    // does the same and also regenerates the base map, the low-detail texture drawn past the
    // terrain's basemapDistance, about 20 ms a call; that stalled a big fire's frames, and nothing
    // in the valley is far enough off to draw the base map.
    static void WritePaint(Chunk ch)
    {
        using var marked = UploadMarker.Auto();
        if (ch.dx1 < 0) return;
        var textures = ch.data.alphamapTextures;
        for (int t = 0; t < textures.Length; t++)
        {
            WriteTexture(textures[t], t * 4, ch);
            textures[t].Apply(true);
        }
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
    }

    // Clears the grass under this frame's bare cells, one read and one write per chunk and grass
    // kind (each SetDetailLayer rebuilds the terrain's grass patches there, so a call per cell made a
    // big fire's frames stall).
    void ClearBareDetail(System.Func<int, int, bool> scorched)
    {
        bareByChunk.Clear();
        foreach (var (x, y) in batch)
        {
            // Snow only lies on the grass (clearing it would be for good: nothing regrows it).
            var c = world.At(x, y);
            if (CellPaint.IsSnowy(c) || CellPaint.IsGrassy(CellPaint.Mix(world, c, scorched(x, y)))) continue;
            var r = layout.TexelRect(x, y);
            if (!chunks.ContainsKey((r.I, r.J))) continue;
            if (!bareByChunk.TryGetValue((r.I, r.J), out var list)) bareByChunk[(r.I, r.J)] = list = new List<ChunkRect>();
            list.Add(r);
        }
        foreach (var kv in bareByChunk) ClearDetail(Writable(chunks[kv.Key]), kv.Value);
    }

    readonly Dictionary<(int i, int j), List<ChunkRect>> bareByChunk = new();

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
            for (int x = 0; x < r.Width; x++) ComposeTexel(block, (layers, baseAlpha), full, x, z, (0, 0));
        return full;
    }

    // One texel of the block over the saved paint (its layer indices and alphamap), into
    // <paramref name="into"/> at (x, z) moved by <paramref name="offset"/>.
    static void ComposeTexel(PaintBlock block, (int[] layers, float[,,] alpha) saved, float[,,] into, int x, int z, (int x, int z) offset)
    {
        var (layers, baseAlpha) = saved;
        int ox = offset.x, oz = offset.z;
        var r = block.Rect;
        float a = block.Cover[z, x];
        for (int n = 0; n < into.GetLength(2); n++) into[oz + z, ox + x, n] = baseAlpha[r.Z + z, r.X + x, n] * (1f - a);
        if (a <= 0f) return;
        for (int k = 0; k < layers.Length; k++)
            if (layers[k] >= 0) into[oz + z, ox + x, layers[k]] += a * block.Mix[z, x, k];
    }

    /// <summary>Clears the detail grass under a texel rectangle (detail and alphamap resolutions may differ).</summary>
    public static void ClearDetail(TerrainData data, ChunkRect r) => ClearDetail(data, new List<ChunkRect> { r });

    /// <summary>
    /// Clears the detail grass under several texel rectangles of one terrain: the box round them read
    /// once per grass kind, the rectangles zeroed in it, and written back once.
    /// </summary>
    public static void ClearDetail(TerrainData data, List<ChunkRect> rects)
    {
        if (rects.Count == 0) return;
        float k = data.detailResolution / (float)data.alphamapResolution;
        int bx0 = int.MaxValue, bz0 = int.MaxValue, bx1 = int.MinValue, bz1 = int.MinValue;
        var spans = new List<(int x0, int z0, int x1, int z1)>(rects.Count);
        foreach (var r in rects)
        {
            int x0 = Mathf.FloorToInt(r.X * k), z0 = Mathf.FloorToInt(r.Z * k);
            int x1 = Mathf.Max(x0, Mathf.CeilToInt((r.XMax + 1) * k) - 1), z1 = Mathf.Max(z0, Mathf.CeilToInt((r.ZMax + 1) * k) - 1);
            spans.Add((x0, z0, x1, z1));
            bx0 = Mathf.Min(bx0, x0); bz0 = Mathf.Min(bz0, z0); bx1 = Mathf.Max(bx1, x1); bz1 = Mathf.Max(bz1, z1);
        }
        int w = bx1 - bx0 + 1, h = bz1 - bz0 + 1;
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            var map = data.GetDetailLayer(bx0, bz0, w, h, layer);
            bool any = false;
            foreach (var (x0, z0, x1, z1) in spans)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        any |= map[z - bz0, x - bx0] != 0;
                        map[z - bz0, x - bx0] = 0;
                    }
            if (any) data.SetDetailLayer(bx0, bz0, layer, map);
        }
    }

    /// <summary>Frees the runtime copies, the saved chunks back on the terrains.</summary>
    public void Destroy()
    {
        foreach (var ch in chunks.Values) Restore(ch);
    }
}
