// Land stamps (docs/design/maps-as-data.md, section 3): the one way land moves. A stamp is a row, a centre, a radius, a
// profile and a rise, and every peer works its cells out alike from the row, in integers: an act lays it, undo_act takes
// the same integers off, so it can be taken back in any order. landMoved is what the stamps add up to, null until the
// first, and the drawn ground reads it (CellHeightfield), so ground and cells stay one thing. A stamp keeps off water
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
        public const string Flat = "flat", Dome = "dome", Cone = "cone", Ridge = "ridge";
        const int Fixed = 256, LatticeCells = 8, RoughSalt = 7331;

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Radius { get; private set; }
        /// <summary>At the middle, in land units; below 0 it's a hollow.</summary>
        public int Rise { get; private set; }
        public string Profile { get; private set; }
        /// <summary>A ridge's line, as a step across the grid; 0, 0 for any other profile.</summary>
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
            && (Profile == Flat || Profile == Dome || Profile == Cone || (Profile == Ridge && (AlongX != 0 || AlongY != 0)));

        /// <summary>What it adds at a cell this far from its middle, in land units.</summary>
        public int At(int dx, int dy)
        {
            long r2 = (long)Radius * Radius, d2 = (long)dx * dx + (long)dy * dy;
            if (d2 > r2) return 0;
            long height = Shape(dx, dy, r2, d2);
            return (int)(height + height * Noise(X + dx, Y + dy) / (100L * LatticeCells * LatticeCells));
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

        // Value noise on a coarse lattice, bilinear: -Rough to Rough percent, times the lattice's 64.
        long Noise(int x, int y)
        {
            if (Rough == 0) return 0;
            int u = x / LatticeCells, v = y / LatticeCells, fu = x % LatticeCells, fv = y % LatticeCells;
            long Corner(int a, int b) => WorldHash.Hash4(Seed, RoughSalt, a, b) % (2u * (uint)Rough + 1u) - Rough;
            long near = Corner(u, v) * (LatticeCells - fu) + Corner(u + 1, v) * fu;
            long far = Corner(u, v + 1) * (LatticeCells - fu) + Corner(u + 1, v + 1) * fu;
            return near * (LatticeCells - fv) + far * fv;
        }
    }

    public sealed partial class World
    {
        /// <summary>A stamp as an act: its kind and its what.</summary>
        public const string ActLand = "land";

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

        // Null when it's stamped; a refusal otherwise.
        RoomVerdict? StampLand(RoomCommand c, ActDone act, JObject ask)
        {
            var stamp = LandStamp.Read(ask["stamp"]);
            if (stamp == null || !stamp.Known) return Refused(c, "that isn't a land stamp");
            if (land == null) SetLand(new int[W * H]);
            string why = LandRefusal(stamp, 1);
            if (why != null) return Refused(c, why);
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
