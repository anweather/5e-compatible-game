// What shatters: terrain with shatter.leaves (a window pane, glass ground) and
// ice on water (the material "ice"). A blast, ground shaken under it, a heavy
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
        /// <summary>The material frozen water and frozen creatures are, for its shatter.cracks.</summary>
        public const string IceMaterial = "ice";
        /// <summary>The status of a frozen creature with its first crack.</summary>
        public const string CrackedStatus = "cracked";

        /// <summary>
        /// A hit on the cell (EffectKind.Shatter: a heavy thing hit it): its ice first, else its terrain if that
        /// has a shatter.leaves. With shatter.cracks the first hit cracks it (once a tick, so one blast is one hit)
        /// and the next shatters it. Returns whether it shattered. A paused chunk doesn't change.
        /// </summary>
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

        // Ice that cracks, on this cell; null for none (a mod's ice that doesn't crack doesn't shatter either).
        TerrainDef IceOver(Cell c) =>
            c.Frozen && Rules.Terrain.TryGetValue(IceMaterial, out var ice) && ice.Cracks ? ice : null;

        // Cracked, with a warning if a creature stands on it.
        void Crack(int x, int y, Cell c, TerrainDef what)
        {
            c.Cracked = Tick + 1;
            string name = what.Name.ToLowerInvariant();
            var on = RaiderAt(x, y);
            string line = on != null ? $"The {name} cracks under {Rules.Entity(on.Kind).Name}" : $"The {name} cracks";
            LogOnce($"crack#{line}#{Tick}", line, LogKind.Notable);
        }

        readonly SortedSet<ulong> crackedThings = new SortedSet<ulong>();

        /// <summary>Whether a thing (a glow ball) has its first crack (EffectKind.Crack), by its stable id.</summary>
        public bool IsCracked(ThingId thing) => crackedThings.Contains(thing.Value);

        // A blast's shock (a spell with a crater, an explosion): everything in it that shatters does.
        void Blast(List<(int x, int y)> cells)
        {
            foreach (var (x, y) in cells) Shatter(x, y);
        }

        void BlastAround(int x, int y, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++) Shatter(x + dx, y + dy);
        }

        // A wall at (x, y) is gone: a pane beside it with no wall left to stand in falls.
        void WallLost(int x, int y)
        {
            foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                if (IsBlocking(At(nx, ny)) && TerrainOf(At(nx, ny)).ShattersTo != null && !Held(nx, ny)) Fall(At(nx, ny));
        }

        // A pane with nothing to stand in goes at once, cracked or not.
        void Fall(Cell c)
        {
            var t = TerrainOf(c);
            c.T = t.ShattersTo;
            c.Cracked = 0;
            LogShattered(t);
        }

        // Whether a wall stands beside a pane (panes don't hold each other up).
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
