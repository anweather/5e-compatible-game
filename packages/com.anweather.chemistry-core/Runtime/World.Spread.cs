// Covers that spread (docs/design/primitives.md §3 P2): a cover with spread
// {every, chance, onto, notOnto, reach} lays itself on a neighbour on the soak
// pass, moss creeping over the stones. No port: the sandbox is frozen.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public const int DefaultMaxSpreadPerTick = 64;

        readonly List<(Cell cell, string cover, int reach)> spreading = new List<(Cell cell, string cover, int reach)>();

        int spreadCap = -1;

        int MaxSpreadPerTick => (int)(roomBudgets?.Number("materials.maxSpreadPerTick", DefaultMaxSpreadPerTick) ?? DefaultMaxSpreadPerTick);

        bool Spread(Chunk ch, int i, Cell c)
        {
            if (!Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.SpreadEvery <= 0 || !SpreadTurn(ch, i, cv.SpreadEvery)) return false;
            int reach = c.Slick > 0 ? c.Slick : cv.SpreadReach;
            if (cv.SpreadReach > 0 && reach <= 1) return false;
            if (spreadCap < 0) spreadCap = MaxSpreadPerTick;
            int max = spreadCap;
            bool spread = false;
            for (int d = 0; d < 4 && spreading.Count < max; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n == null || !nch.Active || !SpreadsOnto(cv, n) || Rand() >= cv.SpreadChance) continue;
                spreading.Add((n, c.Cover, cv.SpreadReach > 0 ? reach - 1 : 0));
                spread = true;
            }
            return spread;
        }

        bool SpreadTurn(Chunk ch, int i, int every)
        {
            int turns = Math.Max(1, every / Rules.Sim.SoakPass);
            int x = (ch.X << Chunks.Shift) + (i & Chunks.Mask), y = (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);
            return (Tick / Rules.Sim.SoakPass + x * 7 + y * 13) % turns == 0;
        }

        bool SpreadsOnto(CoverDef cv, Cell n)
        {
            if (n.Cover != null || n.Fire > 0 || (n.Water > 0 && !n.Frozen)) return false;
            var t = TerrainOf(n);
            return (cv.SpreadOnto.Count == 0 || Names(t, cv.SpreadOnto)) && !Names(t, cv.SpreadNotOnto);
        }

        static bool Names(TerrainDef t, IReadOnlyList<string> names)
        {
            foreach (var name in names)
                if (name == t.Id || t.Has(name)) return true;
            return false;
        }

        void LandSpread()
        {
            foreach (var (cell, cover, reach) in spreading)
            {
                if (cell.Cover != null) continue;
                PutCover(cell, cover);
                cell.Slick = reach;
            }
            spreading.Clear();
            spreadCap = -1;
        }
    }
}
