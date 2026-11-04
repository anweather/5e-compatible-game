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

#pragma warning disable S107 // a stamp's row is its nine fields
        public static LandStamp Make(int x, int y, int radius, int rise, string profile, int alongX, int alongY, int rough, int seed) =>
            new LandStamp { X = x, Y = y, Radius = radius, Rise = rise, Profile = profile, AlongX = alongX, AlongY = alongY, Rough = rough, Seed = seed };
#pragma warning restore S107

        /// <summary>A stamp in a command or a save, or null if it isn't one.</summary>
        public static LandStamp Read(JToken row)
        {
            if (!(row is JArray a) || a.Count != 9) return null;
            return Make((int)a[0], (int)a[1], (int)a[2], (int)a[3], (string)a[4], (int)a[5], (int)a[6], (int)a[7], (int)a[8]);
        }

        /// <summary>The row, for a command or a save.</summary>
        public JArray Row => new JArray(X, Y, Radius, Rise, Profile, AlongX, AlongY, Rough, Seed);

        /// <summary>The row as text, for the checksum.</summary>
        public string Text => Row.ToString(Newtonsoft.Json.Formatting.None);

        public bool Known =>
            Radius > 0 && Rough >= 0 && Rough <= 100
            && (Profile == Flat || Profile == Dome || Profile == Cone || (NeedsLine(Profile) && (AlongX != 0 || AlongY != 0)));

        /// <summary>Whether a profile runs along a line.</summary>
        public static bool NeedsLine(string profile) => profile == Ridge || profile == Canyon || profile == Cliff;

        /// <summary>What it adds at a cell this far from its middle, in land units.</summary>
        public int At(int dx, int dy)
        {
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
            int x = X + dx, y = Y + dy;
            long reach = Radius * One, wall = Math.Abs((long)Rise);
            long off = across + Lattice(x, y, 24, 2) * Radius / 5 + Fractal(x, y, 3) * 3;
            long face = Math.Max(3, wall / FaceSlope) * One;
            long side = Rise < 0 ? -off : off;
            // A canyon's floor climbs to the land at both ends, gently enough to walk out.
            long ramp = Math.Min(One, (reach - along) * One / (reach * 3 / 4));
            long height = Profile == Canyon ? CanyonAt(Math.Abs(off), reach, face, wall, ramp, x, y) : CliffAt(side, reach, face, wall, x, y);
            height += wall * Fractal(x, y, 4) * Rough / (100 * One * 3);
            // Nothing at the rim of the stamp, so it meets the land round it.
            return height * Smooth((reach - Root(d2 * One * One)) * One / (reach * 3 / 10)) / One;
        }

        // Cut down to a floor, the rest of the wall raised as a rim that falls back to nothing.
#pragma warning disable S107 // the place across it, and the five sizes it's cut to
        long CanyonAt(long off, long reach, long face, long wall, long ramp, int x, int y)
#pragma warning restore S107
        {
            long deepest = Math.Min(wall * 2 / 5, CanyonDeepest), cut = deepest * Math.Max(0, ramp) / One, rim = wall - deepest;
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

        /// <summary>How low and high land can be stamped, in land units: inside what a chunk's terrain can draw, with room for a cell's own height.</summary>
        public const int LandLowest = -136, LandHighest = 2176;

        readonly List<LandStamp> landStamps = new List<LandStamp>();
        int[] landMoved;

        /// <summary>A stamp moved the land, or one was taken back: the middle and the reach of the cells to draw again.</summary>
        public event Action<int, int, int> LandMoved;

        /// <summary>For the netcode checksum, beside CellHash: the stamps in force, "" until land is stamped.</summary>
        public string LandHash() => string.Concat(landStamps.Select(s => s.Text));

        /// <summary>Whether any stamp is in force: drawing costs nothing until one is.</summary>
        public bool LandHasMoved => landMoved != null;

        /// <summary>How far the stamps have moved a cell's land, in land units; 0 off the world.</summary>
        public int LandMovedAt(int x, int y) => landMoved != null && InBounds(x, y) ? landMoved[Idx(x, y)] : 0;

        /// <summary>The most the stamps put between a cell and a cell beside it, in land units; 0 until land is stamped.</summary>
        public int LandDrop(int x, int y)
        {
            if (landMoved == null || !InBounds(x, y)) return 0;
            int here = landMoved[Idx(x, y)];
            return Math.Max(Math.Max(Beside(x + 1, y, here), Beside(x - 1, y, here)), Math.Max(Beside(x, y + 1, here), Beside(x, y - 1, here)));
        }

        int Beside(int x, int y, int here) => InBounds(x, y) ? Math.Abs(landMoved[Idx(x, y)] - here) : 0;

        // Null when it's stamped; why not otherwise.
        string StampLand(ActDone act, JObject ask)
        {
            var stamp = LandStamp.Read(ask["stamp"]);
            if (stamp == null || !stamp.Known) return "that isn't a land stamp";
            if (land == null) SetLand(new int[W * H]);
            string why = LandRefusal(stamp, 1);
            if (why != null) return why;
            Press(stamp, 1);
            landStamps.Add(stamp);
            act.Land = stamp;
            LandMoved?.Invoke(stamp.X, stamp.Y, stamp.Radius);
            return null;
        }

        // Why these cells can't take this: water, or the end of what can be drawn. All or nothing, so undo is exact.
        string LandRefusal(LandStamp s, int sign)
        {
            foreach (var (x, y, by) in Reach(s))
            {
                int now = land[Idx(x, y)];
                if (now == NoLand) return "that would cross water, and land stamps keep off it for now";
                if (by == 0) continue;
                int then = now + sign * by;
                if (then < LandLowest || then > LandHighest) return "that goes past how low or high the land can be drawn";
            }
            return null;
        }

        // The cells a stamp reaches, on the world, row by row, and what it adds at each.
        IEnumerable<(int x, int y, int by)> Reach(LandStamp s)
        {
            for (int y = Math.Max(0, s.Y - s.Radius); y <= Math.Min(H - 1, s.Y + s.Radius); y++)
                for (int x = Math.Max(0, s.X - s.Radius); x <= Math.Min(W - 1, s.X + s.Radius); x++)
                    if ((long)(x - s.X) * (x - s.X) + (long)(y - s.Y) * (y - s.Y) <= (long)s.Radius * s.Radius)
                        yield return (x, y, s.At(x - s.X, y - s.Y));
        }

        void Press(LandStamp s, int sign)
        {
            landMoved ??= new int[W * H];
            foreach (var (x, y, by) in Reach(s))
            {
                int i = Idx(x, y);
                landMoved[i] += sign * by;
                if (land != null && land[i] != NoLand) land[i] += sign * by;
            }
        }

        // Taken back, in any order: the same integers off. Null when it's done, else why not.
        string TakeLand(ActDone act)
        {
            string why = LandRefusal(act.Land, -1);
            if (why != null) return why;
            TakeStamp(act.Land);
            return null;
        }

        void TakeStamp(LandStamp s)
        {
            Press(s, -1);
            landStamps.RemoveAt(landStamps.FindLastIndex(k => k.Text == s.Text));
            if (landStamps.Count == 0) landMoved = null;
            LandMoved?.Invoke(s.X, s.Y, s.Radius);
        }

        // The dragon's reset: the land as the map has it. Acts that stamped it have nothing left to take back.
        void ForgetLandStamps()
        {
            for (int k = landStamps.Count - 1; k >= 0; k--) TakeStamp(landStamps[k]);
            acts.RemoveAll(a => a.Land != null);
            foreach (var firing in acts) firing.Parts.RemoveAll(p => p.Land != null);
            acts.RemoveAll(a => a.Kind == ActCalled && a.Parts.Count == 0);
        }

        // SetLand reads the map's land afresh: what's been stamped goes back on it.
        void PutStampsOnLand()
        {
            if (landMoved == null) return;
            for (int i = 0; i < land.Length; i++)
                if (land[i] != NoLand) land[i] += landMoved[i];
        }

        void SaveLand(JObject save)
        {
            if (landStamps.Count > 0) save["land"] = new JArray(landStamps.Select(s => (object)s.Row).ToArray());
        }

        void LoadLand(JObject s)
        {
            foreach (var row in s["land"] as JArray ?? new JArray())
            {
                var stamp = LandStamp.Read(row);
                landStamps.Add(stamp);
                Press(stamp, 1);
            }
        }
    }
}
