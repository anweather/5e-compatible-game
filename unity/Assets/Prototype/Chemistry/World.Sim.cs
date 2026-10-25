// One tick of the cell world. Port of sandbox/src/sim.ts with the entity,
// critter and machine systems left out. The order of what remains is the TS
// order: fire -> water -> gas -> charge decay -> wind decay.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>One tick of the sim.</summary>
        public void Step()
        {
            Tick++;
            logged.Clear();
            TickFire();
            TickWater();
            TickGas();
            foreach (var c in Cells) if (c.Charge > 0) c.Charge--;
            if (Wind.HasValue)
            {
                var (d, t) = Wind.Value;
                Wind = t - 1 <= 0 ? ((int, int)?)null : (d, t - 1);
            }
            Array.Clear(flowMarks, 0, flowMarks.Length);
            Array.Clear(steamMarks, 0, steamMarks.Length);
            Array.Clear(windMarks, 0, windMarks.Length);
        }

        void TickFire()
        {
            var p = Rules.Sim.Fire;
            var burning = new List<int>();
            for (int i = 0; i < Cells.Length; i++) if (Cells[i].Fire > 0) burning.Add(i);
            foreach (int i in burning)
            {
                var c = Cells[i];
                if (c.Fire == 0) continue;
                int x = i % W, y = i / W;
                // Water puts out anything but a floating oil or grease fire.
                if (c.Water > 0 && !c.Frozen && !CoverHasTag(c, "flammable"))
                {
                    c.Fire = 0;
                    c.Fuel = 0;
                    AddGas(c, "steam", 1);
                    continue;
                }
                if (c.Gas != "steam" && Rand() < p.SmokeChance) AddGas(c, "smoke", 1);
                foreach (var (nx, ny, d) in Neighbours(x, y))
                {
                    var n = At(nx, ny);
                    if (n.Fire > 0) continue;
                    double flam = Flammability(n);
                    double downwind = Wind.HasValue && Wind.Value.dir == d ? p.DownwindBonus : 0;
                    bool wet = n.Water > 0 && !n.Frozen;
                    double chance;
                    if (flam > 0) chance = p.SpreadChance * flam * (c.Fire / 3.0) + downwind * flam;
                    else chance = wet ? p.BoilChance : p.HeatChance;
                    if (Rand() < chance) Apply(nx, ny, "fire", 1, -1, false);
                }
                if (--c.Fuel > 0) continue;
                if (CoverHasTag(c, "flammable"))
                {
                    c.Cover = null;
                    var tt = TerrainOf(c);
                    if (tt.Has("flammable"))
                    {
                        c.Fuel = tt.Fuel ?? 5;
                        c.Fire = 2;
                    }
                    else
                    {
                        c.Fire = 0;
                        if (!IsBlocking(c)) c.Cover = "ash";
                    }
                    continue;
                }
                var t = TerrainOf(c);
                c.Fire = 0;
                if (t.BurnsTo != null)
                {
                    c.T = t.BurnsTo;
                    c.Cover = IsBlocking(c) || HasTag(c, "low") ? null : "ash";
                    if (t.BurnLog != null) AddLog(t.BurnLog, LogKind.Notable);
                }
            }
        }

        void TickWater()
        {
            int n = Cells.Length;
            bool forward = Tick % 2 == 0;
            int maxLevel = Rules.Sim.WaterMaxLevel;
            for (int k = 0; k < n; k++)
            {
                int i = forward ? k : n - 1 - k;
                var c = Cells[i];
                if (c.Water == 0 || c.Frozen || IsBlocking(c)) continue;
                int x = i % W, y = i / W;
                int level = ElevationUnits(c) + c.Water;
                // Water always runs onto lower ground; on the flat it needs a 2-step
                // difference, so a still puddle doesn't slosh back and forth forever.
                int best = -1;
                double bestScore = 0;
                foreach (var (nx, ny, _) in Neighbours(x, y))
                {
                    var nc = At(nx, ny);
                    if (nc.Frozen && nc.Water > 0) continue;
                    if (IsBlocking(nc) && !HasTag(nc, "vessel")) continue;
                    if (nc.Water >= maxLevel) continue;
                    int drop = level - (ElevationUnits(nc) + nc.Water);
                    bool downhill = ElevationUnits(nc) < ElevationUnits(c);
                    if (!(drop >= 2 || (downhill && drop >= 1))) continue;
                    double score = drop + (downhill ? 0.5 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = Idx(nx, ny);
                    }
                }
                if (best >= 0)
                {
                    c.Water--;
                    Cells[best].Water++;
                    flowMarks[i] = 1;
                    flowMarks[best] = 1;
                }
            }
        }

        void TickGas()
        {
            var p = Rules.Sim.Gas;
            var opts = new List<Cell>(4);
            for (int i = 0; i < Cells.Length; i++)
            {
                var c = Cells[i];
                if (c.Gas == null) continue;
                if (Rand() < p.DecayChance) c.GasAmt--;
                if (c.GasAmt <= 0)
                {
                    c.Gas = null;
                    c.GasAmt = 0;
                    continue;
                }
                int x = i % W, y = i / W;
                if (Wind.HasValue && Rand() < 0.5)
                {
                    int wd = Wind.Value.dir;
                    var t = At(x + DirX(wd), y + DirY(wd));
                    if (t != null && !IsBlocking(t))
                    {
                        AddGas(t, c.Gas, 1);
                        c.GasAmt--;
                        if (c.GasAmt <= 0) c.Gas = null;
                        continue;
                    }
                }
                if (c.GasAmt >= 2)
                {
                    opts.Clear();
                    foreach (var (nx, ny, _) in Neighbours(x, y))
                    {
                        var nc = At(nx, ny);
                        if (!IsBlocking(nc) && (nc.Gas == null || nc.Gas == c.Gas) && nc.GasAmt < c.GasAmt - 1) opts.Add(nc);
                    }
                    if (opts.Count > 0)
                    {
                        AddGas(opts[(int)Math.Floor(Rand() * opts.Count)], c.Gas, 1);
                        c.GasAmt--;
                    }
                }
            }
        }
    }
}
