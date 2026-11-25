// Covers that spread (docs/design/primitives.md §3 P2): a cover with spread
// {every, chance, onto, notOnto, reach} lays itself on a neighbour on the soak
// pass, moss creeping over the stones. No port: the sandbox is frozen.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The most cells covers spread onto in a tick (budgets.json materials.maxSpreadPerTick when the room has budgets).</summary>
        public const int DefaultMaxSpreadPerTick = 64;

        // What spreading covers reached this tick (World.Spread), laid at its end with the floating ones.
        readonly List<(Cell cell, string cover, int reach)> spreading = new List<(Cell cell, string cover, int reach)>();

        // This tick's cap, looked up once a tick (-1: not yet).
        int spreadCap = -1;

        int MaxSpreadPerTick => (int)(roomBudgets?.Number("materials.maxSpreadPerTick", DefaultMaxSpreadPerTick) ?? DefaultMaxSpreadPerTick);

        /// <summary>
        /// A spreading cover on its turn (every spread.every ticks, each cell on its own turn of the count, as
        /// Soak's): each neighbour in N, E, S, W order that takes it (SpreadsOnto) gets it with spread.chance, a
        /// roll of the world's RNG. What it reaches is laid at the end of the tick (LandSpread), so it creeps a
        /// cell a turn whichever way the rows run, and at most MaxSpreadPerTick cells a tick. Its reach left
        /// rides in Cell.Slick, as a floating cover's does. True if it spread.
        /// </summary>
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

        // Whether this soak pass is the cell's turn to spread.
        bool SpreadTurn(Chunk ch, int i, int every)
        {
            int turns = Math.Max(1, every / Rules.Sim.SoakPass);
            int x = (ch.X << Chunks.Shift) + (i & Chunks.Mask), y = (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);
            return (Tick / Rules.Sim.SoakPass + x * 7 + y * 13) % turns == 0;
        }

        /// <summary>
        /// A cell a spreading cover may lay itself on: bare (no cover, no fire), not open water (a cover on water
        /// is float's), and its terrain's id or one of its tags in spread.onto (empty: any terrain) and none in spread.notOnto.
        /// </summary>
        bool SpreadsOnto(CoverDef cv, Cell n)
        {
            if (n.Cover != null || n.Fire > 0 || (n.Water > 0 && !n.Frozen)) return false;
            var t = TerrainOf(n);
            return (cv.SpreadOnto.Count == 0 || Names(t, cv.SpreadOnto)) && !Names(t, cv.SpreadNotOnto);
        }

        // The terrain's id or one of its tags is in the list.
        static bool Names(TerrainDef t, IReadOnlyList<string> names)
        {
            foreach (var name in names)
                if (name == t.Id || t.Has(name)) return true;
            return false;
        }

        // What spread this tick lands, first come first laid; a cell another cover took first keeps it.
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
