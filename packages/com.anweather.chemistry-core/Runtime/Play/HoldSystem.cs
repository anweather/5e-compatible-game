// The Hold's system (docs/design/the-hold.md, phase 2; docs/design/architecture-review.md step 9, D9): raiders come from a map's spawners in endless waves, go for the keep's
// heart, batter what's in their way and hurt wizards in reach; the run ends when the keep falls, and the score is the wave
// reached. A wizard standing on the plate starts a run; every peer sees that stand at the same tick (a Stand command).
// Raiders are the defense field's, so spells reach them as they do there; their way is worked out here, not in
// Play/DefenseSystem.cs, since a castle's walls can be battered and a field's can't. A wizard on a blocking cell is on the walls:
// no raider reaches it. All whole numbers and the seeded generator, and "" in the checksum until there's been a run.
// Chem.Play's (PlayModes owns one and ticks it after the defense field); the world keeps the site, the run, the plate and
// the battered walls (World.Play.cs, with their text and save). It reads the cells, the wizards' stands and the rules; it
// writes the run, the world's raiders, the castle's cells, wizards' health and hands, the log, and draws from the world's
// generator (a raider's spawner cell, a blow's roll) in the order it always did.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chem
{
    public sealed class HoldSystem
    {
        readonly World world;
        readonly WayField ways;
        readonly DefenseSystem defense;

        internal HoldSystem(World world, WayField ways, DefenseSystem defense)
        {
            this.world = world;
            this.ways = ways;
            this.defense = defense;
        }

        int[] roadWay = Array.Empty<int>(), straightWay = Array.Empty<int>();
        public const string ClimbRoute = World.ClimbRoute, FlyRoute = World.FlyRoute;

        public WorldHold Site => world.HoldSite;

        public HoldState State
        {
            get => world.Hold;
            private set => world.Hold = value;
        }

        public bool Running => world.HoldRunning;

        public int Left => world.HoldLeft;

        public void Set(WorldHold site) => world.SetHold(site);

        public static int WaveSize(HoldParams p, int wave, int peers = 1) =>
            (p.First + p.Growth * (wave - 1)) * ByPeers(p.PeerPercent, peers) / 100;

        static int ByPeers(IReadOnlyList<int> percents, int peers) => percents[Math.Min(Math.Max(peers, 1), percents.Count) - 1];

        public WavePlan Plan(int wave, int peers, string theme) =>
            new WavePlan(wave, WaveSize(world.Rules.Sim.Hold, wave, peers), theme, BossWave(world.Rules.Sim.Hold, wave), 1 + (wave - 1) / world.Rules.Sim.Hold.KindEvery);

        public string ThemeOfWave(int wave)
        {
            var p = world.Rules.Sim.Hold;
            string theme = null;
            for (int w = p.ThemeFrom; w <= wave; w++) theme = NextTheme(p, w, theme);
            return theme;
        }

        public (WavePlan plan, int ticks)? Warning =>
            Running && State.InWave == 0 && State.ToSpawn > 0 ? (Plan(State.Wave, State.Peers, State.Theme), Math.Max(0, State.NextSpawn - world.Tick)) : ((WavePlan, int)?)null;

        public (int x, int y)? Respawn() => Running ? Site.Rise : ((int x, int y)?)null;

        public int BlowsOn(int x, int y) => world.Battered.TryGetValue(world.Idx(x, y), out int n) ? n : 0;

        internal void Tick()
        {
            if (Site == null) return;
            if (!Running)
            {
                WatchPlate();
                return;
            }
            var p = world.Rules.Sim.Hold;
            SpawnHold(p);
            MarchHold(p);
            if (Running) NextHoldWave(p);
        }

        void WatchPlate()
        {
            var p = world.Rules.Sim.Hold;
            bool on = world.Stands.Values.Any(s => !s.Down && s.X == Site.Plate.x && s.Y == Site.Plate.y);
            world.HoldPlate = on ? world.HoldPlate + 1 : 0;
            if (world.HoldPlate < p.PlateTicks || (world.Defense != null && world.Defense.Phase == DefensePhase.Running)) return;
            world.HoldPlate = 0;
            world.RaiderSystem.ClearRaiders();
            world.ReadStands();
            int peers = world.Stands.Values.Count(s => !s.Down);
            State = new HoldState { Phase = HoldPhase.Running, Wave = 1, Health = p.Health, Peers = peers, ToSpawn = WaveSize(p, 1, peers), NextSpawn = world.Tick + p.Warmup, Theme = ThemeOfWave(1) };
            State.NextWave = p.WaveTicks > 0 ? State.NextSpawn + p.WaveTicks : 0;
            world.LetGoLockedAll();
            world.AddLog("Wave 1 is coming! Hold the keep", LogKind.Notable);
        }

        internal bool StartMoment(string id)
        {
            var p = world.Rules.Sim.Hold;
            var m = p.Moment(id);
            if (Site == null || m == null) return false;
            world.RaiderSystem.ClearRaiders();
            MendCastle();
            world.ReadStands();
            int peers = world.Stands.Values.Count(s => !s.Down);
            State = new HoldState
            {
                Phase = HoldPhase.Running,
                Wave = m.Wave,
                Health = m.Health > 0 ? m.Health : p.Health,
                Peers = peers,
                ToSpawn = WaveSize(p, m.Wave, peers),
                NextSpawn = world.Tick + p.Warmup,
                Theme = m.Theme ?? ThemeOfWave(m.Wave),
                Moment = id,
                MomentTick = world.Tick,
            };
            foreach (var player in world.Stands.Keys)
            {
                var h = world.OwnHands(player);
                for (int i = 0; i < HandsDef.Count; i++)
                {
                    h.Holds[i] = m.Holds[i];
                    h.Ready[i] = 0;
                }
                World.DropHands(h);
            }
            State.NextWave = p.WaveTicks > 0 ? State.NextSpawn + p.WaveTicks : 0;
            BreakCastle(p, m.Broken);
            world.AddLog($"{m.Name}: wave {m.Wave}", LogKind.Notable);
            return true;
        }

        void BreakCastle(HoldParams p, int percent)
        {
            const uint broken = 5;
            int n = 0;
            foreach (var cell in BuildingRaster.Cells(BuildingRow.Read(Site.Castle, Site.Row, world.Rules.Buildings), world.Rules.Buildings))
            {
                var c = world.At(cell.X, cell.Y);
                if (c == null || Blows(p, c) == 0) continue;
                if (WaveRoll(State.Wave, n++, broken) % 100 < percent) world.BreakWall(cell.X, cell.Y, c);
            }
        }

        void SpawnHold(HoldParams p)
        {
            var h = State;
            if (h.ToSpawn <= 0 || world.Tick < h.NextSpawn) return;
            var spawner = Site.Spawners[h.Spawned % Site.Spawners.Count];
            var plan = Plan(h.Wave, h.Peers, h.Theme);
            string plain = KindOf(spawner, plan, h.InWave);
            if (world.Rules.Entity(plain) == null)
            {
                h.ToSpawn = 0;
                return;
            }
            bool boss = plan.Boss && h.InWave == 0;
            string kind = boss ? plain + Rules.AdjectiveMark + p.BossAdjective : WithAdjective(p, plain, plan, h.InWave);
            var def = world.Rules.Entity(kind);
            if (h.InWave == 0)
            {
                RaiseBarricades(p);
                world.PickForTheLate();
            }
            var (x, y) = spawner.Cells[(int)(world.Rand() * spawner.Cells.Count)];
            int hp = def.Hp + (p.HpPerWave + (boss ? p.BossHpPerWave : 0)) * (h.Wave - 1);
            var r = world.SpawnRaider(kind, x, y, boss ? hp * ByPeers(p.BossPeerHealth, h.Peers) / 100 : hp);
            if (r == null) return;
            r.Wait = DefenseSystem.PaceOf(def) - 1;
            h.ToSpawn--;
            h.Spawned++;
            h.InWave++;
            h.NextSpawn = world.Tick + p.SpawnEvery;
        }

        internal uint WaveRoll(int wave, int slot, uint salt) => world.WaveRoll(wave, slot, salt);

        static bool BossWave(HoldParams p, int wave) => p.BossEvery > 0 && p.BossAdjective != null && wave % p.BossEvery == 0;

        static string KindOf(HoldSpawner spawner, WavePlan plan, int slot)
        {
            int kinds = Math.Min(plan.Kinds, spawner.Kinds.Count);
            if (kinds == 0) return null;
            return plan.Boss && slot == 0 ? spawner.Kinds[kinds - 1] : spawner.Kinds[slot % kinds];
        }

        string WithAdjective(HoldParams p, string plain, WavePlan plan, int slot)
        {
            const uint themed = 1, chance = 2, which = 3;
            var theme = p.Theme(plan.Theme);
            if (theme != null && theme.Adjectives.Count > 0 && WaveRoll(plan.Wave, slot, themed) % 100 < p.ThemePercent)
                return plain + Rules.AdjectiveMark + theme.Adjectives[(int)(WaveRoll(plan.Wave, slot, which) % (uint)theme.Adjectives.Count)];
            var common = p.CommonAdjectives.ToList();
            int percent = Math.Min(p.AdjectiveMost, p.AdjectivePercent + p.AdjectivePerWave * (plan.Wave - p.AdjectiveFrom));
            if (plan.Wave < p.AdjectiveFrom || common.Count == 0 || WaveRoll(plan.Wave, slot, chance) % 100 >= percent) return plain;
            return plain + Rules.AdjectiveMark + common[(int)(WaveRoll(plan.Wave, slot, which) % (uint)common.Count)].Id;
        }

        string NextTheme(HoldParams p, int wave, string last)
        {
            const uint pick = 4;
            if (wave < p.ThemeFrom) return null;
            var themes = p.Themes.Where(t => t.Id != last).ToList();
            return themes.Count == 0 ? last : themes[(int)(WaveRoll(wave, 0, pick) % (uint)themes.Count)].Id;
        }

        void NextHoldWave(HoldParams p)
        {
            var h = State;
            if (h.ToSpawn > 0) return;
            if (p.Waves > 0 && h.Wave >= p.Waves)
            {
                if (world.RaiderSystem.List.Count == 0) WinRun();
                return;
            }
            if (p.WaveTicks > 0 ? world.Tick < h.NextWave - p.BetweenWaves : world.RaiderSystem.List.Count > 0) return;
            h.Wave++;
            h.Theme = NextTheme(p, h.Wave, h.Theme);
            h.InWave = 0;
            h.ToSpawn = WaveSize(p, h.Wave, h.Peers);
            h.NextSpawn = p.WaveTicks > 0 ? Math.Max(h.NextWave, world.Tick + p.BetweenWaves) : world.Tick + p.BetweenWaves;
            if (p.WaveTicks > 0) h.NextWave = h.NextSpawn + p.WaveTicks;
            world.OfferUpgrades(p);
            world.AddLog(p.Waves > 0 && h.Wave == p.Waves ? $"The last wave, {h.Wave}, is coming!" : $"Wave {h.Wave} is coming!", LogKind.Notable);
        }

        public int? NextWaveIn =>
            Running && State.NextWave > 0 && (world.Rules.Sim.Hold.Waves == 0 || State.Wave < world.Rules.Sim.Hold.Waves) ? Math.Max(0, State.NextWave - world.Tick) : (int?)null;

        public static int BarricadesOf(HoldParams p, int wave) =>
            wave < p.BarricadeFrom ? 0 : Math.Min(p.BarricadeMost, p.BarricadeFirst + (wave - p.BarricadeFrom) / p.BarricadePerWaves);

        void RaiseBarricades(HoldParams p)
        {
            const uint where = 6;
            var places = Site.Barricades.ToList();
            for (int n = Math.Min(BarricadesOf(p, State.Wave), places.Count), i = 0; n > 0; n--, i++)
            {
                int at = (int)(WaveRoll(State.Wave, i, where) % (uint)places.Count);
                var (px, py) = places[at];
                places.RemoveAt(at);
                for (int y = py - p.BarricadeRadius; y <= py + p.BarricadeRadius; y++)
                    for (int x = px - p.BarricadeRadius; x <= px + p.BarricadeRadius; x++)
                        if (world.InBounds(x, y) && world.HoldRoad.Contains(world.Idx(x, y)) && !world.HasTag(world.At(x, y), "blocking")) world.SetTerrainTo(x, y, world.At(x, y), p.BarricadeTerrain);
            }
        }

        void MarchHold(HoldParams p)
        {
            if (world.RaiderSystem.List.Count == 0) return;
            if (roadWay.Length == 0 || world.Tick % p.RouteEvery == 0) WorkOutWays(p);
            world.RaiderSystem.Scratch.Clear();
            world.RaiderSystem.Scratch.AddRange(world.RaiderSystem.List);
            foreach (var r in world.RaiderSystem.Scratch)
            {
                if (!r.Alive) continue;
                Upkeep(r);
                if (r.Wait > 0)
                {
                    r.Wait--;
                    continue;
                }
                if (r.StatusOf("frozen") > 0 || r.StatusOf("snagged") > 0 || r.StatusOf(World.ThrownStatus) > 0 || r.StatusOf(World.DownedStatus) > 0) continue;
                var def = world.Rules.Entity(r.Kind);
                if (!BlowAtWizard(r, def) && !BlowAtHeart(r, def))
                {
                    if (Flying(r, def)) Fly(r, def);
                    else StepHold(p, r, def);
                }
                if (!Running) return;
            }
        }

        public bool Flying(Raider r) => world.Flying(r);

        static bool Flying(Raider r, EntityDef def) => World.Flies(r, def);

        void Fly(Raider r, EntityDef def)
        {
            r.X = world.FoldX(r.X + Math.Sign(world.DeltaX(r.X, Site.Heart.x)));
            r.Y = world.FoldY(r.Y + Math.Sign(world.DeltaY(r.Y, Site.Heart.y)));
            r.Wait = DefenseSystem.PaceOf(def) - 1;
        }

        void Upkeep(Raider r)
        {
            if (r.StatusOf(World.LandedStatus) > 0) r.Status[World.LandedStatus] = 2;
            if (Flying(r) && world.Rules.Sim.Hold.FlyerLands.Any(s => r.StatusOf(s) > 0))
            {
                r.Status[World.LandedStatus] = 2;
                world.AddLog($"The {world.Rules.Entity(r.Kind).Name.ToLowerInvariant()} comes down", LogKind.Notable);
            }
            var adjective = world.Rules.AdjectiveOf(r.Kind);
            string keeps = adjective?.Keeps;
            foreach (var status in adjective?.Never ?? Array.Empty<string>()) r.Status.Remove(status);
            if (keeps != null) r.Status[keeps] = Math.Max(r.StatusOf(keeps), 2);
        }

        bool InReach(int x0, int y0, int x1, int y1, int reach) => Math.Max(Math.Abs(world.DeltaX(x0, x1)), Math.Abs(world.DeltaY(y0, y1))) <= reach;

        bool BlowAtWizard(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2) return false;
            var s = world.Rules.Sim.Strike;
            var foe = world.Stands.Values.FirstOrDefault(w => !w.Down && InReach(r.X, r.Y, w.X, w.Y, s.Reach) && (Flying(r, def) || !world.IsBlocking(world.At(w.X, w.Y))));
            if (foe == null) return false;
            var wizard = world.Rules.Entity(World.WizardEntity);
            int roll = 1 + (int)(world.Rand() * 20), total = roll + World.Modifier(def.Str) + s.Proficiency;
            bool hit = roll == 20 || (roll != 1 && total >= (wizard != null ? wizard.Ac : EntityDef.DefaultScore));
            var result = new StrikeHit { Tick = world.Tick, Attacker = def.Name, TargetPlayer = foe.Player, Roll = roll, Total = total, Hit = hit, ToX = foe.X, ToY = foe.Y };
            world.StrikeHits.Add(result);
            r.Wait = def.Raider.AttackEvery;
            if (!hit) return true;
            result.Damage = Math.Max(1, Harder(world.Roll(dice) + World.Modifier(def.Str), r));
            foe.Hp = Math.Max(0, foe.Hp - result.Damage);
            world.AddLog($"The {def.Name.ToLowerInvariant()} hits {foe.Player}: {result.Damage} damage", LogKind.Hurt);
            if (!foe.Down) return true;
            result.Downed = true;
            world.AddLog($"{foe.Player} is downed", LogKind.Hurt);
            return true;
        }

        int Harder(int damage, Raider r)
        {
            int wave = 100 + world.Rules.Sim.Hold.DamagePerWave * (State.Wave - 1);
            return damage * wave / 100 * (world.Rules.AdjectiveOf(r.Kind)?.Damage ?? 100) / 100;
        }

        bool BlowAtHeart(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2 || !InReach(r.X, r.Y, Site.Heart.x, Site.Heart.y, 1)) return false;
            r.Wait = def.Raider.AttackEvery;
            State.Health -= Harder(world.Roll(dice), r);
            world.LogOnce("heart#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} is at the keep!", LogKind.Hurt);
            if (State.Health <= 0) EndRun();
            return true;
        }

        void StepHold(HoldParams p, Raider r, EntityDef def)
        {
            bool road = def.Raider?.Route != ClimbRoute && def.Raider?.Route != FlyRoute;
            int dir = HoldStep(p, r.X, r.Y, road);
            if (dir < 0) return;
            int nx = r.X + World.DirX(dir), ny = r.Y + World.DirY(dir);
            var to = world.At(nx, ny);
            if (Blows(p, to) > 0 || world.IsRaisedWall(to))
            {
                Batter(p, r, def, nx, ny, to);
                return;
            }
            var ahead = world.RaiderAt(nx, ny);
            if (ahead != null && !Flying(ahead) && !Swap(p, r, ahead)) return;
            bool rough = defense.Rough(world.Rules.Sim.Defense, world.At(r.X, r.Y), to);
            r.X = world.FoldX(nx);
            r.Y = world.FoldY(ny);
            r.Wait = DefenseSystem.PaceOf(def) - 1 + (rough ? world.Rules.Sim.Defense.RoughTicks : 0);
            world.RaiderSystem.Thorns(r, to);
            if (r.Alive && world.Rules.AdjectiveOf(r.Kind)?.Lights == true) world.Apply(r.X, r.Y, "fire", 1, -1, false);
        }

        bool Swap(HoldParams p, Raider r, Raider ahead)
        {
            var route = world.Rules.Entity(ahead.Kind).Raider?.Route;
            int dir = HoldStep(p, ahead.X, ahead.Y, route != ClimbRoute && route != FlyRoute);
            if (dir < 0 || ahead.X + World.DirX(dir) != r.X || ahead.Y + World.DirY(dir) != r.Y) return false;
            ahead.X = r.X;
            ahead.Y = r.Y;
            return true;
        }

        void Batter(HoldParams p, Raider r, EntityDef def, int x, int y, Cell c)
        {
            r.Wait = p.BatterTicks;
            world.LogOnce("batter#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} batters at the walls");
            if (world.IsRaisedWall(c))
            {
                world.Reshape(x, y, -1);
                return;
            }
            int i = world.Idx(x, y), blows = (world.Battered.TryGetValue(i, out int n) ? n : 0) + 1;
            world.Battered[i] = blows;
            if (blows < Blows(p, c)) return;
            world.Battered.Remove(i);
            string was = world.TerrainOf(c).Name.ToLowerInvariant();
            if (world.BreakWall(x, y, c) || world.SetTerrainTo(x, y, c, p.Rubble)) world.AddLog($"The {was} gives way", LogKind.Notable);
        }

        int Blows(HoldParams p, Cell c) =>
            world.HasTag(c, "blocking") && p.Strength.TryGetValue(c.T, out int blows) ? blows : 0;

        void WinRun()
        {
            world.EarnPoints(world.Rules.Sim.Hold, State.Wave);
            State.Phase = HoldPhase.Won;
            State.ToSpawn = 0;
            world.AddLog($"The keep holds! All {State.Wave} waves beaten", LogKind.Notable);
            MendCastle();
        }

        void EndRun()
        {
            world.EarnPoints(world.Rules.Sim.Hold, State.Wave - 1);
            State.Phase = HoldPhase.Lost;
            State.Health = 0;
            State.ToSpawn = 0;
            world.RaiderSystem.ClearRaiders();
            world.AddLog($"The keep falls in wave {State.Wave}", LogKind.Notable);
            MendCastle();
        }

        void MendCastle()
        {
            world.Battered.Clear();
            var row = BuildingRow.Read(Site.Castle, Site.Row, world.Rules.Buildings);
            var touched = new SortedSet<(int y, int x)>();
            foreach (var cell in BuildingRaster.Cells(row, world.Rules.Buildings))
            {
                var c = world.At(cell.X, cell.Y);
                if (c == null || (c.T == cell.Terrain && c.Fire == 0 && c.Cover == null)) continue;
                c.Reset(cell.Terrain);
                c.ForgetDef();
                touched.Add(world.ChunkKey(world.Idx(cell.X, cell.Y)));
            }
            foreach (var key in touched) world.FreshenBuilt(key.x, key.y);
        }

        int HoldStepCost(HoldParams p, Cell from, Cell to, int toIdx, bool road)
        {
            if (to == null) return -1;
            int blows = Blows(p, to);
            if (blows > 0) return blows * p.BatterCost;
            if (world.HasTag(to, "blocking")) return -1;
            var d = world.Rules.Sim.Defense;
            if (world.IsRaisedWall(to)) return d.WallCost;
            int cost = defense.Rough(d, from, to) ? d.RoughCost : 1;
            return road && !world.HoldRoad.Contains(toIdx) ? cost * p.OffRoadCost : cost;
        }

        public (int x, int y)? Next(int x, int y, bool road)
        {
            int dir = roadWay.Length == 0 ? -1 : HoldStep(world.Rules.Sim.Hold, x, y, road);
            return dir < 0 ? ((int, int)?)null : (x + World.DirX(dir), y + World.DirY(dir));
        }

        void WorkOutWays(HoldParams p)
        {
            HoldCostField(p, true);
            roadWay = (int[])ways.Cost.Clone();
            HoldCostField(p, false);
            straightWay = (int[])ways.Cost.Clone();
        }

        void HoldCostField(HoldParams p, bool road)
        {
            var d = world.HoldField;
            int w = d.X1 - d.X0 + 1, n = w * (d.Y1 - d.Y0 + 1);
            if (ways.Cost.Length != n) ways.Cost = new int[n];
            for (int i = 0; i < n; i++) ways.Cost[i] = WayField.Unreached;
            ways.Heap.Clear();
            int start = WayField.Index(d, d.FlagX, d.FlagY);
            ways.Cost[start] = 0;
            ways.Push(start, n);
            while (ways.Heap.Count > 0)
            {
                long top = ways.Pop();
                int i = (int)(top % n), cost = (int)(top / n);
                if (cost > ways.Cost[i]) continue;
                int x = d.X0 + i % w, y = d.Y0 + i / w;
                var here = world.At(x, y);
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = x + World.DirX(dir), ny = y + World.DirY(dir);
                    if (!WayField.InField(d, nx, ny)) continue;
                    int step = HoldStepCost(p, world.At(nx, ny), here, world.Idx(x, y), road);
                    if (step < 0) continue;
                    int ni = WayField.Index(d, nx, ny), total = cost + step;
                    if (total >= ways.Cost[ni]) continue;
                    ways.Cost[ni] = total;
                    ways.Push(ni, n);
                }
            }
        }

        int HoldStep(HoldParams p, int x, int y, bool road)
        {
            var d = world.HoldField;
            if (!WayField.InField(d, x, y)) return -1;
            var here = world.At(x, y);
            int best = -1;
            long bestCost = long.MaxValue;
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + World.DirX(dir), ny = y + World.DirY(dir);
                if (!WayField.InField(d, nx, ny)) continue;
                int on = (road ? roadWay : straightWay)[WayField.Index(d, nx, ny)];
                if (on == WayField.Unreached) continue;
                int step = HoldStepCost(p, here, world.At(nx, ny), world.Idx(nx, ny), road);
                if (step < 0 || (long)on + step >= bestCost) continue;
                bestCost = (long)on + step;
                best = dir;
            }
            return best;
        }

        public string Text() => world.HoldText();
    }
}
