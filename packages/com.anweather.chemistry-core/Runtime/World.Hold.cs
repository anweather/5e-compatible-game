// The Hold's run (docs/design/the-hold.md, phase 2): raiders come from a map's spawners in endless waves, go for the keep's
// heart, batter what's in their way and hurt wizards in reach; the run ends when the keep falls, and the score is the wave
// reached. A wizard standing on the plate starts a run; every peer sees that stand at the same tick (a Stand command).
// Raiders are the defense field's, so spells reach them as they do there; their way is worked out here, not in
// World.Defense.cs, since a castle's walls can be battered and a field's can't. A wizard on a blocking cell is on the walls:
// no raider reaches it. All whole numbers and the seeded generator, and "" in the checksum until there's been a run.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public enum HoldPhase
    {
        Running,
        Lost,
        Won,
    }

    public sealed class HoldState
    {
        public HoldPhase Phase;
        public int Wave, Health, ToSpawn, NextSpawn, Spawned;
        public int Peers = 1, InWave;
        public int NextWave;
        public string Theme;
        public string Moment;
        public SortedDictionary<string, RunWizard> Wizards { get; } = new SortedDictionary<string, RunWizard>(StringComparer.Ordinal);
        public int MomentTick;
    }

    public sealed partial class World
    {
        DefenseState holdField;
        readonly SortedDictionary<int, int> battered = new SortedDictionary<int, int>();
        int holdPlate;
        readonly HashSet<int> holdRoad = new HashSet<int>();
        int[] roadWay = Array.Empty<int>(), straightWay = Array.Empty<int>();
        public const string ClimbRoute = "climb", FlyRoute = "fly";
        const string LandedStatus = "landed";

        public WorldHold HoldSite { get; private set; }

        public HoldState Hold { get; private set; }

        public bool HoldRunning => Hold != null && Hold.Phase == HoldPhase.Running;

        public int HoldLeft => Hold == null ? 0 : raiders.Count + Hold.ToSpawn;

        public void SetHold(WorldHold site)
        {
            HoldSite = site;
            if (site == null) return;
            var p = Rules.Sim.Hold;
            var cells = site.Spawners.SelectMany(s => s.Cells).Concat(new[] { site.Heart }).ToList();
            holdField = new DefenseState
            {
                FlagX = site.Heart.x,
                FlagY = site.Heart.y,
                X0 = Math.Max(0, cells.Min(c => c.x) - p.Margin),
                X1 = Math.Min(W - 1, cells.Max(c => c.x) + p.Margin),
                Y0 = Math.Max(0, cells.Min(c => c.y) - p.Margin),
                Y1 = Math.Min(H - 1, cells.Max(c => c.y) + p.Margin),
            };
            holdRoad.Clear();
            foreach (var cell in BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, Rules.Buildings), Rules.Buildings))
                if (cell.Part == BuildingPart.Road && cell.Terrain == site.RoadTerrain && InBounds(cell.X, cell.Y)) holdRoad.Add(Idx(cell.X, cell.Y));
        }

        public static int HoldWaveSize(HoldParams p, int wave, int peers = 1) =>
            (p.First + p.Growth * (wave - 1)) * ByPeers(p.PeerPercent, peers) / 100;

        static int ByPeers(IReadOnlyList<int> percents, int peers) => percents[Math.Min(Math.Max(peers, 1), percents.Count) - 1];

        public WavePlan HoldPlan(int wave, int peers, string theme) =>
            new WavePlan(wave, HoldWaveSize(Rules.Sim.Hold, wave, peers), theme, BossWave(Rules.Sim.Hold, wave), 1 + (wave - 1) / Rules.Sim.Hold.KindEvery);

        public string ThemeOfWave(int wave)
        {
            var p = Rules.Sim.Hold;
            string theme = null;
            for (int w = p.ThemeFrom; w <= wave; w++) theme = NextTheme(p, w, theme);
            return theme;
        }

        public (WavePlan plan, int ticks)? HoldWarning =>
            HoldRunning && Hold.InWave == 0 && Hold.ToSpawn > 0 ? (HoldPlan(Hold.Wave, Hold.Peers, Hold.Theme), Math.Max(0, Hold.NextSpawn - Tick)) : ((WavePlan, int)?)null;

        public (int x, int y)? HoldRespawn() => HoldRunning ? HoldSite.Rise : ((int x, int y)?)null;

        public int BlowsOn(int x, int y) => battered.TryGetValue(Idx(x, y), out int n) ? n : 0;

        void TickHold()
        {
            if (HoldSite == null) return;
            if (!HoldRunning)
            {
                WatchPlate();
                return;
            }
            var p = Rules.Sim.Hold;
            SpawnHold(p);
            MarchHold(p);
            if (HoldRunning) NextHoldWave(p);
        }

        void WatchPlate()
        {
            var p = Rules.Sim.Hold;
            bool on = stands.Values.Any(s => !s.Down && s.X == HoldSite.Plate.x && s.Y == HoldSite.Plate.y);
            holdPlate = on ? holdPlate + 1 : 0;
            if (holdPlate < p.PlateTicks || (Defense != null && Defense.Phase == DefensePhase.Running)) return;
            holdPlate = 0;
            ClearRaiders();
            ReadStands();
            int peers = stands.Values.Count(s => !s.Down);
            Hold = new HoldState { Phase = HoldPhase.Running, Wave = 1, Health = p.Health, Peers = peers, ToSpawn = HoldWaveSize(p, 1, peers), NextSpawn = Tick + p.Warmup, Theme = ThemeOfWave(1) };
            Hold.NextWave = p.WaveTicks > 0 ? Hold.NextSpawn + p.WaveTicks : 0;
            LetGoLockedAll();
            AddLog("Wave 1 is coming! Hold the keep", LogKind.Notable);
        }

        bool StartMoment(string id)
        {
            var p = Rules.Sim.Hold;
            var m = p.Moment(id);
            if (HoldSite == null || m == null) return false;
            ClearRaiders();
            MendCastle();
            ReadStands();
            int peers = stands.Values.Count(s => !s.Down);
            Hold = new HoldState
            {
                Phase = HoldPhase.Running,
                Wave = m.Wave,
                Health = m.Health > 0 ? m.Health : p.Health,
                Peers = peers,
                ToSpawn = HoldWaveSize(p, m.Wave, peers),
                NextSpawn = Tick + p.Warmup,
                Theme = m.Theme ?? ThemeOfWave(m.Wave),
                Moment = id,
                MomentTick = Tick,
            };
            foreach (var player in stands.Keys)
            {
                var h = OwnHands(player);
                for (int i = 0; i < HandsDef.Count; i++)
                {
                    h.Holds[i] = m.Holds[i];
                    h.Ready[i] = 0;
                }
                DropHands(h);
            }
            Hold.NextWave = p.WaveTicks > 0 ? Hold.NextSpawn + p.WaveTicks : 0;
            BreakCastle(p, m.Broken);
            AddLog($"{m.Name}: wave {m.Wave}", LogKind.Notable);
            return true;
        }

        void BreakCastle(HoldParams p, int percent)
        {
            const uint broken = 5;
            int n = 0;
            foreach (var cell in BuildingRaster.Cells(BuildingRow.Read(HoldSite.Castle, HoldSite.Row, Rules.Buildings), Rules.Buildings))
            {
                var c = At(cell.X, cell.Y);
                if (c == null || Blows(p, c) == 0) continue;
                if (WaveRoll(Hold.Wave, n++, broken) % 100 < percent) BreakWall(cell.X, cell.Y, c);
            }
        }

        void SpawnHold(HoldParams p)
        {
            var h = Hold;
            if (h.ToSpawn <= 0 || Tick < h.NextSpawn) return;
            var spawner = HoldSite.Spawners[h.Spawned % HoldSite.Spawners.Count];
            var plan = HoldPlan(h.Wave, h.Peers, h.Theme);
            string plain = KindOf(spawner, plan, h.InWave);
            if (Rules.Entity(plain) == null)
            {
                h.ToSpawn = 0;
                return;
            }
            bool boss = plan.Boss && h.InWave == 0;
            string kind = boss ? plain + Rules.AdjectiveMark + p.BossAdjective : WithAdjective(p, plain, plan, h.InWave);
            var def = Rules.Entity(kind);
            if (h.InWave == 0)
            {
                RaiseBarricades(p);
                PickForTheLate();
            }
            var (x, y) = spawner.Cells[(int)(Rand() * spawner.Cells.Count)];
            int hp = def.Hp + (p.HpPerWave + (boss ? p.BossHpPerWave : 0)) * (h.Wave - 1);
            var r = SpawnRaider(kind, x, y, boss ? hp * ByPeers(p.BossPeerHealth, h.Peers) / 100 : hp);
            if (r == null) return;
            r.Wait = PaceOf(def) - 1;
            h.ToSpawn--;
            h.Spawned++;
            h.InWave++;
            h.NextSpawn = Tick + p.SpawnEvery;
        }

        uint WaveRoll(int wave, int slot, uint salt)
        {
            unchecked
            {
                uint t = Seed + (uint)wave * 0x9e3779b1u + (uint)slot * 0x85ebca77u + salt * 0xc2b2ae3du;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

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
            var h = Hold;
            if (h.ToSpawn > 0) return;
            if (p.Waves > 0 && h.Wave >= p.Waves)
            {
                if (raiders.Count == 0) WinRun();
                return;
            }
            if (p.WaveTicks > 0 ? Tick < h.NextWave - p.BetweenWaves : raiders.Count > 0) return;
            h.Wave++;
            h.Theme = NextTheme(p, h.Wave, h.Theme);
            h.InWave = 0;
            h.ToSpawn = HoldWaveSize(p, h.Wave, h.Peers);
            h.NextSpawn = p.WaveTicks > 0 ? Math.Max(h.NextWave, Tick + p.BetweenWaves) : Tick + p.BetweenWaves;
            if (p.WaveTicks > 0) h.NextWave = h.NextSpawn + p.WaveTicks;
            OfferUpgrades(p);
            AddLog(p.Waves > 0 && h.Wave == p.Waves ? $"The last wave, {h.Wave}, is coming!" : $"Wave {h.Wave} is coming!", LogKind.Notable);
        }

        public int? HoldNextWaveIn =>
            HoldRunning && Hold.NextWave > 0 && (Rules.Sim.Hold.Waves == 0 || Hold.Wave < Rules.Sim.Hold.Waves) ? Math.Max(0, Hold.NextWave - Tick) : (int?)null;

        public static int BarricadesOf(HoldParams p, int wave) =>
            wave < p.BarricadeFrom ? 0 : Math.Min(p.BarricadeMost, p.BarricadeFirst + (wave - p.BarricadeFrom) / p.BarricadePerWaves);

        void RaiseBarricades(HoldParams p)
        {
            const uint where = 6;
            var places = HoldSite.Barricades.ToList();
            for (int n = Math.Min(BarricadesOf(p, Hold.Wave), places.Count), i = 0; n > 0; n--, i++)
            {
                int at = (int)(WaveRoll(Hold.Wave, i, where) % (uint)places.Count);
                var (px, py) = places[at];
                places.RemoveAt(at);
                for (int y = py - p.BarricadeRadius; y <= py + p.BarricadeRadius; y++)
                    for (int x = px - p.BarricadeRadius; x <= px + p.BarricadeRadius; x++)
                        if (InBounds(x, y) && holdRoad.Contains(Idx(x, y)) && !HasTag(At(x, y), "blocking")) SetTerrainTo(x, y, At(x, y), p.BarricadeTerrain);
            }
        }

        void MarchHold(HoldParams p)
        {
            if (raiders.Count == 0) return;
            if (roadWay.Length == 0 || Tick % p.RouteEvery == 0) WorkOutWays(p);
            raiderScratch.Clear();
            raiderScratch.AddRange(raiders);
            foreach (var r in raiderScratch)
            {
                if (!r.Alive) continue;
                Upkeep(r);
                if (r.Wait > 0)
                {
                    r.Wait--;
                    continue;
                }
                if (r.StatusOf("frozen") > 0 || r.StatusOf("snagged") > 0 || r.StatusOf(ThrownStatus) > 0 || r.StatusOf(DownedStatus) > 0) continue;
                var def = Rules.Entity(r.Kind);
                if (!BlowAtWizard(r, def) && !BlowAtHeart(r, def))
                {
                    if (Flying(r, def)) Fly(r, def);
                    else StepHold(p, r, def);
                }
                if (!HoldRunning) return;
            }
        }

        public bool Flying(Raider r) => Flying(r, Rules.Entity(r.Kind));

        static bool Flying(Raider r, EntityDef def) => def.Raider?.Route == FlyRoute && r.StatusOf(LandedStatus) == 0;

        void Fly(Raider r, EntityDef def)
        {
            r.X = FoldX(r.X + Math.Sign(DeltaX(r.X, HoldSite.Heart.x)));
            r.Y = FoldY(r.Y + Math.Sign(DeltaY(r.Y, HoldSite.Heart.y)));
            r.Wait = PaceOf(def) - 1;
        }

        void Upkeep(Raider r)
        {
            if (r.StatusOf(LandedStatus) > 0) r.Status[LandedStatus] = 2;
            if (Flying(r) && Rules.Sim.Hold.FlyerLands.Any(s => r.StatusOf(s) > 0))
            {
                r.Status[LandedStatus] = 2;
                AddLog($"The {Rules.Entity(r.Kind).Name.ToLowerInvariant()} comes down", LogKind.Notable);
            }
            var adjective = Rules.AdjectiveOf(r.Kind);
            string keeps = adjective?.Keeps;
            foreach (var status in adjective?.Never ?? Array.Empty<string>()) r.Status.Remove(status);
            if (keeps != null) r.Status[keeps] = Math.Max(r.StatusOf(keeps), 2);
        }

        bool InReach(int x0, int y0, int x1, int y1, int reach) => Math.Max(Math.Abs(DeltaX(x0, x1)), Math.Abs(DeltaY(y0, y1))) <= reach;

        bool BlowAtWizard(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2) return false;
            var s = Rules.Sim.Strike;
            var foe = stands.Values.FirstOrDefault(w => !w.Down && InReach(r.X, r.Y, w.X, w.Y, s.Reach) && (Flying(r, def) || !IsBlocking(At(w.X, w.Y))));
            if (foe == null) return false;
            var wizard = Rules.Entity(WizardEntity);
            int roll = 1 + (int)(Rand() * 20), total = roll + Modifier(def.Str) + s.Proficiency;
            bool hit = roll == 20 || (roll != 1 && total >= (wizard != null ? wizard.Ac : EntityDef.DefaultScore));
            var result = new StrikeHit { Tick = Tick, Attacker = def.Name, TargetPlayer = foe.Player, Roll = roll, Total = total, Hit = hit, ToX = foe.X, ToY = foe.Y };
            strikeHits.Add(result);
            r.Wait = def.Raider.AttackEvery;
            if (!hit) return true;
            result.Damage = Math.Max(1, Harder(Roll(dice) + Modifier(def.Str), r));
            foe.Hp = Math.Max(0, foe.Hp - result.Damage);
            AddLog($"The {def.Name.ToLowerInvariant()} hits {foe.Player}: {result.Damage} damage", LogKind.Hurt);
            if (!foe.Down) return true;
            result.Downed = true;
            AddLog($"{foe.Player} is downed", LogKind.Hurt);
            return true;
        }

        int Harder(int damage, Raider r)
        {
            int wave = 100 + Rules.Sim.Hold.DamagePerWave * (Hold.Wave - 1);
            return damage * wave / 100 * (Rules.AdjectiveOf(r.Kind)?.Damage ?? 100) / 100;
        }

        bool BlowAtHeart(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2 || !InReach(r.X, r.Y, HoldSite.Heart.x, HoldSite.Heart.y, 1)) return false;
            r.Wait = def.Raider.AttackEvery;
            Hold.Health -= Harder(Roll(dice), r);
            LogOnce("heart#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} is at the keep!", LogKind.Hurt);
            if (Hold.Health <= 0) EndRun();
            return true;
        }

        void StepHold(HoldParams p, Raider r, EntityDef def)
        {
            bool road = def.Raider?.Route != ClimbRoute && def.Raider?.Route != FlyRoute;
            int dir = HoldStep(p, r.X, r.Y, road);
            if (dir < 0) return;
            int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
            var to = At(nx, ny);
            if (Blows(p, to) > 0 || IsRaisedWall(to))
            {
                Batter(p, r, def, nx, ny, to);
                return;
            }
            var ahead = RaiderAt(nx, ny);
            if (ahead != null && !Flying(ahead) && !Swap(p, r, ahead)) return;
            bool rough = Rough(Rules.Sim.Defense, At(r.X, r.Y), to);
            r.X = FoldX(nx);
            r.Y = FoldY(ny);
            r.Wait = PaceOf(def) - 1 + (rough ? Rules.Sim.Defense.RoughTicks : 0);
            Thorns(r, to);
            if (r.Alive && Rules.AdjectiveOf(r.Kind)?.Lights == true) Apply(r.X, r.Y, "fire", 1, -1, false);
        }

        bool Swap(HoldParams p, Raider r, Raider ahead)
        {
            var route = Rules.Entity(ahead.Kind).Raider?.Route;
            int dir = HoldStep(p, ahead.X, ahead.Y, route != ClimbRoute && route != FlyRoute);
            if (dir < 0 || ahead.X + DirX(dir) != r.X || ahead.Y + DirY(dir) != r.Y) return false;
            ahead.X = r.X;
            ahead.Y = r.Y;
            return true;
        }

        void Batter(HoldParams p, Raider r, EntityDef def, int x, int y, Cell c)
        {
            r.Wait = p.BatterTicks;
            LogOnce("batter#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} batters at the walls");
            if (IsRaisedWall(c))
            {
                Reshape(x, y, -1);
                return;
            }
            int i = Idx(x, y), blows = (battered.TryGetValue(i, out int n) ? n : 0) + 1;
            battered[i] = blows;
            if (blows < Blows(p, c)) return;
            battered.Remove(i);
            string was = TerrainOf(c).Name.ToLowerInvariant();
            if (BreakWall(x, y, c) || SetTerrainTo(x, y, c, p.Rubble)) AddLog($"The {was} gives way", LogKind.Notable);
        }

        int Blows(HoldParams p, Cell c) =>
            HasTag(c, "blocking") && p.Strength.TryGetValue(c.T, out int blows) ? blows : 0;

        void WinRun()
        {
            EarnPoints(Rules.Sim.Hold, Hold.Wave);
            Hold.Phase = HoldPhase.Won;
            Hold.ToSpawn = 0;
            AddLog($"The keep holds! All {Hold.Wave} waves beaten", LogKind.Notable);
            MendCastle();
        }

        void EndRun()
        {
            EarnPoints(Rules.Sim.Hold, Hold.Wave - 1);
            Hold.Phase = HoldPhase.Lost;
            Hold.Health = 0;
            Hold.ToSpawn = 0;
            ClearRaiders();
            AddLog($"The keep falls in wave {Hold.Wave}", LogKind.Notable);
            MendCastle();
        }

        void MendCastle()
        {
            battered.Clear();
            var row = BuildingRow.Read(HoldSite.Castle, HoldSite.Row, Rules.Buildings);
            var touched = new SortedSet<(int y, int x)>();
            foreach (var cell in BuildingRaster.Cells(row, Rules.Buildings))
            {
                var c = At(cell.X, cell.Y);
                if (c == null || (c.T == cell.Terrain && c.Fire == 0 && c.Cover == null)) continue;
                c.Reset(cell.Terrain);
                c.ForgetDef();
                touched.Add(ChunkKey(Idx(cell.X, cell.Y)));
            }
            foreach (var key in touched) FreshenBuilt(key.x, key.y);
        }

        int HoldStepCost(HoldParams p, Cell from, Cell to, int toIdx, bool road)
        {
            if (to == null) return -1;
            int blows = Blows(p, to);
            if (blows > 0) return blows * p.BatterCost;
            if (HasTag(to, "blocking")) return -1;
            var d = Rules.Sim.Defense;
            if (IsRaisedWall(to)) return d.WallCost;
            int cost = Rough(d, from, to) ? d.RoughCost : 1;
            return road && !holdRoad.Contains(toIdx) ? cost * p.OffRoadCost : cost;
        }

        public (int x, int y)? HoldNext(int x, int y, bool road)
        {
            int dir = roadWay.Length == 0 ? -1 : HoldStep(Rules.Sim.Hold, x, y, road);
            return dir < 0 ? ((int, int)?)null : (x + DirX(dir), y + DirY(dir));
        }

        void WorkOutWays(HoldParams p)
        {
            HoldCostField(p, true);
            roadWay = (int[])pathCost.Clone();
            HoldCostField(p, false);
            straightWay = (int[])pathCost.Clone();
        }

        void HoldCostField(HoldParams p, bool road)
        {
            var d = holdField;
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
                if (cost > pathCost[i]) continue;
                int x = d.X0 + i % w, y = d.Y0 + i / w;
                var here = At(x, y);
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = x + DirX(dir), ny = y + DirY(dir);
                    if (!InField(d, nx, ny)) continue;
                    int step = HoldStepCost(p, At(nx, ny), here, Idx(x, y), road);
                    if (step < 0) continue;
                    int ni = FieldIndex(d, nx, ny), total = cost + step;
                    if (total >= pathCost[ni]) continue;
                    pathCost[ni] = total;
                    HeapPush(ni, n);
                }
            }
        }

        int HoldStep(HoldParams p, int x, int y, bool road)
        {
            var d = holdField;
            if (!InField(d, x, y)) return -1;
            var here = At(x, y);
            int best = -1;
            long bestCost = long.MaxValue;
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + DirX(dir), ny = y + DirY(dir);
                if (!InField(d, nx, ny)) continue;
                int on = (road ? roadWay : straightWay)[FieldIndex(d, nx, ny)];
                if (on == Unreached) continue;
                int step = HoldStepCost(p, here, At(nx, ny), Idx(nx, ny), road);
                if (step < 0 || (long)on + step >= bestCost) continue;
                bestCost = (long)on + step;
                best = dir;
            }
            return best;
        }

        public string HoldText()
        {
            if (Hold == null && holdPlate == 0) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("|H").Append(holdPlate.ToString(inv));
            if (Hold != null) sb.Append(':').Append((int)Hold.Phase).Append(':').Append(Hold.Wave).Append(':').Append(Hold.Health).Append(':').Append(Hold.ToSpawn).Append(':').Append(Hold.NextSpawn).Append(':').Append(Hold.Spawned).Append(':').Append(Hold.Peers).Append(':').Append(Hold.InWave).Append(':').Append(Hold.Theme).Append(':').Append(Hold.Moment);
            if (Hold != null && Hold.NextWave != 0) sb.Append(":n").Append(Hold.NextWave);
            foreach (var b in battered) sb.Append("|b").Append(b.Key).Append(':').Append(b.Value);
            foreach (var r in raiders) sb.Append("|r").Append(r.Id).Append(':').Append(r.Kind).Append(':').Append(r.X).Append(':').Append(r.Y).Append(':').Append(r.Hp).Append(':').Append(r.Wait).Append(':').Append(r.StatusOf(LandedStatus));
            return sb.ToString();
        }

        JObject SaveHold()
        {
            var save = new JObject { ["plate"] = holdPlate, ["battered"] = new JArray(battered.Select(b => new JArray(b.Key, b.Value))) };
            if (Hold != null) save["at"] = new JArray((int)Hold.Phase, Hold.Wave, Hold.Health, Hold.ToSpawn, Hold.NextSpawn, Hold.Spawned, Hold.Peers, Hold.InWave, Hold.Theme, Hold.Moment, Hold.MomentTick, Hold.NextWave);
            if (Hold != null && Hold.Wizards.Count > 0) save["wizards"] = SaveWizards(Hold);
            return save;
        }

        void LoadHold(JToken s)
        {
            if (!(s is JObject o)) return;
            holdPlate = (int)o["plate"];
            if (o["at"] is JArray at) Hold = new HoldState { Phase = (HoldPhase)(int)at[0], Wave = (int)at[1], Health = (int)at[2], ToSpawn = (int)at[3], NextSpawn = (int)at[4], Spawned = (int)at[5], Peers = (int)at[6], InWave = (int)at[7], Theme = (string)at[8], Moment = (string)at[9], MomentTick = (int)at[10], NextWave = at.Count > 11 ? (int)at[11] : 0 };
            if (Hold != null) LoadWizards(Hold, o["wizards"]);
            foreach (var b in o["battered"] ?? new JArray()) battered[(int)b[0]] = (int)b[1];
        }
    }
}
