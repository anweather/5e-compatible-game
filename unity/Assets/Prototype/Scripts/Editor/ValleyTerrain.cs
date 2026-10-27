using System.Collections.Generic;
using Chem;
using UnityEditor;
using UnityEngine;

// The valley's ground as a 9×9 grid of chunk Terrains (TerrainGrid.Valley(),
// docs/WORLD_STREAMING.md part 1): 96 m each, 257 heightmaps (4 samples per
// cell) around the play area and 65 elsewhere, all sampled from ValleyShape at
// world positions so shared edges agree, and a full-detail edge facing a coarse
// chunk interpolated between the coarse samples so they meet exactly.
//
// The starting cells are baked in with the runtime's own code: CellHeightfield
// sinks the pond, the pits and the gully into the ground, and CellPaint paints
// the old yard's bare ground with soft edges and clears its grass. Trees are the
// terrain's own, the yard's included. Everything goes into Generated/Valley
// (derived, gitignored).
public static class ValleyTerrain
{
    const string Folder = "Assets/Prototype/Generated/Valley";
    const float Depth = 22f;        // the terrain's base below the yard
    const float MaxHeight = 300f;
    const int DetailRes = TerrainGrid.ChunkCells;   // one detail cell per grid cell
    // The old single terrain had a detail cell every 1.76 m; scale chances to keep the grass as dense.
    const float DetailDensity = 2.25f / 3.1f;

    public static readonly TerrainGrid Layout = TerrainGrid.Valley();
    static TerrainData[] datas;
    static Terrain[] terrains;

    public struct Assets
    {
        public TerrainLayer[] layers;
        public List<TreePrototype> trees;
        public List<DetailPrototype> details;
    }

    static int Index(int i, int j) => j * Layout.ChunksX + i;

    // --- heights -----------------------------------------------------------

    /// <summary>Creates every chunk's TerrainData asset and fills in the valley's own heights (no cells yet).</summary>
    public static void CreateChunks(Assets assets)
    {
        AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets/Prototype/Generated", "Valley");
        datas = new TerrainData[Layout.ChunksX * Layout.ChunksZ];
        // Every asset is created before any is filled: creating assets after one is filled can
        // reload it from disk and lose what was set in memory.
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) datas[Index(i, j)] = NewChunk(i, j);
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) datas[Index(i, j)].SetHeights(0, 0, BaseHeights(i, j));
        if (assets.layers != null)
            foreach (var d in datas) d.terrainLayers = assets.layers;
    }

    static TerrainData NewChunk(int i, int j)
    {
        int texels = Layout.TexelResolution(i, j);
        var data = new TerrainData
        {
            heightmapResolution = Layout.Resolution(i, j),
            alphamapResolution = texels,
            baseMapResolution = Mathf.Max(64, texels),
        };
        data.SetDetailResolution(DetailRes, 8);
        // The layers hold clumps per texel (ScatterDetails), not coverage out of 255 (the default,
        // which would draw next to nothing).
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.size = new Vector3(Layout.ChunkSize, MaxHeight, Layout.ChunkSize);
        AssetDatabase.CreateAsset(data, $"{Folder}/Chunk_{i}_{j}.asset");
        return data;
    }

    // A chunk's heights (0..1 of MaxHeight, from Depth below the yard), [z, x], seams stitched.
    static float[,] BaseHeights(int i, int j)
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
    static void RoundSeamCorners(float[,] h, int i, int j)
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
    static bool TouchesFullDetail(int u, int v)
    {
        for (int j = Chunks.ChunkOf(v - 1); j <= Chunks.ChunkOf(v); j++)
            for (int i = Chunks.ChunkOf(u - 1); i <= Chunks.ChunkOf(u); i++)
                if (Layout.HasChunk(i, j) && Layout.IsHighRes(i, j)) return true;
        return false;
    }

    // On a full-detail chunk's edge facing a coarse chunk, the samples between the coarse
    // ones lie on the straight line between them, as the coarse chunk draws its edge.
    static void StitchSeams(float[,] h, int i, int j)
    {
        if (!Layout.IsHighRes(i, j)) return;
        int last = Layout.Resolution(i, j) - 1;
        if (IsCoarse(i - 1, j)) StitchLine(h, last, k => (0, k));
        if (IsCoarse(i + 1, j)) StitchLine(h, last, k => (last, k));
        if (IsCoarse(i, j - 1)) StitchLine(h, last, k => (k, 0));
        if (IsCoarse(i, j + 1)) StitchLine(h, last, k => (k, last));
    }

    static bool IsCoarse(int i, int j) => Layout.HasChunk(i, j) && !Layout.IsHighRes(i, j);

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

    /// <summary>The starting cells' offsets (pits, the pond) added to the chunks they reach, by CellHeightfield.</summary>
    public static void BakeCells(World world, float stepHeight)
    {
        var field = new CellHeightfield(Layout, stepHeight);
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
    public static float Steepness(Vector2 w)
    {
        var (data, u, v) = Locate(w);
        return data.GetSteepness(u, v);
    }

    // The chunk under a world point, and the point as 0..1 across it.
    static (TerrainData data, float u, float v) Locate(Vector2 w)
    {
        float s = Layout.ChunkSize;
        int i = Mathf.Clamp(Mathf.FloorToInt((w.x - Layout.OriginX) / s), 0, Layout.ChunksX - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((w.y - Layout.OriginZ) / s), 0, Layout.ChunksZ - 1);
        var (cx, cz) = Layout.ChunkCorner(i, j);
        return (datas[Index(i, j)], Mathf.Clamp01((w.x - cx) / s), Mathf.Clamp01((w.y - cz) / s));
    }

    // --- paint -------------------------------------------------------------

    /// <summary>
    /// Paints every chunk from the valley's shape, then lays the given cells' paint (the old
    /// yard's: its bare pad, the gully) over it with the runtime's soft edges (CellPaint).
    /// </summary>
    public static void Paint(TerrainLayer[] layers, World world, IEnumerable<(int x, int y)> cells)
    {
        if (layers == null) return;
        var maps = new float[datas.Length][,,];
        for (int j = 0; j < Layout.ChunksZ; j++)
            for (int i = 0; i < Layout.ChunksX; i++) maps[Index(i, j)] = ShapePaint(i, j, layers.Length);
        var indices = ValleyGround.LayerIndices(layers);
        var bare = new HashSet<(int x, int y)>();
        foreach (var (x, y) in cells)
            if (!CellPaint.IsGrassy(CellPaint.Mix(world, world.At(x, y)))) bare.Add((x, y));
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => bare.Contains((x, y)) ? CellPaint.Mix(world, world.At(x, y)) : null;
        foreach (var r in CellPaint.DirtyRects(Layout, CellPaint.Around(Layout, bare)))
        {
            var map = maps[Index(r.I, r.J)];
            CopyBlock(ValleyGround.Compose(CellPaint.Block(Layout, r, paintOf), indices, map), map, r);
        }
        for (int n = 0; n < datas.Length; n++) datas[n].SetAlphamaps(0, 0, maps[n]);
    }

    static float[,,] ShapePaint(int i, int j, int layers)
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
    public static void PlantTrees(List<TreePrototype> protos, IEnumerable<(Vector2 at, float scale, int species, float turn)> trees)
    {
        if (protos.Count == 0) return;
        var lists = new List<TreeInstance>[datas.Length];
        for (int n = 0; n < datas.Length; n++)
        {
            datas[n].treePrototypes = protos.ToArray();
            lists[n] = new List<TreeInstance>();
        }
        foreach (var (at, scale, species, turn) in trees)
        {
            var (data, u, v) = Locate(at);
            lists[System.Array.IndexOf(datas, data)].Add(new TreeInstance
            {
                position = new Vector3(u, 0f, v),
                prototypeIndex = Mathf.Min(species, protos.Count - 1),
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
    public static void PaintDetails(List<DetailPrototype> protos, World world, IEnumerable<(int x, int y)> cells)
    {
        if (protos.Count == 0) return;
        var rng = new System.Random(11);
        foreach (var data in datas)
        {
            data.detailPrototypes = protos.ToArray();
            var layers = new int[protos.Count][,];
            for (int k = 0; k < protos.Count; k++) layers[k] = new int[DetailRes, DetailRes];
            var corner = ChunkCornerOf(data);
            for (int dz = 0; dz < DetailRes; dz++)
                for (int dx = 0; dx < DetailRes; dx++) ScatterDetails(data, layers, rng, corner, dx, dz);
            for (int k = 0; k < protos.Count; k++) data.SetDetailLayer(0, 0, k, layers[k]);
        }
        foreach (var (x, y) in cells)
        {
            if (CellPaint.IsGrassy(CellPaint.Mix(world, world.At(x, y)))) continue;
            var r = Layout.TexelRect(x, y);
            ValleyGround.ClearDetail(datas[Index(r.I, r.J)], r);
        }
    }

    static Vector2 ChunkCornerOf(TerrainData data)
    {
        int n = System.Array.IndexOf(datas, data);
        var (cx, cz) = Layout.ChunkCorner(n % Layout.ChunksX, n / Layout.ChunksX);
        return new Vector2(cx, cz);
    }

    // One detail cell: lush grass in the meadows, the odd fern or plant where it is drier. One kind
    // of grass to a patch (tens of metres across, blending at the edges) rather than all three mixed
    // everywhere, which read as busy.
    static void ScatterDetails(TerrainData data, int[][,] layers, System.Random rng, Vector2 corner, int dx, int dz)
    {
        float u = (dx + 0.5f) / DetailRes, v = (dz + 0.5f) / DetailRes;
        var w = corner + new Vector2(u, v) * Layout.ChunkSize;
        if (ValleyShape.KeepClearOfCabinAndLake(w)) return;
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

    // --- terrains in the scene ----------------------------------------------

    /// <summary>Saves the chunks and puts their Terrains in the scene, with the flat yard exactly at height 0.</summary>
    public static Terrain[] PlaceTerrains(Vector2Int flatCell)
    {
        foreach (var d in datas) EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssets();
        // Heights are stored in 16 bits: put the terrains where a flat sample reads exactly 0.
        var (fi, fj, fx, fz) = Layout.CellSample(flatCell.x, flatCell.y);
        float y = -datas[Index(fi, fj)].GetHeight(fx, fz);

        var parent = new GameObject("Valley").transform;
        terrains = new Terrain[datas.Length];
        for (int j = 0; j < Layout.ChunksZ; j++)
        {
            for (int i = 0; i < Layout.ChunksX; i++)
            {
                var go = Terrain.CreateTerrainGameObject(datas[Index(i, j)]);
                go.name = $"Chunk {i},{j}";
                go.transform.SetParent(parent, false);
                var (cx, cz) = Layout.ChunkCorner(i, j);
                go.transform.position = new Vector3(cx, y, cz);
                terrains[Index(i, j)] = Configure(go.GetComponent<Terrain>(), Layout.IsHighRes(i, j));
            }
        }
        return terrains;
    }

    // Terrains connect to neighbours of the same group for LOD stitching, and Unity only stitches
    // neighbours of the same resolution, so full-detail and coarse chunks are two groups (their
    // shared edges match by construction: StitchSeams).
    static Terrain Configure(Terrain terrain, bool fullDetail)
    {
        terrain.treeDistance = 900f;
        terrain.treeBillboardDistance = 160f;
        terrain.detailObjectDistance = 120f;
        terrain.heightmapPixelError = 4f;
        terrain.drawInstanced = true;
        terrain.allowAutoConnect = true;
        terrain.groupingID = fullDetail ? 1 : 0;
        return terrain;
    }

    /// <summary>The terrain's height at a world point (after PlaceTerrains).</summary>
    public static float GroundAt(Vector3 p)
    {
        var (data, _, _) = Locate(new Vector2(p.x, p.z));
        var terrain = terrains[System.Array.IndexOf(datas, data)];
        return terrain.SampleHeight(p) + terrain.transform.position.y;
    }
}
