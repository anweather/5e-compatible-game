// A road cut into the hill it climbs (docs/design/the-hold.md): a graded road's surface is one height across and climbs
// evenly along, and the land either side leans back to where it was, a cut uphill and a bank down. Worked out from the
// map's land in integers, the same on every peer, and laid under the stamps (World.LayLand), so the dragon's reset keeps it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public static class RoadGrade
    {
        // In half cells: how far along a road its height is evened, how far out the land takes to lean back, and how far
        // from each end the road comes to meet the land.
        const int Evened = 24, Bank = 6, Pinned = 16;
        const int Scale = 16;

        /// <summary>What the road adds to the land at each cell it moves, in land units, keeping off the cells in <paramref name="keepOff"/>.</summary>
        public static List<(int x, int y, int by)> Of(BuildingRoad road, int rowX, int rowY, Func<int, int, int> land, ISet<(int x, int y)> keepOff)
        {
            var line = Line(road, rowX, rowY);
            var surface = Surface(line.Select(p => land(p.x / 2, p.y / 2)).ToArray());
            int inner = road.Width + 1, outer = inner + Bank;
            var nearest = new Dictionary<(int x, int y), (long d2, int at)>();
            for (int k = 0; k < line.Count; k++)
                for (int y = (line[k].y - outer) / 2; y <= (line[k].y + outer) / 2 + 1; y++)
                    for (int x = (line[k].x - outer) / 2; x <= (line[k].x + outer) / 2 + 1; x++)
                    {
                        long dx = 2 * x + 1 - line[k].x, dy = 2 * y + 1 - line[k].y, d2 = dx * dx + dy * dy;
                        if (d2 <= (long)outer * outer && (!nearest.TryGetValue((x, y), out var had) || d2 < had.d2)) nearest[(x, y)] = (d2, k);
                    }
            var moved = new List<(int x, int y, int by)>();
            foreach (var cell in nearest.OrderBy(c => c.Key.y).ThenBy(c => c.Key.x))
            {
                // Past its ends the land stays as it is: flattened up to a gate, it left a step no one could climb.
                if (keepOff.Contains(cell.Key) || cell.Value.at == 0 || cell.Value.at == line.Count - 1) continue;
                int by = surface[cell.Value.at] - land(cell.Key.x, cell.Key.y);
                int part = by * Kept(cell.Value.d2, inner) / (Scale * Scale);
                if (part != 0) moved.Add((cell.Key.x, cell.Key.y, part));
            }
            return moved;
        }

        // The road's line, a point a half cell.
        static List<(int x, int y)> Line(BuildingRoad road, int rowX, int rowY)
        {
            var line = new List<(int x, int y)>();
            for (int i = 0; i + 1 < road.Points.Count; i++)
                line.AddRange(BuildingRaster.HalfCells(road.Points[i], road.Points[i + 1]).Select(p => (p.x + 2 * rowX, p.y + 2 * rowY)));
            return line;
        }

        // The height along the line: evened, never turning back on its climb, and meeting the land at both ends.
        static int[] Surface(int[] ground)
        {
            int n = ground.Length;
            var even = new int[n];
            for (int k = 0; k < n; k++)
            {
                int from = Math.Max(0, k - Evened), to = Math.Min(n - 1, k + Evened);
                long sum = 0;
                for (int i = from; i <= to; i++) sum += ground[i];
                even[k] = (int)(sum / (to - from + 1));
            }
            int way = Math.Sign(ground[n - 1] - ground[0]);
            for (int k = 1; k < n && way != 0; k++)
                if ((even[k] - even[k - 1]) * way < 0) even[k] = even[k - 1];
            for (int k = 0; k < Math.Min(Pinned, n); k++)
            {
                even[k] = ground[0] + (even[k] - ground[0]) * k / Pinned;
                int last = n - 1 - k;
                even[last] = ground[n - 1] + (even[last] - ground[n - 1]) * k / Pinned;
            }
            return even;
        }

        // How much of the road's height a cell takes, out of Scale squared: all of it on the road, none at the bank's foot.
        static int Kept(long d2, int inner)
        {
            if (d2 <= (long)inner * inner) return Scale * Scale;
            int d = Root(d2 * Scale * Scale);
            int t = Math.Min(Scale, Math.Max(0, (d - inner * Scale) / Bank));
            return Scale * Scale - t * t * (3 * Scale - 2 * t) / Scale;
        }

        static int Root(long v)
        {
            long r = v, next = (v + 1) / 2;
            while (next < r)
            {
                r = next;
                next = (r + v / r) / 2;
            }
            return (int)r;
        }
    }
}
