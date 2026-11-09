using System.Collections.Generic;
using Chem;
using UnityEngine;

// The valley's ground worked out from its shapes (ValleyShape, ValleySurroundings, the coast) for Prototype > Bake Map
// Data (MapBake): a 9×9 grid of chunk TerrainData held in memory only (TerrainGrid.Valley(), docs/WORLD_STREAMING.md
// part 1), 257 heightmaps (4 samples a cell) round the play area and 65 elsewhere, all sampled from ValleyShape at world
// points so shared edges agree, and a full-detail edge facing a coarse chunk drawn between the coarse samples so they
// meet exactly. The starting cells are baked in with the runtime's own code: CellHeightfield sinks the pond, the pits,
// the gully and the map's own stamps into the ground, and CellPaint paints the cells that aren't meadow with soft edges
// and clears their grass. This was ValleyTerrain, the bake phase 6 of docs/design/maps-as-data.md retired from the yard
// build; it lives on only to rewrite the valley's ground/, so nothing is saved as an asset.
public sealed class ValleyBake
{
    const float Depth = 22f;        // the terrain's base below the yard
    const float MaxHeight = 300f;
    const int DetailRes = TerrainGrid.ChunkCells;   // one detail cell per grid cell
    // The old single terrain had a detail cell every 1.76 m; scale chances to keep the grass as dense.
    const float DetailDensity = 2.25f / 3.1f;

    public readonly TerrainGrid Layout;
    readonly TerrainData[] datas;

    /// <summary>The chunks, chunk (i, j) at j * ChunksX + i; destroy them with <see cref="Dispose"/>.</summary>
    public TerrainData[] Chunks => datas;

    int Index(int i, int j) => j * Layout.ChunksX + i;

    // --- heights -----------------------------------------------------------

    /// <summary>Every chunk's TerrainData with the valley's own heights (no cells yet), painted with <paramref name="layers"/>.</summary>
    public ValleyBake(TerrainGrid layout, TerrainLayer[] layers)
    {
        Layout = layout;
        datas = new TerrainData[Layout.ChunksX * Layout.ChunksZ];
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) datas[Index(i, j)] = NewChunk(i, j);
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) datas[Index(i, j)].SetHeights(0, 0, BaseHeights(i, j));
        if (layers != null)
            foreach (var d in datas) d.terrainLayers = layers;
    }

    /// <summary>Destroys the chunks in memory.</summary>
    public void Dispose()
    {
        foreach (var d in datas)
            if (d != null) Object.DestroyImmediate(d);
    }

    TerrainData NewChunk(int i, int j)
    {
        int texels = Layout.TexelResolution(i, j);
        var data = new TerrainData
        {
            name = $"Bake chunk {i},{j}",
            heightmapResolution = Layout.Resolution(i, j),
            alphamapResolution = texels,
            baseMapResolution = Mathf.Max(64, texels),
        };
        data.SetDetailResolution(DetailRes, 8);
        // The layers hold clumps per texel (ScatterDetails), not coverage out of 255 (the default,
        // which would draw next to nothing).
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.size = new Vector3(Layout.ChunkSize, MaxHeight, Layout.ChunkSize);
        return data;
    }

    // A chunk's heights (0..1 of MaxHeight, from Depth below the yard), [z, x], seams stitched.
    float[,] BaseHeights(int i, int j)
    {
        int res = Layout.Resolution(i, j);
        var h = new float[res, res];
        for (int sz = 0; sz < res; sz++)
        {
            for (int sx = 0; sx < res; sx++)
            {
                var (x, z) = Layout.SamplePosition(i, j, sx, sz);
                h[sz, sx] = Mathf.Clamp01((ValleyShape.HeightAt(new Vector2(x, z)) + Depth) / MaxHeight);
            }
        }
        RoundSeamCorners(h, i, j);
        StitchSeams(h, i, j);
        return h;
    }

    // Unity stores heights in 15 bits: one step is 1/32766 of MaxHeight (about 9 mm).
    const float Quantum = 1f / 32766f;

    // A full-detail edge facing a coarse chunk is drawn between the coarse samples, at quarter
    // points. For those to land on stored steps exactly, every cell-corner sample on a
    // full-detail chunk's border is rounded to a multiple of 4 steps, on every chunk that holds
    // it (the rule only looks at the point, so all of them agree).
    void RoundSeamCorners(float[,] h, int i, int j)
    {
        int p = Layout.SamplesPerCell(i, j), last = Layout.Resolution(i, j) - 1;
        for (int sz = 0; sz <= last; sz += p)
        {
            for (int sx = 0; sx <= last; sx += p)
            {
                bool border = sx == 0 || sz == 0 || sx == last || sz == last;
                if (border && TouchesFullDetail(i * TerrainGrid.ChunkCells + sx / p, j * TerrainGrid.ChunkCells + sz / p))
                    h[sz, sx] = Mathf.Round(h[sz, sx] / (4f * Quantum)) * 4f * Quantum;
            }
        }
    }

    // Whether a full-detail chunk holds the cell corner (u, v).
    bool TouchesFullDetail(int u, int v)
    {
        for (int j = Chem.Chunks.ChunkOf(v - 1); j <= Chem.Chunks.ChunkOf(v); j++)
            for (int i = Chem.Chunks.ChunkOf(u - 1); i <= Chem.Chunks.ChunkOf(u); i++)
                if (Layout.HasChunk(i, j) && Layout.IsHighRes(i, j)) return true;
        return false;
    }

    // On a full-detail chunk's edge facing a coarse chunk, the samples between the coarse
    // ones lie on the straight line between them, as the coarse chunk draws its edge.
    void StitchSeams(float[,] h, int i, int j)
    {
        if (!Layout.IsHighRes(i, j)) return;
        int last = Layout.Resolution(i, j) - 1;
        if (IsCoarse(i - 1, j)) StitchLine(h, last, k => (0, k));
        if (IsCoarse(i + 1, j)) StitchLine(h, last, k => (last, k));
        if (IsCoarse(i, j - 1)) StitchLine(h, last, k => (k, 0));
        if (IsCoarse(i, j + 1)) StitchLine(h, last, k => (k, last));
    }

    bool IsCoarse(int i, int j) => Layout.HasChunk(i, j) && !Layout.IsHighRes(i, j);

    static void StitchLine(float[,] h, int last, System.Func<int, (int x, int z)> at)
    {
        int p = TerrainGrid.HighSamplesPerCell;
        for (int k = 0; k < last; k++)
        {
            if (k % p == 0) continue;
            var (ax, az) = at(k - k % p);
            var (bx, bz) = at(k - k % p + p);
            var (x, z) = at(k);
            h[z, x] = Mathf.Lerp(h[az, ax], h[bz, bx], (k % p) / (float)p);
        }
    }

    /// <summary>The starting cells' offsets (pits, the pond, the map's stamps) added to the chunks they reach, by CellHeightfield.</summary>
    public void BakeCells(World world, float stepHeight)
    {
        var field = new CellHeightfield(Layout, stepHeight) { LaidLandDrawn = false };
        field.Reset(world);
        foreach (var (i, j) in field.Chunks)
        {
            var data = datas[Index(i, j)];
            int res = data.heightmapResolution;
            var h = data.GetHeights(0, 0, res, res);
            var offsets = field.Current(i, j);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++) h[z, x] += offsets[z, x] / MaxHeight;
            data.SetHeights(0, 0, h);
        }
    }

    /// <summary>A chunk's own steepness (degrees) at a world point.</summary>
    public float Steepness(Vector2 w)
    {
        var (data, u, v) = Locate(w);
        return data.GetSteepness(u, v);
    }

    // The chunk under a world point, and the point as 0..1 across it.
    (TerrainData data, float u, float v) Locate(Vector2 w)
    {
        float s = Layout.ChunkSize;
        int i = Mathf.Clamp(Mathf.FloorToInt((w.x - Layout.OriginX) / s), 0, Layout.ChunksX - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((w.y - Layout.OriginZ) / s), 0, Layout.ChunksZ - 1);
        var (cx, cz) = Layout.ChunkCorner(i, j);
        return (datas[Index(i, j)], Mathf.Clamp01((w.x - cx) / s), Mathf.Clamp01((w.y - cz) / s));
    }

    /// <summary>Where the chunks stand: the height that puts the flat yard's cell exactly at 0 (heights are stored in 16 bits).</summary>
    public float Bottom(Vector2Int flatCell)
    {
        var (fi, fj, fx, fz) = Layout.CellSample(flatCell.x, flatCell.y);
        return -datas[Index(fi, fj)].GetHeight(fx, fz);
    }

    // --- paint -------------------------------------------------------------

    /// <summary>
    /// Paints every chunk from the valley's shape, then lays the given cells' paint (the old
    /// yard's: its bare pad, the gully) over it with the runtime's soft edges (CellPaint).
    /// </summary>
    public void Paint(TerrainLayer[] layers, World world, IEnumerable<(int x, int y)> cells)
    {
        if (layers == null) return;
        var maps = new float[datas.Length][,,];
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) maps[Index(i, j)] = ShapePaint(i, j, layers.Length);
        var indices = ValleyGround.LayerIndices(layers);
        var bare = new HashSet<(int x, int y)>();
        foreach (var (x, y) in cells)
            if (!CellPaint.IsGrassy(CellPaint.Mix(world, x, y))) bare.Add((x, y));
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => bare.Contains((x, y)) ? CellPaint.Mix(world, x, y) : null;
        foreach (var r in CellPaint.DirtyRects(Layout, CellPaint.Around(Layout, bare)))
        {
            var map = maps[Index(r.I, r.J)];
            CopyBlock(ValleyGround.Compose(CellPaint.Block(Layout, r, paintOf), indices, map), map, r);
        }
        for (int n = 0; n < datas.Length; n++) datas[n].SetAlphamaps(0, 0, maps[n]);
    }

    float[,,] ShapePaint(int i, int j, int layers)
    {
        var data = datas[Index(i, j)];
        int res = data.alphamapResolution;
        var (cx, cz) = Layout.ChunkCorner(i, j);
        var map = new float[res, res, layers];
        for (int tz = 0; tz < res; tz++)
        {
            for (int tx = 0; tx < res; tx++)
            {
                float u = (tx + 0.5f) / res, v = (tz + 0.5f) / res;
                var w = new Vector2(cx + u * Layout.ChunkSize, cz + v * Layout.ChunkSize);
                var weights = Normalized(ValleyShape.LayerWeights(w, data.GetSteepness(u, v)));
                for (int k = 0; k < Mathf.Min(layers, weights.Length); k++) map[tz, tx, k] = weights[k];
            }
        }
        return map;
    }

    static void CopyBlock(float[,,] block, float[,,] map, ChunkRect r)
    {
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++)
                for (int k = 0; k < block.GetLength(2); k++) map[r.Z + z, r.X + x, k] = block[z, x, k];
    }

    // Weights scaled to sum to 1; all grass (the first layer) if they are all zero.
    static float[] Normalized(float[] weights)
    {
        float total = 0f;
        foreach (var x in weights) total += x;
        for (int k = 0; k < weights.Length; k++)
        {
            if (total > 0f) weights[k] /= total;
            else weights[k] = k == 0 ? 1f : 0f;
        }
        return weights;
    }

    // --- trees and details -------------------------------------------------

    /// <summary>Plants trees at world points (x, z) with a scale each, on whichever chunk holds them.</summary>
    public void PlantTrees(TreePrototype[] protos, IEnumerable<(Vector2 at, float scale, int species, float turn)> trees)
    {
        if (protos.Length == 0) return;
        var lists = new List<TreeInstance>[datas.Length];
        for (int n = 0; n < datas.Length; n++)
        {
            datas[n].treePrototypes = protos;
            lists[n] = new List<TreeInstance>();
        }
        foreach (var (at, scale, species, turn) in trees)
        {
            var (data, u, v) = Locate(at);
            lists[System.Array.IndexOf(datas, data)].Add(new TreeInstance
            {
                position = new Vector3(u, 0f, v),
                prototypeIndex = Mathf.Min(species, protos.Length - 1),
                widthScale = scale,
                heightScale = scale,
                rotation = turn,
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
        for (int n = 0; n < datas.Length; n++) datas[n].SetTreeInstances(lists[n].ToArray(), true);
    }

    /// <summary>Grass and plants on every chunk; then cleared off the given cells where they aren't grassy.</summary>
    public void PaintDetails(DetailPrototype[] protos, World world, IEnumerable<(int x, int y)> cells)
    {
        if (protos.Length == 0) return;
        var rng = new System.Random(11);
        foreach (var data in datas)
        {
            data.detailPrototypes = protos;
            var layers = new int[protos.Length][,];
            for (int k = 0; k < protos.Length; k++) layers[k] = new int[DetailRes, DetailRes];
            var corner = ChunkCornerOf(data);
            for (int dz = 0; dz < DetailRes; dz++)
                for (int dx = 0; dx < DetailRes; dx++) ScatterDetails(data, layers, rng, corner, dx, dz);
            for (int k = 0; k < protos.Length; k++) data.SetDetailLayer(0, 0, k, layers[k]);
        }
        foreach (var (x, y) in cells)
        {
            if (CellPaint.IsGrassy(CellPaint.Mix(world, x, y))) continue;
            var r = Layout.TexelRect(x, y);
            ValleyGround.ClearDetail(datas[Index(r.I, r.J)], r);
        }
    }

    Vector2 ChunkCornerOf(TerrainData data)
    {
        int n = System.Array.IndexOf(datas, data);
        var (cx, cz) = Layout.ChunkCorner(n % Layout.ChunksX, n / Layout.ChunksX);
        return new Vector2(cx, cz);
    }

    // One detail cell: lush grass in the meadows, the odd fern or plant where it is drier. One kind
    // of grass to a patch (tens of metres across, blending at the edges) rather than all three mixed
    // everywhere, which read as busy.
    void ScatterDetails(TerrainData data, int[][,] layers, System.Random rng, Vector2 corner, int dx, int dz)
    {
        float u = (dx + 0.5f) / DetailRes, v = (dz + 0.5f) / DetailRes;
        var w = corner + new Vector2(u, v) * Layout.ChunkSize;
        if (ValleyShape.KeepClearOfCabinAndLake(w) || ValleyShape.NearTower(w, 0f)) return;
        if (DefenseFieldBuilder.NearDefenseField(w, 0f)) return;
        if (ValleyShape.HeightAt(w) > 70f || data.GetSteepness(u, v) > 28f) return;
        float lush = Mathf.Clamp01(0.55f + ValleyShape.Fbm(w * 0.05f, 2));
        if (rng.NextDouble() < 1.4 * lush * DetailDensity) layers[Mathf.Min(GrassKind(w, rng), layers.Length - 1)][dz, dx] = 2 + rng.Next(3);
        for (int k = GrassKinds; k < layers.Length; k++)
            if (rng.NextDouble() < 0.025 * (1f - lush) * DetailDensity) layers[k][dz, dx] = 1;
    }

    // The first three details are grass.
    const int GrassKinds = 3;

    // Which grass grows at a point: a slow noise picks the patch's kind, jittered so patches blend.
    static int GrassKind(Vector2 w, System.Random rng)
    {
        float t = Mathf.Clamp01(0.5f + 1.6f * ValleyShape.Fbm(w * 0.015f + new Vector2(37f, 11f), 2));
        t = Mathf.Clamp01(t + ((float)rng.NextDouble() - 0.5f) * 0.3f);
        return Mathf.Min(GrassKinds - 1, (int)(t * GrassKinds));
    }
}
