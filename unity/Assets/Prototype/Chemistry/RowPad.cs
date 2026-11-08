// The flat ground a level row stands on (docs/design/the-hold.md): the land under it and a margin round it brought to one height,
// the middle of what's there, and leaning back to the land over a bank. Worked out from the map's land in integers, the same on
// every peer, and laid under the stamps (World.LayLand) as a road's cut is, before the row's graded roads, which meet it.
// A pad can reach further, over more areas at the same height (its "more"), each with a bank of its own, and an area whose
// face is a crag drops off as bare rock (World.LayCrag) rather than leaning back as earth: the ground a castle looks out from.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>
    /// One more area a pad reaches over, in world cells, corners inclusive: its bank, whether its face is a crag, and whether
    /// it's cleared of the map's trees (the view's: open ground to stand on).
    /// </summary>
    public readonly struct PadArea
    {
        public readonly int X0, Y0, X1, Y1, Bank;
        public readonly bool Crag, Clear;

        public PadArea(int x0, int y0, int x1, int y1, int bank, bool crag, bool clear = false)
        {
            X0 = Math.Min(x0, x1);
            Y0 = Math.Min(y0, y1);
            X1 = Math.Max(x0, x1);
            Y1 = Math.Max(y0, y1);
            Bank = Math.Max(0, bank);
            Crag = crag;
            Clear = clear;
        }

        internal int Away(int x, int y) => Math.Max(Math.Max(X0 - x, x - X1), Math.Max(Math.Max(Y0 - y, y - Y1), 0));
    }

    public static class RowPad
    {
        const int Scale = 16;

        /// <summary>What the pad adds to the land at each cell it moves, in land units, for a row on these cells.</summary>
        public static List<(int x, int y, int by)> Of(IReadOnlyCollection<(int x, int y)> cells, int margin, int bank, Func<int, int, int> land, Func<int, int, bool> inBounds)
            => Of(cells, margin, bank, land, inBounds, Array.Empty<PadArea>()).laid;

        /// <summary>
        /// The pad over the row's cells and over more areas at the same height: what it adds at each cell, as earth (laid)
        /// and, on the banks of the areas whose face is a crag, as rock (crag). A cell takes the most any of them keeps of it.
        /// </summary>
        public static (List<(int x, int y, int by)> laid, List<(int x, int y, int by)> crag) Of(IReadOnlyCollection<(int x, int y)> cells, int margin, int bank,
            Func<int, int, int> land, Func<int, int, bool> inBounds, IReadOnlyList<PadArea> more)
        {
            var main = new PadArea(cells.Min(c => c.x) - margin, cells.Min(c => c.y) - margin, cells.Max(c => c.x) + margin, cells.Max(c => c.y) + margin, bank, false);
            var areas = new List<PadArea> { main };
            areas.AddRange(more);
            var heights = cells.Select(c => land(c.x, c.y)).OrderBy(h => h).ToList();
            int level = heights[heights.Count / 2];
            var laid = new List<(int x, int y, int by)>();
            var crag = new List<(int x, int y, int by)>();
            int x0 = areas.Min(a => a.X0 - a.Bank), x1 = areas.Max(a => a.X1 + a.Bank);
            int y0 = areas.Min(a => a.Y0 - a.Bank), y1 = areas.Max(a => a.Y1 + a.Bank);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!inBounds(x, y)) continue;
                    int kept = 0;
                    bool rock = false;
                    foreach (var a in areas)
                    {
                        int away = a.Away(x, y);
                        if (away > a.Bank) continue;
                        int k = Kept(away, a.Bank);
                        if (k > kept || (k == kept && !a.Crag)) (kept, rock) = (k, a.Crag && away > 0);
                    }
                    int part = (level - land(x, y)) * kept / (Scale * Scale);
                    if (part != 0) (rock ? crag : laid).Add((x, y, part));
                }
            return (laid, crag);
        }

        // How much of the pad's height a cell takes, out of Scale squared: all of it on the pad, none at the bank's foot.
        static int Kept(int d, int bank)
        {
            if (d <= 0) return Scale * Scale;
            if (d > bank) return 0;
            int t = Math.Min(Scale, d * Scale / Math.Max(1, bank));
            return Scale * Scale - t * t * (3 * Scale - 2 * t) / Scale;
        }
    }
}
