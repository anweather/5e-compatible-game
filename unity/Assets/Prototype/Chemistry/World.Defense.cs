// The defense field's waves (docs/BACKLOG.md 17). Port of sandbox/src/defense.ts,
// line for line. A DefenseCommand starts them; each tick (after the raiders'
// upkeep, World.Raiders.cs) the next raider of the wave comes through the gate
// on its schedule, every raider takes a step toward the flag along the
// cheapest way there, and a raider reaching the flag costs a life. The way is
// a cost field over the squares round the field (a Dijkstra search from the
// flag, worked out afresh each tick, so a wall or a trench reroutes them at
// once): open ground costs 1, rough going (deep water, a climb or drop of
// more than a step) more and slower, a raised wall a lot (they batter it down
// a step at a time if there is no better way), and blocking terrain can't be
// crossed. Ties go N, E, S, W. All whole numbers, so every machine agrees.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chem
{
    public sealed partial class World
    {
        const int Unreached = int.MaxValue;

        // The cost field's scratch, reused from tick to tick.
        int[] pathCost = Array.Empty<int>();
        readonly List<long> pathHeap = new List<long>();

        /// <summary>The defense field's waves, or null before anyone started them.</summary>
        public DefenseState Defense { get; private set; }

        /// <summary>Raiders still to beat in the wave under way: on the board and still to come.</summary>
        public int DefenseLeft => Defense == null ? 0 : raiders.Count + Defense.ToSpawn;

        /// <summary>How many raiders a wave brings: First, and Growth more for each wave after.</summary>
        public static int WaveSize(DefenseParams p, int wave) => p.First + p.Growth * (wave - 1);

        /// <summary>
        /// Runs a defense command (startDefense in the TS): Start begins wave 1 with full lives (again,
        /// after a win or a loss), unless waves are already coming. Between ticks, like a cast.
        /// </summary>
        public void ApplyDefense(DefenseCommand command)
        {
            FinishTick(); // commands land between whole ticks (StepSome)
            if (command.Kind != DefenseKind.Start) return;
            if (Defense != null && Defense.Phase == DefensePhase.Running)
            {
                AddLog("The raiders are already coming");
                return;
            }
            if (!InBounds(command.GateX, command.GateY) || !InBounds(command.FlagX, command.FlagY)) return;
            var p = Rules.Sim.Defense;
            int m = Math.Max(0, p.Margin);
            ClearRaiders();
            Defense = new DefenseState
            {
                Phase = DefensePhase.Running,
                Wave = 1,
                Lives = p.Lives,
                ToSpawn = WaveSize(p, 1),
                NextSpawn = Tick + p.Warmup,
                GateX = command.GateX,
                GateY = command.GateY,
                FlagX = command.FlagX,
                FlagY = command.FlagY,
                X0 = Math.Max(0, Math.Min(command.GateX, command.FlagX) - m),
                X1 = Math.Min(W - 1, Math.Max(command.GateX, command.FlagX) + m),
                Y0 = Math.Max(0, Math.Min(command.GateY, command.FlagY) - m),
                Y1 = Math.Min(H - 1, Math.Max(command.GateY, command.FlagY) + m),
            };
            AddLog($"Wave 1 of {p.Waves} is coming! Hold the flag", LogKind.Notable);
        }

        /// <summary>One tick of the waves: the next raider through the gate, every raider's step, the next wave.</summary>
        void TickDefense()
        {
            var d = Defense;
            if (d == null || d.Phase != DefensePhase.Running) return;
            var p = Rules.Sim.Defense;
            SpawnNext(d, p);
            March(d, p);
            if (d.Phase == DefensePhase.Running) NextWave(d, p);
        }

        void SpawnNext(DefenseState d, DefenseParams p)
        {
            if (d.ToSpawn <= 0 || Tick < d.NextSpawn) return;
            string kind = p.Kinds.Count > 0 ? p.Kinds[d.Spawned % p.Kinds.Count] : null;
            var def = Rules.Entity(kind);
            if (def == null)
            {
                d.ToSpawn = 0;
                return;
            }
            var r = SpawnRaider(kind, d.GateX, d.GateY, def.Hp + p.HpPerWave * (d.Wave - 1));
            if (r == null) return; // the gate is crowded: next tick
            r.Wait = PaceOf(def) - 1;
            d.ToSpawn--;
            d.Spawned++;
            d.NextSpawn = Tick + p.SpawnEvery;
        }

        static int PaceOf(EntityDef def) => def.Raider != null ? def.Raider.TicksPerSquare : 4;

        void March(DefenseState d, DefenseParams p)
        {
            if (raiders.Count == 0) return;
            CostField(d, p);
            raiderScratch.Clear();
            raiderScratch.AddRange(raiders);
            foreach (var r in raiderScratch)
            {
                if (!r.Alive) continue;
                if (r.Wait > 0)
                {
                    r.Wait--;
                    continue;
                }
                if (r.StatusOf("frozen") > 0 || r.StatusOf("snagged") > 0 || r.StatusOf(ThrownStatus) > 0) continue;
                Advance(d, p, r);
                if (d.Phase != DefensePhase.Running) return;
            }
        }

        // One raider's step: to the neighbour with the cheapest way on (battering it if it's a wall).
        void Advance(DefenseState d, DefenseParams p, Raider r)
        {
            int dir = BestStep(d, p, r.X, r.Y);
            if (dir < 0) return;
            int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
            var to = At(nx, ny);
            var def = Rules.Entity(r.Kind);
            if (IsRaisedWall(to))
            {
                Reshape(nx, ny, -1);
                LogOnce("batter#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} batters at the wall");
                r.Wait = p.BatterTicks;
                return;
            }
            if (RaiderAt(nx, ny) != null) return; // wait for the one ahead
            bool rough = Rough(p, At(r.X, r.Y), to);
            r.X = nx;
            r.Y = ny;
            r.Wait = PaceOf(def) - 1 + (rough ? p.RoughTicks : 0);
            Thorns(r, to);
            if (r.Alive && r.X == d.FlagX && r.Y == d.FlagY) ReachFlag(d, r, def);
        }

        void ReachFlag(DefenseState d, Raider r, EntityDef def)
        {
            raiders.Remove(r);
            r.Alive = false;
            d.Lives--;
            AddLog($"A {def.Name.ToLowerInvariant()} reaches the flag! {Math.Max(0, d.Lives)} lives left", LogKind.Hurt);
            if (d.Lives > 0) return;
            d.Phase = DefensePhase.Lost;
            d.ToSpawn = 0;
            ClearRaiders();
            AddLog("The flag falls: the raiders win this time", LogKind.Notable);
        }

        void NextWave(DefenseState d, DefenseParams p)
        {
            if (d.ToSpawn > 0 || raiders.Count > 0) return;
            if (d.Wave >= p.Waves)
            {
                d.Phase = DefensePhase.Won;
                AddLog($"All {p.Waves} waves beaten: the flag stands!", LogKind.Notable);
                return;
            }
            d.Wave++;
            d.ToSpawn = WaveSize(p, d.Wave);
            d.NextSpawn = Tick + p.BetweenWaves;
            AddLog($"Wave {d.Wave} of {p.Waves} is coming!", LogKind.Notable);
        }

        // ---------- the way to the flag ----------

        static bool InField(DefenseState d, int x, int y) => x >= d.X0 && x <= d.X1 && y >= d.Y0 && y <= d.Y1;

        static int FieldIndex(DefenseState d, int x, int y) => (y - d.Y0) * (d.X1 - d.X0 + 1) + (x - d.X0);

        /// <summary>
        /// The cost of stepping from one square onto a neighbour: -1 if it can't be done (blocking
        /// terrain), WallCost onto a raised wall (to batter), RoughCost into deep water or up or down
        /// more than a step, else 1.
        /// </summary>
        int StepCost(DefenseParams p, Cell from, Cell to)
        {
            if (to == null) return -1;
            if (HasTag(to, "blocking")) return -1;
            if (IsRaisedWall(to)) return p.WallCost;
            return Rough(p, from, to) ? p.RoughCost : 1;
        }

        bool Rough(DefenseParams p, Cell from, Cell to) =>
            (to.Water >= p.DeepWater && !to.Frozen) || Math.Abs(ElevationUnits(to) - ElevationUnits(from)) > Rules.Sim.WaterUnitsPerStep;

        /// <summary>The cheapest way to the flag from every square of the field (Unreached where there is none).</summary>
        void CostField(DefenseState d, DefenseParams p)
        {
            int w = d.X1 - d.X0 + 1, n = w * (d.Y1 - d.Y0 + 1);
            if (pathCost.Length != n) pathCost = new int[n];
            for (int i = 0; i < n; i++) pathCost[i] = Unreached;
            pathHeap.Clear();
            int start = FieldIndex(d, d.FlagX, d.FlagY);
            pathCost[start] = 0;
            HeapPush(start, n);
            while (pathHeap.Count > 0)
            {
                long top = HeapPop();
                int i = (int)(top % n), cost = (int)(top / n);
                if (cost <= pathCost[i]) Relax(d, p, d.X0 + i % w, d.Y0 + i / w, cost, n);
            }
        }

        // Every neighbour of a settled square gets the cheaper of its cost so far and the way through here.
        void Relax(DefenseState d, DefenseParams p, int x, int y, int cost, int n)
        {
            var here = At(x, y);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + DirX(dir), ny = y + DirY(dir);
                if (!InField(d, nx, ny)) continue;
                // What it costs to step from the neighbour onto this square.
                int step = StepCost(p, At(nx, ny), here);
                if (step < 0) continue;
                int ni = FieldIndex(d, nx, ny), total = cost + step;
                if (total >= pathCost[ni]) continue;
                pathCost[ni] = total;
                HeapPush(ni, n);
            }
        }

        // A min-heap of cost * n + square: no two keys alike, so it pops in one order everywhere.
        void HeapPush(int cell, int n)
        {
            pathHeap.Add((long)pathCost[cell] * n + cell);
            int k = pathHeap.Count - 1;
            while (k > 0)
            {
                int parent = (k - 1) / 2;
                if (pathHeap[parent] <= pathHeap[k]) break;
                (pathHeap[parent], pathHeap[k]) = (pathHeap[k], pathHeap[parent]);
                k = parent;
            }
        }

        long HeapPop()
        {
            long top = pathHeap[0];
            int last = pathHeap.Count - 1;
            pathHeap[0] = pathHeap[last];
            pathHeap.RemoveAt(last);
            int k = 0;
            while (true)
            {
                int a = 2 * k + 1, b = a + 1, least = k;
                if (a < pathHeap.Count && pathHeap[a] < pathHeap[least]) least = a;
                if (b < pathHeap.Count && pathHeap[b] < pathHeap[least]) least = b;
                if (least == k) break;
                (pathHeap[least], pathHeap[k]) = (pathHeap[k], pathHeap[least]);
                k = least;
            }
            return top;
        }

        /// <summary>The way (0..3) a raider at (x, y) steps toward the flag, or -1 to stay (no way, or it's there).</summary>
        int BestStep(DefenseState d, DefenseParams p, int x, int y)
        {
            if (!InField(d, x, y) || pathCost[FieldIndex(d, x, y)] == Unreached) return -1;
            var here = At(x, y);
            int best = -1;
            long bestCost = long.MaxValue;
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + DirX(dir), ny = y + DirY(dir);
                if (!InField(d, nx, ny)) continue;
                int on = pathCost[FieldIndex(d, nx, ny)];
                if (on == Unreached) continue;
                int step = StepCost(p, here, At(nx, ny));
                if (step < 0 || (long)on + step >= bestCost) continue;
                bestCost = (long)on + step;
                best = dir;
            }
            return best;
        }

        /// <summary>The cheapest way's cost from a square to the flag, as last worked out, or -1 off the field or with no way.</summary>
        public int PathCostAt(int x, int y)
        {
            var d = Defense;
            if (d == null || !InField(d, x, y) || pathCost.Length != (d.X1 - d.X0 + 1) * (d.Y1 - d.Y0 + 1)) return -1;
            int c = pathCost[FieldIndex(d, x, y)];
            return c == Unreached ? -1 : c;
        }

        // ---------- hashes ----------

        /// <summary>
        /// A fingerprint (FNV-1a) of the defense: the waves' state and every raider. Shared with the
        /// sandbox's golden (defenseText in sandbox/src/defense.ts) and part of the netcode checksum.
        /// </summary>
        public string DefenseHash() => Fnv1a.Hash(DefenseText());

        /// <summary>
        /// The defense as text: "phase/wave/lives/toSpawn/nextSpawn/spawned|" and then each raider as
        /// "id,kind,x,y,hp,wait,burning,wet,frozen,snagged;", or "-" before any.
        /// </summary>
        public string DefenseText()
        {
            var d = Defense;
            if (d == null) return "-";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(((int)d.Phase).ToString(inv)).Append('/').Append(d.Wave.ToString(inv)).Append('/').Append(d.Lives.ToString(inv)).Append('/')
              .Append(d.ToSpawn.ToString(inv)).Append('/').Append(d.NextSpawn.ToString(inv)).Append('/').Append(d.Spawned.ToString(inv)).Append('|');
            foreach (var r in raiders)
            {
                sb.Append(r.Id.ToString(inv)).Append(',').Append(r.Kind).Append(',').Append(r.X.ToString(inv)).Append(',').Append(r.Y.ToString(inv)).Append(',')
                  .Append(r.Hp.ToString(inv)).Append(',').Append(r.Wait.ToString(inv));
                foreach (var s in HashedStatuses) sb.Append(',').Append(r.StatusOf(s).ToString(inv));
                // Thrown (a Geyser's jet) joins only while it lasts, so older defenses hash as they did.
                if (r.StatusOf(ThrownStatus) > 0) sb.Append(",t").Append(r.StatusOf(ThrownStatus).ToString(inv));
                sb.Append(';');
            }
            return sb.ToString();
        }

        static readonly string[] HashedStatuses = { "burning", "wet", "frozen", "snagged" };

        /// <summary>The status of a raider thrown up in the air (a Geyser's jet, reactions.json e-geyser): it can't walk until it lands.</summary>
        public const string ThrownStatus = "thrown";
    }
}
