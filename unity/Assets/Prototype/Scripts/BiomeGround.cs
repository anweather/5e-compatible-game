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
    // The field's samples are read a tile of Tile x Tile at a time, as a cell of the tile is first asked about, and kept
    // (at most KeptTiles; all let go when that's reached): read for the whole map at the start, they were 24 MB and
    // 600 ms on the 12 x 10 island, twice over (the near ground's and the far ring's), most never looked at (view-mem).
    const int Tile = 8, KeptTiles = 1024;
    // The most samples Grounds reads (the nine has about 90 thousand: all of them).
    const int MostGroundSamples = 131072;
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Samples> tiles = new();
    readonly int tilesAcross;
    // The tile each thread read last, which nearly every next read is in (a terrain's texels in a row).
    [System.ThreadStatic] static Samples lastTile;
    readonly BiomeField field;
    readonly BiomeTable table;
    readonly float[][] looks;
    readonly int fieldH, ox, oy;
    // Each biome's own ground (Looks) by its id.
    readonly Dictionary<string, float[]> ownGround = new();
    readonly bool[] pinnedChunks;
    readonly int chunksW;

    // One tile's samples: each one's ground (layers weights) and its strongest biome.
    sealed class Samples
    {
        public BiomeGround Owner;
        public int Key;
        public float[] Ground;
        public string[] Strongest;
    }

    BiomeGround(GeneratedMap map)
    {
        var gen = map.Gen;
        field = gen.Field;
        table = field.Table;
        layers = CellPaint.Layers.Count;
        grass = Index(CellPaint.Grass);
        across = gen.CellsW / Step + 2;
        down = gen.CellsH / Step + 2;
        tilesAcross = (across + Tile - 1) / Tile;
        looks = LooksOf(table);
        for (int b = 0; b < looks.Length; b++) ownGround[table.Rows[b].Id] = looks[b];
        fieldH = map.Plan.Grid.H * WorldPlan.SquareCells;
        ox = gen.OriginX * WorldPlan.SquareCells;
        oy = gen.OriginY * WorldPlan.SquareCells;
        chunksW = gen.ChunksW;
        pinnedChunks = new bool[gen.ChunksW * gen.ChunksH];
        for (int cy = 0; cy < gen.ChunksH; cy++)
            for (int cx = 0; cx < gen.ChunksW; cx++) pinnedChunks[cy * chunksW + cx] = gen.IsPinned(cx, cy);
    }

    /// <summary>Tiles of samples read and kept now, for a check.</summary>
    public int TilesKept => tiles.Count;

    // The tile holding sample (i, j), read now if it isn't kept.
    Samples TileOf(int i, int j)
    {
        int key = j / Tile * tilesAcross + i / Tile;
        var last = lastTile;
        if (last != null && last.Owner == this && last.Key == key) return last;
        if (!tiles.TryGetValue(key, out var t))
        {
            t = Read(i / Tile, j / Tile, key);
            if (tiles.Count >= KeptTiles) tiles.Clear();
            tiles[key] = t;
        }
        lastTile = t;
        return t;
    }

    Samples Read(int ti, int tj, int key)
    {
        var t = new Samples { Owner = this, Key = key, Ground = new float[Tile * Tile * layers], Strongest = new string[Tile * Tile] };
        for (int sj = 0; sj < Tile; sj++)
            for (int si = 0; si < Tile; si++)
            {
                int i = ti * Tile + si, j = tj * Tile + sj;
                if (i >= across || j >= down) continue;
                var blend = Blend(i, j);
                int n = sj * Tile + si;
                Add(blend, t.Ground, n * layers);
                t.Strongest[n] = table.Rows[Strongest(blend)].Id;
            }
        return t;
    }

    // The plan's field at sample (i, j) the way ChunkGen reads it: from the plan's corner, its rows from the south.
    BiomeBlend Blend(int i, int j) => field.At(i * Step + ox, fieldH - 1 - (j * Step + oy), table.BlendCells);

    // Sample (i, j)'s ground starts at Ground[at] of the tile returned.
    Samples Sample(int i, int j, out int at)
    {
        var t = TileOf(i, j);
        at = ((j % Tile) * Tile + i % Tile) * layers;
        return t;
    }

    string StrongestAt(int i, int j) => TileOf(i, j).Strongest[(j % Tile) * Tile + i % Tile];

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
    void Add(BiomeBlend blend, float[] samples, int at)
    {
        for (int s = 0; s < 4; s++)
        {
            float w = blend.Weight(s) / (float)BiomeMath.One;
            if (w <= 0f) continue;
            var look = looks[blend.Id(s)];
            for (int k = 0; k < layers; k++) samples[at + k] += w * look[k];
        }
    }

    static int Strongest(BiomeBlend blend)
    {
        int best = 0;
        for (int s = 1; s < 4; s++)
            if (blend.Weight(s) > blend.Weight(best)) best = s;
        return blend.Id(best);
    }

    /// <summary>
    /// Each biome's ground as the near ground draws it, as weights over CellPaint's layers: the mean of the paint of the
    /// map's dry cells where that biome is strongest (their own terrain's paint, their grass turned to the biome's ground),
    /// read every Step cells; a biome with no such cell on the map has its own ground (Looks). For the far ring
    /// (FarMeshes.Palette), so a known chunk's colour comes from the same paint as the drawn chunk beside it.
    /// </summary>
    public Dictionary<string, float[]> Grounds(World world)
    {
        var sums = new Dictionary<string, float[]>();
        var counts = new Dictionary<string, int>();
        var mix = new float[layers];
        // Every sample on a map up to MostGroundSamples of them; on a bigger one, every few (the means barely move), so the
        // far ring's start reads no more of the field than the nine's does.
        int every = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Sqrt(across * (double)down / MostGroundSamples)));
        for (int j = 0; j < down; j += every)
            for (int i = 0; i < across; i += every)
            {
                int x = i * Step, y = j * Step;
                if (x >= world.W || y >= world.H || Pinned(x, y) || CellAsIs(world, x, y) is not { } cell || cell.Water > 0) continue;
                var paint = CellPaint.Mix(world, x, y, cell);
                for (int k = 0; k < layers; k++) mix[k] = paint[k];
                var blend = Blend(i, j);
                string id = table.Rows[Strongest(blend)].Id;
                RepaintWith(blend, mix, x, y);
                if (!sums.TryGetValue(id, out var sum)) sums[id] = sum = new float[layers];
                for (int k = 0; k < layers; k++) sum[k] += mix[k];
                counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            }
        foreach (var (id, sum) in sums)
            for (int k = 0; k < layers; k++) sum[k] /= counts[id];
        foreach (var (id, own) in ownGround)
            if (!sums.ContainsKey(id)) sums[id] = (float[])own.Clone();
        return sums;
    }

    // A cell as the world holds it, without unpacking its chunk (D17): a packed chunk's cell is made from the world's source,
    // as it started (a packed chunk's changes since aren't read: Grounds runs once, as the far ring starts). Reading every
    // Step cells through world.At unpacked every chunk of the map.
    static Cell CellAsIs(World world, int x, int y)
    {
        if (world.Source == null || !world.IsPacked(x >> Chunks.Shift, y >> Chunks.Shift)) return world.At(x, y);
        made.Reset();
        world.Source.Make(x, y, made);
        return made;
    }

    static readonly Cell made = new();

    static int Index(string layer)
    {
        for (int k = 0; k < CellPaint.Layers.Count; k++)
            if (CellPaint.Layers[k] == layer) return k;
        return 0;
    }

    /// <summary>The biome ground of a generated map already read.</summary>
    public static BiomeGround Of(GeneratedMap map) => new(map);

    /// <summary>The biome ground of the generated map made from this folder, or null for a map that isn't generated.</summary>
    public static BiomeGround For(string mapSource)
    {
        Last = null;
        if (string.IsNullOrEmpty(mapSource) || !GeneratedMap.IsGenerated(mapSource)) return null;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var ground = new BiomeGround(LoadedMaps.Get(mapSource));
        StartupTimeline.Mark("biome ground read");
        UnityEngine.Debug.Log($"BIOME GROUND: the plan's biomes read in {timer.Elapsed.TotalMilliseconds:0} ms");
        Last = ground;
        return ground;
    }

    /// <summary>The biome ground For read last, for the map in play (null on a map that isn't generated).</summary>
    public static BiomeGround Last { get; private set; }

    /// <summary>The biome strongest at a cell (its nearest sample's), or null in the pinned square, which keeps its own.</summary>
    public string BiomeAt(int x, int y)
    {
        if (Pinned(x, y)) return null;
        int i = System.Math.Clamp((x + Step / 2) / Step, 0, across - 1), j = System.Math.Clamp((y + Step / 2) / Step, 0, down - 1);
        return StrongestAt(i, j);
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
        float[] sa = Sample(i, j, out int a).Ground, sb = Sample(i + 1, j, out int b).Ground;
        float[] sc = Sample(i, j + 1, out int c).Ground, sd = Sample(i + 1, j + 1, out int d).Ground;
        for (int k = 0; k < layers; k++)
            into[k] = (sa[a + k] * (1f - fu) + sb[b + k] * fu) * (1f - fv) + (sc[c + k] * (1f - fu) + sd[d + k] * fu) * fv;
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

    // A sample's paint with its grass turned to the ground of its blend (Repaint at a sample, without keeping its tile).
    void RepaintWith(BiomeBlend blend, float[] mix, int x, int y)
    {
        float g = mix[grass];
        if (g <= 0f || Pinned(x, y)) return;
        var look = buffer ??= new float[layers];
        System.Array.Clear(look, 0, layers);
        Add(blend, look, 0);
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
