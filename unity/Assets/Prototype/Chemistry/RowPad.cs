// The flat ground a level row stands on (docs/design/the-hold.md): the land under it and a margin round it brought to one height,
// the middle of what's there, and leaning back to the land over a bank. Worked out from the map's land in integers, the same on
// every peer, and laid under the stamps (World.LayLand) as a road's cut is, before the row's graded roads, which meet it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public static class RowPad
    {
        const int Scale = 16;

        /// <summary>What the pad adds to the land at each cell it moves, in land units, for a row on these cells.</summary>
        public static List<(int x, int y, int by)> Of(IReadOnlyCollection<(int x, int y)> cells, int margin, int bank, Func<int, int, int> land, Func<int, int, bool> inBounds)
        {
            int x0 = cells.Min(c => c.x) - margin, x1 = cells.Max(c => c.x) + margin;
            int y0 = cells.Min(c => c.y) - margin, y1 = cells.Max(c => c.y) + margin;
            var heights = cells.Select(c => land(c.x, c.y)).OrderBy(h => h).ToList();
            int level = heights[heights.Count / 2];
            var moved = new List<(int x, int y, int by)>();
            for (int y = y0 - bank; y <= y1 + bank; y++)
                for (int x = x0 - bank; x <= x1 + bank; x++)
                {
                    if (!inBounds(x, y)) continue;
                    int away = Math.Max(Math.Max(x0 - x, x - x1), Math.Max(Math.Max(y0 - y, y - y1), 0));
                    int part = (level - land(x, y)) * Kept(away, bank) / (Scale * Scale);
                    if (part != 0) moved.Add((x, y, part));
                }
            return moved;
        }

        // How much of the pad's height a cell takes, out of Scale squared: all of it on the pad, none at the bank's foot.
        static int Kept(int d, int bank)
        {
            if (d <= 0) return Scale * Scale;
            int t = Math.Min(Scale, d * Scale / Math.Max(1, bank));
            return Scale * Scale - t * t * (3 * Scale - 2 * t) / Scale;
        }
    }
}
