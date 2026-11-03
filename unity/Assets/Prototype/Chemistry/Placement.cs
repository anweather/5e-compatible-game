// Picking cells the same on every peer (docs/design/events-and-placing.md, "Pick"): the
// area's open cells, each scored by a hash of the seed, a salt and the cell, taken best
// first while keeping spacing cells apart. No draw from the world's generator, so
// nothing else's randomness moves; a pure function, so PickGolden pins it exactly.
// What counts as open is the caller's (World.PlaceOpen for a cast).
//
// Not a port: the sandbox has no placing.
using System;
using System.Collections.Generic;

namespace Chem
{
    public static class Placement
    {
        /// <summary>The cells within <paramref name="within"/> of (x, y) as the crow flies (0 is the cell itself), in row order.</summary>
        public static List<(int x, int y)> Around(int x, int y, int within)
        {
            var cells = new List<(int x, int y)>();
            int r2 = within * within;
            for (int dy = -within; dy <= within; dy++)
                for (int dx = -within; dx <= within; dx++)
                    if (dx * dx + dy * dy <= r2) cells.Add((x + dx, y + dy));
            return cells;
        }

        /// <summary>
        /// Up to <paramref name="count"/> of the area's cells that pass <paramref name="open"/>, lowest
        /// WorldHash.Hash4(seed, salt, x, y) first (then north to south, west to east on a tie), with at least
        /// <paramref name="spacing"/> cells between any two picked, in steps either way (0 lets them touch).
        /// A cell listed twice counts once. In the order picked.
        /// </summary>
        public static List<(int x, int y)> Pick(IEnumerable<(int x, int y)> area, int count, int spacing, Func<int, int, bool> open, int seed, int salt)
        {
            var picked = new List<(int x, int y)>();
            if (count <= 0) return picked;
            var seen = new HashSet<(int, int)>();
            var scored = new List<(uint score, int x, int y)>();
            foreach (var (x, y) in area)
                if (seen.Add((x, y)) && open(x, y)) scored.Add((WorldHash.Hash4(seed, salt, x, y), x, y));
            scored.Sort(Lowest);
            foreach (var (_, x, y) in scored)
            {
                if (picked.Count == count) break;
                if (picked.TrueForAll(p => Math.Max(Math.Abs(p.x - x), Math.Abs(p.y - y)) > spacing)) picked.Add((x, y));
            }
            return picked;
        }

        // Lowest score first, then north to south, then west to east: a whole order, so the sort's own order never shows.
        static int Lowest((uint score, int x, int y) a, (uint score, int x, int y) b)
        {
            if (a.score != b.score) return a.score.CompareTo(b.score);
            return a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        }
    }
}
