// One tick of the cell world. Port of sandbox/src/sim.ts with the entity,
// critter and machine systems left out. The order of what remains is the TS
// order: fire -> water -> gas -> weather -> charge decay -> wind decay, then
// the day clock (Hasten Time, World.Time.cs).
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        // Reused by TickFire so a tick allocates nothing.
        readonly List<(Chunk ch, int i)> burning = new List<(Chunk ch, int i)>();
        readonly List<Cell> gasOptions = new List<Cell>(4);

        /// <summary>
        /// One tick of the sim, over the active chunks only. Cells are visited in world row-major
        /// order (y, then x), as on the old single grid, so with every chunk active a tick is
        /// exactly what it was.
        /// </summary>
        public void Step()
        {
            Tick++;
            logged.Clear();
            strikes.Clear();
            EndSetWeather();
            Weather = SkyNow();
            var rows = Spans;
            TickFire(rows);
            TickWater(rows);
            TickGas(rows);
            TickWeather(rows);
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Charge > 0) cells[i].Charge--;
            }
            if (Wind.HasValue)
            {
                var (d, t) = Wind.Value;
                Wind = t - 1 <= 0 ? ((int, int)?)null : (d, t - 1);
            }
            TickClock();
            foreach (var ch in activeChunks) Array.Clear(ch.Marks, 0, ch.Marks.Length);
        }

        void TickFire(RowSpan[] rows)
        {
            burning.Clear();
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Fire > 0) burning.Add((s.Chunk, i));
            }
            foreach (var (ch, i) in burning) Burn(ch, i);
        }

        void Burn(Chunk ch, int i)
        {
            var p = Rules.Sim.Fire;
            var c = ch.Cells[i];
            if (c.Fire == 0) return;
            // Water puts out anything but a floating oil or grease fire.
            if (c.Water > 0 && !c.Frozen && !CoverHasTag(c, "flammable"))
            {
                c.Fire = 0;
                c.Fuel = 0;
                AddGas(c, "steam", 1);
                return;
            }
            if (Doused(c)) return;
            if (c.Gas != "steam" && Rand() < p.SmokeChance) AddGas(c, "smoke", 1);
            Spread(ch, i, c, p);
            if (--c.Fuel > 0) return;
            BurnOut(ch, i, c);
        }

        /// <summary>Out of fuel: a burning cover leaves ash or the terrain under it alight; terrain burns to its BurnsTo.</summary>
        void BurnOut(Chunk ch, int i, Cell c)
        {
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
                return;
            }
            var t = TerrainOf(c);
            c.Fire = 0;
            ch.FireGeneration[i] = 0;
            if (t.BurnsTo != null)
            {
                c.T = t.BurnsTo;
                c.Cover = IsBlocking(c) || HasTag(c, "low") ? null : "ash";
                if (t.BurnLog != null) AddLog(t.BurnLog, LogKind.Notable);
            }
        }

        /// <summary>A burning cell's chance to light each neighbour, N, E, S, W; paused chunks don't catch.</summary>
        void Spread(Chunk ch, int i, Cell c, FireParams p)
        {
            // Near full strength for most of the run, then dying away toward FadeCells.
            double reach = p.FadeCells > 0 ? ch.FireGeneration[i] / (double)p.FadeCells : 0.0;
            double fade = p.FadeCells > 0 ? Math.Max(0.0, 1.0 - reach * reach * reach) : 1.0;
            int x = CellX(ch, i), y = CellY(ch, i);
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out int ni);
                if (n == null || !nch.Active || n.Fire > 0) continue;
                if (Rand() < SpreadChance(c, n, d, p) * fade)
                {
                    Apply(x + DirX(d), y + DirY(d), "fire", 1, -1, false);
                    if (p.FadeCells > 0 && n.Fire > 0) nch.FireGeneration[ni] = ch.FireGeneration[i] + 1;
                }
            }
        }

        double SpreadChance(Cell c, Cell n, int d, FireParams p)
        {
            double flam = Flammability(n);
            double downwind = WindDir == d ? p.DownwindBonus : 0;
            if (flam > 0) return (p.SpreadChance * flam * (c.Fire / 3.0) + downwind * flam) * RainDamp();
            return n.Water > 0 && !n.Frozen ? p.BoilChance : p.HeatChance;
        }

        void TickWater(RowSpan[] rows)
        {
            Springs(rows);
            // Even ticks sweep forward, odd ticks backward, so water doesn't drift one way.
            if (Tick % 2 == 0) FlowForward(rows);
            else FlowBackward(rows);
        }

        /// <summary>A spring's square (a river) refills to its level each tick, unless it is ice: water dug out of it keeps coming.</summary>
        void Springs(RowSpan[] rows)
        {
            int max = Rules.Sim.WaterMaxLevel;
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    int level = TerrainOf(c).Spring;
                    if (level > 0 && !c.Frozen && c.Water < level) c.Water = Math.Min(level, max);
                }
            }
        }

        void FlowForward(RowSpan[] rows)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Water != 0) Flow(s.Chunk, i);
            }
        }

        void FlowBackward(RowSpan[] rows)
        {
            for (int k = rows.Length - 1; k >= 0; k--)
            {
                var s = rows[k];
                var cells = s.Chunk.Cells;
                for (int i = s.End - 1; i >= s.Start; i--) if (cells[i].Water != 0) Flow(s.Chunk, i);
            }
        }

        /// <summary>Moves one unit of a wet cell's water to its best lower neighbour, if any.</summary>
        void Flow(Chunk ch, int i)
        {
            var c = ch.Cells[i];
            if (c.Frozen || IsBlocking(c)) return;
            int elevation = ElevationUnits(c);
            int level = elevation + c.Water;
            // Water always runs onto lower ground; on the flat it needs a 2-step
            // difference, so a still puddle doesn't slosh back and forth forever.
            // Neighbours are visited inline in N, E, S, W order (the order Neighbours()
            // gives): this runs for every wet cell every tick, a whole lake of them.
            // A paused chunk next door is a wall.
            Chunk best = null;
            int bestI = 0;
            double bestScore = 0;
            for (int d = 0; d < 4; d++)
            {
                var nc = Chunk.Next(ch, i, d, out var nch, out int ni);
                if (nc == null || !nch.Active) continue;
                double score = FlowScore(nc, elevation, level);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = nch;
                    bestI = ni;
                }
            }
            if (best == null) return;
            c.Water--;
            best.Cells[bestI].Water++;
            ch.Marks[i] |= FlowMark;
            best.Marks[bestI] |= FlowMark;
        }

        /// <summary>How much water at this level and elevation wants to run onto nc; 0 if it can't.</summary>
        double FlowScore(Cell nc, int elevation, int level)
        {
            if (nc.Frozen && nc.Water > 0) return 0;
            var nt = TerrainOf(nc);
            if ((nt.Has("blocking") && !nt.Has("vessel")) || nc.Height >= Rules.Sim.Height.BlockAt) return 0;
            if (nc.Water >= Rules.Sim.WaterMaxLevel) return 0;
            int nElevation = (nt.Elevation + nc.Height) * Rules.Sim.WaterUnitsPerStep;
            int drop = level - (nElevation + nc.Water);
            bool downhill = nElevation < elevation;
            if (!(drop >= 2 || (downhill && drop >= 1))) return 0;
            return drop + (downhill ? 0.5 : 0);
        }

        void TickGas(RowSpan[] rows)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Gas != null) Drift(s.Chunk, i);
            }
        }

        /// <summary>Moves one unit of gas downwind, unless something (or a paused chunk) is in the way.</summary>
        bool BlowDownwind(Chunk ch, int i, Cell c)
        {
            var t = Chunk.Next(ch, i, WindDir, out var tch, out _);
            if (t == null || !tch.Active || IsBlocking(t)) return false;
            AddGas(t, c.Gas, 1);
            c.GasAmt--;
            if (c.GasAmt <= 0) c.Gas = null;
            return true;
        }

        /// <summary>A gas's chance per tick to lose a unit: its own decay (fog lingers), else sim.gas.decayChance.</summary>
        double DecayOf(string gas) =>
            Rules.Gases.TryGetValue(gas, out var g) && g.Decay.HasValue ? g.Decay.Value : Rules.Sim.Gas.DecayChance;

        /// <summary>A gas cell decays, blows downwind, or spreads to a thinner neighbour.</summary>
        void Drift(Chunk ch, int i)
        {
            var c = ch.Cells[i];
            if (Rand() < DecayOf(c.Gas)) c.GasAmt--;
            if (c.GasAmt <= 0)
            {
                c.Gas = null;
                c.GasAmt = 0;
                return;
            }
            if (WindDir >= 0 && Rand() < 0.5 && BlowDownwind(ch, i, c)) return;
            if (c.GasAmt < 2) return;
            gasOptions.Clear();
            for (int d = 0; d < 4; d++)
            {
                var nc = Chunk.Next(ch, i, d, out var nch, out _);
                if (nc != null && nch.Active && !IsBlocking(nc) && (nc.Gas == null || nc.Gas == c.Gas) && nc.GasAmt < c.GasAmt - 1)
                    gasOptions.Add(nc);
            }
            if (gasOptions.Count > 0)
            {
                AddGas(gasOptions[(int)Math.Floor(Rand() * gasOptions.Count)], c.Gas, 1);
                c.GasAmt--;
            }
        }
    }
}
