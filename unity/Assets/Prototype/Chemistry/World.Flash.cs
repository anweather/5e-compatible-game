// Gas that goes up all at once: a gas with burn.flash (dust, materials.json).
// Fire meeting it, from a spell, a spreading fire or a burning cell it drifted
// over, flashes the whole cloud joined to that cell. Port of flashGas in
// sandbox/src/react.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        // The flashes of the last HistoryTicks ticks, for a view that steps several ticks in one frame. Not world state.
        readonly List<(int x, int y, int push, int tick)> flashHistory = new List<(int x, int y, int push, int tick)>();

        /// <summary>Cells that flashed after <paramref name="tick"/> (within the last 600 ticks), oldest first: where, the shove, the tick.</summary>
        public IEnumerable<(int x, int y, int push, int tick)> FlashesSince(int tick)
        {
            foreach (var f in flashHistory) if (f.tick > tick) yield return f;
        }

        FlashDef FlashOf(Cell c) => c.Gas != null && Rules.Gases.TryGetValue(c.Gas, out var g) ? g.Flash : null;

        /// <summary>
        /// Fire has met a gas that flashes at (x, y): the whole cloud joined to it goes up at once and is used up.
        /// Each of its cells burns for a moment at the flash's fire, and the fire reaches what's there through the
        /// rules like any other. Shoving things is the view's (FlashesSince).
        /// </summary>
        void FlashGas(int x, int y)
        {
            var first = FlashOf(At(x, y));
            if (first == null) return;
            var seen = new HashSet<int> { Idx(x, y) };
            var cloud = new List<(int x, int y, FlashDef flash)> { (x, y, first) };
            for (int k = 0; k < cloud.Count; k++)
                foreach (var (nx, ny, _) in ActiveNeighbours(cloud[k].x, cloud[k].y))
                {
                    var next = FlashOf(At(nx, ny));
                    if (next != null && seen.Add(Idx(nx, ny))) cloud.Add((nx, ny, next));
                }
            foreach (var (cx, cy, _) in cloud)
            {
                var c = At(cx, cy);
                c.Gas = null;
                c.GasAmt = 0;
            }
            if (first.Log != null) LogOnce("flash#" + Tick.ToString(System.Globalization.CultureInfo.InvariantCulture), first.Log, LogKind.Notable);
            foreach (var (cx, cy, flash) in cloud)
            {
                var c = At(cx, cy);
                c.Fire = Math.Max(c.Fire, flash.Fire);
                c.Fuel = Math.Max(c.Fuel, flash.Fire);
                Apply(cx, cy, "fire", flash.Fire, -1, false);
                flashHistory.Add((cx, cy, flash.Push, Tick));
            }
        }
    }
}
