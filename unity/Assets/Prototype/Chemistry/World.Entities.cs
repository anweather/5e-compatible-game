// What entities do to cells. Port of the cell parts of destroy and explode in
// sandbox/src/entities.ts, and what a broken item spills (Items.cs). The
// entities themselves are not in the core: in Unity they are physics props,
// which ask for these through ChemEffects.
using System;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// A blast like explode in the TS: every open cell within the radius takes the
        /// explosion's cover, lit, and burns at least at its fire level. Returns the cells stamped.
        /// </summary>
        public int Explode(int x, int y, EntityDef e)
        {
            var ex = e?.Explodes;
            if (ex == null || !InBounds(x, y)) return 0;
            AddLog($"KABOOM! The {e.Name.ToLowerInvariant()} explodes", LogKind.Notable);
            return Stamp(x, y, ex);
        }

        /// <summary>
        /// A dropped item that broke and spilled what it held (a bullseye lantern's burning oil),
        /// stamped like a small explosion with no blast. Returns the cells stamped.
        /// </summary>
        public int Spill(int x, int y, ItemDef item)
        {
            var spill = item?.Breaks?.Spill;
            if (spill == null || !InBounds(x, y)) return 0;
            AddLog($"The {item.Name.ToLowerInvariant()} smashes and spills burning {spill.Cover}", LogKind.Notable);
            return Stamp(x, y, spill);
        }

        int Stamp(int x, int y, ExplosionDef ex)
        {
            int stamped = 0;
            for (int dy = -ex.Radius; dy <= ex.Radius; dy++)
            {
                for (int dx = -ex.Radius; dx <= ex.Radius; dx++)
                {
                    var c = LocateActive(x + dx, y + dy, out var ch, out int i);
                    if (c == null || IsBlocking(c)) continue;
                    c.Cover = ex.Cover;
                    IgniteCover(c);
                    c.Fire = Math.Max(c.Fire, ex.Fire);
                    ch.FireGeneration[i] = 0;
                    stamped++;
                }
            }
            return stamped;
        }

        /// <summary>A burning thing that burns away leaves its fire in the cell (destroy in the TS).</summary>
        public void LeaveEmbers(int x, int y, EntityDef e)
        {
            var c = At(x, y);
            if (c == null || e == null || !IsActiveAt(x, y)) return;
            c.Fire = Math.Max(c.Fire, 2);
            c.Fuel = Math.Max(c.Fuel, 6);
            AddLog($"The {e.Name.ToLowerInvariant()} burns to cinders");
        }
    }
}
