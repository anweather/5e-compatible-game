using System.Collections.Generic;
using Chem;

// How a generated map's ground looks by biome (docs/design/world-squares.md, phase 6): its cells are grass wherever the
// land isn't water, stone or a road, and the plan's biome field says which biome each cell is in, so the paint that grass
// would get goes to the biome's own ground instead: snow on the tundra, ash on the ashlands, salt on the flats, moss in the
// marsh, rock on the mountains. The field is read every few cells and blended between, as it changes only over tens of
// cells. The pinned square (the valley) keeps its own paint. Only drawn: nothing here touches the cells (D4).
public sealed class BiomeGround
{
    // Cells between the field's samples: its blend runs over 80.
    const int Step = 8;

    // Each biome's ground, as weights over CellPaint's layers by name; a biome not listed stays grass.
    static readonly Dictionary<string, (string layer, float weight)[]> Looks = new()
    {
        ["temperate"] = new[] { (CellPaint.Grass, 1f) },
        ["grassland"] = new[] { (CellPaint.Grass, 0.85f), (CellPaint.Earth, 0.15f) },
        ["mountains"] = new[] { (CellPaint.Rock, 0.6f), (CellPaint.Grass, 0.4f) },
        ["tundra"] = new[] { (CellPaint.Snow, 0.85f), (CellPaint.Rock, 0.15f) },
        ["salt-flat"] = new[] { (CellPaint.Sand, 0.65f), (CellPaint.Snow, 0.35f) },
        ["foggy-marsh"] = new[] { (CellPaint.Moss, 0.7f), (CellPaint.Soil, 0.3f) },
        ["ashlands"] = new[] { (CellPaint.Charred, 0.75f), (CellPaint.Rock, 0.25f) },
    };

    readonly int layers, across, down, grass;
    [System.ThreadStatic] static float[] buffer;
    readonly float[] samples;
    readonly bool[] pinnedChunks;
    readonly int chunksW;

    BiomeGround(GeneratedMap map)
    {
        var gen = map.Gen;
        var field = gen.Field;
        var table = field.Table;
        layers = CellPaint.Layers.Count;
        grass = Index(CellPaint.Grass);
        across = gen.CellsW / Step + 2;
        down = gen.CellsH / Step + 2;
        var looks = LooksOf(table);
        samples = new float[across * down * layers];
        int fieldH = map.Plan.Grid.H * WorldPlan.SquareCells, ox = gen.OriginX * WorldPlan.SquareCells, oy = gen.OriginY * WorldPlan.SquareCells;
        System.Threading.Tasks.Parallel.For(0, down, j =>
        {
            for (int i = 0; i < across; i++)
            {
                // The plan's field the way ChunkGen reads it: from the plan's corner, its rows from the south.
                var blend = field.At(i * Step + ox, fieldH - 1 - (j * Step + oy), table.BlendCells);
                Add(blend, looks, (j * across + i) * layers);
            }
        });
        chunksW = gen.ChunksW;
        pinnedChunks = new bool[gen.ChunksW * gen.ChunksH];
        for (int cy = 0; cy < gen.ChunksH; cy++)
            for (int cx = 0; cx < gen.ChunksW; cx++) pinnedChunks[cy * chunksW + cx] = gen.IsPinned(cx, cy);
    }

    // Each biome row's ground as weights over CellPaint's layers.
    float[][] LooksOf(BiomeTable table)
    {
        var looks = new float[table.Rows.Count][];
        for (int b = 0; b < looks.Length; b++)
        {
            looks[b] = new float[layers];
            if (Looks.TryGetValue(table.Rows[b].Id, out var look))
                foreach (var (layer, weight) in look) looks[b][Index(layer)] += weight;
            else looks[b][grass] = 1f;
        }
        return looks;
    }

    // A sample's ground: its biomes' grounds by their weights.
    void Add(BiomeBlend blend, float[][] looks, int at)
    {
        for (int s = 0; s < 4; s++)
        {
            float w = blend.Weight(s) / (float)BiomeMath.One;
            if (w <= 0f) continue;
            var look = looks[blend.Id(s)];
            for (int k = 0; k < layers; k++) samples[at + k] += w * look[k];
        }
    }

    static int Index(string layer)
    {
        for (int k = 0; k < CellPaint.Layers.Count; k++)
            if (CellPaint.Layers[k] == layer) return k;
        return 0;
    }

    /// <summary>The biome ground of the generated map made from this folder, or null for a map that isn't generated.</summary>
    public static BiomeGround For(string mapSource)
    {
        if (string.IsNullOrEmpty(mapSource) || !GeneratedMap.IsGenerated(mapSource)) return null;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var ground = new BiomeGround(GeneratedMap.Load(mapSource));
        UnityEngine.Debug.Log($"BIOME GROUND: the plan's biomes read in {timer.Elapsed.TotalMilliseconds:0} ms");
        return ground;
    }

    bool Pinned(int x, int y)
    {
        int cx = x / TerrainGrid.ChunkCells, cy = y / TerrainGrid.ChunkCells;
        return cx < 0 || cy < 0 || cx >= chunksW || cy * chunksW + cx >= pinnedChunks.Length || pinnedChunks[cy * chunksW + cx];
    }

    // The biomes' ground at a cell, between the samples round it, into a buffer of CellPaint's layers.
    void At(int x, int y, float[] into)
    {
        float u = x / (float)Step, v = y / (float)Step;
        int i = System.Math.Clamp((int)u, 0, across - 2), j = System.Math.Clamp((int)v, 0, down - 2);
        float fu = System.Math.Clamp(u - i, 0f, 1f), fv = System.Math.Clamp(v - j, 0f, 1f);
        int a = (j * across + i) * layers, b = a + layers, c = a + across * layers, d = c + layers;
        for (int k = 0; k < layers; k++)
            into[k] = (samples[a + k] * (1f - fu) + samples[b + k] * fu) * (1f - fv) + (samples[c + k] * (1f - fu) + samples[d + k] * fu) * fv;
    }

    /// <summary>A cell's paint (weights over CellPaint's layers) with its grass turned to its biome's ground.</summary>
    public void Repaint(int x, int y, float[] mix)
    {
        float g = mix[grass];
        if (g <= 0f || Pinned(x, y)) return;
        var look = buffer ??= new float[layers];
        At(x, y, look);
        mix[grass] = 0f;
        for (int k = 0; k < layers; k++) mix[k] += g * look[k];
    }

    /// <summary>Whether grass grows at a cell: where its biome's ground is mostly grass.</summary>
    public bool Grassy(int x, int y)
    {
        if (Pinned(x, y)) return true;
        var look = buffer ??= new float[layers];
        At(x, y, look);
        return look[grass] >= 0.5f;
    }
}
