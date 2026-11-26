// What shatters: terrain with shatter.leaves (a window pane, glass ground) and
// ice on water (the terrain sim.json water.freezes names). A blast, ground shaken under it, a heavy
// thing thrown at it or lightning on ice is a hit, and one with shatter.cracks
// goes intact, cracked, shattered: the first hit cracks it (Cell.Cracked), the
// next breaks it. A pane falls with its wall, cracked or not: with no wall left
// beside it, it can't stand. Fire does nothing to glass. Port of the shatter
// section of sandbox/src/react.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public const string CrackedStatus = "cracked";

        public bool Shatter(int x, int y)
        {
            var c = LocateActive(x, y, out _, out _);
            if (c == null) return false;
            var ice = IceOver(c);
            var t = TerrainOf(c);
            if (ice == null && t.ShattersTo == null) return false;
            var what = ice ?? t;
            if (what.Cracks)
            {
                if (c.Cracked == Tick + 1) return false;
                if (c.Cracked == 0)
                {
                    Crack(x, y, c, what);
                    return false;
                }
            }
            if (ice != null) c.Frozen = false;
            else c.T = t.ShattersTo;
            c.Cracked = 0;
            LogShattered(what);
            return true;
        }

        void LogShattered(TerrainDef what) =>
            LogOnce($"shatter#{what.Id}#{Tick}", $"The {what.Name.ToLowerInvariant()} shatters", LogKind.Notable);

        TerrainDef IceOver(Cell c) =>
            c.Frozen && FrozenWater() is TerrainDef ice && ice.Cracks ? ice : null;

        TerrainDef FrozenWater() =>
            Rules.Sim.WaterFreezes != null && Rules.Terrain.TryGetValue(Rules.Sim.WaterFreezes, out var ice) ? ice : null;

        void Crack(int x, int y, Cell c, TerrainDef what)
        {
            c.Cracked = Tick + 1;
            string name = what.Name.ToLowerInvariant();
            var on = RaiderAt(x, y);
            string line = on != null ? $"The {name} cracks under {Rules.Entity(on.Kind).Name}" : $"The {name} cracks";
            LogOnce($"crack#{line}#{Tick}", line, LogKind.Notable);
        }

        readonly SortedSet<ulong> crackedThings = new SortedSet<ulong>();

        public bool IsCracked(ThingId thing) => crackedThings.Contains(thing.Value);

        void Blast(List<(int x, int y)> cells)
        {
            foreach (var (x, y) in cells) Shatter(x, y);
        }

        void BlastAround(int x, int y, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++) Shatter(x + dx, y + dy);
        }

        void WallLost(int x, int y)
        {
            foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                if (IsBlocking(At(nx, ny)) && TerrainOf(At(nx, ny)).ShattersTo != null && !Held(nx, ny)) Fall(At(nx, ny));
        }

        void Fall(Cell c)
        {
            var t = TerrainOf(c);
            c.T = t.ShattersTo;
            c.Cracked = 0;
            LogShattered(t);
        }

        bool Held(int x, int y)
        {
            foreach (var (nx, ny, _) in Neighbours(x, y))
            {
                var n = At(nx, ny);
                if (IsBlocking(n) && TerrainOf(n).ShattersTo == null) return true;
            }
            return false;
        }
    }
}
