// A hills map's roads (docs/design/hills.md, "Roads"), laid by the world squares' road layout (docs/design/world-squares.md
// sections 2 and 5): the network is decided per world square and per side when the generator is made, a pure function of
// the seed and map.json, and every chunk then draws the roads that reach it from that alone, so chunks made in any order,
// on any thread, on any peer, agree at their edges and over both seams of the torus.
//
// - Main roads run the whole way round the torus along the rows and columns of world squares map.json names; lesser
//   roads join every other world square to them (a spanning tree over the squares' sides, seeded), plus a few loops.
// - A road crosses a side of a world square at one of its 9 slots (one per chunk), the same slot seen from both sides.
// - In each world square, every road side has an arm: a curve from its slot to the square's hub, leaving the side square
//   on, wandering by seeded noise between, and running straight through the hub where only two arms meet. All in Q16
//   cells from the square's north-west corner, integers only.
// - A cell nearest an arm's line within its half width is road, levelled across to the land at the line; the land either
//   side leans back to its own over RoadBank cells (cut on the uphill side, fill on the downhill).
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The roads of a hills map: per world square its hub and arms, per side its road and slot.</summary>
    public sealed class HillsRoads
    {
        /// <summary>A road's kind across a side: none, a lesser road or a main road.</summary>
        public const byte None = 0, Lesser = 1, Main = 2;
        /// <summary>Half a road's width in cells (a cell nearer the line than this is road), and the bank it leans back over.</summary>
        public const int MainHalf = 3, LesserHalf = 2, Bank = 6;
        /// <summary>How far a road's curve leaves a side square on, and comes into a through hub straight, in cells.</summary>
        const int SideReach = 160, HubReach = 140;
        /// <summary>The farthest an arm wanders from its curve, in cells, and the pieces an arm is drawn in.</summary>
        const int WanderCells = 40, Pieces = 32;
        /// <summary>Hubs stand at least this far in from every side of their square, in cells.</summary>
        const int HubMargin = 160;
        const int SaltHub = 0x7a01, SaltSlot = 0x7a02, SaltTree = 0x7a03, SaltWander = 0x7a04;
        const long One = BiomeMath.One;
        const int Square = WorldPlan.SquareCells, Slots = WorldPlan.ChunksPerSquare, Chunk = WorldPlan.ChunkCells;

        /// <summary>One road from a side's slot to its square's hub.</summary>
        public sealed class Arm
        {
            /// <summary>The square it's in, the side it leaves by (0 north, 1 east, 2 south, 3 west) and its kind.</summary>
            public int Home, Side;
            public byte Kind;
            /// <summary>Its line, Q16 cells from its square's north-west corner, from the slot to the hub.</summary>
            public long[] X, Y;
            /// <summary>Its line's bounding box in cells from the square's corner.</summary>
            public int MinX, MinY, MaxX, MaxY;
            public int Half => Kind == Main ? MainHalf : LesserHalf;
            /// <summary>
            /// Where the land along it is steeper than the grade limit, its graded height along its line (Q16 land units,
            /// <see cref="Sub"/> samples a piece, both ends at the land there); null where the land is gentle enough to follow.
            /// </summary>
            public long[] Profile;
            /// <summary>The deepest cut and the highest fill its profile makes, in land units.</summary>
            public int Cut, Fill;
            /// <summary>Its grade in percent where its ends are too far apart for the grade limit (0 where it keeps to it).</summary>
            public int Over;

            /// <summary>The road's height at a point on its line (piece <paramref name="k"/>, Q16 <paramref name="t"/> along it), from its profile.</summary>
            public int ProfileAt(int k, long t)
            {
                long q = t * Sub;
                int s = (int)(q / One), i = k * Sub + s;
                if (i >= Profile.Length - 1) return (int)BiomeMath.FloorDiv(Profile[Profile.Length - 1] + One / 2, One);
                long v = Profile[i] + (Profile[i + 1] - Profile[i]) * (q - (long)s * One) / One;
                return (int)BiomeMath.FloorDiv(v + One / 2, One);
            }
        }

        /// <summary>Samples of a graded arm's profile a piece.</summary>
        public const int Sub = 4;
        /// <summary>How far from either end, in cells, a graded road still keeps to the land, so it meets the roads there.</summary>
        public const int EndCells = 16;

        public readonly int SquaresW, SquaresH;
        // Per world square, its hub in cells from its north-west corner; the road across its east side and its south side,
        // and the slot each crosses at.
        readonly (int x, int y)[] hub;
        readonly byte[] east, south;
        readonly int[] eastSlot, southSlot;
        /// <summary>The rows and columns of world squares the main roads run along.</summary>
        public IReadOnlyList<int> MainRows => mainRows;
        public IReadOnlyList<int> MainColumns => mainColumns;
        readonly int[] mainRows, mainColumns;

        /// <summary>A world square's hub, in cells from its north-west corner.</summary>
        public (int x, int y) HubOf(int square) => hub[square];
        readonly List<Arm>[] arms;
        readonly int seed;
        // Where only main roads go (a massif's ring): a lesser road is never laid where it would reach it.
        readonly Func<int, int, bool> wild;

        public HillsRoads(int seed, int squaresW, int squaresH, int[] mainRows, int[] mainColumns, int loopPercent, Func<int, int, bool> wild = null)
        {
            if (squaresW < 3 || squaresH < 3) throw new FormatException($"{MapFile.FileName}: a hills map's roads need at least 3 x 3 world squares, not {squaresW} x {squaresH}");
            this.seed = seed;
            SquaresW = squaresW;
            SquaresH = squaresH;
            this.mainRows = mainRows ?? Array.Empty<int>();
            this.mainColumns = mainColumns ?? Array.Empty<int>();
            foreach (int r in this.mainRows)
                if (r < 0 || r >= squaresH) throw new FormatException($"{MapFile.FileName}: a main road's row {r} is off the map's {squaresH} rows of world squares");
            foreach (int c in this.mainColumns)
                if (c < 0 || c >= squaresW) throw new FormatException($"{MapFile.FileName}: a main road's column {c} is off the map's {squaresW} columns of world squares");
            int n = squaresW * squaresH;
            hub = new (int, int)[n];
            east = new byte[n];
            south = new byte[n];
            eastSlot = new int[n];
            southSlot = new int[n];
            for (int i = 0; i < n; i++)
            {
                uint h = WorldHash.Hash4(seed, SaltHub, i % squaresW, i / squaresW);
                int span = Square - 2 * HubMargin + 1;
                hub[i] = (HubMargin + (int)(h % (uint)span), HubMargin + (int)((h >> 16) % (uint)span));
                // Slots 1..7: never a corner chunk, so a road never runs along the side it crosses.
                eastSlot[i] = 1 + (int)(WorldHash.Hash4(seed, SaltSlot, i, 1) % (Slots - 2));
                southSlot[i] = 1 + (int)(WorldHash.Hash4(seed, SaltSlot, i, 2) % (Slots - 2));
            }
            this.wild = wild;
            Network(loopPercent);
            arms = new List<Arm>[n];
            for (int i = 0; i < n; i++) arms[i] = LayArms(i);
        }

        /// <summary>map.json's generated "hills" "roads": { "mainRows": [..], "mainColumns": [..], "loops": percent }, or null.</summary>
        public static HillsRoads Read(JObject o, int seed, int squaresW, int squaresH, Func<int, int, bool> wild = null)
        {
            if (o == null) return null;
            int[] Ints(string key)
            {
                var a = o[key] as JArray;
                if (a == null) return Array.Empty<int>();
                var list = new int[a.Count];
                for (int k = 0; k < a.Count; k++) list[k] = (int)a[k];
                return list;
            }
            return new HillsRoads(seed, squaresW, squaresH, Ints("mainRows"), Ints("mainColumns"), (int?)o["loops"] ?? 12, wild)
            {
                GradePercent = (int?)o["grade"] ?? 12,
            };
        }

        int Index(int sx, int sy) => PlanGrid.Mod(sy, SquaresH) * SquaresW + PlanGrid.Mod(sx, SquaresW);

        // Main roads along their rows and columns, then a spanning tree over the other sides in a seeded order (Kruskal),
        // so every world square is on the network; then a side the tree didn't need is a road loopPercent times in 100.
        void Network(int loopPercent)
        {
            int n = SquaresW * SquaresH;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }
            bool Join(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return false;
                if (ra < rb) parent[rb] = ra;
                else parent[ra] = rb;
                return true;
            }
            foreach (int r in mainRows)
                for (int sx = 0; sx < SquaresW; sx++)
                {
                    int i = Index(sx, r);
                    east[i] = Main;
                    Join(i, Index(sx + 1, r));
                }
            foreach (int c in mainColumns)
                for (int sy = 0; sy < SquaresH; sy++)
                {
                    int i = Index(c, sy);
                    south[i] = Main;
                    Join(i, Index(c, sy + 1));
                }
            // Every side once: 2 i for square i's east side, 2 i + 1 for its south side, in a seeded order.
            var sides = new List<(uint key, int side)>(2 * n);
            for (int k = 0; k < 2 * n; k++) sides.Add((WorldHash.Hash4(seed, SaltTree, k, 0), k));
            sides.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.side.CompareTo(b.side));
            foreach (var (key, k) in sides)
            {
                int i = k / 2;
                bool isEast = k % 2 == 0;
                if ((isEast ? east[i] : south[i]) != None) continue;
                int j = isEast ? Index(i % SquaresW + 1, i / SquaresW) : Index(i % SquaresW, i / SquaresW + 1);
                if (Wild(i, isEast ? 1 : 2) || Wild(j, isEast ? 3 : 0)) continue;
                bool road = Join(i, j) || (key >> 8) % 100 < (uint)loopPercent;
                if (!road) continue;
                if (isEast) east[i] = Lesser;
                else south[i] = Lesser;
            }
        }

        // Whether a lesser road from square i's side to its hub would reach wild land: the straight line from the slot to
        // the hub, every 16 cells, with a margin for the arm's wander.
        bool Wild(int i, int side)
        {
            if (wild == null) return false;
            int ox = i % SquaresW * Square, oy = i / SquaresW * Square;
            int qx = i % SquaresW, qy = i / SquaresW;
            int slot = side switch { 0 => southSlot[Index(qx, qy - 1)], 1 => eastSlot[i], 2 => southSlot[i], _ => eastSlot[Index(qx - 1, qy)] };
            var (sx, sy) = SlotPoint(side, slot);
            long ax = sx / One, ay = sy / One, bx = hub[i].x, by = hub[i].y;
            long len = Math.Max(1, BiomeMath.Length((bx - ax) * One, (by - ay) * One) / One);
            for (long d = 0; d <= len; d += 16)
            {
                int x = (int)(ox + ax + (bx - ax) * d / len), y = (int)(oy + ay + (by - ay) * d / len);
                for (int m = -1; m <= 1; m++)
                    if (wild(x + m * WanderCells, y) || wild(x, y + m * WanderCells)) return true;
            }
            return false;
        }

        /// <summary>The grade limit along a road, in percent (map.json's "roads" "grade"): steeper land is cut and filled.</summary>
        public int GradePercent { get; private set; } = 12;

        /// <summary>The main roads' arms' lines, Q16 map cells from each square's corner on the map: what a massif's passes follow.</summary>
        public List<(long[] x, long[] y)> MainLines()
        {
            var lines = new List<(long[], long[])>();
            for (int i = 0; i < arms.Length; i++)
            {
                long ox = (long)(i % SquaresW) * Square * One, oy = (long)(i / SquaresW) * Square * One;
                foreach (var a in arms[i])
                {
                    if (a.Kind != Main) continue;
                    var x = new long[a.X.Length];
                    var y = new long[a.Y.Length];
                    for (int k = 0; k < x.Length; k++)
                    {
                        x[k] = a.X[k] + ox;
                        y[k] = a.Y[k] + oy;
                    }
                    lines.Add((x, y));
                }
            }
            return lines;
        }

        /// <summary>
        /// Grades every arm over <paramref name="land"/> (map cells to land units): an arm whose land is steeper than the
        /// grade limit anywhere along its line gets a profile within it (only arms reaching where <paramref name="steepAt"/>
        /// says the land may be steep are looked at, when it's given), half way between the deepest cut and the highest fill
        /// that would keep to it, held to the land at both ends so arms meet. Call it once, before any chunk is made.
        /// </summary>
        public void Grade(Func<int, int, int> land, Func<int, int, bool> steepAt = null)
        {
            long g = (long)GradePercent * 12 * One / 100; // land units a cell, Q16: a cell is 1.5 m, a unit 0.125 m
            for (int i = 0; i < arms.Length; i++)
            {
                int ox = i % SquaresW * Square, oy = i / SquaresW * Square;
                foreach (var a in arms[i])
                {
                    // Only an arm that reaches land that may be steep (a massif's) is looked at: the rolling hills never are.
                    bool may = steepAt == null;
                    for (int k = 0; k < a.X.Length && !may; k++) may = steepAt(ox + (int)BiomeMath.FloorDiv(a.X[k], One), oy + (int)BiomeMath.FloorDiv(a.Y[k], One));
                    if (may) GradeArm(a, ox, oy, land, g);
                }
            }
        }

        static void GradeArm(Arm a, int ox, int oy, Func<int, int, int> land, long g)
        {
            int n = Pieces * Sub + 1;
            var h = new long[n];
            var s = new long[n];
            long px = 0, py = 0;
            for (int q = 0; q < n; q++)
            {
                int k = Math.Min(Pieces - 1, q / Sub), f = q - k * Sub;
                long x = a.X[k] + (a.X[k + 1] - a.X[k]) * f / Sub, y = a.Y[k] + (a.Y[k + 1] - a.Y[k]) * f / Sub;
                h[q] = (long)land(ox + (int)BiomeMath.FloorDiv(x, One), oy + (int)BiomeMath.FloorDiv(y, One)) * One;
                s[q] = q == 0 ? 0 : s[q - 1] + BiomeMath.Length(x - px, y - py);
                px = x;
                py = y;
            }
            bool steep = false;
            for (int q = 1; q < n && !steep; q++) steep = Math.Abs(h[q] - h[q - 1]) > (s[q] - s[q - 1]) * g / One;
            a.Profile = null;
            if (!steep) return;
            // Within EndCells of either end the road keeps to the land, so it meets the arms at its hub and over its side
            // whatever they do; between, it's half way from the cut to the fill, within the grade of both held ends.
            int a0 = 0, b0 = n - 1;
            while (a0 + 1 < n && s[a0 + 1] <= (long)EndCells * One) a0++;
            while (b0 - 1 > a0 && s[n - 1] - s[b0 - 1] <= (long)EndCells * One) b0--;
            // Where the held ends are further apart than the grade allows, this arm's grade is what it takes (a little over it).
            long need = s[b0] - s[a0] <= 0 ? 0 : Math.Abs(h[b0] - h[a0]) * One / (s[b0] - s[a0]) * 21 / 20 + 1;
            if (need > g)
            {
                a.Over = (int)(need * 100 / (12 * One));
                g = need;
            }
            // The highest profile under the land that keeps to the grade (a cut), and the lowest over it (a fill).
            var up = (long[])h.Clone();
            var down = (long[])h.Clone();
            for (int q = 1; q < n; q++)
            {
                long ds = (s[q] - s[q - 1]) * g / One;
                up[q] = Math.Min(up[q], up[q - 1] + ds);
                down[q] = Math.Max(down[q], down[q - 1] - ds);
            }
            for (int q = n - 2; q >= 0; q--)
            {
                long ds = (s[q + 1] - s[q]) * g / One;
                up[q] = Math.Min(up[q], up[q + 1] + ds);
                down[q] = Math.Max(down[q], down[q + 1] - ds);
            }
            var p = new long[n];
            int cut = 0, fill = 0;
            for (int q = 0; q < n; q++)
            {
                if (q <= a0 || q >= b0) p[q] = h[q];
                else
                {
                    long mid = (up[q] + down[q]) / 2;
                    long lo = Math.Max(h[a0] - (s[q] - s[a0]) * g / One, h[b0] - (s[b0] - s[q]) * g / One);
                    long hi = Math.Min(h[a0] + (s[q] - s[a0]) * g / One, h[b0] + (s[b0] - s[q]) * g / One);
                    p[q] = Math.Min(hi, Math.Max(lo, mid));
                }
                cut = Math.Max(cut, (int)((h[q] - p[q]) / One));
                fill = Math.Max(fill, (int)((p[q] - h[q]) / One));
            }
            // Land that only just steps past the grade somewhere keeps its own line (the hills' roads as they were).
            if (cut == 0 && fill == 0) return;
            a.Profile = p;
            a.Cut = cut;
            a.Fill = fill;
        }

        /// <summary>The road across a side of a world square (0 north, 1 east, 2 south, 3 west) and the slot it crosses at.</summary>
        public (byte kind, int slot) SideOf(int square, int side)
        {
            int sx = square % SquaresW, sy = square / SquaresW;
            switch (side)
            {
                case 0: { int j = Index(sx, sy - 1); return (south[j], southSlot[j]); }
                case 1: return (east[square], eastSlot[square]);
                case 2: return (south[square], southSlot[square]);
                default: { int j = Index(sx - 1, sy); return (east[j], eastSlot[j]); }
            }
        }

        /// <summary>Where a road crosses a side, in Q16 cells from the square's north-west corner: its slot's middle.</summary>
        static (long x, long y) SlotPoint(int side, int slot)
        {
            long along = ((long)slot * Chunk + Chunk / 2) * One;
            switch (side)
            {
                case 0: return (along, 0);
                case 1: return ((long)Square * One, along);
                case 2: return (along, (long)Square * One);
                default: return (0, along);
            }
        }

        // The way into the square from a side, a unit in Q16.
        static (long x, long y) Inward(int side) => side switch { 0 => (0, One), 1 => (-One, 0), 2 => (0, -One), _ => (One, 0) };

        /// <summary>A world square's arms, north side first.</summary>
        public IReadOnlyList<Arm> ArmsOf(int square) => arms[square];

        List<Arm> LayArms(int i)
        {
            var list = new List<Arm>();
            var roads = new List<(int side, byte kind, int slot)>();
            for (int s = 0; s < 4; s++)
            {
                var (kind, slot) = SideOf(i, s);
                if (kind != None) roads.Add((s, kind, slot));
            }
            long hx = (long)hub[i].x * One + One / 2, hy = (long)hub[i].y * One + One / 2;
            foreach (var (side, kind, slot) in roads)
            {
                var (cx, cy) = SlotPoint(side, slot);
                var (nx, ny) = Inward(side);
                long k1x = cx + nx * SideReach, k1y = cy + ny * SideReach;
                // Where only two roads meet, each comes into the hub along the line between their slots, so the road runs
                // through it without a bend; at a junction or a dead end, each comes straight at the hub.
                long k2x = hx, k2y = hy;
                if (roads.Count == 2)
                {
                    var other = roads[0].side == side ? roads[1] : roads[0];
                    var (ox, oy) = SlotPoint(other.side, other.slot);
                    long len = Math.Max(1, BiomeMath.Length(ox - cx, oy - cy));
                    k2x = hx - (ox - cx) * One / len * HubReach;
                    k2y = hy - (oy - cy) * One / len * HubReach;
                }
                list.Add(Lay(i, side, kind, (cx, cy), (k1x, k1y), (k2x, k2y), (hx, hy)));
            }
            return list;
        }

        // A cubic from the slot (a) to the hub (d), with control points b and c, and a seeded wander across the line from a to
        // d that is nothing at either end and level there (16 t² (1 − t)²), so the curve still leaves the side square on and
        // meets the hub as the cubic does.
        Arm Lay(int square, int side, byte kind, (long x, long y) a, (long x, long y) b, (long x, long y) c, (long x, long y) d)
        {
            var (ax, ay) = a;
            var (bx, by) = b;
            var (cx, cy) = c;
            var (dx, dy) = d;
            var arm = new Arm { Home = square, Side = side, Kind = kind, X = new long[Pieces + 1], Y = new long[Pieces + 1] };
            long len = Math.Max(1, BiomeMath.Length(dx - ax, dy - ay));
            long px = -(dy - ay) * One / len, py = (dx - ax) * One / len; // across the line, a unit in Q16
            // Four lattice values along the arm in Q16 [-1, 1], from the seed, the square and the side.
            var lattice = new long[5];
            for (int k = 0; k < lattice.Length; k++) lattice[k] = (WorldHash.Hash4(seed, SaltWander, square * 4 + side, k) % (2 * One + 1)) - One;
            for (int k = 0; k <= Pieces; k++)
            {
                long t = (long)k * One / Pieces, u = One - t;
                long w0 = u * u / One * u / One, w1 = 3 * u * u / One * t / One, w2 = 3 * u * t / One * t / One, w3 = t * t / One * t / One;
                long x = (w0 * ax + w1 * bx + w2 * cx + w3 * dx) / One;
                long y = (w0 * ay + w1 * by + w2 * cy + w3 * dy) / One;
                long q = t * (lattice.Length - 1);
                int li = (int)Math.Min(lattice.Length - 2, q / One);
                long f = BiomeMath.Smoothstep(q - li * One);
                long v = lattice[li] + (lattice[li + 1] - lattice[li]) * f / One;
                long env = 16 * (t * t / One) * (u * u / One) / One;
                long off = v * env / One * WanderCells;
                arm.X[k] = x + px * off / One;
                arm.Y[k] = y + py * off / One;
            }
            arm.MinX = arm.MinY = int.MaxValue;
            arm.MaxX = arm.MaxY = int.MinValue;
            for (int k = 0; k <= Pieces; k++)
            {
                int x = (int)BiomeMath.FloorDiv(arm.X[k], One), y = (int)BiomeMath.FloorDiv(arm.Y[k], One);
                arm.MinX = Math.Min(arm.MinX, x);
                arm.MaxX = Math.Max(arm.MaxX, x);
                arm.MinY = Math.Min(arm.MinY, y);
                arm.MaxY = Math.Max(arm.MaxY, y);
            }
            return arm;
        }

        /// <summary>An arm placed on the map: its square's north-west corner in map cells, unwrapped (past a seam it runs on).</summary>
        public struct Placed
        {
            public Arm Arm;
            public int Ox, Oy;
        }

        /// <summary>
        /// Every arm whose road or bank may reach a cell of [x0, x1] × [y0, y1] (map cells, unwrapped: a box over a seam reads
        /// the squares across it), each placed at its square's copy nearest the box, in a fixed order.
        /// </summary>
        public List<Placed> Near(int x0, int y0, int x1, int y1)
        {
            int reach = MainHalf + Bank + 2;
            var near = new List<Placed>();
            int sx0 = (int)BiomeMath.FloorDiv(x0 - reach - Square, Square), sx1 = (int)BiomeMath.FloorDiv(x1 + reach + Square, Square);
            int sy0 = (int)BiomeMath.FloorDiv(y0 - reach - Square, Square), sy1 = (int)BiomeMath.FloorDiv(y1 + reach + Square, Square);
            for (int sy = sy0; sy <= sy1; sy++)
                for (int sx = sx0; sx <= sx1; sx++)
                {
                    int ox = sx * Square, oy = sy * Square;
                    foreach (var a in arms[Index(sx, sy)])
                        if (ox + a.MaxX + reach >= x0 && ox + a.MinX - reach <= x1 && oy + a.MaxY + reach >= y0 && oy + a.MinY - reach <= y1)
                            near.Add(new Placed { Arm = a, Ox = ox, Oy = oy });
                }
            return near;
        }

        /// <summary>The nearest point on a road line to a cell, and where on which arm it is.</summary>
        public struct Hit
        {
            /// <summary>Its distance in Q16 cells (long.MaxValue for none within its bank), and the point in map cells (unwrapped).</summary>
            public long Distance;
            public int X, Y, Half;
            public Arm Arm;
            /// <summary>The arm's piece it's on and how far along that, Q16.</summary>
            public int Piece;
            public long Along;
        }

        /// <summary>
        /// The nearest road line to a map cell's middle among <paramref name="near"/>: its distance in Q16 cells (long.MaxValue
        /// when none is within its bank), the nearest point on it in map cells (unwrapped) and that road's half width.
        /// </summary>
        public static long Nearest(List<Placed> near, int x, int y, out int cx, out int cy, out int half)
        {
            var hit = Nearest(near, x, y);
            cx = hit.X;
            cy = hit.Y;
            half = hit.Half;
            return hit.Distance;
        }

        /// <summary>The nearest road line to a map cell's middle among <paramref name="near"/>, as a <see cref="Hit"/>.</summary>
        public static Hit Nearest(List<Placed> near, int x, int y)
        {
            long qx = (long)x * One + One / 2, qy = (long)y * One + One / 2;
            long best = long.MaxValue;
            var hit = new Hit { Distance = long.MaxValue };
            foreach (var p in near)
            {
                var a = p.Arm;
                long reach = (long)(a.Half + Bank + 1) * One;
                long ox = (long)p.Ox * One, oy = (long)p.Oy * One;
                for (int k = 0; k < Pieces; k++)
                {
                    long ax = a.X[k] + ox, ay = a.Y[k] + oy, bx = a.X[k + 1] + ox, by = a.Y[k + 1] + oy;
                    if (qx < Math.Min(ax, bx) - reach || qx > Math.Max(ax, bx) + reach || qy < Math.Min(ay, by) - reach || qy > Math.Max(ay, by) + reach) continue;
                    long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
                    long t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
                    long px = ax + (bx - ax) * t / One, py = ay + (by - ay) * t / One;
                    long d = BiomeMath.Length(qx - px, qy - py);
                    if (d >= best || d >= reach) continue;
                    best = d;
                    hit = new Hit { Distance = d, X = (int)BiomeMath.FloorDiv(px, One), Y = (int)BiomeMath.FloorDiv(py, One), Half = a.Half, Arm = a, Piece = k, Along = t };
                }
            }
            return hit;
        }

        /// <summary>
        /// A main road's line along row <paramref name="index"/> of world squares (west to east, <paramref name="alongRow"/>) or
        /// along that column (north to south),
        /// as map cells every few cells, unwrapped from the first square's west (north) side, once round the torus: what a walk
        /// along it follows.
        /// </summary>
        public List<(int x, int y)> Route(bool alongRow, int index)
        {
            var path = new List<(int, int)>();
            int count = alongRow ? SquaresW : SquaresH;
            int inSide = alongRow ? 3 : 0, outSide = alongRow ? 1 : 2;
            for (int k = 0; k < count; k++)
            {
                int sx = alongRow ? k : index, sy = alongRow ? index : k;
                int i = Index(sx, sy);
                Arm armIn = null, armOut = null;
                foreach (var a in arms[i])
                {
                    if (a.Side == inSide) armIn = a;
                    if (a.Side == outSide) armOut = a;
                }
                if (armIn == null || armOut == null) throw new InvalidOperationException($"world square ({sx}, {sy}) has no road through it {(alongRow ? "west to east" : "north to south")}");
                int ox = sx * Square, oy = sy * Square;
                for (int p = 0; p <= Pieces; p++) path.Add(Cell(armIn, p, ox, oy));
                for (int p = Pieces - 1; p > 0; p--) path.Add(Cell(armOut, p, ox, oy));
            }
            return path;
        }

        static (int, int) Cell(Arm a, int p, int ox, int oy) => (ox + (int)BiomeMath.FloorDiv(a.X[p], One), oy + (int)BiomeMath.FloorDiv(a.Y[p], One));
    }
}
