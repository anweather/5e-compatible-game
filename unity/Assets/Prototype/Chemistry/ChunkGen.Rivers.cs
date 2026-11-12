// The paths a generated chunk's land reads its water and roads from (docs/design/world-squares.md section 5, level 2):
// every river, road and lake of the plan, laid once when the generator is made, so a cell's land stays a function of
// its position. A river runs from the slot it crosses a side by to its square's hub as a curve that leaves the side
// square on (as the paper prototype draws it), passes straight through the hub where one river comes in and one goes out,
// and wanders either side of that by seeded noise (at most WanderCells, none at either end, so neighbours meet). It
// widens downstream with the squares it has drained, and its water only goes down: every crossing stands below the hub
// upstream of it and every hub below each crossing flowing in, worked out over the whole plan before any chunk.
// Roads stay straight from slot to hub; a lake is a disc at a hub where a river ends.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A river as laid in one world square, from upstream to downstream (ChunkGen.Rivers).</summary>
    public sealed class LaidRiver
    {
        public int Square;
        /// <summary>Its path's points, Q16 map cells.</summary>
        public long[] X, Y;
        /// <summary>At each point: the water's surface in land units × 256, and half the river's width in Q16 cells.</summary>
        public long[] Surface, Half;
    }

    public sealed partial class ChunkGen
    {
        /// <summary>The farthest a river wanders from its curve, in cells; and how many pieces a river's path is drawn in.</summary>
        const int WanderCells = 44, PathPieces = 48;
        // A river's curve leaves a side square on for SideReach cells, and runs through a hub straight for HubReach.
        const int SideReach = 110, HubReach = 90;
        // Half a river's width at a spring, and the widest it gets.
        const int SpringHalf = 2, WidestHalf = 8;
        const int SaltRiver = 0x0c65;
        // A canyon (generator 9): half its floor's width, its walls' width, in cells; how deep it cuts, in metres (a land
        // stamp's canyon cuts 14 m: World.Land's).
        const int CanyonHalf = 7, CanyonWall = 7, CanyonDepthMetres = 14;

        sealed class Segment
        {
            public bool River, Lake;
            /// <summary>A canyon (generator 9): a cut, Half cells of floor either side of its path, its walls out to Reach.</summary>
            public bool Canyon;
            /// <summary>The path's points from the slot to the hub, Q16 map cells (one point for a lake).</summary>
            public long[] Px, Py;
            /// <summary>For water, at each point: its surface (land units × Q) and half its width (Q16 cells).</summary>
            public long[] S, H;
            /// <summary>A road's half width in cells; how far past the path the segment touches the land, in cells.</summary>
            public int Half, Reach;
            public int MinX, MinY, MaxX, MaxY; // the path's cells
            /// <summary>The world square it is laid in.</summary>
            public int Home;
            /// <summary>A river laid from the slot it flows out by: its points run upstream.</summary>
            public bool Out;
        }

        /// <summary>The plan's rivers as laid, each from upstream to downstream (not the lakes).</summary>
        public IReadOnlyList<LaidRiver> Rivers
        {
            get
            {
                var list = new List<LaidRiver>();
                foreach (var s in segments)
                {
                    if (!s.River || s.Lake) continue;
                    var r = new LaidRiver { Square = s.Home, X = (long[])s.Px.Clone(), Y = (long[])s.Py.Clone(), Surface = (long[])s.S.Clone(), Half = (long[])s.H.Clone() };
                    if (s.Out) foreach (var a in new[] { r.X, r.Y, r.Surface, r.Half }) Array.Reverse(a);
                    list.Add(r);
                }
                return list;
            }
        }

        readonly List<Segment> segments = new List<Segment>();
        // Round a world that wraps, the segments near the seam again a world's width east or west, each with the index of
        // the one it copies, so the land just across the seam reads them as the land beside them does.
        readonly List<(int of, Segment copy)> seamCopies = new List<(int, Segment)>();
        long[] hubLevel;
        int[] drained;
        // The squares being worked out, so a loop in the plan's rivers (it has none) can't recurse for ever.
        readonly HashSet<int> open = new HashSet<int>();

        // The rivers and roads from each side's slot to the square's hub, a lake at a hub where a river ends.
        void SetUpPaths()
        {
            int n = Plan.Count;
            hubLevel = new long[n];
            drained = new int[n];
            for (int i = 0; i < n; i++) hubLevel[i] = long.MinValue;
            for (int i = 0; i < n; i++)
            {
                var (hx, hy) = Plan.Hub[i];
                var hub = MapCell(i, hx, hy);
                int ins = 0, outs = 0, inSide = -1, outSide = -1, inSlot = 0, outSlot = 0;
                for (int s = 0; s < 4; s++)
                    foreach (var f in Plan.Features[i][s])
                        if (f.Kind == EdgeKind.River && f.Flow > 0) (ins, inSide, inSlot) = (ins + 1, s, f.Slot);
                        else if (f.Kind == EdgeKind.River && f.Flow < 0) (outs, outSide, outSlot) = (outs + 1, s, f.Slot);
                // One river in and one out with no lake: it runs through the hub, along the line from where it comes in to
                // where it goes out.
                long dirX = 0, dirY = 0;
                if (ins == 1 && outs == 1 && !Plan.Lake[i])
                {
                    var a = SlotCell(i, inSide, inSlot);
                    var b = SlotCell(i, outSide, outSlot);
                    long len = Math.Max(1, BiomeMath.Length((long)(b.x - a.x) * One, (long)(b.y - a.y) * One));
                    dirX = (long)(b.x - a.x) * One * One / len;
                    dirY = (long)(b.y - a.y) * One * One / len;
                }
                long level = HubLevel(i);
                long hubHalf = (ins == 0 && !Plan.Lake[i] ? SpringHalf : HalfFor(Drained(i))) * One;
                for (int s = 0; s < 4; s++)
                    foreach (var f in Plan.Features[i][s])
                    {
                        if (f.Kind == EdgeKind.Canyon && Generator >= 9)
                        {
                            // A canyon from its slot to the hub, as a road is laid: where canyons meet at a hub they join,
                            // and where one ends there ("close"), it ends in a cove.
                            var from = SlotCell(i, s, f.Slot);
                            segments.Add(Bound(new Segment
                            {
                                Canyon = true,
                                Px = new[] { Mid(from.x), Mid(hub.x) },
                                Py = new[] { Mid(from.y), Mid(hub.y) },
                                Half = CanyonHalf,
                                Reach = CanyonHalf + CanyonWall,
                                Home = i,
                            }));
                            continue;
                        }
                        if (f.Kind != EdgeKind.River && f.Kind != EdgeKind.Road) continue;
                        var at = SlotCell(i, s, f.Slot);
                        if (f.Kind == EdgeKind.Road)
                        {
                            int half = f.Main ? MainRoadHalf : RoadHalf;
                            segments.Add(Bound(new Segment
                            {
                                Px = new[] { Mid(at.x), Mid(hub.x) },
                                Py = new[] { Mid(at.y), Mid(hub.y) },
                                Half = half,
                                Reach = half + RoadBank,
                            }));
                            continue;
                        }
                        // Coming in, the water and width are what the square upstream sends; going out, this square's.
                        // The curve reaches the hub along the through line: coming in, along it; going out, against it
                        // (the path is laid from the slot).
                        int j = Plan.Grid.Neighbour(i, s), upstream = f.Flow > 0 && j >= 0 ? Drained(j) : 1;
                        var water = f.Flow > 0
                            ? new Water(CrossingLevel(i, s, f.Slot), level, HalfFor(upstream) * One, hubHalf)
                            : new Water(Math.Min(Surface(at.x, at.y), level - PerMetre * Q), level, HalfFor(Drained(i)) * One, hubHalf);
                        long sign = f.Flow > 0 ? 1 : -1;
                        var curve = new Curve(Mid(at.x), Mid(at.y), Mid(hub.x), Mid(hub.y), s, sign * dirX, sign * dirY);
                        var river = RiverPath(SaltRiver ^ (i * 64 + s * 16 + f.Slot), curve, water);
                        river.Home = i;
                        river.Out = f.Flow < 0;
                        segments.Add(river);
                    }
                if (Plan.Lake[i])
                    segments.Add(Bound(new Segment
                    {
                        Lake = true,
                        River = true,
                        Px = new[] { Mid(hub.x) },
                        Py = new[] { Mid(hub.y) },
                        S = new[] { level },
                        H = new[] { (long)LakeRadius * One },
                        Reach = LakeRadius + RiverFade,
                    }));
            }
            if (WrapX) CopyAcrossTheSeam();
        }

        void CopyAcrossTheSeam()
        {
            const int margin = MainRoadHalf + RoadBank + 2;
            for (int k = 0; k < segments.Count; k++)
            {
                var s = segments[k];
                if (s.MaxX + s.Reach + margin + Size >= CellsW) seamCopies.Add((k, Shifted(s, -CellsW)));
                if (s.MinX - s.Reach - margin - Size <= 0) seamCopies.Add((k, Shifted(s, CellsW)));
            }
        }

        static Segment Shifted(Segment s, int dx)
        {
            var px = new long[s.Px.Length];
            for (int k = 0; k < px.Length; k++) px[k] = s.Px[k] + (long)dx * One;
            return new Segment
            {
                River = s.River,
                Lake = s.Lake,
                Px = px,
                Py = s.Py,
                S = s.S,
                H = s.H,
                Half = s.Half,
                Reach = s.Reach,
                MinX = s.MinX + dx,
                MaxX = s.MaxX + dx,
                MinY = s.MinY,
                MaxY = s.MaxY,
                Home = s.Home,
                Out = s.Out,
            };
        }

        static long Mid(int cell) => (long)cell * One + One / 2;

        // The map cell a feature crosses side s of square i by: on the east and south sides it is the next square's first
        // cell, so the two squares a river crosses between name the same cell.
        (int x, int y) SlotCell(int i, int s, int slot)
        {
            var (sx, sy) = WorldPlan.Inside(s, slot * Size + Size / 2, 0);
            return MapCell(i, sx, sy);
        }

        static int HalfFor(int drainedSquares) => Math.Min(WidestHalf, RiverHalf + drainedSquares - 1);

        // How many squares' rivers drain through square i, itself included.
        int Drained(int i)
        {
            if (drained[i] > 0) return drained[i];
            if (!open.Add(i)) return 1;
            int sum = 1;
            for (int s = 0; s < 4; s++)
                foreach (var f in Plan.Features[i][s])
                {
                    if (f.Kind != EdgeKind.River || f.Flow <= 0) continue;
                    int j = Plan.Grid.Neighbour(i, s);
                    if (j >= 0) sum += Drained(j);
                }
            open.Remove(i);
            drained[i] = sum;
            return sum;
        }

        // The water at square i's hub: its own surface, and a metre below every river flowing in.
        long HubLevel(int i)
        {
            if (hubLevel[i] != long.MinValue) return hubLevel[i];
            var (hx, hy) = Plan.Hub[i];
            var hub = MapCell(i, hx, hy);
            long level = Surface(hub.x, hub.y);
            if (!open.Add(i)) return level;
            for (int s = 0; s < 4; s++)
                foreach (var f in Plan.Features[i][s])
                    if (f.Kind == EdgeKind.River && f.Flow > 0) level = Math.Min(level, CrossingLevel(i, s, f.Slot) - PerMetre * Q);
            open.Remove(i);
            hubLevel[i] = level;
            return level;
        }

        // The water where a river flows into square i across side s: its surface there, and a metre below the hub it
        // comes from (what that square says of the same crossing, as the same cell).
        long CrossingLevel(int i, int s, int slot)
        {
            var at = SlotCell(i, s, slot);
            long level = Surface(at.x, at.y);
            int j = Plan.Grid.Neighbour(i, s);
            if (j < 0 || open.Contains(j)) return level;
            foreach (var f in Plan.Features[j][(s + 2) & 3])
                if (f.Kind == EdgeKind.River && f.Flow < 0 && f.Slot == slot) return Math.Min(level, HubLevel(j) - PerMetre * Q);
            return level;
        }

        // A river's water at its two ends, the slot and the hub: its surface (land units × Q) and half its width (Q16 cells).
        readonly struct Water
        {
            public readonly long S0, S1, H0, H1;

            public Water(long s0, long s1, long h0, long h1)
            {
                S0 = s0;
                S1 = s1;
                H0 = h0;
                H1 = h1;
            }
        }

        // A cubic curve from a slot (A) to a hub (D), Q16 cells: it leaves side s square on for SideReach cells and reaches
        // the hub along (toX, toY) (a Q16 unit, or zero to come in as it likes) for HubReach.
        readonly struct Curve
        {
            readonly long ax, ay, bx, by, cx, cy, dx, dy;

            public Curve(long ax, long ay, long dx, long dy, int s, long toX, long toY)
            {
                this.ax = ax;
                this.ay = ay;
                this.dx = dx;
                this.dy = dy;
                bx = ax - PlanGrid.Dx[s] * (long)SideReach * One;
                by = ay - PlanGrid.Dy[s] * (long)SideReach * One;
                cx = dx - toX * HubReach;
                cy = dy - toY * HubReach;
            }

            public long Chord => BiomeMath.Length(dx - ax, dy - ay) / One + 1;

            static long L(long p, long q, long t) => p + (q - p) * t / One;

            /// <summary>The point at t (Q16), by de Casteljau.</summary>
            public (long x, long y) At(long t)
            {
                long abx = L(ax, bx, t), bcx = L(bx, cx, t), cdx = L(cx, dx, t), aby = L(ay, by, t), bcy = L(by, cy, t), cdy = L(cy, dy, t);
                return (L(L(abx, bcx, t), L(bcx, cdx, t), t), L(L(aby, bcy, t), L(bcy, cdy, t), t));
            }

            /// <summary>The way the curve runs at t, a third of its derivative.</summary>
            public (long x, long y) Tangent(long t)
            {
                long abx = L(ax, bx, t), bcx = L(bx, cx, t), cdx = L(cx, dx, t), aby = L(ay, by, t), bcy = L(by, cy, t), cdy = L(cy, dy, t);
                return (L(bcx, cdx, t) - L(abx, bcx, t), L(bcy, cdy, t) - L(aby, bcy, t));
            }
        }

        // A river along a curve, wandering either side of it, its surface and width running straight from one end to the
        // other by how far along the curve a point is.
        Segment RiverPath(int salt, Curve curve, Water w)
        {
            long length = curve.Chord;
            var seg = new Segment { River = true, Px = new long[PathPieces + 1], Py = new long[PathPieces + 1], S = new long[PathPieces + 1], H = new long[PathPieces + 1] };
            for (int k = 0; k <= PathPieces; k++)
            {
                long t = (long)k * One / PathPieces;
                var (px, py) = curve.At(t);
                // Across the curve: its tangent turned a quarter.
                var (tx, ty) = curve.Tangent(t);
                long tl = BiomeMath.Length(tx / 64, ty / 64);
                long nx = tl == 0 ? 0 : -(ty / 64) * One / tl, ny = tl == 0 ? 0 : (tx / 64) * One / tl;
                // None at either end, and level there, so the river meets the next square's and the hub's straight on.
                long u = 4 * t * (One - t) / One, envelope = u * u / One;
                long along = t * length;
                long wander = (BiomeMath.Centred(seed, salt, along, 0, 150) * 3 + BiomeMath.Centred(seed, salt + 1, along, 0, 47)) / 4;
                long off = wander * WanderCells * envelope / One; // Q16 cells
                seg.Px[k] = px + nx * off / One;
                seg.Py[k] = py + ny * off / One;
                seg.S[k] = w.S0 + (w.S1 - w.S0) * t / One;
                seg.H[k] = w.H0 + (w.H1 - w.H0) * t / One;
            }
            seg.Reach = (int)(Math.Max(w.H0, w.H1) / One) + 1 + RiverFade;
            return Bound(seg);
        }

        static Segment Bound(Segment s)
        {
            s.MinX = s.MinY = int.MaxValue;
            s.MaxX = s.MaxY = int.MinValue;
            for (int k = 0; k < s.Px.Length; k++)
            {
                int x = (int)BiomeMath.FloorDiv(s.Px[k], One), y = (int)BiomeMath.FloorDiv(s.Py[k], One);
                s.MinX = Math.Min(s.MinX, x);
                s.MaxX = Math.Max(s.MaxX, x);
                s.MinY = Math.Min(s.MinY, y);
                s.MaxY = Math.Max(s.MaxY, y);
            }
            return s;
        }

        // The segments whose reach touches the cells [x0, x1] × [y0, y1], and a road's width past them: a road's cell takes
        // the land at its middle, which a river beyond the cell's own reach may have carved.
        List<Segment> Nearby(int x0, int y0, int x1, int y1)
        {
            const int margin = MainRoadHalf + RoadBank + 2;
            var near = new List<Segment>();
            bool Touches(Segment s) =>
                s.MaxX + s.Reach + margin >= x0 && s.MinX - s.Reach - margin <= x1 && s.MaxY + s.Reach + margin >= y0 && s.MinY - s.Reach - margin <= y1;
            int c = 0;
            for (int k = 0; k < segments.Count; k++)
            {
                if (Touches(segments[k])) near.Add(segments[k]);
                // A copy follows the one it copies, so a cell and its twin a world round read their paths in one order.
                for (; c < seamCopies.Count && seamCopies[c].of == k; c++)
                    if (Touches(seamCopies[c].copy)) near.Add(seamCopies[c].copy);
            }
            return near;
        }

        /// <summary>Whether a map cell is within reach of a river, lake or road: land they carve or level, not the biomes' own.</summary>
        public bool NearPath(int x, int y)
        {
            foreach (var s in Nearby(x, y, x, y))
            {
                long d = Distance(s, x, y, out _, out _, out _, out long half);
                long reach = s.River ? half + (long)RiverFade * One : (long)(s.Half + RoadBank) * One;
                if (d < reach + One) return true;
            }
            return false;
        }

        static long Distance(Segment s, int x, int y, out long cx, out long cy) => Distance(s, x, y, out cx, out cy, out _, out _);

        // Distance from a cell's middle to a segment's path, Q16 cells; the nearest point, and for water the surface and
        // half width there. Pieces farther than the segment's reach are passed over.
        static long Distance(Segment s, int x, int y, out long cx, out long cy, out long surface, out long half)
        {
            long qx = Mid(x), qy = Mid(y), reach = ((long)s.Reach + 1) * One;
            long best = long.MaxValue;
            cx = cy = surface = half = 0;
            int pieces = Math.Max(1, s.Px.Length - 1);
            for (int k = 0; k < pieces; k++)
            {
                int k1 = Math.Min(k + 1, s.Px.Length - 1);
                long ax = s.Px[k], ay = s.Py[k], bx = s.Px[k1], by = s.Py[k1];
                if (qx < Math.Min(ax, bx) - reach || qx > Math.Max(ax, bx) + reach || qy < Math.Min(ay, by) - reach || qy > Math.Max(ay, by) + reach) continue;
                long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
                long t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
                long px = ax + (bx - ax) * t / One, py = ay + (by - ay) * t / One;
                long d = BiomeMath.Length(qx - px, qy - py);
                if (d >= best) continue;
                best = d;
                cx = px;
                cy = py;
                if (s.S == null) continue;
                surface = s.S[k] + (s.S[k1] - s.S[k]) * t / One;
                half = s.H[k] + (s.H[k1] - s.H[k]) * t / One;
            }
            return best;
        }
    }
}
