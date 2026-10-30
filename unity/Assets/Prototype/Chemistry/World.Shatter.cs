// Glass that shatters to an open gap: terrain with shatter.leaves (a window
// pane, materials.json). A blast, ground shaken under it or a heavy thing
// thrown at it breaks it, and it falls with its wall: a pane with no wall left
// beside it can't stand. Fire does nothing to it. Port of the shatter section
// of sandbox/src/react.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// Shatters the cell if its terrain has a shatter.leaves (EffectKind.Shatter: a heavy thing
        /// hit it). Returns whether it did. A paused chunk doesn't change.
        /// </summary>
        public bool Shatter(int x, int y)
        {
            var c = LocateActive(x, y, out _, out _);
            if (c == null) return false;
            var t = TerrainOf(c);
            if (t.ShattersTo == null) return false;
            c.T = t.ShattersTo;
            LogOnce($"shatter#{t.Id}#{Tick}", $"The {t.Name.ToLowerInvariant()} shatters", LogKind.Notable);
            return true;
        }

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
                if (TerrainOf(At(nx, ny)).ShattersTo != null && !Held(nx, ny)) Shatter(nx, ny);
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
