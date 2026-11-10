// A cell's land in a generated chunk, from its position alone (docs/design/world-squares.md section 5, level 2): the
// bands of the world squares round it blended over 300 cells, its biomes' base (BiomeField's shape weights, from the
// world's field: the sketch, else latitude and the bands, WorldBiomes), high ground shaped as a massif (ranges along the world's grain, saddles, shoulders) and the
// biomes' relief, the coast (the plan's shore depths, wobbled along each side and the same where two squares meet; the
// sea floor shelving by the distance out from the shore, across the squares' sides),
// river channels carved by distance to the plan's rivers (ChunkGen.Rivers.cs: a wandering curve from a slot on a side
// to the square's hub, the land either side leaning down to the water over 110 m), lakes at a hub where a river ends,
// roads drawn and levelled through their slots, and last the pinned valley's own land met over at least 90 cells. All
// integers (Q16 and land units) on WorldHash, so every peer gets the same land bit for bit.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class ChunkGen
    {
        /// <summary>The map's sea level in land units (map.json's): land under it is under water.</summary>
        public const int SeaLevel = -4;
        const long One = BiomeMath.One;
        /// <summary>Land units per metre.</summary>
        const int PerMetre = 8;
        /// <summary>Sub-units per land unit while land is worked out, rounded at the end.</summary>
        const long Q = 256;
        const int BandBlend = 300, ValleyBlend = 90, MeetWidest = 240, MeetPitch = 6, RiverFade = 73, ShoreRamp = 100, SeaRamp = 90, BeachCells = 5;
        const int RiverHalf = 4, LakeRadius = 30, MainRoadHalf = 3, RoadHalf = 2, RoadBank = 6;
        // bands.json's wall: an east-west world's edge rows (none in the nine).
        const int WallMetres = 900;
        // Land units: a face this steep (1.25 m a cell) a cell is bare dirt, no tree or brush; trees thin out above the
        // tree line.
        const int SteepUnits = 10, TreeLine = 220 * PerMetre;
        internal const byte WetSea = 1, WetRiver = 2, WetLake = 3, WetShore = 4;
        const int SaltRelief = 0x0c61, SaltCell = 0x0c62, SaltSpot = 0x0c63, SaltShore = 0x0c64, SaltMassif = 0x0c66;
        // Metres: the plain above which land is a massif, and the height of a terrace step its shoulders sit on.
        const int MassifFrom = 60, ShoulderMetres = 32;

        sealed class Shape
        {
            public long Base, Relief; // land units × Q
            public int Kind, Lattice, Octaves, Salt;
            public long Wx, Wy; // the wind's direction in Q16, for dunes
            public long PeriodX, PeriodY; // the cells its noise repeats over in its own axes, round a world that wraps
        }

        static readonly string[] Kinds = { "hills", "ridged", "dunes" };
        Shape[] shapes;
        // The world's grain, the way its ranges run: a seeded one of WindSin's sixteen directions, Q16.
        long grainX, grainY;

        // Sixteen wind directions, (sin, −cos) in Q16, so a dune's crest needs no float.
        static readonly int[] WindSin = { 0, 25080, 46341, 60547, 65536, 60547, 46341, 25080, 0, -25080, -46341, -60547, -65536, -60547, -46341, -25080 };

        // Cells round the world east-west where it wraps (0 where it doesn't): what every noise lattice repeats over.
        int Period => WrapX ? Plan.Grid.W * Square : 0;

        // The periods of noise read at (across, along / div) for a direction (dx, dy) in Q16, Period along x: an
        // axis-aligned direction (all a wrapped world allows) puts x in one of them.
        (long across, long along) Periods(long dx, long dy, int div)
        {
            if (!WrapX) return (0, 0);
            long across = dx != 0 ? Period : 0, along = dy != 0 ? Period / div : 0;
            return (across, along);
        }

        // a / d, rounding down where the world wraps (so a point and its copy a world round agree bit for bit), toward
        // zero where it doesn't (as it always has).
        long Div(long a, long d) => WrapX ? BiomeMath.FloorDiv(a, d) : a / d;

        void SetUpLand()
        {
            SetUpRim();
            SetUpShelf();
            int grain = (int)(WorldHash.Hash4(seed, SaltMassif, 0, 0) & 15);
            // Round a world that wraps, ranges run along an axis: a slanted grain's noise can't repeat round it.
            if (WrapX) grain &= 12;
            grainX = WindSin[grain];
            grainY = WindSin[(grain + 12) & 15];
            shapes = new Shape[table.Rows.Count];
            for (int b = 0; b < table.Rows.Count; b++)
            {
                var o = table.Rows[b].Shape;
                string kind = (string)o?["kind"] ?? "hills";
                int wind = (int)Math.Round(((double?)o?["wind"] ?? 0) / 22.5, MidpointRounding.AwayFromZero) & 15;
                if (WrapX) wind &= 12; // dunes too, as the grain
                shapes[b] = new Shape
                {
                    Base = (long)Math.Round(((double?)o?["baseElevation"] ?? 0) * PerMetre * Q, MidpointRounding.AwayFromZero),
                    Relief = (long)Math.Round(((double?)o?["relief"] ?? 0) * PerMetre * Q, MidpointRounding.AwayFromZero),
                    Kind = Array.IndexOf(Kinds, kind) < 0 ? 0 : Array.IndexOf(Kinds, kind),
                    Lattice = Math.Max(8, (int)Math.Round(((double?)o?["featureSize"] ?? 300) / 1.5, MidpointRounding.AwayFromZero)),
                    Octaves = Math.Max(1, (int?)o?["octaves"] ?? 3),
                    Salt = SaltRelief ^ unchecked((int)table.Rows[b].NameHash),
                    Wx = WindSin[wind],
                    Wy = WindSin[(wind + 12) & 15],
                };
                var sh = shapes[b];
                (sh.PeriodX, sh.PeriodY) = sh.Kind == 2 ? Periods(sh.Wx, sh.Wy, 3) : (Period, 0L);
            }
            SetUpPaths();
        }

        // Where a river's water stands at a map cell: the bands and the biomes' base there, shaped as a massif, two metres
        // down, without the biomes' relief.
        long Surface(int x, int y)
        {
            int px = x + OriginX * Square, py = y + OriginY * Square;
            var b = Field.At(px, FieldY(py), table.ShapeBlendCells);
            long land = BandAt(px, py);
            for (int s = 0; s < 4; s++) land += b.Weight(s) * shapes[b.Id(s)].Base / One;
            return land + Massif(land, (long)px * One + One / 2, (long)py * One + One / 2) - 2 * PerMetre * Q;
        }

        int FieldY(int py) => Plan.Grid.H * Square - 1 - py;

        /// <summary>The biome most of a map cell is (a biomes.json id), from the field alone: no water, roads or landmarks.</summary>
        public string BiomeAt(int x, int y) => table.Rows[Field.At(x + OriginX * Square, FieldY(y + OriginY * Square)).Dominant].Id;

        struct LandPoint
        {
            public int Land;
            public byte Wet;
            public bool Road, Clear;
        }

        /// <summary>The land at a map cell, in land units, before any landmark's stamps: a pure function of position.</summary>
        public int LandAt(int x, int y) => Point(x, y, Nearby(x, y, x, y), out _).Land;

        LandPoint Point(int x, int y, List<Segment> near, out BiomeBlend blend)
        {
            long g = Wet(x, y, near, out byte wet, out blend);
            bool road = false;
            if (wet == 0 || wet == WetShore)
            {
                // A road's cells are levelled to the land at its middle, and the land either side leans back to where it was.
                foreach (var s in near)
                {
                    if (s.River) continue;
                    long d = Distance(s, x, y, out long cx, out long cy);
                    long half = s.Half * One;
                    if (d >= (s.Half + RoadBank) * One) continue;
                    int mx = (int)BiomeMath.FloorDiv(cx, One), my = (int)BiomeMath.FloorDiv(cy, One);
                    long centre = Wet(mx, my, near, out byte centreWet, out _);
                    if (centreWet != 0 && centreWet != WetShore) continue;
                    if (d < half)
                    {
                        g = centre;
                        road = true;
                    }
                    else g = centre + (g - centre) * BiomeMath.Smoothstep((d - half) / RoadBank) / One;
                }
            }
            g = MeetPinned(x, y, g, ref wet);
            // A spawn's clearing levels a road through it too (a lay-by: its cells stay road), so a spawn beside a road
            // that climbs still starts on flat ground.
            bool clear = (wet == 0 || wet == WetShore) && Clearing(x, y, ref g);
            return new LandPoint { Land = (int)BiomeMath.FloorDiv(g + Q / 2, Q), Wet = wet, Road = road, Clear = clear };
        }

        // The land with its coast, rivers and lakes, before roads and the pinned square.
        long Wet(int x, int y, List<Segment> near, out byte wet, out BiomeBlend blend)
        {
            int px = x + OriginX * Square, py = y + OriginY * Square;
            blend = Field.At(px, FieldY(py), table.ShapeBlendCells);
            long plain = BandAt(px, py), relief = 0;
            long xq = (long)px * One + One / 2, yq = (long)py * One + One / 2;
            for (int s = 0; s < 4; s++)
            {
                long w = blend.Weight(s);
                if (w <= 0) continue;
                var sh = shapes[blend.Id(s)];
                plain += w * sh.Base / One;
                relief += w * Relief(sh, xq, yq) / One;
            }
            long g = plain + Massif(plain, xq, yq) + relief;
            wet = 0;
            long shore = Shore(px, py);
            long shoreLand = 4 * Q;
            if (shore <= 0)
            {
                long deep = (long)Plan.Library.BandHeights[0] * PerMetre * Q, shallow = -12 * Q;
                wet = WetSea;
                return shallow + (deep - shallow) * BiomeMath.Smoothstep(ShelfAt(px, py) / ShelfCells) / One;
            }
            // Dry land stands above the shore: only the sea, a river or a lake is under it.
            g = Math.Max(g, shoreLand);
            if (shore < ShoreRamp * One) g = shoreLand + (g - shoreLand) * BiomeMath.Smoothstep(shore / ShoreRamp) / One;
            if (shore < BeachCells * One) wet = WetShore;
            foreach (var s in near)
            {
                if (!s.River) continue;
                long d = Distance(s, x, y, out _, out _, out long surface, out long half);
                if (d >= half + (long)RiverFade * One) continue;
                if (d < half)
                {
                    long depth = (s.Lake ? 2 : 1) * PerMetre * Q + PerMetre * Q / 2;
                    long r = d * One / half;
                    g = Math.Min(g, surface - depth * (One - r * r / One) / One);
                    wet = s.Lake ? WetLake : WetRiver;
                }
                else if (g > surface) g = surface + (g - surface) * BiomeMath.Smoothstep((d - half) / RiverFade) / One;
            }
            return g;
        }

        // The pinned square's own land, met over at least 90 cells outside it: at its edge a cell is all the square's. The
        // square's rim is read as a mean along it, over no cells either side for the first cell out (so it meets its pinned
        // neighbour, #193) and half a cell more for each cell further, so its cell-to-cell roughness fades within a few
        // cells instead of running out as a stripe per row or column, and no more than half of it a cell. Where the
        // rim stands far above or below the land beside it, the meet widens (up to 240 cells) so its steepest pitch stays
        // under MeetPitch, a slope, not a stone face along the rim.
        long MeetPinned(int x, int y, long g, ref byte wet)
        {
            if (Pinned == null) return g;
            int x0 = PinnedX, y0 = PinnedY;
            x = x0 + Square / 2 + Dx(x, x0 + Square / 2);
            int cx = Math.Max(x0, Math.Min(x0 + Square - 1, x)), cy = Math.Max(y0, Math.Min(y0 + Square - 1, y));
            long dx = x - cx, dy = y - cy;
            if (dx == 0 && dy == 0) return g;
            long d = BiomeMath.Sqrt((dx * dx + dy * dy) * One * One);
            if (d >= MeetWidest * One) return g;
            long v = RimMean(RimAt(cx - x0, cy - y0), Math.Max(0, d - One) / 2);
            // Smoothstep's steepest pitch is 1.5 times the mean: wide enough that 1.5 × drop / width ≤ MeetPitch.
            long width = Math.Max(ValleyBlend * One, Math.Min(MeetWidest * One, Math.Abs(g - v) * 3 / 2 * One / (MeetPitch * Q)));
            if (d >= width) return g;
            g = v + (g - v) * BiomeMath.Smoothstep(d * One / width) / One;
            if (g < SeaLevel * Q && wet != WetRiver && wet != WetLake) wet = WetSea;
            else if (wet == WetSea) wet = WetShore;
            return g;
        }

        // The pinned square's rim as one loop, clockwise from its north-west cell (north, east, south, west sides), in land
        // units × Q, and its running sums: what the meet reads its rim from.
        long[] rimSums;

        void SetUpRim()
        {
            if (Pinned == null) return;
            int n = 4 * (Square - 1);
            rimSums = new long[n + 1];
            for (int p = 0; p < n; p++)
            {
                var (rx, ry) = RimCell(p);
                rimSums[p + 1] = rimSums[p] + (long)Pinned.Land[ry * Square + rx] * Q;
            }
        }

        static (int x, int y) RimCell(int p)
        {
            int e = Square - 1;
            if (p < e) return (p, 0);
            if (p < 2 * e) return (e, p - e);
            if (p < 3 * e) return (e - (p - 2 * e), e);
            return (0, e - (p - 3 * e));
        }

        // A rim cell's place in the loop, from the pinned square's own coordinates.
        static int RimAt(int rx, int ry)
        {
            int e = Square - 1;
            if (ry == 0) return rx;
            if (rx == e) return e + ry;
            if (ry == e) return 2 * e + (e - rx);
            return 3 * e + (e - ry);
        }

        // The mean of the rim round place p, round the loop, over a reach of r (Q16 cells): between the means over the
        // 2k + 1 and 2k + 3 cells round it, k the whole cells of r, by its fraction.
        long RimMean(int p, long r)
        {
            int k = (int)(r / One);
            long f = r - k * One, lo = RimMean(p, k);
            return f == 0 ? lo : lo + (RimMean(p, k + 1) - lo) * f / One;
        }

        // The mean of the rim over the 2r + 1 cells round place p, round the loop.
        long RimMean(int p, int r)
        {
            int n = rimSums.Length - 1;
            r = Math.Min(r, n / 2 - 1);
            int a = p - r, b = p + r + 1;
            long sum = Span(a, b, n);
            return BiomeMath.FloorDiv(sum, 2 * r + 1);
        }

        long Span(int a, int b, int n)
        {
            if (a < 0) return rimSums[n] - rimSums[a + n] + rimSums[b];
            if (b > n) return rimSums[n] - rimSums[a] + rimSums[b - n];
            return rimSums[b] - rimSums[a];
        }

        // The sea floor shelves by the distance out from the shore (#176; the shores paper's shelf, docs/design/world-squares.md
        // "Shores"): from 1.5 m down at the shore to the sea band's depth 360 cells out, so a coast square's sea runs on into
        // an open-sea square's instead of stepping down where they meet. The distance is a chamfer over a 16-cell grid of the
        // plan, set up once: a sample of land starts at none, one of a coast square's sea at its distance out from that
        // square's shore, and every sea sample takes the nearest of its neighbours' plus the way to them. Read between the
        // samples, it is continuous, and near the shore, where it is roughest, the shelf is flattest.
        const int ShelfCells = 360, ShelfGrid = 16;
        const long Root2 = 92682; // √2 in Q16
        long[] shelf;
        int shelfW, shelfH;

        void SetUpShelf()
        {
            var grid = Plan.Grid;
            shelfW = grid.W * Square / ShelfGrid;
            shelfH = grid.H * Square / ShelfGrid;
            shelf = new long[shelfW * shelfH];
            const long far = ShelfCells * One;
            for (int j = 0; j < shelfH; j++)
                for (int i = 0; i < shelfW; i++)
                {
                    int px = i * ShelfGrid + ShelfGrid / 2, py = j * ShelfGrid + ShelfGrid / 2;
                    long d = far;
                    if (Plan.Band[grid.Index(px / Square, py / Square)] > 0)
                    {
                        long s = Shore(px, py);
                        d = s > 0 ? 0 : Math.Min(far, -s);
                    }
                    shelf[j * shelfW + i] = d;
                }
            // Forward and back over the grid until nothing moves (a few times: the shelf is 23 samples wide).
            bool moved = true;
            while (moved)
            {
                moved = false;
                for (int j = 0; j < shelfH; j++)
                    for (int i = 0; i < shelfW; i++) moved = Relax(i, j, 1) || moved;
                for (int j = shelfH - 1; j >= 0; j--)
                    for (int i = shelfW - 1; i >= 0; i--) moved = Relax(i, j, -1) || moved;
            }
        }

        // The four neighbours a forward pass has been to (west, north-west, north, north-east); a pass back, the others.
        static readonly int[] PassDx = { -1, -1, 0, 1 }, PassDy = { 0, -1, -1, -1 };

        // Sample (i, j) takes the nearest of the four neighbours the pass has been to (way 1 forward, −1 back) plus the
        // way to it, if that's nearer; whether it moved.
        bool Relax(int i, int j, int way)
        {
            long d = shelf[j * shelfW + i], was = d;
            for (int k = 0; k < 4; k++)
            {
                int ni = i + PassDx[k] * way, nj = j + PassDy[k] * way;
                if (nj < 0 || nj >= shelfH) continue;
                if (WrapX) ni = PlanGrid.Mod(ni, shelfW);
                else if (ni < 0 || ni >= shelfW) continue;
                d = Math.Min(d, shelf[nj * shelfW + ni] + (PassDx[k] == 0 || PassDy[k] == 0 ? ShelfGrid * One : ShelfGrid * Root2));
            }
            shelf[j * shelfW + i] = d;
            return d < was;
        }

        // The distance out from the shore at a plan cell (Q16 cells), read between the grid's samples.
        long ShelfAt(int px, int py)
        {
            const int span = 2 * ShelfGrid; // half cells between samples
            long u = 2L * px + 1 - ShelfGrid, v = 2L * py + 1 - ShelfGrid;
            int i0 = (int)BiomeMath.FloorDiv(u, span), j0 = (int)BiomeMath.FloorDiv(v, span);
            long fx = u - (long)i0 * span, fy = v - (long)j0 * span;
            long Sample(int i, int j)
            {
                i = WrapX ? PlanGrid.Mod(i, shelfW) : Math.Max(0, Math.Min(shelfW - 1, i));
                j = Math.Max(0, Math.Min(shelfH - 1, j));
                return shelf[j * shelfW + i];
            }
            long top = Sample(i0, j0) * (span - fx) + Sample(i0 + 1, j0) * fx;
            long bottom = Sample(i0, j0 + 1) * (span - fx) + Sample(i0 + 1, j0 + 1) * fx;
            return (top * (span - fy) + bottom * fy) / (span * span);
        }

        // The bands of the four squares nearest, each square its own across its middle and blended with the next over
        // the 300 cells either side of their border. A sea square counts as low land: the coast takes it down.
        long BandAt(int px, int py)
        {
            var (ax, ux) = Axis(px);
            var (ay, uy) = Axis(py);
            long a = BandHeight(ax, ay), b = BandHeight(ax + 1, ay), c = BandHeight(ax, ay + 1), d = BandHeight(ax + 1, ay + 1);
            long top = a + (b - a) * ux / One, bottom = c + (d - c) * ux / One;
            return top + (bottom - top) * uy / One;
        }

        static (int a, long u) Axis(int p)
        {
            int s = (int)BiomeMath.FloorDiv(p, Square), l = p - s * Square;
            int a = l < Square / 2 ? s - 1 : s;
            long fromBorder = (long)p - ((long)a + 1) * Square;
            return (a, BiomeMath.Smoothstep((fromBorder * 2 + BandBlend + 1) * One / (2 * BandBlend)));
        }

        long BandHeight(int sx, int sy)
        {
            var g = Plan.Grid;
            int i = (g.WrapX || (sx >= 0 && sx < g.W)) && sy >= 0 && sy < g.H ? g.Index(sx, sy) : -1;
            var heights = Plan.Library.BandHeights;
            int metres = heights[1];
            if (i >= 0 && Plan.Band[i] > 0) metres = Plan.Wall[i] != 0 || Plan.Band[i] >= heights.Length ? WallMetres : heights[Plan.Band[i]];
            return (long)metres * PerMetre * Q;
        }

        // What the land's plain (its bands and biomes' base, land units × Q) gains or loses as a massif at a point (Q16
        // cells): land standing more than MassifFrom metres up is the uplift above that, and the uplift is shaped so a high
        // band reads as mountains, not one cap: ridged noise raises crests and spurs with valleys between, broad noise
        // drops some of it to saddles and lifts the rest to summits, and a soft terrace leaves shoulders on the slopes.
        long Massif(long plain, long xq, long yq)
        {
            long above = plain - MassifFrom * PerMetre * Q;
            if (above <= 0) return 0;
            long Crest(long n)
            {
                long r = One - Math.Abs(n);
                return r * r / One;
            }
            // The crests run along where the noise crosses zero: stretched three times along the world's grain (a seeded
            // direction) and warped, so they run on as ranges and wander instead of closing into rings. The finer crests
            // ride on the coarse ones (spurs off a ridge), not on the valleys between.
            long p = Period;
            var (pa, pl) = Periods(grainX, grainY, 3);
            long wx = xq + BiomeMath.Centred(seed, SaltMassif + 3, xq, yq, 340, p, 0) * 90, wy = yq + BiomeMath.Centred(seed, SaltMassif + 4, xq, yq, 340, p, 0) * 90;
            long across = (wx * grainX + wy * grainY) / One, along = Div((wy * grainX - wx * grainY) / One, 3);
            long coarse = Crest(BiomeMath.Centred(seed, SaltMassif, across, along, 200, pa, pl));
            long fine = Crest(BiomeMath.Centred(seed, SaltMassif + 1, across, Div(along * 3, 2), 90, pa, pl * 3 / 2)) * coarse / One;
            long finest = Crest(BiomeMath.Centred(seed, SaltMassif + 5, wx, wy, 45, p, 0)) * fine / One;
            long ridge = (4 * coarse + 3 * fine + finest) / 8;
            long broad = BiomeMath.Centred(seed, SaltMassif + 2, xq, yq, 430, p, 0);
            // 0.25 + 0.85 ridge + 0.3 broad: from nothing in a deep saddle to 1.4 times the uplift on a summit.
            long shape = One / 4 + ridge * 85 / 100 + broad * 3 / 10;
            long lifted = Math.Max(0, above * shape / One);
            // A quarter of the way to terraces ShoulderMetres high, eased: a slope eases to a shoulder, then steepens.
            const long step = ShoulderMetres * PerMetre * Q;
            long k = BiomeMath.FloorDiv(lifted, step), f = (lifted - k * step) * One / step;
            lifted += (k * step + step * BiomeMath.Smoothstep(f) / One - lifted) / 4;
            return lifted - above;
        }

        // A biome's relief at a point (Q16 cells), land units × Q: hills a sum of octaves either side of the base, ridged
        // the sharp crests of folded noise rising from it, dunes ridged crests stretched across the wind.
        long Relief(Shape sh, long xq, long yq)
        {
            if (sh.Relief == 0) return 0;
            if (sh.Kind == 2)
            {
                long across = (xq * sh.Wx + yq * sh.Wy) / One, along = (yq * sh.Wx - xq * sh.Wy) / One;
                xq = across;
                yq = Div(along, 3);
            }
            long sum = 0, amps = 0, amp = One;
            int lattice = sh.Lattice;
            for (int o = 0; o < sh.Octaves; o++)
            {
                long n = BiomeMath.Centred(seed, sh.Salt + o, xq, yq, Math.Max(4, lattice), sh.PeriodX, sh.PeriodY);
                if (sh.Kind != 0)
                {
                    long r = One - Math.Abs(n);
                    n = r * r / One;
                }
                sum += n * amp / One;
                amps += amp;
                amp /= 2;
                lattice /= 2;
            }
            return sum * One / amps * sh.Relief / One;
        }

        // Q16 cells from the shore, inland positive and out to sea zero or less; a large number with no coast near. Each
        // open side's shoreline wobbles along it, the wobble gone at the square's corners so it runs on into the next
        // square. Kept in fractions of a cell: a shore rounded to whole cells steps the land beside it column by column.
        long Shore(int px, int py)
        {
            var grid = Plan.Grid;
            int sx = (int)BiomeMath.FloorDiv(px, Square), sy = (int)BiomeMath.FloorDiv(py, Square);
            if ((!grid.WrapX && (sx < 0 || sx >= grid.W)) || sy < 0 || sy >= grid.H) return -SeaRamp * One;
            int i = grid.Index(sx, sy);
            if (Plan.Band[i] == 0) return -SeaRamp * One;
            var c = Plan.Coasts[i];
            int lx = px - sx * Square, ly = py - sy * Square;
            int[] from = { ly, Square - 1 - lx, Square - 1 - ly, lx };
            int[] along = { lx, ly, lx, ly };
            long best = long.MaxValue / 4;
            for (int s = 0; s < 4; s++)
            {
                if (!c.Open[s]) continue;
                int t = along[s];
                long bow = 4L * t * (Square - t) * One / ((long)Square * Square);
                long wobble = BiomeMath.Centred(seed, SaltShore + i * 4 + s, (long)t * One, 0, 96) * bow / One * c.Depth[s] * 42 / 100;
                best = Math.Min(best, (long)(from[s] - c.Depth[s]) * One - wobble);
            }
            for (int k = 0; k < 4; k++)
            {
                if (c.Notch[k] == 0) continue;
                int a = PlanGrid.CornerSides[k][0], b = PlanGrid.CornerSides[k][1];
                long r = BiomeMath.Sqrt(((long)from[a] * from[a] + (long)from[b] * from[b]) * One * One);
                best = Math.Min(best, r - (long)c.Notch[k] * One);
            }
            return best;
        }

        // Whether a landmark may build on a map cell: dry land off the beach and away from the rivers.
        bool Buildable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= CellsW || y >= CellsH) return false;
            if (Shore(x + OriginX * Square, y + OriginY * Square) < BeachCells * 2 * One) return false;
            foreach (var s in segments)
                if (s.River && Distance(s, x, y, out _, out _, out _, out long half) < half + 3 * One) return false;
            foreach (var (_, s) in seamCopies)
                if (s.River && Distance(s, x, y, out _, out _, out _, out long half) < half + 3 * One) return false;
            return !IsPinned(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
        }
    }
}
