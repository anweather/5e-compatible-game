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
        public const int SeaLevel = -4;
        const long One = BiomeMath.One;
        const int PerMetre = 8;
        const long Q = 256;
        const int BandBlend = 300, ValleyBlend = 90, MeetWidest = 240, MeetPitch = 6, RiverFade = 73, ShoreRamp = 100, SeaRamp = 90, BeachCells = 5;
        const int RiverHalf = 4, LakeRadius = 30, MainRoadHalf = 3, RoadHalf = 2, RoadBank = 6;
        const int WallMetres = 900;
        const int SteepUnits = 10, TreeLine = 220 * PerMetre;
        internal const byte WetSea = 1, WetRiver = 2, WetLake = 3, WetShore = 4;
        const int SaltRelief = 0x0c61, SaltCell = 0x0c62, SaltSpot = 0x0c63, SaltShore = 0x0c64, SaltMassif = 0x0c66;
        const int MassifFrom = 60, ShoulderMetres = 32;

        sealed class Shape
        {
            public long Base, Relief;
            public int Kind, Lattice, Octaves, Salt;
            public long Wx, Wy;
            public long PeriodX, PeriodY;
        }

        static readonly string[] Kinds = { "hills", "ridged", "dunes" };
        Shape[] shapes;
        long grainX, grainY;

        static readonly int[] WindSin = { 0, 25080, 46341, 60547, 65536, 60547, 46341, 25080, 0, -25080, -46341, -60547, -65536, -60547, -46341, -25080 };

        int Period => WrapX ? Plan.Grid.W * Square : 0;

        (long across, long along) Periods(long dx, long dy, int div)
        {
            if (!WrapX) return (0, 0);
            long across = dx != 0 ? Period : 0, along = dy != 0 ? Period / div : 0;
            return (across, along);
        }

        long Div(long a, long d) => WrapX ? BiomeMath.FloorDiv(a, d) : a / d;

        void SetUpLand()
        {
            SetUpRim();
            SetUpShelf();
            int grain = (int)(WorldHash.Hash4(seed, SaltMassif, 0, 0) & 15);
            if (WrapX) grain &= 12;
            grainX = WindSin[grain];
            grainY = WindSin[(grain + 12) & 15];
            shapes = new Shape[table.Rows.Count];
            for (int b = 0; b < table.Rows.Count; b++)
            {
                var o = table.Rows[b].Shape;
                string kind = (string)o?["kind"] ?? "hills";
                int wind = (int)Math.Round(((double?)o?["wind"] ?? 0) / 22.5, MidpointRounding.AwayFromZero) & 15;
                if (WrapX) wind &= 12;
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

        long Surface(int x, int y)
        {
            int px = x + OriginX * Square, py = y + OriginY * Square;
            var b = Field.At(px, FieldY(py), table.ShapeBlendCells);
            long land = BandAt(px, py);
            for (int s = 0; s < 4; s++) land += b.Weight(s) * shapes[b.Id(s)].Base / One;
            return land + Massif(land, (long)px * One + One / 2, (long)py * One + One / 2) - 2 * PerMetre * Q;
        }

        int FieldY(int py) => Plan.Grid.H * Square - 1 - py;

        public string BiomeAt(int x, int y) => table.Rows[Field.At(x + OriginX * Square, FieldY(y + OriginY * Square)).Dominant].Id;

        struct LandPoint
        {
            public int Land;
            public byte Wet;
            public bool Road, Clear;
        }

        public int LandAt(int x, int y) => Hills != null ? HillsLandAt(x, y, out _) : Point(x, y, Nearby(x, y, x, y), out _).Land;

        LandPoint Point(int x, int y, List<Segment> near, out BiomeBlend blend)
        {
            long g = Wet(x, y, near, out byte wet, out blend);
            bool road = false;
            if (wet == 0 || wet == WetShore)
            {
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
            bool clear = (wet == 0 || wet == WetShore) && Clearing(x, y, ref g);
            return new LandPoint { Land = (int)BiomeMath.FloorDiv(g + Q / 2, Q), Wet = wet, Road = road, Clear = clear };
        }

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

        long MeetPinned(int x, int y, long g, ref byte wet)
        {
            if (Pinned == null) return g;
            int x0 = PinnedX, y0 = PinnedY, pw = PinnedW, ph = PinnedH;
            x = x0 + pw / 2 + Dx(x, x0 + pw / 2);
            int cx = Math.Max(x0, Math.Min(x0 + pw - 1, x)), cy = Math.Max(y0, Math.Min(y0 + ph - 1, y));
            long dx = x - cx, dy = y - cy;
            if (dx == 0 && dy == 0) return g;
            long d = BiomeMath.Sqrt((dx * dx + dy * dy) * One * One);
            if (d >= MeetWidest * One) return g;
            long v = RimMean(RimAt(cx - x0, cy - y0), Math.Max(0, d - One) / 2);
            long width = Math.Max(ValleyBlend * One, Math.Min(MeetWidest * One, Math.Abs(g - v) * 3 / 2 * One / (MeetPitch * Q)));
            if (d >= width) return g;
            g = v + (g - v) * BiomeMath.Smoothstep(d * One / width) / One;
            if (g < SeaLevel * Q && wet != WetRiver && wet != WetLake) wet = WetSea;
            else if (wet == WetSea) wet = WetShore;
            return g;
        }

        long[] rimSums;

        void SetUpRim()
        {
            if (Pinned == null) return;
            int ew = PinnedW - 1, eh = PinnedH - 1, n = 2 * (ew + eh);
            var rim = Pinned.Inner != null ? BlockRim() : null;
            rimSums = new long[n + 1];
            for (int p = 0; p < n; p++)
            {
                var (rx, ry) = RimCell(p);
                int land = rim != null ? rim[p] : Pinned.Land[ry * Square + rx];
                rimSums[p + 1] = rimSums[p] + (long)land * Q;
            }
        }

        int[] BlockRim()
        {
            int cw = Pinned.W * WorldPlan.ChunksPerSquare, ch = Pinned.H * WorldPlan.ChunksPerSquare;
            var edge = new List<(int x, int y)>();
            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                    if (i == 0 || j == 0 || i == cw - 1 || j == ch - 1) edge.Add((i, j));
            var made = new GeneratedChunk[edge.Count];
            var known = new KnownChunk[edge.Count];
            System.Threading.Tasks.Parallel.For(0, edge.Count, k =>
            {
                made[k] = Pinned.Inner.Make(edge[k].x, edge[k].y);
                known[k] = Pinned.Inner.Know(edge[k].x, edge[k].y);
            });
            var byChunk = new Dictionary<(int, int), GeneratedChunk>();
            for (int k = 0; k < edge.Count; k++)
            {
                byChunk[edge[k]] = made[k];
                blockKnown[edge[k]] = known[k];
            }
            int n = 2 * (PinnedW - 1 + PinnedH - 1);
            var rim = new int[n];
            for (int p = 0; p < n; p++)
            {
                var (rx, ry) = RimCell(p);
                rim[p] = byChunk[(rx / Size, ry / Size)].Land[(ry % Size) * Size + rx % Size];
            }
            return rim;
        }

        readonly Dictionary<(int x, int y), KnownChunk> blockKnown = new Dictionary<(int x, int y), KnownChunk>();

        (int x, int y) RimCell(int p)
        {
            int ew = PinnedW - 1, eh = PinnedH - 1;
            if (p < ew) return (p, 0);
            if (p < ew + eh) return (ew, p - ew);
            if (p < 2 * ew + eh) return (ew - (p - ew - eh), eh);
            return (0, eh - (p - 2 * ew - eh));
        }

        int RimAt(int rx, int ry)
        {
            int ew = PinnedW - 1, eh = PinnedH - 1;
            if (ry == 0) return rx;
            if (rx == ew) return ew + ry;
            if (ry == eh) return ew + eh + (ew - rx);
            return 2 * ew + eh + (eh - ry);
        }

        long RimMean(int p, long r)
        {
            int k = (int)(r / One);
            long f = r - k * One, lo = RimMean(p, k);
            return f == 0 ? lo : lo + (RimMean(p, k + 1) - lo) * f / One;
        }

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

        const int ShelfCells = 360, ShelfGrid = 16;
        const long Root2 = 92682;
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

        static readonly int[] PassDx = { -1, -1, 0, 1 }, PassDy = { 0, -1, -1, -1 };

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

        long ShelfAt(int px, int py)
        {
            const int span = 2 * ShelfGrid;
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

        long Massif(long plain, long xq, long yq)
        {
            long above = plain - MassifFrom * PerMetre * Q;
            if (above <= 0) return 0;
            long Crest(long n)
            {
                long r = One - Math.Abs(n);
                return r * r / One;
            }
            long p = Period;
            var (pa, pl) = Periods(grainX, grainY, 3);
            long wx = xq + BiomeMath.Centred(seed, SaltMassif + 3, xq, yq, 340, p, 0) * 90, wy = yq + BiomeMath.Centred(seed, SaltMassif + 4, xq, yq, 340, p, 0) * 90;
            long across = (wx * grainX + wy * grainY) / One, along = Div((wy * grainX - wx * grainY) / One, 3);
            long coarse = Crest(BiomeMath.Centred(seed, SaltMassif, across, along, 200, pa, pl));
            long fine = Crest(BiomeMath.Centred(seed, SaltMassif + 1, across, Div(along * 3, 2), 90, pa, pl * 3 / 2)) * coarse / One;
            long finest = Crest(BiomeMath.Centred(seed, SaltMassif + 5, wx, wy, 45, p, 0)) * fine / One;
            long ridge = (4 * coarse + 3 * fine + finest) / 8;
            long broad = BiomeMath.Centred(seed, SaltMassif + 2, xq, yq, 430, p, 0);
            long shape = One / 4 + ridge * 85 / 100 + broad * 3 / 10;
            long lifted = Math.Max(0, above * shape / One);
            const long step = ShoulderMetres * PerMetre * Q;
            long k = BiomeMath.FloorDiv(lifted, step), f = (lifted - k * step) * One / step;
            lifted += (k * step + step * BiomeMath.Smoothstep(f) / One - lifted) / 4;
            return lifted - above;
        }

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

        long Shore(int px, int py)
        {
            var grid = Plan.Grid;
            int sx = (int)BiomeMath.FloorDiv(px, Square), sy = (int)BiomeMath.FloorDiv(py, Square);
            if ((!grid.WrapX && (sx < 0 || sx >= grid.W)) || sy < 0 || sy >= grid.H) return -SeaRamp * One;
            int i = grid.Index(sx, sy);
            if (Plan.Band[i] == 0) return -SeaRamp * One;
            var c = Plan.Coasts[i];
            int lx = px - sx * Square, ly = py - sy * Square;
            Span<int> from = stackalloc int[] { ly, Square - 1 - lx, Square - 1 - ly, lx };
            Span<int> along = stackalloc int[] { lx, ly, lx, ly };
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
