// Liquids in the world (docs/design/primitives.md §3, P1; Rules.Liquids.cs). Cell.Water is
// the amount and Cell.Liquid the kind, 0 water. Two kinds never share a cell: a liquid
// flows only into a dry cell or its own kind (World.Flow), and water from rain, springs,
// melting or condensing doesn't fall into another's. Meeting another kind, a liquid lays
// its touch element there at power 1 and the reactions decide; with no touch the other is
// a wall. Everything that asked "is it water" still means kind 0, so a world of water
// runs as it did. The kind is an index into the rules a chunk was made to fit, the core's own:
// at a rule switch a chunk's cells are remapped by id when it's made to fit (World.Fit.cs; a
// liquid gone lapses into water or dries).
using System;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public LiquidDef LiquidOf(Cell c) => Rules.Liquid(c.Kind < Rules.Liquids.Count ? c.Kind : 0);

        public string LiquidId(Cell c) => LiquidOf(c).Id;

        string BoilsOf(LiquidDef l) => l.Index == 0 ? Rules.Sim.WaterBoils : l.Boils;

        public static bool IsOpenWater(Cell c) => c.Water > 0 && !c.Frozen && c.Liquid == 0;

        public int DrownDepth(Cell c) => c.Water > 0 && !c.Frozen && LiquidOf(c).Drowns ? c.Water : 0;

        static bool TakesWater(Cell c) => c.Water == 0 || c.Liquid == 0;

        static bool HoldsOther(Cell c) => c.Water > 0 && c.Liquid != 0;

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

        static bool Pour(Cell c, int kind, int n, int max = int.MaxValue)
        {
            if (c.Water > 0 && c.Liquid != kind) return false;
            if (c.Water >= max) return false;
            if (c.Water == 0) c.Sets = 0;
            c.Water = Math.Min(max, c.Water + n);
            c.Liquid = kind;
            return true;
        }

        void Touch(Chunk ch, LiquidDef l, Chunk nch, int ni)
        {
            if (l.Touch == null) return;
            Apply(CellX(nch, ni), CellY(nch, ni), l.Touch, 1, -1, false);
            WaterMoved(ch);
            WaterMoved(nch);
        }

        static void Fresh(Cell c, LiquidDef l)
        {
            if (l.HotFor > c.Heat) c.Heat = l.HotFor;
            if (l.SetsInto != null && c.Sets == 0) c.Sets = l.SetsAfter;
        }

        static void Carry(Cell from, Cell to, LiquidDef l, bool dry)
        {
            if (l.HotFor > 0 && from.Heat > to.Heat) to.Heat = from.Heat;
            if (l.SetsInto == null) return;
            if (dry || (from.Sets > 0 && (to.Sets == 0 || from.Sets < to.Sets))) to.Sets = from.Sets;
        }

        void Pushed(Cell from, Cell to, int kind)
        {
            if (kind != 0) Carry(from, to, Rules.Liquid(kind), to.Water == 0);
        }

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

        public const int HotTouchEvery = 4;

        bool HoldsHot(Cell c) => HoldsOther(c) && LiquidOf(c).HotFor > 0;

        string LiquidFreezes(Cell c) => HoldsOther(c) ? LiquidOf(c).Freezes : null;

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

        void Solidify(Chunk ch, Cell c)
        {
            c.Water = 0;
            c.Liquid = 0;
            c.Sets = 0;
            c.Frozen = false;
            WaterMoved(ch);
        }

        int StillTicks()
        {
            if (stillTicksFor == Rules) return stillTicks;
            stillTicksFor = Rules;
            stillTicks = Math.Max(1, Rules.Liquids.Max(l => l.Thick));
            return stillTicks;
        }

        Rules stillTicksFor;
        int stillTicks = 1;

        int[] LiquidMap(string[] was)
        {
            if (SameLiquids(was, Rules)) return Array.Empty<int>();
            var map = new int[was.Length];
            for (int k = 0; k < map.Length; k++)
            {
                int to = Rules.LiquidIndex(was[k]);
                map[k] = to >= 0 ? to : RetiredLiquidInto(was[k]);
            }
            return map;
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

        static bool SameLiquids(Rules a, Rules b) => SameLiquids(a.Liquids.Select(l => l.Id).ToArray(), b);

        static bool SameLiquids(string[] ids, Rules b)
        {
            if (ids.Length != b.Liquids.Count) return false;
            for (int k = 0; k < ids.Length; k++)
                if (ids[k] != b.Liquids[k].Id) return false;
            return true;
        }

        int RetiredLiquidInto(string id)
        {
            var r = Room?.Retired.LastOrDefault(x => x.Table == Rules.LiquidsKey && x.Id == id);
            return r?.Into == Rules.WaterLiquid ? 0 : -1;
        }
    }
}
