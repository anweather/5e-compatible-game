// Earth shaping: terraform spells raise and dig cells (Cell.Height), and raised
// ground sheds its water. Port of sandbox/src/terraform.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// Raises or digs every cell of a terraform spell aimed at (x, y): by Raise or Dig
        /// steps (times the charge), or (Height) up to that height (2 more per charge level),
        /// leaving higher cells alone.
        /// </summary>
        void Terraform(SpellDef s, List<(int x, int y)> cells, int x, int y, int charge)
        {
            bool toHeight = s.Height.HasValue;
            int steps = (s.Raise > 0 ? s.Raise : -s.Dig) * charge;
            if (toHeight) steps = s.Height.Value + 2 * (charge - 1);
            foreach (var (cx, cy) in cells)
            {
                int d = Math.Max(Math.Abs(cx - x), Math.Abs(cy - y));
                if (s.Taper && d > s.Radius) continue;
                int n = s.Taper ? Tapered(steps, d, s.Radius) : steps;
                if (toHeight) n = Math.Max(0, n - At(cx, cy).Height);
                if (n != 0) Reshape(cx, cy, n, s.Terrain);
            }
        }

        /// <summary>
        /// A blast's crater at (x, y), like crater in the TS: the ground dug Depth steps in the
        /// middle, tapering to none past Radius, in row order. Nothing for a null crater.
        /// </summary>
        public void Crater(int x, int y, CraterDef crater)
        {
            if (crater == null) return;
            int r = crater.Radius;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int n = Tapered(-crater.Depth, Math.Max(Math.Abs(dx), Math.Abs(dy)), r);
                    if (n != 0) Reshape(x + dx, y + dy, n);
                }
        }

        /// <summary>
        /// Steps at distance d (in squares) from the middle of a hill or crater of the given
        /// radius: all of them in the middle, rounding up toward the edge, none past it.
        /// </summary>
        static int Tapered(int steps, int d, int radius)
        {
            if (d > radius) return 0;
            int size = Math.Abs(steps);
            return Math.Sign(steps) * ((size * (radius + 1 - d) + radius) / (radius + 1));
        }

        /// <summary>
        /// Raises (n &gt; 0) or digs (n &lt; 0) one cell by n steps, clamped to the sim's height
        /// range. A terrain that uproots (a tree, a cabin's log wall) turns to its UprootsTo
        /// first. Blocking terrain and machines can't be shaped. Digging strips the cover and
        /// turns the terrain to its dugTo; raised ground spills its water onto its neighbours.
        /// A cell that ends up blocking loses its cover and fire. Returns whether it changed.
        /// </summary>
        public bool Reshape(int x, int y, int n, string terrain = null)
        {
            var c = At(x, y);
            if (c == null || !IsActiveAt(x, y)) return false;
            var t = TerrainOf(c);
            bool uprooted = t.UprootsTo != null;
            if (uprooted)
            {
                if (t.UprootLog != null) LogOnce($"uproot#{t.Id}#{Tick}", t.UprootLog, LogKind.Notable);
                c.T = t.UprootsTo;
                t = TerrainOf(c);
            }
            if (t.Has("blocking") || t.Has("machine")) return uprooted;
            var range = Rules.Sim.Height;
            int height = Math.Max(range.Min, Math.Min(range.Max, c.Height + n));
            if (height == c.Height) return uprooted;
            c.Height = height;
            if (n < 0) c.Cover = null;
            var to = terrain ?? (n < 0 ? t.DugTo : null);
            if (to != null) c.T = to;
            if (n > 0) SpillWater(x, y);
            if (IsBlocking(c)) c.Cover = null;
            if (IsBlocking(c) || (!HasTag(c, "flammable") && c.Cover == null))
            {
                c.Fire = 0;
                c.Fuel = 0;
            }
            return true;
        }

        /// <summary>
        /// Moves all of a cell's water (ice melts) onto the open cells around it: one unit at a
        /// time round each ring of neighbours, nearest ring first, in N, E, S, W order. Nothing is
        /// lost unless the whole world is full.
        /// </summary>
        void SpillWater(int x, int y)
        {
            var c = At(x, y);
            int units = c.Water, max = Rules.Sim.WaterMaxLevel;
            c.Water = 0;
            c.Frozen = false;
            var seen = new HashSet<int> { Idx(x, y) };
            var ring = new List<int> { Idx(x, y) };
            while (units > 0 && ring.Count > 0)
            {
                var next = new List<int>();
                foreach (int i in ring)
                    foreach (var (nx, ny, _) in ActiveNeighbours(i % W, i / W))
                    {
                        int k = Idx(nx, ny);
                        var nc = Cells[k];
                        if (seen.Add(k) && !IsBlocking(nc) && !(nc.Frozen && nc.Water > 0)) next.Add(k);
                    }
                for (bool moved = true; units > 0 && moved;)
                {
                    moved = false;
                    foreach (int k in next)
                    {
                        if (units == 0) break;
                        if (Cells[k].Water >= max) continue;
                        Cells[k].Water++;
                        units--;
                        moved = true;
                    }
                }
                ring = next;
            }
        }
    }
}
