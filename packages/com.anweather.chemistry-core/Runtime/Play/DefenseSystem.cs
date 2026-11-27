// The defense field's waves (docs/BACKLOG.md 17), the defense field's system (docs/design/architecture-review.md
// step 9, D9). Port of sandbox/src/defense.ts, line for line. A DefenseCommand starts them; each tick (after the
// raiders' upkeep, Play/RaiderSystem.cs) the next raider of the wave comes through the gate on its schedule, every raider
// takes a step toward the flag along the cheapest way there, and a raider reaching the flag costs a life. The way is
// a cost field over the squares round the field (a Dijkstra search from the flag in the world's WayField, worked out
// afresh each tick, so a wall or a trench reroutes them at once): open ground costs 1, rough going (deep water, a
// climb or drop of more than a step) more and slower, a raised wall a lot (they batter it down a step at a time if
// there is no better way), and blocking terrain can't be crossed. Ties go N, E, S, W. All whole numbers, so every
// machine agrees. The world owns one (World.DefenseSystem) and ticks it after the raiders. It reads the cells and the
// rules; it writes its DefenseState, the world's raiders (spawned, stepped, cleared), a raised wall it batters, and
// the log.
using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class DefenseSystem
    {
        const int Unreached = WayField.Unreached;

        readonly World world;
        readonly WayField ways;

        internal DefenseSystem(World world)
        {
            this.world = world;
            ways = world.Ways;
        }

        public DefenseState State { get; private set; }

        public int Left => State == null ? 0 : world.RaiderSystem.List.Count + State.ToSpawn;

        public static int WaveSize(DefenseParams p, int wave) => p.First + p.Growth * (wave - 1);

        public void Apply(DefenseCommand command)
        {
            world.FinishTick();
            if (command.Kind != DefenseKind.Start) return;
            if (State != null && State.Phase == DefensePhase.Running)
            {
                world.AddLog("The raiders are already coming");
                return;
            }
            if (!world.InBounds(command.GateX, command.GateY) || !world.InBounds(command.FlagX, command.FlagY)) return;
            var p = world.Rules.Sim.Defense;
            int m = Math.Max(0, p.Margin);
            world.RaiderSystem.ClearRaiders();
            State = new DefenseState
            {
                Phase = DefensePhase.Running,
                Wave = 1,
                Lives = p.Lives,
                ToSpawn = WaveSize(p, 1),
                NextSpawn = world.Tick + p.Warmup,
                GateX = command.GateX,
                GateY = command.GateY,
                FlagX = command.FlagX,
                FlagY = command.FlagY,
                X0 = Math.Max(0, Math.Min(command.GateX, command.FlagX) - m),
                X1 = Math.Min(world.W - 1, Math.Max(command.GateX, command.FlagX) + m),
                Y0 = Math.Max(0, Math.Min(command.GateY, command.FlagY) - m),
                Y1 = Math.Min(world.H - 1, Math.Max(command.GateY, command.FlagY) + m),
            };
            world.AddLog($"Wave 1 of {p.Waves} is coming! Hold the flag", LogKind.Notable);
        }

        internal void Tick()
        {
            var d = State;
            if (d == null || d.Phase != DefensePhase.Running) return;
            var p = world.Rules.Sim.Defense;
            SpawnNext(d, p);
            March(d, p);
            if (d.Phase == DefensePhase.Running) NextWave(d, p);
        }

        void SpawnNext(DefenseState d, DefenseParams p)
        {
            if (d.ToSpawn <= 0 || world.Tick < d.NextSpawn) return;
            string kind = p.Kinds.Count > 0 ? p.Kinds[d.Spawned % p.Kinds.Count] : null;
            var def = world.Rules.Entity(kind);
            if (def == null)
            {
                d.ToSpawn = 0;
                return;
            }
            var r = world.SpawnRaider(kind, d.GateX, d.GateY, def.Hp + p.HpPerWave * (d.Wave - 1));
            if (r == null) return;
            r.Wait = PaceOf(def) - 1;
            d.ToSpawn--;
            d.Spawned++;
            d.NextSpawn = world.Tick + p.SpawnEvery;
        }

        internal static int PaceOf(EntityDef def) => def.Raider != null ? def.Raider.TicksPerSquare : 4;

        void March(DefenseState d, DefenseParams p)
        {
            if (world.RaiderSystem.List.Count == 0) return;
            CostField(d, p);
            world.RaiderSystem.Scratch.Clear();
            world.RaiderSystem.Scratch.AddRange(world.RaiderSystem.List);
            foreach (var r in world.RaiderSystem.Scratch)
            {
                if (!r.Alive) continue;
                if (r.Wait > 0)
                {
                    r.Wait--;
                    continue;
                }
                if (r.StatusOf("frozen") > 0 || r.StatusOf("snagged") > 0 || r.StatusOf(World.ThrownStatus) > 0 || r.StatusOf(World.DownedStatus) > 0) continue;
                Advance(d, p, r);
                if (d.Phase != DefensePhase.Running) return;
            }
        }

        void Advance(DefenseState d, DefenseParams p, Raider r)
        {
            int dir = BestStep(d, p, r.X, r.Y);
            if (dir < 0) return;
            int nx = r.X + World.DirX(dir), ny = r.Y + World.DirY(dir);
            var to = world.At(nx, ny);
            var def = world.Rules.Entity(r.Kind);
            if (world.IsRaisedWall(to))
            {
                world.Reshape(nx, ny, -1);
                world.LogOnce("batter#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} batters at the wall");
                r.Wait = p.BatterTicks;
                return;
            }
            if (world.RaiderAt(nx, ny) != null) return;
            bool rough = Rough(p, world.At(r.X, r.Y), to);
            r.X = world.FoldX(nx);
            r.Y = world.FoldY(ny);
            r.Wait = PaceOf(def) - 1 + (rough ? p.RoughTicks : 0);
            world.RaiderSystem.Thorns(r, to);
            if (r.Alive && r.X == d.FlagX && r.Y == d.FlagY) ReachFlag(d, r, def);
        }

        void ReachFlag(DefenseState d, Raider r, EntityDef def)
        {
            world.RaiderSystem.List.Remove(r);
            r.Alive = false;
            d.Lives--;
            world.AddLog($"A {def.Name.ToLowerInvariant()} reaches the flag! {Math.Max(0, d.Lives)} lives left", LogKind.Hurt);
            if (d.Lives > 0) return;
            d.Phase = DefensePhase.Lost;
            d.ToSpawn = 0;
            world.RaiderSystem.ClearRaiders();
            world.AddLog("The flag falls: the raiders win this time", LogKind.Notable);
        }

        void NextWave(DefenseState d, DefenseParams p)
        {
            if (d.ToSpawn > 0 || world.RaiderSystem.List.Count > 0) return;
            if (d.Wave >= p.Waves)
            {
                d.Phase = DefensePhase.Won;
                world.AddLog($"All {p.Waves} waves beaten: the flag stands!", LogKind.Notable);
                return;
            }
            d.Wave++;
            d.ToSpawn = WaveSize(p, d.Wave);
            d.NextSpawn = world.Tick + p.BetweenWaves;
            world.AddLog($"Wave {d.Wave} of {p.Waves} is coming!", LogKind.Notable);
        }

        int StepCost(DefenseParams p, Cell from, Cell to)
        {
            if (to == null) return -1;
            if (world.HasTag(to, "blocking")) return -1;
            if (world.IsRaisedWall(to)) return p.WallCost;
            return Rough(p, from, to) ? p.RoughCost : 1;
        }

        internal bool Rough(DefenseParams p, Cell from, Cell to) =>
            world.DrownDepth(to) >= p.DeepWater || Math.Abs(world.ElevationUnits(to) - world.ElevationUnits(from)) > world.Rules.Sim.WaterUnitsPerStep;

        void CostField(DefenseState d, DefenseParams p)
        {
            int w = d.X1 - d.X0 + 1, n = w * (d.Y1 - d.Y0 + 1);
            if (ways.Cost.Length != n) ways.Cost = new int[n];
            for (int i = 0; i < n; i++) ways.Cost[i] = Unreached;
            ways.Heap.Clear();
            int start = WayField.Index(d, d.FlagX, d.FlagY);
            ways.Cost[start] = 0;
            ways.Push(start, n);
            while (ways.Heap.Count > 0)
            {
                long top = ways.Pop();
                int i = (int)(top % n), cost = (int)(top / n);
                if (cost <= ways.Cost[i]) Relax(d, p, d.X0 + i % w, d.Y0 + i / w, cost, n);
            }
        }

        void Relax(DefenseState d, DefenseParams p, int x, int y, int cost, int n)
        {
            var here = world.At(x, y);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + World.DirX(dir), ny = y + World.DirY(dir);
                if (!WayField.InField(d, nx, ny)) continue;
                int step = StepCost(p, world.At(nx, ny), here);
                if (step < 0) continue;
                int ni = WayField.Index(d, nx, ny), total = cost + step;
                if (total >= ways.Cost[ni]) continue;
                ways.Cost[ni] = total;
                ways.Push(ni, n);
            }
        }

        int BestStep(DefenseState d, DefenseParams p, int x, int y)
        {
            if (!WayField.InField(d, x, y) || ways.Cost[WayField.Index(d, x, y)] == Unreached) return -1;
            var here = world.At(x, y);
            int best = -1;
            long bestCost = long.MaxValue;
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + World.DirX(dir), ny = y + World.DirY(dir);
                if (!WayField.InField(d, nx, ny)) continue;
                int on = ways.Cost[WayField.Index(d, nx, ny)];
                if (on == Unreached) continue;
                int step = StepCost(p, here, world.At(nx, ny));
                if (step < 0 || (long)on + step >= bestCost) continue;
                bestCost = (long)on + step;
                best = dir;
            }
            return best;
        }

        public int PathCostAt(int x, int y)
        {
            var d = State;
            if (d == null || !WayField.InField(d, x, y) || ways.Cost.Length != (d.X1 - d.X0 + 1) * (d.Y1 - d.Y0 + 1)) return -1;
            int c = ways.Cost[WayField.Index(d, x, y)];
            return c == Unreached ? -1 : c;
        }

        public string Hash() => Fnv1a.Hash(Text());

        public string Text()
        {
            var d = State;
            if (d == null) return "-";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(((int)d.Phase).ToString(inv)).Append('/').Append(d.Wave.ToString(inv)).Append('/').Append(d.Lives.ToString(inv)).Append('/')
              .Append(d.ToSpawn.ToString(inv)).Append('/').Append(d.NextSpawn.ToString(inv)).Append('/').Append(d.Spawned.ToString(inv)).Append('|');
            foreach (var r in world.RaiderSystem.List)
            {
                sb.Append(r.Id.ToString(inv)).Append(',').Append(r.Kind).Append(',').Append(r.X.ToString(inv)).Append(',').Append(r.Y.ToString(inv)).Append(',')
                  .Append(r.Hp.ToString(inv)).Append(',').Append(r.Wait.ToString(inv));
                foreach (var s in HashedStatuses) sb.Append(',').Append(r.StatusOf(s).ToString(inv));
                if (r.StatusOf(World.ThrownStatus) > 0) sb.Append(",t").Append(r.StatusOf(World.ThrownStatus).ToString(inv));
                if (r.StatusOf(World.DownedStatus) > 0) sb.Append(",d").Append(r.StatusOf(World.DownedStatus).ToString(inv));
                if (r.StatusOf(World.ShovedStatus) > 0) sb.Append(",s").Append(r.StatusOf(World.ShovedStatus).ToString(inv));
                sb.Append(';');
            }
            return sb.ToString();
        }

        static readonly string[] HashedStatuses = { "burning", "wet", "frozen", "snagged" };

        internal JToken Save() => State == null ? JValue.CreateNull() : new JObject
        {
            ["phase"] = (int)State.Phase,
            ["at"] = new JArray(State.Wave, State.Lives, State.ToSpawn, State.NextSpawn, State.Spawned),
            ["gate"] = new JArray(State.GateX, State.GateY, State.FlagX, State.FlagY),
            ["field"] = new JArray(State.X0, State.Y0, State.X1, State.Y1),
        };

        internal void Load(JObject d)
        {
            var at = d["at"];
            var gate = d["gate"];
            var field = d["field"];
            State = new DefenseState
            {
                Phase = (DefensePhase)(int)d["phase"],
                Wave = (int)at[0],
                Lives = (int)at[1],
                ToSpawn = (int)at[2],
                NextSpawn = (int)at[3],
                Spawned = (int)at[4],
                GateX = (int)gate[0],
                GateY = (int)gate[1],
                FlagX = (int)gate[2],
                FlagY = (int)gate[3],
                X0 = (int)field[0],
                Y0 = (int)field[1],
                X1 = (int)field[2],
                Y1 = (int)field[3],
            };
        }
    }
}
