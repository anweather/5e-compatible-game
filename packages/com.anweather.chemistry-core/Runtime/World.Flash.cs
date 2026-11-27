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
        readonly List<(int x, int y, int push, int tick)> flashHistory = new List<(int x, int y, int push, int tick)>();

        readonly List<(int x, int y, int tick)> shedStrikes = new List<(int x, int y, int tick)>();

        public IEnumerable<(int x, int y, int tick)> ShedStrikesSince(int tick)
        {
            foreach (var s in shedStrikes) if (s.tick > tick) yield return s;
        }

        public IEnumerable<(int x, int y, int push, int tick)> FlashesSince(int tick)
        {
            foreach (var f in flashHistory) if (f.tick > tick) yield return f;
        }

        FlashDef FlashOf(Cell c) => c.Gas != null && Rules.Gases.TryGetValue(c.Gas, out var g) ? g.Flash : null;

        void FlashGas(int x, int y)
        {
            var first = FlashOf(At(x, y));
            if (first == null) return;
            var seen = new HashSet<long> { CellKey(x, y) };
            var cloud = new List<(int x, int y, FlashDef flash)> { (x, y, first) };
            for (int k = 0; k < cloud.Count; k++)
                foreach (var (nx, ny, _) in ActiveNeighbours(cloud[k].x, cloud[k].y))
                {
                    var next = FlashOf(At(nx, ny));
                    if (next != null && seen.Add(CellKey(nx, ny))) cloud.Add((nx, ny, next));
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
                var r = flash.Push > 0 ? RaiderAt(cx, cy) : null;
                if (r != null) RaiderSystem.ShoveRaider(r, RaiderSystem.AwayDir(DeltaX(x, cx), DeltaY(y, cy)), flash.Push);
            }
        }
    }
}
