// Land stamps (docs/design/maps-as-data.md, section 3): the one way land moves. A stamp is a row, a centre, a radius, a
// profile and a rise, and every peer works its cells out alike from the row, in integers: an act lays it, undo_act takes
// the same integers off, so it can be taken back in any order. landMoved is what the stamps add up to, null until the
// first, and the drawn ground reads it (CellHeightfield), so ground and cells stay one thing. Canyons and cliffs are the
// stamp's own shapes, walls that wander in bands of rock: land has one height a point, so a true overhang is the rocks'
// job (LandRocks), and a wall stands as steep as a cell's width allows. A stamp keeps off water
// (J-073), and moves land only: raiders, sight and spells pass over it as they do any hill. The dragon's reset takes all
// stamps back.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One change to the land: a shape applied to height, in land units.</summary>
    public sealed class LandStamp
    {
        public const string Flat = "flat", Dome = "dome", Cone = "cone", Ridge = "ridge", Canyon = "canyon", Cliff = "cliff";
        const int Fixed = 256, RoughSalt = 7331, LatticeShift = 4096;
        // Fixed point: One is 1.0.
        const long One = 1024;
        // How deep a canyon cuts at most, in land units, to stay inside what can be drawn; its rim takes the rest of the wall.
        const long CanyonDeepest = 112;
        // A face is a cell wide for this many land units of wall.
        const long FaceSlope = 48;

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Radius { get; private set; }
        /// <summary>At the middle, in land units; below 0 it's a hollow. A canyon's or cliff's wall, however it's signed; below 0 a cliff's high side is the other way.</summary>
        public int Rise { get; private set; }
        public string Profile { get; private set; }
        /// <summary>A ridge's, canyon's or cliff's line, as a step across the grid; 0, 0 for any other profile.</summary>
        public int AlongX { get; private set; }
        public int AlongY { get; private set; }
        /// <summary>How uneven, 0 to 100 percent of the height at a cell.</summary>
        public int Rough { get; private set; }
        public int Seed { get; private set; }

        /// <summary>
        /// A canyon's or cliff's line as a path of points from its middle, in cells, in place of one straight line: empty for a
        /// straight one. Its face follows the path round its bends, Radius either side of it, and its ends taper over
        /// three tenths of Radius past the first and last points.
        /// </summary>
        public IReadOnlyList<(int x, int y)> Path { get; private set; } = Array.Empty<(int x, int y)>();

#pragma warning disable S107 // a stamp's row is its nine fields
        public static LandStamp Make(int x, int y, int radius, int rise, string profile, int alongX, int alongY, int rough, int seed) =>
            new LandStamp { X = x, Y = y, Radius = radius, Rise = rise, Profile = profile, AlongX = alongX, AlongY = alongY, Rough = rough, Seed = seed };
#pragma warning restore S107

        /// <summary>The same stamp along a path of points from its middle (Path).</summary>
        public LandStamp Along(IEnumerable<(int x, int y)> path)
        {
            var copy = (LandStamp)MemberwiseClone();
            copy.Path = path.ToArray();
            return copy;
        }

        /// <summary>A stamp in a command or a save, or null if it isn't one: nine fields, and a path as a tenth.</summary>
        public static LandStamp Read(JToken row)
        {
            if (!(row is JArray a) || (a.Count != 9 && a.Count != 10)) return null;
            var stamp = Make((int)a[0], (int)a[1], (int)a[2], (int)a[3], (string)a[4], (int)a[5], (int)a[6], (int)a[7], (int)a[8]);
            if (a.Count == 9) return stamp;
            if (!(a[9] is JArray points) || points.Any(p => !(p is JArray xy) || xy.Count != 2)) return null;
            return stamp.Along(points.Select(p => ((int)p[0], (int)p[1])));
        }

        /// <summary>The row, for a command or a save.</summary>
        public JArray Row
        {
            get
            {
                var row = new JArray(X, Y, Radius, Rise, Profile, AlongX, AlongY, Rough, Seed);
                if (Path.Count > 0) row.Add(new JArray(Path.Select(p => (object)new JArray(p.x, p.y)).ToArray()));
                return row;
            }
        }

        /// <summary>The row as text, for the checksum.</summary>
        public string Text => Row.ToString(Newtonsoft.Json.Formatting.None);

        public bool Known =>
            Radius > 0 && Rough >= 0 && Rough <= 100
            && (Path.Count == 0 ? Profile == Flat || Profile == Dome || Profile == Cone || (NeedsLine(Profile) && (AlongX != 0 || AlongY != 0)) : PathKnown);

        // A path is a canyon's or a cliff's, two points or more, each a step from the last.
        bool PathKnown => (Profile == Canyon || Profile == Cliff) && Path.Count >= 2 && Enumerable.Range(1, Path.Count - 1).All(k => Path[k] != Path[k - 1]);

        /// <summary>Whether a profile runs along a line.</summary>
        public static bool NeedsLine(string profile) => profile == Ridge || profile == Canyon || profile == Cliff;

        /// <summary>How far from its middle, in cells, it can reach: Radius, or for a path its furthest point and Radius past it.</summary>
        public int Bound => Path.Count == 0 ? Radius : (int)Root(Path.Max(p => (long)p.x * p.x + (long)p.y * p.y)) + 1 + Radius;

        /// <summary>What it adds at a cell this far from its middle, in land units.</summary>
        public int At(int dx, int dy)
        {
            if (Path.Count > 0) return (int)FormedOnPath(dx, dy);
            long r2 = (long)Radius * Radius, d2 = (long)dx * dx + (long)dy * dy;
            if (d2 > r2) return 0;
            if (Profile == Canyon || Profile == Cliff) return (int)Formed(dx, dy, d2);
            long height = Shape(dx, dy, r2, d2);
            return (int)(height + height * Fractal(X + dx, Y + dy, 1) * Rough * 2 / (100L * One));
        }

        long Shape(int dx, int dy, long r2, long d2)
        {
            switch (Profile)
            {
                case Flat: return Rise;
                case Dome: return Rise * (r2 - d2) / r2;
                case Cone: return Rise * Fall(Root(d2 * Fixed * Fixed)) / (Radius * (long)Fixed);
                default:
                    // Highest on its line, and down to nothing at the line's two ends.
                    long length2 = (long)AlongX * AlongX + (long)AlongY * AlongY;
                    long off = Root(Square((long)dx * AlongY - (long)dy * AlongX) * Fixed * Fixed / length2);
                    long along = Root(Square((long)dx * AlongX + (long)dy * AlongY) * Fixed * Fixed / length2);
                    return Rise * Fall(off) / (Radius * (long)Fixed) * Fall(along) / (Radius * (long)Fixed);
            }
        }

        long Fall(long fixedDistance) => Math.Max(0, Radius * (long)Fixed - fixedDistance);

        static long Square(long v) => v * v;

        // The whole square root, the same on every machine.
        static long Root(long v)
        {
            if (v <= 0) return 0;
            long r = v, next = (v + 1) / 2;
            while (next < r)
            {
                r = next;
                next = (r + v / r) / 2;
            }
            return r;
        }

        // Smoothed value noise on a lattice this many cells wide: -One to One.
        long Lattice(int x, int y, int cells, int salt)
        {
            x += LatticeShift;
            y += LatticeShift;
            int u = x / cells, v = y / cells;
            long fu = Smooth(x % cells * One / cells), fv = Smooth(y % cells * One / cells);
            long Corner(int a, int b) => WorldHash.Hash4(Seed, RoughSalt + salt * 64 + cells, a, b) % (2u * (uint)One + 1u) - One;
            long near = Corner(u, v) * (One - fu) + Corner(u + 1, v) * fu;
            long far = Corner(u, v + 1) * (One - fu) + Corner(u + 1, v + 1) * fu;
            return (near / One * (One - fv) + far / One * fv) / One;
        }

        // Four octaves, 16 cells down to 2: -One to One, mostly within half of that.
        long Fractal(int x, int y, int salt) =>
            (8 * Lattice(x, y, 16, salt) + 4 * Lattice(x, y, 8, salt) + 2 * Lattice(x, y, 4, salt) + Lattice(x, y, 2, salt)) / 15;

        static long Smooth(long t)
        {
            t = Math.Max(0, Math.Min(One, t));
            return t * t / One * (3 * One - 2 * t) / One;
        }

        // Bands of rock: a climb of 0 to One held on ledges, with a short steep riser between them.
        static long Strata(long t, int bands)
        {
            t = Math.Max(0, Math.Min(One, t));
            long band = t * bands / One, inside = t * bands % One;
            return Math.Min(One, (band * One + Smooth(inside * 2 - One / 2)) / bands);
        }

        // A canyon or a cliff at a cell: a face that wanders, with broken ground beside it, in land units.
        long Formed(int dx, int dy, long d2)
        {
            long length = Root(((long)AlongX * AlongX + (long)AlongY * AlongY) * One * One);
            long across = ((long)dx * AlongY - (long)dy * AlongX) * One * One / length;
            long along = Math.Abs((long)dx * AlongX + (long)dy * AlongY) * One * One / length;
            long reach = Radius * One;
            // A canyon's floor climbs to the land at both ends, gently enough to walk out.
            long ramp = Math.Min(One, (reach - along) * One / (reach * 3 / 4));
            // Nothing at the rim of the stamp, so it meets the land round it.
            return FormedAt(X + dx, Y + dy, across, ramp, Smooth((reach - Root(d2 * One * One)) * One / (reach * 3 / 10)));
        }

        // A face across a line at a cell this far across it (fixed point, signed), its floor this far down its ramp, tapered.
        long FormedAt(int x, int y, long across, long ramp, long taper)
        {
            long reach = Radius * One, wall = Math.Abs((long)Rise);
            long off = across + Lattice(x, y, 24, 2) * Radius / 5 + Fractal(x, y, 3) * 3;
            long face = Math.Max(3, wall / FaceSlope) * One;
            long side = Rise < 0 ? -off : off;
            long height = Profile == Canyon ? CanyonAt(Math.Abs(off), reach, face, wall, ramp, x, y) : CliffAt(side, reach, face, wall, x, y);
            height += wall * Fractal(x, y, 4) * Rough / (100 * One * 3);
            // A canyon's floor is as deep as it's asked, on any seed: noise past it folds back up, so the floor keeps its grain.
            long deepest = CanyonDepth(wall);
            if (Profile == Canyon && height < -deepest) height = -2 * deepest - height;
            return height * taper / One;
        }

        // Along a path: across is from the nearest point on it, signed as a straight line's (the high side of a cliff on
        // the same hand all the way), and the ends are where the floor climbs out and the stamp tapers away.
        long FormedOnPath(int dx, int dy)
        {
            long reach = Radius * One, cap = reach * 3 / 10;
            var (across, at, length) = NearestOnPath(dx, dy);
            long inward = Math.Min(at, length - at);
            if (Math.Abs(across) > reach || inward < -cap) return 0;
            long ramp = Math.Max(0, Math.Min(One, (inward + cap) * One / (reach * 3 / 4)));
            long taper = Smooth((reach - Math.Abs(across)) * One / (reach * 3 / 10)) * Smooth((inward + cap) * One / cap) / One;
            return FormedAt(X + dx, Y + dy, across, ramp, taper);
        }

        /// <summary>
        /// The middle of a canyon's floor, cell by cell from the first point of its line or path to the last, two cells
        /// wide: where it wanders off the line, this follows it, for a trail along the bottom. Empty for any other profile.
        /// </summary>
        public IEnumerable<(int x, int y)> Trail()
        {
            if (Profile != Canyon) yield break;
            var points = Path.Count > 0 ? Path : StraightEnds();
            var seen = new HashSet<(int x, int y)>();
            for (int k = 0; k + 1 < points.Count; k++)
            {
                long ax = points[k].Item1, ay = points[k].Item2, vx = points[k + 1].Item1 - ax, vy = points[k + 1].Item2 - ay;
                long length = Root((vx * vx + vy * vy) * One * One), steps = 2 * Math.Max(Math.Abs(vx), Math.Abs(vy));
                for (long n = 0; n <= steps; n++)
                {
                    long px = ax * One + vx * One * n / steps, py = ay * One + vy * One * n / steps;
                    int cx = X + (int)Div(px, One), cy = Y + (int)Div(py, One);
                    // Where CanyonAt's off is 0: the line, less how far the floor wanders from it here.
                    long wander = Lattice(cx, cy, 24, 2) * Radius / 5;
                    foreach (long side in new[] { -One / 2, One / 2 })
                    {
                        long across = side - wander;
                        var cell = (X + (int)Div(px + vy * One * across / length, One), Y + (int)Div(py - vx * One * across / length, One));
                        if (seen.Add(cell)) yield return cell;
                    }
                }
            }
        }

        // A straight canyon's line as a path: its radius either way of the middle.
        (int x, int y)[] StraightEnds()
        {
            long l = Root(((long)AlongX * AlongX + (long)AlongY * AlongY) * One * One);
            int ex = (int)(AlongX * Radius * One / l), ey = (int)(AlongY * Radius * One / l);
            return new[] { (-ex, -ey), (ex, ey) };
        }

        // Rounded to the nearest whole, halves up, the same way for either sign.
        static long Div(long v, long by)
        {
            long t = v + by / 2;
            return t >= 0 ? t / by : -((-t + by - 1) / by);
        }

        // The nearest point of the path to a cell: how far across (fixed point, signed), how far along from its start (past
        // either end the first and last steps run on), and the path's whole length.
        (long across, long at, long length) NearestOnPath(int dx, int dy)
        {
            long best = long.MaxValue, across = 0, at = 0, walked = 0;
            int last = Path.Count - 2;
            for (int k = 0; k <= last; k++)
            {
                long ax = Path[k].x, ay = Path[k].y, vx = Path[k + 1].x - ax, vy = Path[k + 1].y - ay;
                long len2 = vx * vx + vy * vy, step = Root(len2 * One * One);
                long t = ((dx - ax) * vx + (dy - ay) * vy) * One / len2;
                if (k > 0) t = Math.Max(0, t);
                if (k < last) t = Math.Min(One, t);
                long ex = (dx - ax) * One - vx * t, ey = (dy - ay) * One - vy * t;
                long d2 = ex * ex + ey * ey;
                if (d2 < best)
                {
                    best = d2;
                    long cross = ex * vy - ey * vx;
                    across = cross == 0 ? 0 : Math.Sign(cross) * Root(d2);
                    at = walked + step * t / One;
                }
                walked += step;
            }
            return (across, at, walked);
        }

        static long CanyonDepth(long wall) => Math.Min(wall * 2 / 5, CanyonDeepest);

        /// <summary>The least and the most it can add anywhere, whatever its seed: what decides if the land can take it.</summary>
        public (int low, int high) Extent
        {
            get
            {
                long wall = Math.Abs((long)Rise), noise = wall * Rough / 300;
                if (Profile == Canyon) return ((int)-CanyonDepth(wall), (int)(wall + noise));
                if (Profile == Cliff) return ((int)-noise, (int)(wall + noise));
                long a = Rise * (50L + Rough) / 50, b = Rise * (50L - Rough) / 50;
                return ((int)Math.Min(0, Math.Min(a, b)), (int)Math.Max(0, Math.Max(a, b)));
            }
        }

        // Cut down to a floor, the rest of the wall raised as a rim that falls back to nothing.
#pragma warning disable S107 // the place across it, and the five sizes it's cut to
        long CanyonAt(long off, long reach, long face, long wall, long ramp, int x, int y)
#pragma warning restore S107
        {
            long deepest = CanyonDepth(wall), cut = deepest * Math.Max(0, ramp) / One, rim = wall - deepest;
            long half = reach / 5 + Lattice(x, y, 16, 5) * Radius / 16;
            if (off <= half) return wall * Math.Abs(Fractal(x, y, 6)) / (16 * One) - cut;
            if (off <= half + face) return (rim + cut) * Strata((off - half) * One / face + Lattice(x, y, 6, 7) / 8, 3) / One - cut;
            return rim * (One - Smooth((off - half - face) * One / Math.Max(1, reach - half - face))) / One;
        }

        // The high side above the face, sloping back to nothing; a skirt of broken rock at the foot.
        long CliffAt(long off, long reach, long face, long wall, int x, int y)
        {
            long foot = wall / 8;
            if (off < 0)
            {
                long skirt = Math.Max(0, One + off / 8);
                return (foot + wall * Math.Abs(Fractal(x, y, 8)) / (6 * One)) * skirt / One * skirt / One;
            }
            if (off <= face) return foot + (wall - foot) * Strata(off * One / face + Lattice(x, y, 6, 7) / 8, 4) / One;
            return wall * (One - Smooth((off - face) * One / Math.Max(1, reach - face))) / One;
        }
    }

    public sealed partial class World
    {
        /// <summary>A stamp as an act: its kind and its what.</summary>
        public const string ActLand = "land";

        /// <summary>A stamp's slope this steep, in land units across a cell, is bare rock: about 30 degrees.</summary>
        public const int LandSteep = 7;

        /// <summary>A steep face this tall, in land units (5 m), is a crag: bare rock with boulders. A shorter one is a bank of earth, so a small stamp reads as a bump (#148).</summary>
        public const int LandCrag = 40;

        /// <summary>How far round a cell, in cells, its face is measured (LandFace).</summary>
        public const int LandFaceReach = 3;

        /// <summary>The valley's drawable range: how low and high land can be stamped, in land units, inside what a chunk's terrain can draw with room for a cell's own height.</summary>
        public const int LandLowest = -136, LandHighest = 2176;

        /// <summary>This world's drawable range: its map's (map.json), every peer's the same.</summary>
        public (int low, int high) LandRange { get; private set; } = (LandLowest, LandHighest);

        /// <summary>Sets the drawable range, before the world starts.</summary>
        public void SetLandRange((int low, int high) range) => LandRange = range;

        readonly List<LandStamp> landStamps = new List<LandStamp>();
        // What the stamps and the map moved, a chunk at a time and only where they reached (ChunkCells); null until any did.
        ChunkCells<int> landMoved;
        ChunkCells<int> landLaid;

        ChunkCells<int> NewMoved() => new ChunkCells<int>(W, H, WrapX);

        /// <summary>Land the map itself lays when the world is made, a road's cut: under the stamps, and no reset takes it.</summary>
        public void LayLand(IEnumerable<(int x, int y, int by)> cells)
        {
            landMoved ??= NewMoved();
            landLaid ??= NewMoved();
            foreach (var (x, y, by) in cells)
            {
                landMoved.Cell(x, y) += by;
                landLaid.Cell(x, y) += by;
                MoveLand(x, y, by);
            }
            DropLandPeek();
        }

        ChunkCells<int> landCrag;

        /// <summary>
        /// Land the map lays as a crag's face (a row's pad dropping off as rock, RowPad): moved as a stamp's land is, so it's
        /// bare rock where it's steep and tall, dressed with boulders, and a tree on its face goes; but no undo or reset takes it.
        /// </summary>
        public void LayCrag(IEnumerable<(int x, int y, int by)> cells)
        {
            var list = cells.ToList();
            if (list.Count == 0) return;
            landMoved ??= NewMoved();
            landCrag ??= NewMoved();
            foreach (var (x, y, by) in list)
            {
                landMoved.Cell(x, y) += by;
                landCrag.Cell(x, y) += by;
                MoveLand(x, y, by);
            }
            DropLandPeek();
            foreach (var (x, y, _) in list)
                for (int v = y - LandFaceReach; v <= y + LandFaceReach; v++)
                    for (int u = x - LandFaceReach; u <= x + LandFaceReach; u++)
                        if (InBounds(u, v) && At(u, v).T == StandingTree && IsStampedRock(u, v)) Paint(u, v, ClearedGround);
        }

        /// <summary>The cells the map laid as a crag's face (LayCrag), row by row.</summary>
        public IEnumerable<(int x, int y)> LandCragCells()
        {
            return landCrag?.Cells() ?? Enumerable.Empty<(int, int)>();
        }

        readonly List<LandStamp> mapStamps = new List<LandStamp>();
        // A map stamp's crags and deep floors lose their trees to grass.
        const string StandingTree = "tree", ClearedGround = "grass";

        /// <summary>The map's own stamps (MapStamps), in the order they were laid.</summary>
        public IReadOnlyList<LandStamp> MapStampsLaid => mapStamps;

        /// <summary>
        /// The map's own land stamps (its stamps.json), laid when the world is made, before its rows (whose pads are cut on
        /// the stamped land): stamped land like any stamp's, rock where it's a crag, but part of the map, so no undo or reset
        /// takes them. A tree they leave on a crag's face or a deep cut's floor goes, so the forest stands on the tops and not
        /// on the walls. Each refused says why, and is left off; one that would move a row's or a road's laid land is
        /// refused (none is laid yet when the map's go down), as is one on water.
        /// </summary>
        public List<string> LayMapStamps(IEnumerable<LandStamp> stamps)
        {
            var refused = new List<string>();
            var laid = new List<LandStamp>();
            foreach (var s in stamps)
            {
                string why = s == null || !s.Known ? "that isn't a land stamp" : LandRefusal(s) ?? LaidRefusal(s);
                if (why != null)
                {
                    refused.Add(s == null ? why : $"{s.Text}: {why}");
                    continue;
                }
                Press(s, 1);
                mapStamps.Add(s);
                laid.Add(s);
            }
            // Round each, to as far as a face is measured past what it moved.
            foreach (var s in laid)
            {
                int reach = s.Bound + LandFaceReach + 1;
                var (xFrom, xTo) = XSpan(s.X - reach, s.X + reach);
                for (int y = Math.Max(0, s.Y - reach); y <= Math.Min(H - 1, s.Y + reach); y++)
                    for (int x = xFrom; x <= xTo; x++)
                        if (At(x, y).T == StandingTree && IsStampedRock(x, y)) Paint(x, y, ClearedGround);
            }
            return refused;
        }

        // The cells a bare map stamp painted (MapStamps' "bare"), null until one does: map data, the same on every peer.
        ChunkCells<bool> bareCells;

        internal void MarkBare(int x, int y)
        {
            bareCells ??= new ChunkCells<bool>(W, H, WrapX);
            bareCells.Cell(x, y) = true;
        }

        /// <summary>Whether a bare map stamp left this cell bare (a volcano's rock): no tree is drawn on it.</summary>
        public bool IsStampedBare(int x, int y) => bareCells != null && InBounds(x, y) && bareCells[x, y];

        /// <summary>Whether the stamps left a cell bare rock: a crag's face, or a floor cut a crag deep (CellPaint paints them so).</summary>
        public bool IsStampedRock(int x, int y) =>
            landMoved != null && (LandStampedAt(x, y) <= -LandCrag || (LandDrop(x, y) >= LandSteep && LandFace(x, y) >= LandCrag));

        string LaidRefusal(LandStamp s) =>
            landLaid != null && Reach(s).Any(c => landLaid[c.x, c.y] != 0) ? "that would move a row's or a road's laid land" : null;

        /// <summary>A stamp moved the land, or one was taken back: the middle and the reach of the cells to draw again.</summary>
        public event Action<int, int, int> LandMoved;

        /// <summary>For the netcode checksum, beside CellHash: the stamps in force, "" until land is stamped.</summary>
        public string LandHash() => string.Concat(landStamps.Select(s => s.Text));

        /// <summary>Whether any stamp is in force: drawing costs nothing until one is.</summary>
        public bool LandHasMoved => landMoved != null;

        /// <summary>How far the stamps have moved a cell's land, in land units; 0 off the world.</summary>
        public int LandMovedAt(int x, int y) => landMoved != null && InBounds(x, y) ? landMoved[x, y] : 0;

        /// <summary>The most the stamps put between a cell and a cell beside it, in land units; 0 until land is stamped. What the map laid is no stamp: a road's bank is grass.</summary>
        public int LandDrop(int x, int y)
        {
            if (landMoved == null || !InBounds(x, y)) return 0;
            int here = Stamped(x, y);
            return Math.Max(Math.Max(Beside(x + 1, y, here), Beside(x - 1, y, here)), Math.Max(Beside(x, y + 1, here), Beside(x, y - 1, here)));
        }

        /// <summary>How tall the face a cell is on: the most the stamps put between it and any cell within LandFaceReach, in land units; 0 until land is stamped.</summary>
        public int LandFace(int x, int y)
        {
            if (landMoved == null || !InBounds(x, y)) return 0;
            int here = Stamped(x, y), most = 0;
            for (int v = y - LandFaceReach; v <= y + LandFaceReach; v++)
                for (int u = x - LandFaceReach; u <= x + LandFaceReach; u++)
                    most = Math.Max(most, Beside(u, v, here));
            return most;
        }

        int Beside(int x, int y, int here) => InBounds(x, y) ? Math.Abs(Stamped(x, y) - here) : 0;

        /// <summary>What the stamps alone have moved a cell by, in land units: the map's laid land left out.</summary>
        public int LandStampedAt(int x, int y) => landMoved != null && InBounds(x, y) ? Stamped(x, y) : 0;

        /// <summary>The cells the map's laid land moved, row by row.</summary>
        public IEnumerable<(int x, int y)> LandLaidCells()
        {
            return landLaid?.Cells() ?? Enumerable.Empty<(int, int)>();
        }

        /// <summary>Whether a road's cut leaves this cell a bank: the most it moved past a cell beside it is steep.</summary>
        public bool LandBank(int x, int y)
        {
            if (landLaid == null || !InBounds(x, y)) return false;
            int here = landLaid[x, y];
            for (int v = -1; v <= 1; v++)
                for (int u = -1; u <= 1; u++)
                    if (InBounds(x + u, y + v) && Math.Abs(landLaid[x + u, y + v] - here) >= LandSteep) return true;
            return false;
        }

        int Stamped(int x, int y) => landMoved[x, y] - (landLaid?[x, y] ?? 0);

        // Null when it's stamped; why not otherwise.
        string StampLand(ActDone act, JObject ask)
        {
            var stamp = LandStamp.Read(ask["stamp"]);
            if (stamp == null || !stamp.Known) return "that isn't a land stamp";
            if (!hasLand) SetLand((_, row) => Array.Clear(row, 0, row.Length));
            string why = LandRefusal(stamp);
            if (why != null) return why;
            Press(stamp, 1);
            landStamps.Add(stamp);
            act.Land = stamp;
            LandMoved?.Invoke(stamp.X, stamp.Y, stamp.Bound);
            return null;
        }

        const string PastTheLand = "that goes past how low or high the land can be drawn";

        /// <summary>Why the land as it stands can't take this stamp, or null. Its seed has no say.</summary>
        public string LandRefusal(LandStamp s)
        {
            var (low, high) = s.Extent;
            foreach (var (x, y, _) in Reach(s))
            {
                int now = LandNow(x, y);
                if (now == NoLand) return Wet;
                if (now + low < LandRange.low || now + high > LandRange.high) return PastTheLand;
            }
            return null;
        }

        // Why a stamp in force can't come off: taking it back moves what it really moved. All or nothing, so undo is exact.
        string UndoRefusal(LandStamp s)
        {
            foreach (var (x, y, by) in Reach(s))
            {
                int now = LandAtCell(x, y);
                if (now == NoLand) return Wet;
                if (by == 0) continue;
                int then = now - by;
                if (then < LandRange.low || then > LandRange.high) return PastTheLand;
            }
            return null;
        }

        // A cell's land in land units, NoLand on water: before the first stamp sets the land, read as SetLand would mark it,
        // so a stamp asked of a dry world is refused on the lake as it would be after (#156: a spot search took one there).
        int LandNow(int x, int y)
        {
            if (hasLand) return LandAtCell(x, y);
            return IsWaterBody(At(x, y)) ? NoLand : landMoved?[x, y] ?? 0;
        }

        const string Wet = "that would cross water, and land stamps keep off it for now";

        // The cells a stamp reaches, on the world, row by row, and what it adds at each.
        IEnumerable<(int x, int y, int by)> Reach(LandStamp s)
        {
            if (s.Path.Count > 0) return ReachAlong(s);
            return ReachRound(s);
        }

        // Along a path: the box round it, the cells it moves.
        IEnumerable<(int x, int y, int by)> ReachAlong(LandStamp s)
        {
            int x0 = s.X + s.Path.Min(p => p.x) - s.Radius, x1 = s.X + s.Path.Max(p => p.x) + s.Radius;
            int y0 = s.Y + s.Path.Min(p => p.y) - s.Radius, y1 = s.Y + s.Path.Max(p => p.y) + s.Radius;
            var (xFrom, xTo) = XSpan(x0, x1);
            for (int y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++)
                for (int x = xFrom; x <= xTo; x++)
                {
                    int by = s.At(x - s.X, y - s.Y);
                    if (by != 0) yield return (FoldX(x), y, by);
                }
        }

        IEnumerable<(int x, int y, int by)> ReachRound(LandStamp s)
        {
            var (xFrom, xTo) = XSpan(s.X - s.Radius, s.X + s.Radius);
            for (int y = Math.Max(0, s.Y - s.Radius); y <= Math.Min(H - 1, s.Y + s.Radius); y++)
                for (int x = xFrom; x <= xTo; x++)
                    if ((long)(x - s.X) * (x - s.X) + (long)(y - s.Y) * (y - s.Y) <= (long)s.Radius * s.Radius)
                        yield return (FoldX(x), y, s.At(x - s.X, y - s.Y));
        }

        void Press(LandStamp s, int sign)
        {
            landMoved ??= NewMoved();
            foreach (var (x, y, by) in Reach(s))
            {
                landMoved.Cell(x, y) += sign * by;
                MoveLand(x, y, sign * by);
            }
            DropLandPeek();
        }

        // Taken back, in any order: the same integers off. Null when it's done, else why not.
        string TakeLand(ActDone act)
        {
            string why = UndoRefusal(act.Land);
            if (why != null) return why;
            TakeStamp(act.Land);
            return null;
        }

        void TakeStamp(LandStamp s)
        {
            Press(s, -1);
            landStamps.RemoveAt(landStamps.FindLastIndex(k => k.Text == s.Text));
            if (landStamps.Count == 0 && landLaid == null && landCrag == null && mapStamps.Count == 0) landMoved = null;
            LandMoved?.Invoke(s.X, s.Y, s.Bound);
        }

        // The dragon's reset: the land as the map has it. Acts that stamped it have nothing left to take back.
        void ForgetLandStamps()
        {
            for (int k = landStamps.Count - 1; k >= 0; k--) TakeStamp(landStamps[k]);
            acts.RemoveAll(a => a.Land != null);
            foreach (var firing in acts) firing.Parts.RemoveAll(p => p.Land != null);
            acts.RemoveAll(a => a.Kind == ActCalled && a.Parts.Count == 0);
        }

        void SaveLand(JObject save)
        {
            if (landStamps.Count > 0) save["land"] = new JArray(landStamps.Select(s => (object)s.Row).ToArray());
            if (mapStamps.Count > 0) save["mapStamps"] = new JArray(mapStamps.Select(s => (object)s.Row).ToArray());
            if (landLaid != null) save["landLaid"] = new JArray(LandLaidCells().SelectMany(c => new object[] { c.x, c.y, landLaid[c.x, c.y] }).ToArray());
            if (landCrag != null) save["landCrag"] = new JArray(LandCragCells().SelectMany(c => new object[] { c.x, c.y, landCrag[c.x, c.y] }).ToArray());
        }

        void LoadLand(JObject s)
        {
            if (s["landLaid"] is JArray laid)
                LayLand(Enumerable.Range(0, laid.Count / 3).Select(k => ((int)laid[3 * k], (int)laid[3 * k + 1], (int)laid[3 * k + 2])));
            if (s["landCrag"] is JArray crag)
                LayCrag(Enumerable.Range(0, crag.Count / 3).Select(k => ((int)crag[3 * k], (int)crag[3 * k + 1], (int)crag[3 * k + 2])));
            // The map's own stamps, as they were laid: the save's cells already have what they cleared.
            foreach (var row in s["mapStamps"] as JArray ?? new JArray())
            {
                var stamp = LandStamp.Read(row);
                mapStamps.Add(stamp);
                Press(stamp, 1);
            }
            foreach (var row in s["land"] as JArray ?? new JArray())
            {
                var stamp = LandStamp.Read(row);
                landStamps.Add(stamp);
                Press(stamp, 1);
            }
        }
    }
}
