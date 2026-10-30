// One tick of the cell world. Port of sandbox/src/sim.ts with the critter and
// machine systems left out, and of its entities only the defense field's
// raiders. The order of what remains is the TS order: fire -> water -> gas ->
// weather -> raiders (World.Raiders.cs) -> the defense's waves
// (World.Defense.cs) -> charge decay -> wind decay, then the day clock
// (Hasten Time, World.Time.cs).
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        // Reused by TickFire so a tick allocates nothing.
        readonly List<(Chunk ch, int i)> burning = new List<(Chunk ch, int i)>();
        // What floating covers reached this tick (World.Float), laid at its end.
        readonly List<(Cell cell, string cover, int slick)> floating = new List<(Cell cell, string cover, int slick)>();
        readonly List<Cell> gasOptions = new List<Cell>(4);

        /// <summary>
        /// One tick of the sim, over the active chunks only. Cells are visited in world row-major
        /// order (y, then x), as on the old single grid, so with every chunk active a tick is
        /// exactly what it was. Finishes a tick that StepSome left part done first.
        /// </summary>
        public void Step()
        {
            FinishTick();
            BeginTick();
            RunToEnd();
        }

        // A tick in slices (StepSome): the same passes over the same cells in the same order as
        // Step, only spread over several calls, so a peer that slices its ticks one way computes
        // exactly the ticks of a peer that slices them another (or not at all). Fire burns, then
        // springs, water, gas, the weather, raiders, defense and cast meters, then the timers, as in sim.ts.
        enum TickStage { None, Burn, WaterCheck, Springs, Flow, Gas, Rest, Timers, End }

        // Row segments or burning cells handled per slice: small enough to stop near a frame's budget.
        const int SliceSpans = 64, SliceBurns = 128, SliceChunks = 4;

        TickStage stage = TickStage.None;
        int stagePos;
        RowSpan[] tickRows;
        Chunk[] tickChunks;

        /// <summary>Whether a tick has begun (StepSome) and not yet finished.</summary>
        public bool TickInProgress => stage != TickStage.None;

        /// <summary>
        /// Runs the tick in progress (beginning one if none is) a slice at a time until it finishes
        /// (true) or <paramref name="stop"/> says to stop for now (false; the next call carries on).
        /// For a view that spreads a tick over frames to keep each frame short. Anything that
        /// changes the world from outside (a cast, an effect, a chunk switch, a rule switch) first
        /// finishes the tick in progress, so commands always land between whole ticks, the same on
        /// every peer. Meanwhile a view may see a tick half done for a frame or two.
        /// </summary>
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

        /// <summary>Finishes the tick in progress at once, if there is one.</summary>
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
            Tick++;
            logged.Clear();
            strikes.Clear();
            combosFired.Clear();
            TrimWeatherHistory();
            EndSetWeather();
            Weather = SkyNow();
            tickRows = Spans;
            tickChunks = activeChunks;
            CollectBurning(tickRows);
            stage = TickStage.Burn;
            stagePos = 0;
        }

        // One slice of the tick in progress; true once the tick has finished.
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
                    TickRaiders();
                    TickDefense();
                    TickCombos();
                    TickWhirlwinds();
                    NextStage(TickStage.Timers);
                    return false;
                case TickStage.End:
                    EndTick();
                    return true;
                default:
                    return true;
            }
        }

        // The next slice of a stage that goes over burning cells, chunks or row segments.
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
                case TickStage.WaterCheck: CheckWater(tickChunks, from, to); break;
                case TickStage.Springs: Springs(tickRows, from, to, true); break;
                // Odd ticks sweep backward: the slices go from the last row segment to the first.
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
                _ => TickStage.End,
            });
        }

        void NextStage(TickStage next)
        {
            stage = next;
            stagePos = 0;
        }

        void EndTick()
        {
            foreach (var (cell, cover, slick) in floating)
            {
                if (cell.Cover != null) continue;
                PutCover(cell, cover);
                cell.Slick = slick;
            }
            floating.Clear();
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
        }

        /// <summary>
        /// Charge decays and Life counts down (ripening when it runs out), and every chunk with a
        /// live cell remembers this tick (Chunk.LastLive), and on which of its edges (EdgeLive): fire,
        /// gas, charge, a timer, or water that moved this tick (before the marks are cleared). Weather
        /// alone doesn't make a cell live: while rain falls, water moving doesn't count, or every wet
        /// chunk would stay live for as long as it rained.
        /// </summary>
        void TickTimers(RowSpan[] rows) => TickTimers(rows, 0, rows.Length);


        // The timers of row segments [from, to).
        void TickTimers(RowSpan[] rows, int from, int to)
        {
            byte flow = Raining ? (byte)0 : FlowMark;
            // sim.water.soakPass 0: no soaking (tests of the flow alone).
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

        // One row segment's timers: the edges its live cells lie on (Chunks.EdgesOf), or -1 if none was live.
        int TickTimers(RowSpan s, byte flow, bool soak)
        {
            var ch = s.Chunk;
            var cells = ch.Cells;
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

        /// <summary>One cell's timers, and its soak pass on soak ticks; true if it is live (it was, or something changed).</summary>
        bool TickCell(Chunk ch, int i, Cell c, bool live, bool soak)
        {
            if (c.Charge > 0) c.Charge--;
            if (c.Lit > 0) c.Lit--;
            // A slick's reach goes with its oil.
            if (c.Slick != 0 && c.Cover == null) c.Slick = 0;
            if (c.Life > 0 && --c.Life == 0) Ripen(ch, i, c);
            if (c.Heat > 0 && Cool(ch, c)) live = true;
            if (soak && SoakPass(ch, i, c)) live = true;
            return live;
        }

        /// <summary>The soak pass on one cell: rust, a cover floating off, water soaking in, mud drying. True if anything changed.</summary>
        bool SoakPass(Chunk ch, int i, Cell c)
        {
            bool changed = Rust(c);
            if (c.Water > 0 && c.Cover != null && Float(ch, i, c)) changed = true;
            if (c.Water > 0 && Soak(ch, i, c)) changed = true;
            if (DryOut(c)) changed = true;
            return changed;
        }

        /// <summary>
        /// Hot metal cools a tick at a time (materials.json hot): water on it boils off a unit a tick as
        /// steam, each unit taking hot.quench ticks of its heat. A terrain that can't be hot drops it.
        /// </summary>
        bool Cool(Chunk ch, Cell c)
        {
            var t = TerrainOf(c);
            if (t.HotFor <= 0)
            {
                c.Heat = 0;
                return true;
            }
            if (c.Water > 0 && !c.Frozen)
            {
                c.Water--;
                AddGas(c, "steam", 1);
                c.Heat = Math.Max(0, c.Heat - t.HotQuench);
                WaterMoved(ch);
                LogOnce("hot-quench", "Water hisses into steam on the hot metal");
            }
            if (c.Heat > 0) c.Heat--;
            return true;
        }

        /// <summary>
        /// Wet metal rusts (materials.json rust), on the soak pass: a cell whose terrain rusts and is
        /// wet (unfrozen water on it, raining, or damp, Cell.DampUntil) counts the pass's ticks in
        /// Cell.Rust; at rust.every it becomes rust.leaves. True if it did.
        /// </summary>
        bool Rust(Cell c)
        {
            if (!((c.Water > 0 && !c.Frozen) || Raining || Tick < c.DampUntil)) return false;
            var t = TerrainOf(c);
            if (t.RustEvery <= 0) return false;
            c.Rust += Rules.Sim.SoakPass;
            if (c.Rust < t.RustEvery) return false;
            c.Rust = 0;
            c.T = t.RustsTo;
            LogOnce("rust", "The metal rusts through");
            return true;
        }

        /// <summary>
        /// Floating oil (a cover with float.every, materials.json): on open water it spreads to each open,
        /// uncovered water cell next to it, on the soak passes of every float.every ticks. What it
        /// reaches is laid at the end of the tick (EndTick), so the slick grows a cell a turn whichever
        /// way the rows run. It reaches float.reach cells from where it was laid (Cell.Slick counts down
        /// the cells it has left), so a slick on a lake stops. True if it spread.
        /// </summary>
        bool Float(Chunk ch, int i, Cell c)
        {
            if (c.Frozen || !Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.FloatEvery <= 0) return false;
            if (Tick / Rules.Sim.SoakPass % Math.Max(1, cv.FloatEvery / Rules.Sim.SoakPass) != 0) return false;
            // Its reach left (fresh oil has its float.reach); a slick at the end of its reach spreads no further.
            int reach = c.Slick > 0 ? c.Slick : cv.FloatReach;
            if (cv.FloatReach > 0 && reach <= 1) return false;
            bool spread = false;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n == null || !nch.Active || n.Cover != null || n.Water == 0 || n.Frozen || IsBlocking(n)) continue;
                floating.Add((n, c.Cover, cv.FloatReach > 0 ? reach - 1 : 0));
                spread = true;
            }
            return spread;
        }

        /// <summary>
        /// Water on the ground, every sim.water.soakPass ticks (materials.json): on ground whose water
        /// turns (waterTo) it freezes (arctic) or boils off a unit to steam (lava), any depth; thin water
        /// (it and its four neighbours no deeper than sim.water.thin, so not a lake's shore, the river,
        /// the sea or a filled pit) soaks in and dries up a unit every soakEvery ticks of its terrain,
        /// soakEveryWet on damp ground (raining, or damp from water soaked in before), and leaves it damp
        /// (and its soak.leaves: dirt turns to mud).
        /// Each cell on its own turn of the count, so a puddle shrinks from all over. True if it changed.
        /// </summary>
        bool Soak(Chunk ch, int i, Cell c)
        {
            var t = TerrainOf(c);
            if (t.WaterTo == "ice")
            {
                if (c.Frozen) return false;
                c.Frozen = true;
                WaterMoved(ch);
                return true;
            }
            if (t.WaterTo == "steam")
            {
                c.Water--;
                c.Frozen = false;
                AddGas(c, "steam", 1);
                WaterMoved(ch);
                return true;
            }
            int thin = Rules.Sim.ThinWater;
            if (c.Frozen || c.Water > thin) return false;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out _, out _);
                if (n != null && n.Water > thin) return false;
            }
            int every = Raining || Tick < c.DampUntil ? t.SoakEveryWet : t.SoakEvery;
            if (every <= 0) return false;
            int turns = Math.Max(1, every / Rules.Sim.SoakPass);
            int x = (ch.X << Chunks.Shift) + (i & Chunks.Mask), y = (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);
            if ((Tick / Rules.Sim.SoakPass + x * 7 + y * 13) % turns != 0) return false;
            c.Water--;
            c.DampUntil = Tick + Rules.Sim.DampTicks;
            if (t.SoaksTo != null) c.T = t.SoaksTo;
            WaterMoved(ch);
            return true;
        }

        /// <summary>
        /// Mud dries (materials.json soak.dries), on the soak pass: a cell whose terrain dries, with no water
        /// on it, not rained on and no longer damp (Cell.DampUntil), becomes its driesTo. True if it did.
        /// </summary>
        bool DryOut(Cell c)
        {
            var to = TerrainOf(c).DriesTo;
            if (to == null || c.Water > 0 || Raining || Tick < c.DampUntil) return false;
            c.T = to;
            return true;
        }

        /// <summary>Whether nothing in a loaded chunk has been live for sim.json's streaming.settleTicks.</summary>
        public bool IsSettled(int cx, int cy) => Tick - ChunkOrThrow(cx, cy).LastLive >= Rules.Sim.Streaming.SettleTicks;

        /// <summary>
        /// Whether something on a loaded chunk's edge facing direction d (0 to 3: N, E, S, W, as
        /// Neighbours) has been live within streaming.settleTicks: fire, water or gas there may cross
        /// into the chunk next door.
        /// </summary>
        public bool IsLiveAtEdge(int cx, int cy, int d) => Tick - ChunkOrThrow(cx, cy).EdgeLive[d] < Rules.Sim.Streaming.SettleTicks;

        /// <summary>LastLive for a chunk where nothing has been live.</summary>
        public const int NeverLive = int.MinValue / 2;

        /// <summary>The last tick anything in a loaded chunk was live (NeverLive if nothing has been).</summary>
        public int LastLive(int cx, int cy) => ChunkOrThrow(cx, cy).LastLive;

        /// <summary>
        /// A cell's Life ran out (ripen in sim.ts): a cover that lasts (thorns) withers away, and a
        /// terrain that grows (green shoots, a sapling) becomes its growsTo. While it burns it waits
        /// a tick at a time; a tree growing on wet ground pushes the water out.
        /// </summary>
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
            }
            var to = TerrainOf(c).GrowsTo;
            if (to == null || !Rules.Terrain.TryGetValue(to, out var grown)) return;
            // A tree waits while a raider stands where it would grow.
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

        // The cells burning as the tick starts (cells lit during it burn from the next tick).
        void CollectBurning(RowSpan[] rows)
        {
            burning.Clear();
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
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
            var c = ch.Cells[i];
            if (c.Fire == 0) return;
            // Water puts out anything but a floating oil or grease fire.
            if (c.Water > 0 && !c.Frozen && !CoverBurns(c))
            {
                c.Fire = 0;
                c.Fuel = 0;
                AddGas(c, "steam", 1);
                return;
            }
            if (Doused(c)) return;
            if (c.Gas != null) FlashGas(CellX(ch, i), CellY(ch, i));
            if (c.Gas != "steam" && Rand() < p.SmokeChance) AddGas(c, "smoke", 1);
            Spread(ch, i, c, p);
            if (--c.Fuel > 0) return;
            BurnOut(ch, i, c);
        }

        /// <summary>Out of fuel: a burning cover leaves ash or the terrain under it alight; terrain burns to its BurnsTo.</summary>
        void BurnOut(Chunk ch, int i, Cell c)
        {
            if (CoverBurns(c))
            {
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
                    // Burnt off open water (an oil slick) leaves no ash.
                    if (!IsBlocking(c) && !(c.Water > 0 && !c.Frozen)) c.Cover = "ash";
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
                WallLost(CellX(ch, i), CellY(ch, i));
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
            double flam = Flammability(n) * Dryness(n);
            double downwind = WindDir == d ? p.DownwindBonus : 0;
            // A burning terrain's heat (thatch flares) scales its reach; a burning cover burns at its own.
            double heat = CoverBurns(c) ? 1 : TerrainOf(c).Heat;
            if (flam > 0) return (p.SpreadChance * flam * (c.Fire / 3.0) * heat + downwind * flam) * RainDamp();
            // Bare water beside a burning slick (a burning cover on water) boils at its own, lower chance.
            if (n.Water > 0 && !n.Frozen) return CoverBurns(c) && c.Water > 0 && !c.Frozen ? p.SlickBoilChance : p.BoilChance;
            return p.HeatChance;
        }

        /// <summary>
        /// How much of its catch a cell keeps (soaked burns poorly, docs/MATERIALS.md): damp ground
        /// (raining, or damp from water soaked in, Cell.DampUntil) loses its terrain's soak.dampens of
        /// it; 1 when dry, or when what would catch is its cover (covers don't soak).
        /// </summary>
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
            // Even ticks sweep forward, odd ticks backward, so water doesn't drift one way.
            if (Tick % 2 == 0) FlowForward(rows, 0, rows.Length, false);
            else FlowBackward(rows, 0, rows.Length, false);
        }

        /// <summary>A spring's square (a river) refills to its level each tick, unless it is ice: water dug out of it keeps coming.</summary>
        // Row segments [from, to); skipStill passes over still chunks (World.StillWater). A refill
        // counts as water moving, so the chunks round it aren't skipped this tick.
        void Springs(RowSpan[] rows, int from, int to, bool skipStill)
        {
            int max = Rules.Sim.WaterMaxLevel;
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    int level = TerrainOf(c).Spring;
                    if (level <= 0 || c.Frozen || c.Water >= level) continue;
                    c.Water = Math.Min(level, max);
                    WaterMoved(s.Chunk);
                }
            }
        }

        // Row segments [from, to), first to last.
        void FlowForward(RowSpan[] rows, int from, int to, bool skipStill)
        {
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) if (cells[i].Water != 0) Flow(s.Chunk, i);
            }
        }

        // Row segments [from, to), last to first.
        void FlowBackward(RowSpan[] rows, int from, int to, bool skipStill)
        {
            for (int k = to - 1; k >= from; k--)
            {
                var s = rows[k];
                if (skipStill && Skips(s.Chunk)) continue;
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
            int here = LandOf(ch, i);
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
                // Between two dry-land cells the land's slope counts too; a water body's cells keep level.
                int there = here == NoLand ? NoLand : LandOf(nch, ni);
                int slope = there == NoLand ? 0 : here - there;
                double score = FlowScore(nc, elevation + slope, level + slope);
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
            WaterMoved(ch);
            if (best != ch) WaterMoved(best);
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

        void TickGas(RowSpan[] rows) => TickGas(rows, 0, rows.Length);

        // The gas of row segments [from, to).
        void TickGas(RowSpan[] rows, int from, int to)
        {
            for (int k = from; k < to; k++)
            {
                var s = rows[k];
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    if (cells[i].Gas == null) continue;
                    RainFrom(s.Chunk, cells[i]);
                    Drift(s.Chunk, i);
                }
            }
        }

        /// <summary>A rain cloud (a gas with rainEvery) rains a unit of water on its cell every rainEvery ticks; not into a wall, but into a vessel.</summary>
        void RainFrom(Chunk ch, Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.RainEvery <= 0 || Tick % g.RainEvery != 0) return;
            if (IsBlocking(c) && !HasTag(c, "vessel")) return;
            c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + 1);
            WaterMoved(ch);
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

        /// <summary>
        /// A unit of gas just decayed: a gas with condenseEvery (steam) leaves a unit of water each time
        /// its cell's count falls from a multiple of it (2 to 1, not 1 to 0), so thick steam cools back
        /// into water and thin steam fades. Not into a wall, but into a vessel.
        /// </summary>
        void Condense(Chunk ch, int i, Cell c)
        {
            if (!Rules.Gases.TryGetValue(c.Gas, out var g) || g.CondenseEvery <= 0 || (c.GasAmt + 1) % g.CondenseEvery != 0) return;
            if (IsBlocking(c) && !HasTag(c, "vessel")) return;
            if (NearHeat(CellX(ch, i), CellY(ch, i), g.CondenseAway)) return;
            c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + 1);
            WaterMoved(ch);
        }

        /// <summary>Whether a cell within <paramref name="radius"/> of (x, y), this one too, is burning or hot (Cell.Heat); never with radius 0.</summary>
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

        /// <summary>A gas cell decays, blows downwind, or spreads to a thinner neighbour.</summary>
        void Drift(Chunk ch, int i)
        {
            var c = ch.Cells[i];
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
