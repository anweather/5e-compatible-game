// A cell's land in a generated chunk, from its position alone (docs/design/world-squares.md section 5, level 2): the
// bands of the world squares round it blended over 300 cells, its biomes' base and relief (BiomeField's shape weights,
// anchored by the plan's templates), the coast (the plan's shore depths, wobbled along each side and the same where two
// squares meet), river channels carved by distance to the plan's rivers (a slot on a side to the square's hub, the land
// either side leaning down to the water over 110 m), lakes at a hub where a river ends, roads drawn and levelled through
// their slots, and last the pinned valley's own land met over 90 cells. All integers (Q16 and land units) on WorldHash,
// so every peer gets the same land bit for bit.
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
        const int BandBlend = 300, ValleyBlend = 90, RiverFade = 73, ShoreRamp = 100, SeaRamp = 90, BeachCells = 5;
        const int RiverHalf = 4, LakeRadius = 30, MainRoadHalf = 3, RoadHalf = 2, RoadBank = 6;
        // Land units: a face this steep a cell is bare stone; trees thin out above the tree line.
        // bands.json's wall: an east-west world's edge rows (none in the nine).
        const int WallMetres = 900;
        const int SteepUnits = 10, TreeLine = 220 * PerMetre;
        internal const byte WetSea = 1, WetRiver = 2, WetLake = 3, WetShore = 4;
        const int SaltRelief = 0x0c61, SaltCell = 0x0c62, SaltSpot = 0x0c63, SaltShore = 0x0c64;

        sealed class Shape
        {
            public long Base, Relief; // land units × Q
            public int Kind, Lattice, Octaves, Salt;
            public long Wx, Wy; // the wind's direction in Q16, for dunes
        }

        sealed class Segment
        {
            public bool River, Lake;
            public int X0, Y0, X1, Y1, Half, Reach; // map cells
            public long S0, S1; // the water's surface at each end, land units × Q
        }

        static readonly string[] Kinds = { "hills", "ridged", "dunes" };
        Shape[] shapes;
        readonly List<Segment> segments = new List<Segment>();

        // Sixteen wind directions, (sin, −cos) in Q16, so a dune's crest needs no float.
        static readonly int[] WindSin = { 0, 25080, 46341, 60547, 65536, 60547, 46341, 25080, 0, -25080, -46341, -60547, -65536, -60547, -46341, -25080 };

        void SetUpLand()
        {
            shapes = new Shape[table.Rows.Count];
            for (int b = 0; b < table.Rows.Count; b++)
            {
                var o = table.Rows[b].Shape;
                string kind = (string)o?["kind"] ?? "hills";
                int wind = (int)Math.Round(((double?)o?["wind"] ?? 0) / 22.5, MidpointRounding.AwayFromZero) & 15;
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
            }
            // The rivers and roads from each side's slot to the square's hub, a lake at a hub where a river ends.
            for (int i = 0; i < Plan.Count; i++)
            {
                var (hx, hy) = Plan.Hub[i];
                var hub = MapCell(i, hx, hy);
                long hubSurface = Surface(hub.x, hub.y), lowestIn = long.MaxValue;
                var rivers = new List<Segment>();
                for (int s = 0; s < 4; s++)
                    foreach (var f in Plan.Features[i][s])
                    {
                        if (f.Kind != EdgeKind.River && f.Kind != EdgeKind.Road) continue;
                        var (sx, sy) = WorldPlan.Inside(s, f.Slot * Size + Size / 2, 0);
                        var at = MapCell(i, sx, sy);
                        var seg = new Segment { X0 = at.x, Y0 = at.y, X1 = hub.x, Y1 = hub.y };
                        if (f.Kind == EdgeKind.River)
                        {
                            seg.River = true;
                            seg.Half = RiverHalf;
                            seg.Reach = RiverHalf + RiverFade;
                            seg.S0 = Surface(at.x, at.y);
                            if (f.Flow > 0) lowestIn = Math.Min(lowestIn, seg.S0);
                            rivers.Add(seg);
                        }
                        else
                        {
                            seg.Half = f.Main ? MainRoadHalf : RoadHalf;
                            seg.Reach = seg.Half + RoadBank;
                        }
                        segments.Add(seg);
                    }
                // The water at the hub stands below every river flowing in, so it runs down to it.
                if (lowestIn != long.MaxValue) hubSurface = Math.Min(hubSurface, lowestIn - PerMetre * Q);
                foreach (var r in rivers) r.S1 = hubSurface;
                if (Plan.Lake[i])
                    segments.Add(new Segment { Lake = true, River = true, X0 = hub.x, Y0 = hub.y, X1 = hub.x, Y1 = hub.y, Half = LakeRadius, Reach = LakeRadius + RiverFade, S0 = hubSurface, S1 = hubSurface });
            }
        }

        // Where a river's water stands at a map cell: the bands and the biomes' base there, two metres down, without relief.
        long Surface(int x, int y)
        {
            int px = x + OriginX * Square, py = y + OriginY * Square;
            var b = Field.At(px, FieldY(py), table.ShapeBlendCells);
            long land = BandAt(px, py);
            for (int s = 0; s < 4; s++) land += b.Weight(s) * shapes[b.Id(s)].Base / One;
            return land - 2 * PerMetre * Q;
        }

        int FieldY(int py) => Plan.Grid.H * Square - 1 - py;

        // The segments whose reach touches the cells [x0, x1] × [y0, y1], and a road's width past them: a road's cell takes
        // the land at its middle, which a river beyond the cell's own reach may have carved.
        List<Segment> Nearby(int x0, int y0, int x1, int y1)
        {
            const int margin = MainRoadHalf + RoadBank + 2;
            var near = new List<Segment>();
            foreach (var s in segments)
                if (Math.Max(s.X0, s.X1) + s.Reach + margin >= x0 && Math.Min(s.X0, s.X1) - s.Reach - margin <= x1 &&
                    Math.Max(s.Y0, s.Y1) + s.Reach + margin >= y0 && Math.Min(s.Y0, s.Y1) - s.Reach - margin <= y1) near.Add(s);
            return near;
        }

        struct LandPoint
        {
            public int Land;
            public byte Wet;
            public bool Road;
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
            return new LandPoint { Land = (int)BiomeMath.FloorDiv(g + Q / 2, Q), Wet = wet, Road = road };
        }

        // The land with its coast, rivers and lakes, before roads and the pinned square.
        long Wet(int x, int y, List<Segment> near, out byte wet, out BiomeBlend blend)
        {
            int px = x + OriginX * Square, py = y + OriginY * Square;
            blend = Field.At(px, FieldY(py), table.ShapeBlendCells);
            long g = BandAt(px, py);
            long xq = (long)px * One + One / 2, yq = (long)py * One + One / 2;
            for (int s = 0; s < 4; s++)
            {
                long w = blend.Weight(s);
                if (w <= 0) continue;
                var sh = shapes[blend.Id(s)];
                g += w * (sh.Base + Relief(sh, xq, yq)) / One;
            }
            wet = 0;
            int shore = ShoreDistance(px, py);
            long shoreLand = 4 * Q;
            if (shore <= 0)
            {
                long deep = (long)Plan.Library.BandHeights[0] * PerMetre * Q, shallow = -12 * Q;
                wet = WetSea;
                return shallow + (deep - shallow) * BiomeMath.Smoothstep(-(long)shore * One / SeaRamp) / One;
            }
            // Dry land stands above the shore: only the sea, a river or a lake is under it.
            g = Math.Max(g, shoreLand);
            if (shore < ShoreRamp) g = shoreLand + (g - shoreLand) * BiomeMath.Smoothstep((long)shore * One / ShoreRamp) / One;
            if (shore < BeachCells) wet = WetShore;
            foreach (var s in near)
            {
                if (!s.River) continue;
                long d = Distance(s, x, y, out _, out _, out long t);
                if (d >= (long)s.Reach * One) continue;
                long surface = s.S0 + (s.S1 - s.S0) * t / One, half = s.Half * One;
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

        // The pinned square's own land, met over 90 cells outside it: at its edge a cell is all the square's.
        long MeetPinned(int x, int y, long g, ref byte wet)
        {
            if (Pinned == null) return g;
            int x0 = PinnedX, y0 = PinnedY;
            int cx = Math.Max(x0, Math.Min(x0 + Square - 1, x)), cy = Math.Max(y0, Math.Min(y0 + Square - 1, y));
            long dx = x - cx, dy = y - cy;
            if (dx == 0 && dy == 0) return g;
            long d = BiomeMath.Sqrt((dx * dx + dy * dy) * One * One);
            if (d >= ValleyBlend * One) return g;
            long v = (long)Pinned.Land[(cy - y0) * Square + cx - x0] * Q;
            g = v + (g - v) * BiomeMath.Smoothstep(d / ValleyBlend) / One;
            if (g < SeaLevel * Q && wet != WetRiver && wet != WetLake) wet = WetSea;
            else if (wet == WetSea) wet = WetShore;
            return g;
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
            int i = sx >= 0 && sy >= 0 && sx < g.W && sy < g.H ? g.Index(sx, sy) : -1;
            var heights = Plan.Library.BandHeights;
            int metres = heights[1];
            if (i >= 0 && Plan.Band[i] > 0) metres = Plan.Wall[i] != 0 || Plan.Band[i] >= heights.Length ? WallMetres : heights[Plan.Band[i]];
            return (long)metres * PerMetre * Q;
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
                yq = along / 3;
            }
            long sum = 0, amps = 0, amp = One;
            int lattice = sh.Lattice;
            for (int o = 0; o < sh.Octaves; o++)
            {
                long n = BiomeMath.Centred(seed, sh.Salt + o, xq, yq, Math.Max(4, lattice));
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

        // Cells from the shore, inland positive and out to sea zero or less; a large number with no coast near. Each open
        // side's shoreline wobbles along it, the wobble gone at the square's corners so it runs on into the next square.
        int ShoreDistance(int px, int py)
        {
            var grid = Plan.Grid;
            int sx = (int)BiomeMath.FloorDiv(px, Square), sy = (int)BiomeMath.FloorDiv(py, Square);
            if (sx < 0 || sy < 0 || sx >= grid.W || sy >= grid.H) return -SeaRamp;
            int i = grid.Index(sx, sy);
            if (Plan.Band[i] == 0) return -SeaRamp;
            var c = Plan.Coasts[i];
            int lx = px - sx * Square, ly = py - sy * Square;
            int[] from = { ly, Square - 1 - lx, Square - 1 - ly, lx };
            int[] along = { lx, ly, lx, ly };
            int best = int.MaxValue;
            for (int s = 0; s < 4; s++)
            {
                if (!c.Open[s]) continue;
                int t = along[s];
                long bow = 4L * t * (Square - t) * One / ((long)Square * Square);
                long wobble = BiomeMath.Centred(seed, SaltShore + i * 4 + s, (long)t * One, 0, 96) * bow / One * c.Depth[s] * 42 / 100 / One;
                best = Math.Min(best, from[s] - c.Depth[s] - (int)wobble);
            }
            for (int k = 0; k < 4; k++)
            {
                if (c.Notch[k] == 0) continue;
                int a = PlanGrid.CornerSides[k][0], b = PlanGrid.CornerSides[k][1];
                long r = BiomeMath.Sqrt(((long)from[a] * from[a] + (long)from[b] * from[b]) * One * One) / One;
                best = Math.Min(best, (int)r - c.Notch[k]);
            }
            return best;
        }

        // Distance from a cell's middle to a segment, Q16 cells; the nearest point, and how far along it is (Q16).
        static long Distance(Segment s, int x, int y, out long cx, out long cy) => Distance(s, x, y, out cx, out cy, out _);

        static long Distance(Segment s, int x, int y, out long cx, out long cy, out long t)
        {
            long ax = s.X0 * One + One / 2, ay = s.Y0 * One + One / 2, bx = s.X1 * One + One / 2, by = s.Y1 * One + One / 2;
            long qx = x * One + One / 2, qy = y * One + One / 2;
            long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
            t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
            cx = ax + (bx - ax) * t / One;
            cy = ay + (by - ay) * t / One;
            return BiomeMath.Length(qx - cx, qy - cy);
        }

        // Whether a landmark may build on a map cell: dry land off the beach and away from the rivers.
        bool Buildable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= CellsW || y >= CellsH) return false;
            if (ShoreDistance(x + OriginX * Square, y + OriginY * Square) < BeachCells * 2) return false;
            foreach (var s in segments)
                if (s.River && Distance(s, x, y, out _, out _) < (long)(s.Half + 3) * One) return false;
            return !IsPinned(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
        }
    }
}
