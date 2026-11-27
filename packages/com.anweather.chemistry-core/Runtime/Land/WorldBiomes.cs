// What the land's biome comes from (J-085): the world, not the square template. A world-wide field over the plan: the
// world sketch where one is drawn (WorldSketch, its shapes on top of everything), else a climate read from latitude
// (the plan's coldness by row) and the plan's bands (low land, hills, high ground, peaks), both interpolated between
// world square centres so the land's biome runs on smoothly across world squares and, round a world that wraps, across
// the east-west seam. A biome keeps to the bands its templates come in, so its base and relief stand on the land they
// were shaped for. BiomeField reads it: each region's biome from this climate, and a sketch's shapes laid over the
// regions' blend; the one anchor is the pinned valley's own biome, at the region nearest its middle. The plan weighs
// its templates by what this field puts under each world square, and ChunkGen makes the land from the same field, so
// both agree on every peer.
using System;

namespace Chem
{
    public sealed class WorldBiomes
    {
        const long One = BiomeMath.One;
        const int Square = WorldPlan.SquareCells;

        static readonly long[] BandAltitude = { 6554, 14418, 29491, 47186, 58982 };
        const long BandMargin = 6554;
        const long IceAltitude = 62259, DuneAltitude = 9830;
        internal const long LowAltitude = 14418;

        readonly int gridW, gridH;
        readonly bool wrapX;
        readonly long[] altitude;
        readonly long[] cold;
        readonly (long lo, long hi)[] rowAltitude;
        internal readonly long Latitude;
        internal readonly PlacedSketch Sketch;

        WorldBiomes(WorldPlan plan, BiomeTable table)
        {
            var g = plan.Grid;
            gridW = g.W;
            gridH = g.H;
            wrapX = g.WrapX;
            altitude = new long[g.Count];
            for (int i = 0; i < g.Count; i++)
                altitude[i] = plan.Wall[i] switch
                {
                    1 => IceAltitude,
                    2 => DuneAltitude,
                    _ => Altitude(plan.Band[i]),
                };
            cold = new long[g.H];
            for (int y = 0; y < g.H; y++) cold[y] = plan.Cold[g.Index(0, y)];
            Latitude = Math.Max(0, Math.Min(100, plan.Inputs.Latitude)) * One / 100;
            rowAltitude = new (long, long)[table.Rows.Count];
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var (lo, hi) = plan.Library.BandsOf(table.Rows[r].Id);
                rowAltitude[r] = lo < 0 ? (-1L, -1L) : (Altitude(lo) - BandMargin, Altitude(hi) + BandMargin);
            }
            if (plan.Inputs.Sketch != null) Sketch = new PlacedSketch(plan.Inputs.Sketch, table, plan.Seed, g.W, g.H, g.WrapX);
        }

        public static BiomeField Field(BiomeTable biomes, WorldPlan plan)
        {
            var g = plan.Grid;
            var t = new BiomeTable
            {
                Seed = plan.Seed,
                RegionCells = biomes.RegionCells,
                WarpCells = biomes.WarpCells,
                ClimateCells = biomes.ClimateCells,
                BlendCells = biomes.BlendCells,
                ShapeBlendCells = biomes.ShapeBlendCells,
                SquareCells = biomes.SquareCells,
                ClimateInfluence = biomes.ClimateInfluence,
                AnchorBias = biomes.AnchorBias,
                WrapCells = g.WrapX ? g.W * Square : 0,
                Layout = new int[g.W, g.H],
            };
            t.Rows.AddRange(biomes.Rows);
            for (int x = 0; x < g.W; x++)
                for (int y = 0; y < g.H; y++) t.Layout[x, y] = -1;
            var lib = plan.Library;
            int valley = lib.ValleyOption < 0 ? -1 : t.IndexOf(lib.Options[lib.ValleyOption].Template.Biome ?? "");
            if (valley >= 0 && t.Rows[valley].On && !t.Rows[valley].Ocean) t.Layout[g.X(plan.Valley), g.H - 1 - g.Y(plan.Valley)] = valley;
            return new BiomeField(t, plan.Seed, new WorldBiomes(plan, t));
        }

        internal (long x, long y) PlanPoint(long xq, long yq) => (xq, (long)gridH * Square * One - yq);

        static long Altitude(int band) => BandAltitude[Math.Max(0, Math.Min(BandAltitude.Length - 1, band))];

        internal (long lo, long hi) AltitudeOf(int row) => row < 0 ? (-1L, -1L) : rowAltitude[row];

        internal int PlanRow(int fy) => gridH * Square - 1 - fy;

        internal long Altitude(long xq, long yq) => Between(xq, yq, altitude, gridW);

        internal long Cold(long xq, long yq) => Between(xq, yq, cold, 0);

        long Between(long xq, long yq, long[] values, int stride)
        {
            long u = xq / Square - One / 2, v = yq / Square - One / 2;
            long ix = BiomeMath.FloorDiv(u, One), iy = BiomeMath.FloorDiv(v, One);
            long fx = u - ix * One, fy = v - iy * One;
            int X(long a) => wrapX ? (int)BiomeMath.Mod(a, gridW) : (int)Math.Max(0, Math.Min(gridW - 1, a));
            int Y(long a) => (int)Math.Max(0, Math.Min(gridH - 1, a));
            long At(int x, int y) => stride == 0 ? values[y] : values[y * stride + x];
            long a0 = At(X(ix), Y(iy)), b0 = At(X(ix + 1), Y(iy)), c0 = At(X(ix), Y(iy + 1)), d0 = At(X(ix + 1), Y(iy + 1));
            long top = a0 + (b0 - a0) * fx / One, bottom = c0 + (d0 - c0) * fx / One;
            return top + (bottom - top) * fy / One;
        }
    }
}
