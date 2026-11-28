// One tick of the cell world. Port of sandbox/src/sim.ts with the critter and
// machine systems left out, and of its entities only the defense field's
// raiders. The order of what remains is the TS order: fire -> water -> gas ->
// weather -> raiders (RaiderSystem.cs) -> the defense's waves
// (Play/DefenseSystem.cs) -> charge decay -> wind decay, then the day clock
// (Hasten Time, World.Time.cs).
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        readonly List<(Chunk ch, int i)> burning = new List<(Chunk ch, int i)>();
        readonly List<(Cell cell, string cover, int slick)> floating = new List<(Cell cell, string cover, int slick)>();
        readonly List<Cell> gasOptions = new List<Cell>(4);

        public void Step()
        {
            FinishTick();
            BeginTick();
            RunToEnd();
        }

        enum TickStage { None, Burn, WaterCheck, Springs, Flow, Gas, Rest, Timers, Dragon, End }

        const int SliceSpans = 64, SliceBurns = 128, SliceChunks = 4;

        TickStage stage = TickStage.None;
        int stagePos;
        RowSpan[] tickRows;
        Chunk[] tickChunks;

        public bool TickInProgress => stage != TickStage.None;

        public int TickStageNumber => (int)stage;

        public bool StepSome(Func<bool> stop)
        {
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            if (stage == TickStage.None) BeginTick();
            while (true)
            {
                if (RunSlice()) return true;
                if (stop()) return false;
            }
        }

        public void FinishTick()
        {
            if (stage == TickStage.None) return;
            RunToEnd();
        }

        void RunToEnd()
        {
            bool done;
            do done = RunSlice();
            while (!done);
        }

        void BeginTick()
        {
            ModesStepping();
            Tick++;
            logged.Clear();
            strikes.Clear();
            combosFired.Clear();
            BeginPlaceTick();
            BeginTriggerTick();
            TrimWeatherHistory();
            EndSetWeather();
            Weather = SkyNow();
            Ticked();
            tickRows = Spans;
            tickChunks = activeChunks;
            UnpackActive(tickChunks);
            CollectBurning(tickRows);
            stage = TickStage.Burn;
            stagePos = 0;
        }

        bool RunSlice()
        {
            switch (stage)
            {
                case TickStage.Burn:
                case TickStage.WaterCheck:
                case TickStage.Springs:
                case TickStage.Flow:
                case TickStage.Gas:
                case TickStage.Timers:
                    RunRange();
                    return false;
                case TickStage.Rest:
                    TickWeather(tickRows);
                    RaiderSystem.Tick();
                    TickModes();
                    TickCombos();
                    TickWhirlwinds();
                    TickWaves();
                    TickBoulders();
                    TickBodies();
                    TickPlaced();
                    TickMachines(tickRows);
                    NextStage(TickStage.Timers);
                    return false;
                case TickStage.Dragon:
                    if (RunDragon()) NextStage(TickStage.End);
                    return false;
                case TickStage.End:
                    EndTick();
                    return true;
                default:
                    return true;
            }
        }

        void RunRange()
        {
            var (count, size) = stage switch
            {
                TickStage.Burn => (burning.Count, SliceBurns),
                TickStage.WaterCheck => (tickChunks.Length, SliceChunks),
                _ => (tickRows.Length, SliceSpans),
            };
            int from = stagePos, to = Math.Min(count, stagePos + size);
            switch (stage)
            {
                case TickStage.Burn: BurnRange(from, to); break;
                case TickStage.WaterCheck: CheckWater(tickChunks, from, to, Rules.HasLiquids); break;
                case TickStage.Springs: Springs(tickRows, from, to, true); break;
                case TickStage.Flow when Tick % 2 == 0: FlowForward(tickRows, from, to, true); break;
                case TickStage.Flow: FlowBackward(tickRows, count - to, count - from, true); break;
                case TickStage.Gas: TickGas(tickRows, from, to); break;
                default: TickTimers(tickRows, from, to); break;
            }
            stagePos = to;
            if (to < count) return;
            if (stage == TickStage.WaterCheck) MarkStill(tickChunks);
            NextStage(stage switch
            {
                TickStage.Burn => TickStage.WaterCheck,
                TickStage.WaterCheck => TickStage.Springs,
                TickStage.Springs => TickStage.Flow,
                TickStage.Flow => TickStage.Gas,
                TickStage.Gas => TickStage.Rest,
                _ => TickStage.Dragon,
            });
        }

        void NextStage(TickStage next)
        {
            stage = next;
            stagePos = 0;
        }

        void RunInto(Cell from, Cell to)
        {
            if (IsBlocking(to)) return;
            to.T = from.T;
            to.Life = from.Life;
            to.Heat = from.Heat;
            to.Cover = null;
            from.T = TerrainOf(from).GrowsTo ?? from.T;
            from.Life = 0;
            from.Heat = 0;
        }

        void LandFloating()
        {
            LandSpread();
            foreach (var (cell, cover, slick) in floating)
            {
                if (cell.Cover != null) continue;
                PutCover(cell, cover);
                cell.Slick = slick;
            }
            floating.Clear();
            foreach (var (from, to) in runs) RunInto(from, to);
            runs.Clear();
        }

        void EndTick()
        {
            if (Wind.HasValue)
            {
                var (d, t) = Wind.Value;
                Wind = t - 1 <= 0 ? ((int, int)?)null : (d, t - 1);
            }
            TickClock();
            foreach (var ch in activeChunks) Array.Clear(ch.Marks, 0, ch.Marks.Length);
            stage = TickStage.None;
            tickRows = null;
            tickChunks = null;
            LapseDue();
            MixDue();
            PackDue();
        }

        void TickTimers(RowSpan[] rows) => TickTimers(rows, 0, rows.Length);

        void TickTimers(RowSpan[] rows, int from, int to)
        {
            byte flow = Raining ? (byte)0 : FlowMark;
            bool soak = Rules.Sim.SoakPass > 0 && Tick % Rules.Sim.SoakPass == 0;
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                int edges = TickTimers(s, flow, soak);
                if (edges < 0) continue;
                s.Chunk.LastLive = Tick;
                for (int d = 0; d < 4; d++) if ((edges & (1 << d)) != 0) s.Chunk.EdgeLive[d] = Tick;
            }
        }

        int TickTimers(RowSpan s, byte flow, bool soak)
        {
            var ch = s.Chunk;
            var cells = ch.ActiveCells;
            var marks = ch.Marks;
            int edges = -1;
            for (int i = s.Start; i < s.End; i++)
            {
                var c = cells[i];
                bool live = TickCell(ch, i, c, c.Charge > 0 || c.Life > 0 || c.Fire > 0 || c.GasAmt > 0 || (marks[i] & flow) != 0, soak);
                if (live) edges = Math.Max(edges, 0) | Chunks.EdgesOf(i);
            }
            return edges;
        }

        bool TickCell(Chunk ch, int i, Cell c, bool live, bool soak)
        {
            if (c.Charge > 0) c.Charge--;
            if (c.Lit > 0) c.Lit--;
            if (c.Slick != 0 && c.Cover == null) c.Slick = 0;
            if (c.Cracked != 0 && !c.Frozen && !TerrainOf(c).Cracks) c.Cracked = 0;
            if (c.Life > 0 && --c.Life == 0) Ripen(ch, i, c);
            if (c.Sets > 0)
            {
                TickSets(ch, c);
                live = true;
            }
            if (c.Heat > 0 && Cool(ch, i, c)) live = true;
            if (soak && SoakPass(ch, i, c)) live = true;
            return live;
        }

        bool SoakPass(Chunk ch, int i, Cell c)
        {
            bool changed = Rust(c);
            if (c.Water > 0 && c.Cover != null && Float(ch, i, c)) changed = true;
            if (Rules.AnySpreads && c.Cover != null && Spread(ch, i, c)) changed = true;
            if (c.Water > 0 && Soak(ch, i, c)) changed = true;
            if (DryOut(c)) changed = true;
            return changed;
        }

        bool Cool(Chunk ch, int i, Cell c)
        {
            if (HoldsHot(c))
            {
                HotLiquid(ch, i, c);
                return true;
            }
            var t = TerrainOf(c);
            if (t.HotFor <= 0)
            {
                c.Heat = 0;
                return true;
            }
            if (IsOpenWater(c) && t.QuenchesTo != null)
            {
                Quench(ch, c, t);
                return true;
            }
            if (t.RunsEvery > 0 && Tick % t.RunsEvery == 0) Run(ch, i, c);
            if (c.Water > 0 && !c.Frozen) BoilOnHot(ch, c, t);
            if (c.Heat > 0) c.Heat--;
            return true;
        }

        void BoilOnHot(Chunk ch, Cell c, TerrainDef t)
        {
            var l = LiquidOf(c);
            if (c.Liquid != 0 && l.Boils == null) return;
            c.Water--;
            AddGas(c, BoilsOf(l), 1);
            c.Heat = Math.Max(0, c.Heat - t.HotQuench);
            WaterMoved(ch);
            if (c.Liquid == 0) LogOnce("hot-quench", "Water hisses into steam on the hot metal");
        }

        void Quench(Chunk ch, Cell c, TerrainDef t)
        {
            c.Water--;
            AddGas(c, Rules.Sim.WaterBoils, 2);
            c.T = t.QuenchesTo;
            c.Heat = 0;
            c.Life = 0;
            WaterMoved(ch);
            if (t.QuenchLog != null) LogOnce("quench-" + t.Id, t.QuenchLog, LogKind.Notable);
        }

        void Run(Chunk ch, int i, Cell c)
        {
            int here = ElevationUnits(c), best = here;
            Cell to = null;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n == null || !nch.Active || IsBlocking(n) || HasTag(n, "machine") || n.T == c.T) continue;
                int level = ElevationUnits(n);
                if (level >= best || runs.Exists(r => r.to == n)) continue;
                best = level;
                to = n;
            }
            if (to != null) runs.Add((c, to));
        }

        bool Rust(Cell c)
        {
            if (!(IsOpenWater(c) || Raining || Tick < c.DampUntil)) return false;
            var t = TerrainOf(c);
            if (t.RustEvery <= 0) return false;
            c.Rust += Rules.Sim.SoakPass;
            if (c.Rust < t.RustEvery) return false;
            c.Rust = 0;
            c.T = t.RustsTo;
            LogOnce("rust", "The metal rusts through");
            return true;
        }

        bool Float(Chunk ch, int i, Cell c)
        {
            if (c.Frozen || !LiquidOf(c).Floats || !Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.FloatEvery <= 0) return false;
            if (Tick / Rules.Sim.SoakPass % Math.Max(1, cv.FloatEvery / Rules.Sim.SoakPass) != 0) return false;
            int reach = c.Slick > 0 ? c.Slick : cv.FloatReach;
            if (cv.FloatReach > 0 && reach <= 1) return false;
            bool spread = false;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n == null || !nch.Active || n.Cover != null || n.Water == 0 || n.Frozen || !LiquidOf(n).Floats || IsBlocking(n)) continue;
                floating.Add((n, c.Cover, cv.FloatReach > 0 ? reach - 1 : 0));
                spread = true;
            }
            return spread;
        }

        bool Soak(Chunk ch, int i, Cell c)
        {
            var t = TerrainOf(c);
            var l = LiquidOf(c);
            if (t.FreezesWater && FreezeLiquid(ch, c)) return true;
            if (t.FreezesWater && c.Liquid == 0)
            {
                if (c.Frozen) return false;
                c.Frozen = true;
                WaterMoved(ch);
                return true;
            }
            if (t.BoilsWater && (c.Liquid == 0 || l.Boils != null))
            {
                c.Water--;
                c.Frozen = false;
                AddGas(c, BoilsOf(l), 1);
                WaterMoved(ch);
                return true;
            }
            return l.Soaks && SoakIn(ch, i, c, t);
        }

        bool SoakIn(Chunk ch, int i, Cell c, TerrainDef t)
        {
            var p = Rules.Sim;
            int thin = p.ThinWater;
            if (c.Frozen || c.Water > thin) return false;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out _, out _);
                if (n != null && n.Water > thin) return false;
            }
            int every = Raining || Tick < c.DampUntil ? t.SoakEveryWet : t.SoakEvery;
            if (every <= 0) return false;
            int turns = Math.Max(1, every / p.SoakPass);
            int x = (ch.X << Chunks.Shift) + (i & Chunks.Mask), y = (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);
            if ((Tick / p.SoakPass + x * 7 + y * 13) % turns != 0) return false;
            if (t.SoaksTo != null && !Raining && Tick >= c.DampUntil) c.T = t.SoaksTo;
            c.Water--;
            c.DampUntil = Tick + p.DampTicks;
            WaterMoved(ch);
            return true;
        }

        bool DryOut(Cell c)
        {
            var to = TerrainOf(c).DriesTo;
            if (to == null || c.Water > 0 || Raining || Tick < c.DampUntil) return false;
            c.T = to;
            return true;
        }

        public bool IsSettled(int cx, int cy) => Tick - ChunkOrThrow(cx, cy).LastLive >= Rules.Sim.Streaming.SettleTicks;

        public bool IsLiveAtEdge(int cx, int cy, int d) => Tick - ChunkOrThrow(cx, cy).EdgeLive[d] < Rules.Sim.Streaming.SettleTicks;

        public const int NeverLive = int.MinValue / 2;

        public int LastLive(int cx, int cy) => ChunkOrThrow(cx, cy).LastLive;

        void RootsBreak(Chunk ch, int i, Cell c)
        {
            var t = TerrainOf(c);
            if (t.BreaksTo == null) return;
            LogOnce("roots-" + t.Id, $"Roots crack the {t.Name.ToLowerInvariant()} apart", LogKind.Notable);
            c.T = t.BreaksTo;
            WallLost(CellX(ch, i), CellY(ch, i));
        }

        void Ripen(Chunk ch, int i, Cell c)
        {
            if (c.Fire > 0)
            {
                c.Life = 1;
                return;
            }
            if (c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) && cv.Lasts > 0)
            {
                c.Cover = null;
                if (cv.WitherLog != null) LogOnce("wither-" + cv.Id, cv.WitherLog);
                if (cv.RotBreaks) RootsBreak(ch, i, c);
            }
            var to = TerrainOf(c).GrowsTo;
            if (to == null || !Rules.Terrain.TryGetValue(to, out var grown)) return;
            if (grown.Has("blocking"))
            {
                if (RaiderAt(CellX(ch, i), CellY(ch, i)) != null)
                {
                    c.Life = 1;
                    return;
                }
                DisplaceWater(CellX(ch, i), CellY(ch, i));
                c.Cover = null;
            }
            var line = TerrainOf(c).GrowLog;
            c.T = to;
            if (line != null) LogOnce("grow-" + to, line);
        }

        void TickFire(RowSpan[] rows)
        {
            CollectBurning(rows);
            BurnRange(0, burning.Count);
        }

        void CollectBurning(RowSpan[] rows)
        {
            burning.Clear();
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Fire > 0) burning.Add((s.Chunk, i));
            }
        }

        void BurnRange(int from, int to)
        {
            for (int k = from; k < to; k++) Burn(burning[k].ch, burning[k].i);
        }

        void Burn(Chunk ch, int i)
        {
            var p = Rules.Sim.Fire;
            var c = ch.ActiveCells[i];
            if (c.Fire == 0) return;
            if (IsOpenWater(c) && !CoverBurns(c))
            {
                c.Fire = 0;
                c.Fuel = 0;
                AddGas(c, Rules.Sim.WaterBoils, 1);
                return;
            }
            if (Doused(c)) return;
            if (c.Gas != null) FlashGas(CellX(ch, i), CellY(ch, i));
            if (c.Gas != Rules.Sim.WaterBoils && Rand() < p.SmokeChance) AddGas(c, SmokeOf(c), 1);
            Spread(ch, i, c, p);
            if (--c.Fuel > 0) return;
            BurnOut(ch, i, c);
        }

        void BurnOut(Chunk ch, int i, Cell c)
        {
            if (CoverBurns(c))
            {
                string ash = CoverOf(c).BurnAsh ?? Rules.Sim.Fire.Ash;
                c.Cover = null;
                var tt = TerrainOf(c);
                if (tt.Burns)
                {
                    c.Fuel = DampFuel(tt.Fuel ?? 5, Dryness(c));
                    c.Fire = 2;
                }
                else
                {
                    c.Fire = 0;
                    if (!IsBlocking(c) && !(c.Water > 0 && !c.Frozen)) c.Cover = ash;
                }
                return;
            }
            var t = TerrainOf(c);
            c.Fire = 0;
            ch.FireGeneration[i] = 0;
            if (t.BurnsTo != null)
            {
                c.T = t.BurnsTo;
                c.Cover = IsBlocking(c) || HasTag(c, "low") ? null : t.BurnAsh ?? Rules.Sim.Fire.Ash;
                if (t.BurnLog != null) AddLog(t.BurnLog, LogKind.Notable);
                WallLost(CellX(ch, i), CellY(ch, i));
            }
        }

        string SmokeOf(Cell c) => (CoverBurns(c) ? CoverOf(c).BurnSmoke : TerrainOf(c).BurnSmoke) ?? Rules.Sim.Fire.Smoke;

        void Spread(Chunk ch, int i, Cell c, FireParams p)
        {
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
            double flam = Flammability(n) * Dryness(n);
            double downwind = WindDir == d ? p.DownwindBonus : 0;
            double heat = CoverBurns(c) ? 1 : TerrainOf(c).Heat;
            if (flam > 0) return (p.SpreadChance * flam * (c.Fire / 3.0) * heat + downwind * flam) * RainDamp();
            if (IsOpenWater(n)) return CoverBurns(c) && IsOpenWater(c) ? p.SlickBoilChance : p.BoilChance;
            return p.HeatChance;
        }

        double Dryness(Cell c)
        {
            if (CoverBurns(c)) return 1;
            double dampens = TerrainOf(c).Dampens;
            if (dampens <= 0 || !(Raining || Tick < c.DampUntil)) return 1;
            return 1 - dampens;
        }

        void TickWater(RowSpan[] rows)
        {
            Springs(rows, 0, rows.Length, false);
            if (Tick % 2 == 0) FlowForward(rows, 0, rows.Length, false);
            else FlowBackward(rows, 0, rows.Length, false);
        }

        void Springs(RowSpan[] rows, int from, int to, bool skipStill)
        {
            int max = Rules.Sim.WaterMaxLevel;
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    int level = TerrainOf(c).Spring;
                    if (level <= 0 || c.Frozen || c.Water >= level || HoldsOther(c)) continue;
                    c.Water = Math.Min(level, max);
                    c.Liquid = 0;
                    WaterMoved(s.Chunk);
                }
            }
        }

        void FlowForward(RowSpan[] rows, int from, int to, bool skipStill)
        {
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Water != 0) Flow(s.Chunk, i);
            }
        }

        void FlowBackward(RowSpan[] rows, int from, int to, bool skipStill)
        {
            for (int k = to - 1; k >= from; k--)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
                var cells = s.Chunk.ActiveCells;
                for (int i = s.End - 1; i >= s.Start; i--) if (cells[i].Water != 0) Flow(s.Chunk, i);
            }
        }

        void Flow(Chunk ch, int i)
        {
            var c = ch.ActiveCells[i];
            if (c.Frozen || IsBlocking(c)) return;
            int kind = c.Liquid;
            var liquid = Rules.HasLiquids ? Rules.Liquid(kind) : null;
            if (kind != 0 && (liquid.Thick == 0 || Tick % liquid.Thick != 0)) return;
            if (liquid?.Mix != null && Contact(ch, i, c, liquid))
            {
                kind = c.Liquid;
                liquid = Rules.Liquid(kind);
            }
            var (best, bestI, other) = Lowest(ch, i, c, liquid);
            if (best == null) return;
            var to = best.ActiveCells[bestI];
            int lands = kind;
            if (other)
            {
                if (!Mixes(liquid, to))
                {
                    Touch(ch, liquid, best, bestI);
                    return;
                }
                lands = MeetLiquid(liquid, to, ch, best);
                if (lands < 0) return;
            }
            if (kind != 0) Carry(c, to, liquid, to.Water == 0);
            c.Water--;
            if (c.Water == 0)
            {
                c.Liquid = 0;
                c.Sets = 0;
            }
            to.Water++;
            to.Liquid = lands;
            ch.Marks[i] |= FlowMark;
            best.Marks[bestI] |= FlowMark;
            WaterMoved(ch);
            if (best != ch) WaterMoved(best);
        }

        (Chunk best, int bestI, bool other) Lowest(Chunk ch, int i, Cell c, LiquidDef liquid)
        {
            int kind = c.Liquid, heap = liquid?.Heap ?? LiquidDef.WaterHeap;
            int elevation = ElevationUnits(c);
            int level = elevation + c.Water;
            int here = LandOf(ch, i);
            Chunk best = null;
            int bestI = 0;
            double bestScore = 0;
            bool other = false;
            for (int d = 0; d < 4; d++)
            {
                var nc = Chunk.Next(ch, i, d, out var nch, out int ni);
                if (nc == null || !nch.Active) continue;
                int there = here == NoLand ? NoLand : LandOf(nch, ni);
                int slope = there == NoLand ? 0 : here - there;
                double score = FlowScore(nc, elevation + slope, level + slope, heap);
                if (score <= bestScore) continue;
                bool held = liquid != null && nc.Water > 0 && nc.Liquid != kind;
                if (held && liquid.Touch == null && !Mixes(liquid, nc)) continue;
                bestScore = score;
                best = nch;
                bestI = ni;
                other = held;
            }
            return (best, bestI, other);
        }

        double FlowScore(Cell nc, int elevation, int level, int heap)
        {
            if (nc.Frozen && nc.Water > 0) return 0;
            var nt = TerrainOf(nc);
            if ((nt.Blocking && !nt.Vessel) || nc.Height >= Rules.Sim.Height.BlockAt) return 0;
            if (nc.Water >= Rules.Sim.WaterMaxLevel) return 0;
            int nElevation = (nt.Elevation + nc.Height) * Rules.Sim.WaterUnitsPerStep;
            int drop = level - (nElevation + nc.Water);
            bool downhill = nElevation < elevation;
            if (!(drop >= heap || (downhill && drop >= 1))) return 0;
            return drop + (downhill ? 0.5 : 0);
        }

        void TickGas(RowSpan[] rows) => TickGas(rows, 0, rows.Length);

        void TickGas(RowSpan[] rows, int from, int to)
        {
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    if (cells[i].Gas == null) continue;
                    RainFrom(s.Chunk, cells[i]);
                    ShedFrom(s.Chunk, i, cells[i]);
                    if (cells[i].Gas == null) continue;
                    RustUnder(cells[i]);
                    Drift(s.Chunk, i);
                }
            }
        }

        void RainFrom(Chunk ch, Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.RainEvery <= 0 || Tick % g.RainEvery != 0) return;
            if ((IsBlocking(c) && !HasTag(c, "vessel")) || !TakesWater(c)) return;
            c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + 1);
            c.Liquid = 0;
            WaterMoved(ch);
        }

        void RustUnder(Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.Rusts <= 0 || TerrainOf(c).RustEvery <= 0) return;
            c.Rust += g.Rusts;
        }

        void ShedFrom(Chunk ch, int i, Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.Sheds == null || g.ShedEvery <= 0 || Tick % g.ShedEvery != 0) return;
            if (g.ShedChance < 1 && Rand() >= g.ShedChance) return;
            Apply(CellX(ch, i), CellY(ch, i), g.Sheds, 1, -1, false);
            if (g.Sheds == "lightning") shedStrikes.Add((CellX(ch, i), CellY(ch, i), Tick));
        }

        bool BlowDownwind(Chunk ch, int i, Cell c)
        {
            var t = Chunk.Next(ch, i, WindDir, out var tch, out _);
            if (t == null || !tch.Active || IsBlocking(t)) return false;
            AddGas(t, c.Gas, 1);
            c.GasAmt--;
            if (c.GasAmt <= 0) c.Gas = null;
            return true;
        }

        double DecayOf(string gas) =>
            Rules.Gases.TryGetValue(gas, out var g) && g.Decay.HasValue ? g.Decay.Value : Rules.Sim.Gas.DecayChance;

        void Condense(Chunk ch, int i, Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.CondenseEvery <= 0 || (c.GasAmt + 1) % g.CondenseEvery != 0) return;
            if ((IsBlocking(c) && !HasTag(c, "vessel")) || !TakesWater(c)) return;
            if (NearHeat(CellX(ch, i), CellY(ch, i), g.CondenseAway)) return;
            c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + 1);
            c.Liquid = 0;
            WaterMoved(ch);
        }

        bool NearHeat(int x, int y, int radius)
        {
            if (radius <= 0) return false;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var n = At(x + dx, y + dy);
                    if (n != null && (n.Fire > 0 || n.Heat > 0)) return true;
                }
            return false;
        }

        void Drift(Chunk ch, int i)
        {
            var c = ch.ActiveCells[i];
            if (Rand() < DecayOf(c.Gas))
            {
                c.GasAmt--;
                Condense(ch, i, c);
            }
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
