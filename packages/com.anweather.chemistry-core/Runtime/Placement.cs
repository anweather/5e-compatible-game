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
        public static List<(int x, int y)> Around(int x, int y, int within)
        {
            var cells = new List<(int x, int y)>();
            int r2 = within * within;
            for (int dy = -within; dy <= within; dy++)
                for (int dx = -within; dx <= within; dx++)
                    if (dx * dx + dy * dy <= r2) cells.Add((x + dx, y + dy));
            return cells;
        }

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

        static int Lowest((uint score, int x, int y) a, (uint score, int x, int y) b)
        {
            if (a.score != b.score) return a.score.CompareTo(b.score);
            return a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        }
    }
}
