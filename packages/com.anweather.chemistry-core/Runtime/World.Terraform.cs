// Earth shaping: terraform spells raise and dig cells (Cell.Height), and raised
// ground sheds its water. Port of sandbox/src/terraform.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        void Terraform(SpellDef s, List<(int x, int y)> cells, int x, int y, int charge)
        {
            bool toHeight = s.Height.HasValue;
            int steps = (s.Raise > 0 ? s.Raise : -s.Dig) * charge;
            if (toHeight) steps = s.Height.Value + 2 * (charge - 1);
            int k = 0;
            foreach (var (cx, cy) in cells)
            {
                int d = Math.Max(Math.Abs(cx - x), Math.Abs(cy - y));
                if (s.Taper && d > s.Radius) continue;
                int n = s.Taper ? Tapered(steps, d, s.Radius) : steps;
                if (s.Ramp) n = Math.Min(n, ++k);
                var c = At(cx, cy);
                if (toHeight && s.Stepping > 0 && c.Water > 0 && !c.Frozen) n = Math.Min(n, s.Stepping);
                if (toHeight) n = Math.Max(0, n - c.Height);
                if (n != 0) Reshape(cx, cy, n, s.Terrain, s.Strength, s.Name);
            }
        }

        public void Crater(int x, int y, CraterDef crater)
        {
            if (crater == null) return;
            int r = crater.Radius;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int n = Tapered(-crater.Depth, Math.Max(Math.Abs(dx), Math.Abs(dy)), r);
                    if (n != 0) Reshape(x + dx, y + dy, n, null, crater.Strength);
                }
            var mid = crater.Leaves != null ? At(x, y) : null;
            if (mid != null && !IsBlocking(mid)) mid.T = crater.Leaves;
        }

        public void Dig(int x, int y, ItemDef tool)
        {
            if (tool != null && tool.Use == ItemUses.Dig) Reshape(x, y, -1, null, tool.Strength, tool.Name);
        }

        static int Tapered(int steps, int d, int radius)
        {
            if (d > radius) return 0;
            int size = Math.Abs(steps);
            return Math.Sign(steps) * ((size * (radius + 1 - d) + radius) / (radius + 1));
        }

        public bool Reshape(int x, int y, int n, string terrain = null, int strength = 1, string by = null)
        {
            var c = At(x, y);
            if (c == null || !IsActiveAt(x, y)) return false;
            bool shattered = Shatter(x, y);
            bool uprooted = Uproot(c) || shattered;
            if (uprooted) WallLost(x, y);
            var t = TerrainOf(c);
            if (TooHard(t, n, strength, by) || Unshapeable(t)) return uprooted;
            var range = Rules.Sim.Height;
            int height = Math.Max(range.Min, Math.Min(range.Max, c.Height + n));
            if (Unmoved(c, x, y, height)) return uprooted;
            c.Height = height;
            if (n < 0) c.Cover = null;
            var to = terrain ?? (n < 0 ? t.DugTo : null);
            if (to != null) c.T = to;
            if (n > 0) SpillWater(x, y);
            SettleReshaped(c);
            return true;
        }

        bool Uproot(Cell c)
        {
            var t = TerrainOf(c);
            if (t.UprootsTo == null) return false;
            if (t.UprootLog != null) LogOnce($"uproot#{t.Id}#{Tick}", t.UprootLog, LogKind.Notable);
            c.T = t.UprootsTo;
            return true;
        }

        static bool Unshapeable(TerrainDef t) => t.Has("blocking") || t.Has("machine");

        bool TooHard(TerrainDef t, int n, int strength, string by)
        {
            bool never = t.Hardness <= 0 || Unshapeable(t);
            if (n >= 0 || (!never && strength >= t.Hardness)) return false;
            string name = t.Name.ToLowerInvariant();
            string why;
            if (never) why = $"The {name} can't be dug";
            else if (by == null) why = $"The {name} is too hard to dig";
            else why = $"The {name} is too hard for {by}";
            LogOnce($"hard#{t.Id}#{Tick}", why);
            return true;
        }

        void SettleReshaped(Cell c)
        {
            if (IsBlocking(c)) c.Cover = null;
            if (IsBlocking(c) || (!TerrainOf(c).Burns && c.Cover == null))
            {
                c.Fire = 0;
                c.Fuel = 0;
            }
        }

        bool Unmoved(Cell c, int x, int y, int height) =>
            height == c.Height || (height >= Rules.Sim.Height.BlockAt && RaiderAt(x, y) != null);

        void SpillWater(int x, int y)
        {
            var c = At(x, y);
            int units = c.Water, max = Rules.Sim.WaterMaxLevel;
            c.Water = 0;
            c.Frozen = false;
            var seen = new HashSet<long> { CellKey(x, y) };
            var ring = new List<(int x, int y)> { (x, y) };
            while (units > 0 && ring.Count > 0)
            {
                var next = NextRing(ring, seen);
                units = PourRound(c, next, units, max);
                ring = next;
            }
        }

        List<(int x, int y)> NextRing(List<(int x, int y)> ring, HashSet<long> seen)
        {
            var next = new List<(int x, int y)>();
            foreach (var (x, y) in ring)
                foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                {
                    var nc = At(nx, ny);
                    if (seen.Add(CellKey(nx, ny)) && !IsBlocking(nc) && !(nc.Frozen && nc.Water > 0)) next.Add((nx, ny));
                }
            return next;
        }

        int PourRound(Cell from, List<(int x, int y)> next, int units, int max)
        {
            int kind = from.Liquid;
            for (bool moved = true; units > 0 && moved;)
            {
                moved = false;
                foreach (var (x, y) in next)
                {
                    if (units == 0) break;
                    var n = At(x, y);
                    if (n.Water >= max || (n.Water > 0 && n.Liquid != kind)) continue;
                    Pushed(from, n, kind);
                    n.Water++;
                    n.Liquid = kind;
                    units--;
                    moved = true;
                }
            }
            return units;
        }
    }
}
