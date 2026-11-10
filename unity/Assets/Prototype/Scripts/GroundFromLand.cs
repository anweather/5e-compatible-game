using System;
using System.Collections.Generic;
using System.Linq;
using Chem;

// The ground of a map with no ground/ folder (docs/design/maps-as-data.md, section 1): heights from its land, paint from
// CellPaint, trees and grass scattered by the cell's terrain from the world's seed. It fills the same arrays a pack
// holds, so GroundBuilder draws either alike, and it's only drawn: nothing here decides anything shared (D4).
public static class GroundFromLand
{
    /// <summary>What a map's ground is made from.</summary>
    public sealed class Source
    {
        public World World;
        public int[] Land;
        public TerrainGrid Layout;
        public float StepHeight, Unit;
        /// <summary>The map's lowest land, in land units: the terrain reaches at least this far down.</summary>
        public int Lowest;
        public string[] Layers, Grasses, TreeKinds;
        /// <summary>Grass texels along a chunk's side.</summary>
        public int DetailResolution;
        /// <summary>A generated map's biomes, whose ground the grass is painted as; null for any other map.</summary>
        public BiomeGround Biomes;
    }

    // Room below and above the drawable range for a cell's own height, in metres: the valley's chunks have this.
    const float RoomBelow = 5f, RoomAbove = 6f, RoomUnderBed = 1f;
    const int TreeSalt = 8101, GrassSalt = 8102, SizeSalt = 8103, TurnSalt = 8104;
    const int GrassEvery = 3, MostClumps = 3;
    const float TreeSmallest = 0.85f, TreeSpread = 0.5f, TreeInset = 0.2f;
    const uint White = 0xffffffffu;

    /// <summary>The ground a map's land and cells draw.</summary>
    public static GroundPack Build(Source s)
    {
        var pack = Shared(s);
        var field = new CellHeightfield(s.Layout, s.StepHeight, 1f);
        var trees = TreesByChunk(s);
        int across = s.Layout.ChunksX, chunks = across * s.Layout.ChunksZ;
        var built = new GroundChunk[chunks];
        // The chunks side by side (view-rebuild: 729 of them one after another were 5 s of a big map's start). Each reads
        // the world, the land and the layout, which nothing changes meanwhile, and writes only its own chunk, so the order
        // they run in changes nothing; they're added in order after.
        void One(int n)
        {
            int i = n % across, j = n / across;
            built[n] = Chunk(s, field, pack, i, j, trees.TryGetValue((i, j), out var mine) ? mine : new List<(int, int)>());
        }
        if (Threaded) System.Threading.Tasks.Parallel.For(0, chunks, One);
        else for (int n = 0; n < chunks; n++) One(n);
        pack.Chunks.AddRange(built);
        return pack;
    }

    /// <summary>
    /// The same ground a chunk at a time (#205): the pack holds what the chunks share and no chunk, and the function makes
    /// chunk (i, j) as Build would, from the world <paramref name="s"/> holds when it's asked (a new world on the same map
    /// sets it). Safe to call side by side while nothing changes the world.
    /// </summary>
    public static (GroundPack pack, Func<(int i, int j), GroundChunk> make) Lazy(Source s)
    {
        var pack = Shared(s);
        var field = new CellHeightfield(s.Layout, s.StepHeight, 1f);
        return (pack, at => Chunk(s, field, pack, at.i, at.j, TreesIn(s, at.i, at.j)));
    }

    // What the chunks share: the heights' range and the names.
    static GroundPack Shared(Source s)
    {
        var (low, high) = s.World.LandRange;
        double bottom = Math.Min(low * (double)s.Unit - RoomBelow, s.Lowest * (double)s.Unit - RoomUnderBed);
        return new GroundPack { Bottom = bottom, Top = high * (double)s.Unit + RoomAbove, Layers = s.Layers, Grasses = s.Grasses, TreeKinds = s.TreeKinds };
    }

    static GroundChunk Chunk(Source s, CellHeightfield field, GroundPack pack, int i, int j, List<(int x, int y)> trees)
    {
        var chunk = new GroundChunk { I = i, J = j, Resolution = s.Layout.Resolution(i, j), Texels = s.Layout.TexelResolution(i, j), DetailResolution = s.DetailResolution };
        chunk.Heights = Heights(s, field, pack, i, j);
        chunk.Paint = Paint(s, i, j);
        chunk.Details = Details(s, i, j);
        chunk.Trees = Placed(s, pack, chunk, trees);
        return chunk;
    }

    // One chunk's tree cells, in the order TreesByChunk lists them (row by row of the grid).
    static List<(int x, int y)> TreesIn(Source s, int i, int j)
    {
        var list = new List<(int x, int y)>();
        foreach (var (x, y) in s.Layout.CellsIn(i, j))
            if (s.World.At(x, y).T == TerrainIds.Tree) list.Add((x, y));
        return list;
    }

    /// <summary>Whether Build works the chunks out side by side (the default); off, one after another, for comparison: both give the same arrays.</summary>
    public static bool Threaded { get; set; } = true;

    // The land at a world cell, the nearest on the grid past its edge.
    static float LandAt(Source s, int u, int v)
    {
        var (x, y) = s.Layout.GridCell(u, v);
        x = Math.Max(0, Math.Min(s.Layout.GridW - 1, x));
        y = Math.Max(0, Math.Min(s.Layout.GridH - 1, y));
        return s.Land[y * s.Layout.GridW + x];
    }

    // Between the cells' centres, in world cells.
    static float Smooth(Source s, float u, float v)
    {
        int u0 = (int)Math.Floor(u - 0.5f), v0 = (int)Math.Floor(v - 0.5f);
        float fu = u - 0.5f - u0, fv = v - 0.5f - v0;
        float south = LandAt(s, u0, v0) * (1f - fu) + LandAt(s, u0 + 1, v0) * fu;
        float north = LandAt(s, u0, v0 + 1) * (1f - fu) + LandAt(s, u0 + 1, v0 + 1) * fu;
        return south * (1f - fv) + north * fv;
    }

    // Along a coarse chunk's edge: its samples, one a cell, and straight between them.
    static float AlongCoarse(Source s, float u, float v)
    {
        int u0 = (int)Math.Floor(u), v0 = (int)Math.Floor(v);
        float fu = u - u0, fv = v - v0;
        return fu > 0f ? LandAt(s, u0, v0) * (1f - fu) + LandAt(s, u0 + 1, v0) * fu : LandAt(s, u0, v0) * (1f - fv) + LandAt(s, u0, v0 + 1) * fv;
    }

    // A sample's land. A coarse chunk's sample is its cell's; a full-detail chunk's runs smoothly between the cells' centres,
    // and on an edge it shares with a coarse chunk it follows that chunk's, so no crack opens between them.
    static float SampleLand(Source s, int i, int j, int sx, int sz)
    {
        var g = s.Layout;
        int p = g.SamplesPerCell(i, j), last = g.Resolution(i, j) - 1;
        float u = i * TerrainGrid.ChunkCells + sx / (float)p, v = j * TerrainGrid.ChunkCells + sz / (float)p;
        if (p == 1) return LandAt(s, (int)u, (int)v);
        bool Coarse(int a, int b) => g.HasChunk(a, b) && !g.IsHighRes(a, b);
        bool west = sx == 0 && Coarse(i - 1, j), east = sx == last && Coarse(i + 1, j), south = sz == 0 && Coarse(i, j - 1), north = sz == last && Coarse(i, j + 1);
        return west || east || south || north ? AlongCoarse(s, u, v) : Smooth(s, u, v);
    }

    static ushort[] Heights(Source s, CellHeightfield field, GroundPack pack, int i, int j)
    {
        int res = s.Layout.Resolution(i, j);
        var offsets = field.Compute(s.World, i, j);
        var heights = new ushort[res * res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                double metres = SampleLand(s, i, j, x, z) * (double)s.Unit + offsets[z, x];
                double step = (metres - pack.Bottom) / (pack.Top - pack.Bottom) * GroundPack.HeightSteps;
                heights[z * res + x] = (ushort)Math.Max(0, Math.Min(GroundPack.HeightSteps, Math.Round(step)));
            }
        return heights;
    }

    // The map's cells, as CellPaint paints them over their own mix, by the layers' place in the pack.
    static byte[] Paint(Source s, int i, int j)
    {
        var g = s.Layout;
        int texels = g.TexelResolution(i, j);
        var block = CellPaint.Block(g, new ChunkRect(i, j, 0, 0, texels - 1, texels - 1), (x, y) => CellPaint.Mix(s.World, x, y));
        var slots = ValleyGround.LayerIndices(s.Layers);
        var paint = new byte[texels * texels * s.Layers.Length];
        var mix = new float[slots.Length];
        for (int z = 0; z < texels; z++)
            for (int x = 0; x < texels; x++)
            {
                var (cx, cy) = g.CellOfTexel(i, j, x, z);
                cx = Math.Max(0, Math.Min(g.GridW - 1, cx));
                cy = Math.Max(0, Math.Min(g.GridH - 1, cy));
                var own = CellPaint.Mix(s.World, cx, cy);
                float cover = block.Cover[z, x];
                for (int k = 0; k < slots.Length; k++) mix[k] = cover * block.Mix[z, x, k] + (1f - cover) * own[k];
                s.Biomes?.Repaint(cx, cy, mix);
                for (int k = 0; k < slots.Length; k++)
                {
                    if (slots[k] < 0) continue;
                    paint[(slots[k] * texels + z) * texels + x] = (byte)Math.Round(Math.Max(0f, Math.Min(1f, mix[k])) * 255f);
                }
            }
        return paint;
    }

    // Grass on grassy cells, off every few texels by the cell's hash, so it stands in tufts rather than in rows.
    static byte[] Details(Source s, int i, int j)
    {
        var g = s.Layout;
        int res = s.DetailResolution, kinds = s.Grasses.Length, seed = unchecked((int)s.World.Seed);
        var details = new byte[res * res * kinds];
        if (kinds == 0) return details;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                var (cx, cy) = g.GridCell(i * TerrainGrid.ChunkCells + x * TerrainGrid.ChunkCells / res, j * TerrainGrid.ChunkCells + z * TerrainGrid.ChunkCells / res);
                if (!g.InGrid(cx, cy) || !CellPaint.IsGrassy(CellPaint.Mix(s.World, cx, cy)) || s.Biomes?.Grassy(cx, cy) == false) continue;
                uint hash = WorldHash.Hash4(seed, GrassSalt, i * res + x, j * res + z);
                if (hash % GrassEvery != 0) continue;
                int kind = (int)(hash / GrassEvery % (uint)kinds);
                details[(kind * res + z) * res + x] = (byte)(1 + hash / 64 % MostClumps);
            }
        return details;
    }

    static Dictionary<(int i, int j), List<(int x, int y)>> TreesByChunk(Source s)
    {
        var chunks = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var (x, y) in s.Layout.Cells())
        {
            if (s.World.At(x, y).T != TerrainIds.Tree) continue;
            var at = s.Layout.ChunkOfCell(x, y);
            if (!chunks.TryGetValue(at, out var list)) chunks[at] = list = new List<(int, int)>();
            list.Add((x, y));
        }
        return chunks;
    }

    /// <summary>
    /// Where in its cell, how big and which way a tree cell's tree stands, and which kind it is, as the ground draws it:
    /// Inset and Across are the offsets into the world cell (u, v) along x and z, in cells.
    /// </summary>
    public readonly struct TreeSpot
    {
        public readonly float Inset, Across, Size, Rotation;
        public readonly int Kind;

        public TreeSpot(float inset, float across, float size, float rotation, int kind)
        {
            Inset = inset;
            Across = across;
            Size = size;
            Rotation = rotation;
            Kind = kind;
        }
    }

    /// <summary>The tree grid cell (x, y) draws on a world of this seed with this many tree kinds (Placed's, for any view that draws it too).</summary>
    public static TreeSpot Spot(int seed, int x, int y, int kinds)
    {
        kinds = Math.Max(1, kinds);
        float Part(int salt) => WorldHash.Hash4(seed, salt, x, y) % 1024u / 1024f;
        float inset = TreeInset + (1f - 2f * TreeInset) * Part(TreeSalt), across = TreeInset + (1f - 2f * TreeInset) * Part(TreeSalt + 1);
        return new TreeSpot(inset, across, TreeSmallest + TreeSpread * Part(SizeSalt), Part(TurnSalt) * 6.2831855f,
            (int)(WorldHash.Hash4(seed, TreeSalt + 2, x, y) % (uint)kinds));
    }

    // One tree in each tree cell, somewhere inside it, so the view's tree of a cell is that cell's.
    static GroundTree[] Placed(Source s, GroundPack pack, GroundChunk chunk, List<(int x, int y)> cells)
    {
        int seed = unchecked((int)s.World.Seed), kinds = s.TreeKinds.Length;
        return cells.Select(c =>
        {
            var (u, v) = s.Layout.WorldCell(c.x, c.y);
            var spot = Spot(seed, c.x, c.y, kinds);
            var (i, j, sx, sz) = s.Layout.CellSample(c.x, c.y);
            return new GroundTree
            {
                X = (u - chunk.I * TerrainGrid.ChunkCells + spot.Inset) / TerrainGrid.ChunkCells,
                Y = chunk.Heights[sz * chunk.Resolution + sx] / (float)GroundPack.HeightSteps,
                Z = (v - chunk.J * TerrainGrid.ChunkCells + spot.Across) / TerrainGrid.ChunkCells,
                Width = spot.Size,
                Height = spot.Size,
                Rotation = spot.Rotation,
                Color = White,
                LightmapColor = White,
                Prototype = spot.Kind,
            };
        }).ToArray();
    }
}
