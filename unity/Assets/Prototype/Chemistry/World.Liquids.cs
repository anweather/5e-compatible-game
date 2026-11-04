// Liquids in the world (docs/design/primitives.md §3, P1; Rules.Liquids.cs). Cell.Water is
// the amount and Cell.Liquid the kind, 0 water. Two kinds never share a cell: a liquid
// flows only into a dry cell or its own kind (World.Flow), and water from rain, springs,
// melting or condensing doesn't fall into another's. Meeting another kind, a liquid lays
// its touch element there at power 1 and the reactions decide; with no touch the other is
// a wall. Everything that asked "is it water" still means kind 0, so a world of water
// runs as it did. The kind is an index into the rules in force, the core's own: at a rule
// switch every cell is remapped by id (a liquid gone lapses into water or dries).
using System;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The liquid a cell holds (water when it's dry).</summary>
        public LiquidDef LiquidOf(Cell c) => Rules.Liquid(c.Kind);

        /// <summary>The id of the liquid a cell holds ("water" when it's dry).</summary>
        public string LiquidId(Cell c) => LiquidOf(c).Id;

        /// <summary>The gas a liquid boils off as: water's is sim.json water.boils.</summary>
        string BoilsOf(LiquidDef l) => l.Index == 0 ? Rules.Sim.WaterBoils : l.Boils;

        /// <summary>Unfrozen water, kind 0: what puts fire out, conducts and matches a reaction's water condition.</summary>
        public static bool IsOpenWater(Cell c) => c.Water > 0 && !c.Frozen && c.Liquid == 0;

        /// <summary>How deep a cell's liquid is for breath, wading and sheltering: its units if the liquid drowns (water does) and isn't frozen, else 0.</summary>
        public int DrownDepth(Cell c) => c.Water > 0 && !c.Frozen && LiquidOf(c).Drowns ? c.Water : 0;

        /// <summary>Whether water (kind 0) may be added to a cell: it's dry or holds water already.</summary>
        static bool TakesWater(Cell c) => c.Water == 0 || c.Liquid == 0;

        /// <summary>Whether a cell holds a liquid other than water.</summary>
        static bool HoldsOther(Cell c) => c.Water > 0 && c.Liquid != 0;

        /// <summary>
        /// Map building: <paramref name="units"/> of the liquid with this id onto a cell, up to the
        /// most a cell holds; refused (false) where another kind already is, or if the rules have no such liquid.
        /// </summary>
        public bool AddLiquid(int x, int y, string id, int units)
        {
            var c = At(x, y);
            int kind = Rules.LiquidIndex(id);
            if (c == null || kind < 0 || IsBlocking(c) || units <= 0) return false;
            if (!Pour(c, kind, units, Rules.Sim.WaterMaxLevel)) return false;
            if (kind != 0) Fresh(c, Rules.Liquid(kind));
            if (Locate(x, y, out var ch, out _) != null) WaterMoved(ch);
            return true;
        }

        /// <summary>Pours n units of liquid kind onto a cell, dry or of that kind; false if it's full or holds another.</summary>
        static bool Pour(Cell c, int kind, int n, int max = int.MaxValue)
        {
            if (c.Water > 0 && c.Liquid != kind) return false;
            if (c.Water >= max) return false;
            if (c.Water == 0) c.Sets = 0;
            c.Water = Math.Min(max, c.Water + n);
            c.Liquid = kind;
            return true;
        }

        // A liquid that would run onto a cell of another kind lays its touch there, at power 1:
        // the reactions on that cell (their liquid condition) say what comes of it. The chunk
        // counts as moved, so the still-water pass never skips a touch whose roll could differ.
        void Touch(Chunk ch, LiquidDef l, Chunk nch, int ni)
        {
            if (l.Touch == null) return;
            Apply(CellX(nch, ni), CellY(nch, ni), l.Touch, 1, -1, false);
            WaterMoved(ch);
            WaterMoved(nch);
        }

        // A liquid poured fresh onto a cell (AddLiquid, an addWater effect): hot for its hot.for, and
        // setting in its sets.after unless what was there already is setting sooner.
        static void Fresh(Cell c, LiquidDef l)
        {
            if (l.HotFor > c.Heat) c.Heat = l.HotFor;
            if (l.SetsInto != null && c.Sets == 0) c.Sets = l.SetsAfter;
        }

        // A unit of liquid l run from one cell onto another (World.Flow): it takes its heat, and the
        // soonest setting of the two; onto a dry cell, the setting it brought.
        static void Carry(Cell from, Cell to, LiquidDef l, bool dry)
        {
            if (l.HotFor > 0 && from.Heat > to.Heat) to.Heat = from.Heat;
            if (l.SetsInto == null) return;
            if (dry || (from.Sets > 0 && (to.Sets == 0 || from.Sets < to.Sets))) to.Sets = from.Sets;
        }

        // A unit pushed out of a cell (DisplaceWater, SpillWater) onto another: as if it ran there. Before the unit lands.
        void Pushed(Cell from, Cell to, int kind)
        {
            if (kind != 0) Carry(from, to, Rules.Liquid(kind), to.Water == 0);
        }

        /// <summary>
        /// A hot liquid (hot.for) cools a tick at a time (Cell.Heat); every HotTouchEvery ticks while it's
        /// hot it lays fire at power 1, quietly, on the cell under it and on each dry cell beside it, and
        /// the fire rows decide: grass catches, a creature there burns. Called by World.Cool.
        /// </summary>
        void HotLiquid(Chunk ch, int i, Cell c)
        {
            c.Heat--;
            if (Tick % HotTouchEvery != 0) return;
            int x = CellX(ch, i), y = CellY(ch, i);
            if (c.Fire == 0) Apply(x, y, "fire", 1, -1, false);
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n != null && nch.Active && n.Fire == 0 && n.Water == 0) Apply(x + DirX(d), y + DirY(d), "fire", 1, -1, false);
            }
        }

        /// <summary>How often a hot liquid lays its fire on what it touches, in ticks.</summary>
        public const int HotTouchEvery = 4;

        /// <summary>Whether a cell holds a hot liquid (not water): its heat is the liquid's, not the ground's.</summary>
        bool HoldsHot(Cell c) => HoldsOther(c) && LiquidOf(c).HotFor > 0;

        /// <summary>What the liquid on a cell freezes into (a terrain or cover id), or null: water (which turns to ice) and a liquid without freezes.</summary>
        string LiquidFreezes(Cell c) => HoldsOther(c) ? LiquidOf(c).Freezes : null;

        /// <summary>
        /// A liquid with freezes, where water would freeze (a freeze effect, freezing ground, snow on a
        /// thin pool): it's gone, and its terrain or cover is there instead. True if it froze.
        /// </summary>
        bool FreezeLiquid(Chunk ch, Cell c)
        {
            var into = LiquidFreezes(c);
            if (into == null) return false;
            var l = LiquidOf(c);
            Solidify(ch, c);
            if (Rules.Covers.ContainsKey(into)) PutCover(c, into);
            else c.T = into;
            LogOnce("freezes-" + l.Id, $"The {l.Name.ToLowerInvariant()} freezes solid");
            return true;
        }

        // A cell's Sets ran a tick (World.TickCell): at 0 its liquid sets into sets.into. A dry cell, or
        // one whose liquid doesn't set, just drops its count.
        void TickSets(Chunk ch, Cell c)
        {
            var l = LiquidOf(c);
            if (c.Water == 0 || l.SetsInto == null)
            {
                c.Sets = 0;
                return;
            }
            if (--c.Sets > 0) return;
            Solidify(ch, c);
            c.T = l.SetsInto;
            c.Life = 0;
            LogOnce("sets-" + l.Id, $"The {l.Name.ToLowerInvariant()} sets hard");
        }

        // The liquid on a cell becomes solid: the cell is dry (what it turns into is the caller's).
        void Solidify(Chunk ch, Cell c)
        {
            c.Water = 0;
            c.Liquid = 0;
            c.Sets = 0;
            c.Frozen = false;
            WaterMoved(ch);
        }

        /// <summary>The longest Thick of any liquid in force (water's 1): how long the still-water pass waits before it trusts a chunk to be still.</summary>
        int StillTicks()
        {
            if (stillTicksFor == Rules) return stillTicks;
            stillTicksFor = Rules;
            stillTicks = Math.Max(1, Rules.Liquids.Max(l => l.Thick));
            return stillTicks;
        }

        Rules stillTicksFor;
        int stillTicks = 1;

        // At a rule switch: each wet cell's kind from the rules before to the rules now, by id. A
        // kind the new rules don't have lapses: into water if a retired row says so, else it dries.
        void RemapLiquids(Rules before, Rules now)
        {
            if (before == null || SameLiquids(before, now)) return;
            var map = new int[before.Liquids.Count];
            for (int k = 0; k < map.Length; k++)
            {
                string id = before.Liquids[k].Id;
                int to = now.LiquidIndex(id);
                map[k] = to >= 0 ? to : RetiredLiquidInto(id);
            }
            foreach (var c in Cells) RemapLiquid(c, map);
        }

        static void RemapLiquid(Cell c, int[] map)
        {
            if (c.Water == 0 || c.Liquid == 0) return;
            int to = c.Liquid < map.Length ? map[c.Liquid] : -1;
            if (to >= 0)
            {
                c.Liquid = to;
                return;
            }
            c.Water = 0;
            c.Liquid = 0;
            c.Frozen = false;
        }

        // Both rules list the same liquids in the same order: no index moves.
        static bool SameLiquids(Rules a, Rules b)
        {
            if (a.Liquids.Count != b.Liquids.Count) return false;
            for (int k = 0; k < a.Liquids.Count; k++)
                if (a.Liquids[k].Id != b.Liquids[k].Id) return false;
            return true;
        }

        /// <summary>A retired liquid's lapse: 0 (water) if it lapses into water, else -1 (it dries).</summary>
        int RetiredLiquidInto(string id)
        {
            var r = Room?.Retired.LastOrDefault(x => x.Table == Rules.LiquidsKey && x.Id == id);
            return r?.Into == Rules.WaterLiquid ? 0 : -1;
        }
    }
}
