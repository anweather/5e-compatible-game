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
    }

    /// <summary>A run: how it stands.</summary>
    public sealed class HoldState
    {
        public HoldPhase Phase;
        /// <summary>The wave under way (the score, once it's lost), the keep's health, raiders of this wave still to come, the tick the next comes on, and how many have come in all.</summary>
        public int Wave, Health, ToSpawn, NextSpawn, Spawned;
    }

    public sealed partial class World
    {
        // Where a raider's way may run, as the defense field's box; and blows taken by each cell, by index.
        DefenseState holdField;
        readonly SortedDictionary<int, int> battered = new SortedDictionary<int, int>();
        int holdPlate;

        /// <summary>The map's Hold, or null: a world without one has no runs.</summary>
        public WorldHold HoldSite { get; private set; }

        /// <summary>The run under way or last lost, or null before any.</summary>
        public HoldState Hold { get; private set; }

        /// <summary>Whether a run is under way.</summary>
        public bool HoldRunning => Hold != null && Hold.Phase == HoldPhase.Running;

        /// <summary>Raiders still to beat in the wave under way.</summary>
        public int HoldLeft => Hold == null ? 0 : raiders.Count + Hold.ToSpawn;

        /// <summary>Gives the world its map's Hold, before the first tick.</summary>
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
        }

        /// <summary>A raider wave's size: First, and Growth more for each wave after.</summary>
        public static int HoldWaveSize(HoldParams p, int wave) => p.First + p.Growth * (wave - 1);

        /// <summary>Where a downed wizard gets up during a run, or null: then it's the yard, as ever.</summary>
        public (int x, int y)? HoldRespawn() => HoldRunning ? HoldSite.Rise : ((int x, int y)?)null;

        /// <summary>How many blows this cell has taken in the run.</summary>
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

        // ---------- starting ----------

        void WatchPlate()
        {
            var p = Rules.Sim.Hold;
            bool on = stands.Values.Any(s => !s.Down && s.X == HoldSite.Plate.x && s.Y == HoldSite.Plate.y);
            holdPlate = on ? holdPlate + 1 : 0;
            if (holdPlate < p.PlateTicks || (Defense != null && Defense.Phase == DefensePhase.Running)) return;
            holdPlate = 0;
            ClearRaiders();
            Hold = new HoldState { Phase = HoldPhase.Running, Wave = 1, Health = p.Health, ToSpawn = HoldWaveSize(p, 1), NextSpawn = Tick + p.Warmup };
            AddLog("Wave 1 is coming! Hold the keep", LogKind.Notable);
        }

        // ---------- waves ----------

        void SpawnHold(HoldParams p)
        {
            var h = Hold;
            if (h.ToSpawn <= 0 || Tick < h.NextSpawn) return;
            var spawner = HoldSite.Spawners[h.Spawned % HoldSite.Spawners.Count];
            string plain = spawner.Kinds.Count > 0 ? spawner.Kinds[h.Spawned / HoldSite.Spawners.Count % spawner.Kinds.Count] : null;
            if (Rules.Entity(plain) == null)
            {
                h.ToSpawn = 0;
                return;
            }
            string kind = WithAdjective(p, plain, h.Wave);
            var def = Rules.Entity(kind);
            var (x, y) = spawner.Cells[(int)(Rand() * spawner.Cells.Count)];
            var r = SpawnRaider(kind, x, y, def.Hp + p.HpPerWave * (h.Wave - 1));
            if (r == null) return; // someone stands there: next tick
            r.Wait = PaceOf(def) - 1;
            h.ToSpawn--;
            h.Spawned++;
            h.NextSpawn = Tick + p.SpawnEvery;
        }

        string WithAdjective(HoldParams p, string plain, int wave)
        {
            if (wave < p.AdjectiveFrom || p.Adjectives.Count == 0 || (int)(Rand() * 100) >= p.AdjectivePercent) return plain;
            return plain + Rules.AdjectiveMark + p.Adjectives[(int)(Rand() * p.Adjectives.Count)].Id;
        }

        void NextHoldWave(HoldParams p)
        {
            var h = Hold;
            if (h.ToSpawn > 0 || raiders.Count > 0) return;
            h.Wave++;
            h.ToSpawn = HoldWaveSize(p, h.Wave);
            h.NextSpawn = Tick + p.BetweenWaves;
            AddLog($"Wave {h.Wave} is coming!", LogKind.Notable);
        }

        // ---------- what a raider does ----------

        void MarchHold(HoldParams p)
        {
            if (raiders.Count == 0) return;
            HoldCostField(p);
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
                if (!BlowAtWizard(r, def) && !BlowAtHeart(r, def)) StepHold(p, r, def);
                if (!HoldRunning) return;
            }
        }

        // What an adjective keeps up: a status it never loses.
        void Upkeep(Raider r)
        {
            string keeps = Rules.AdjectiveOf(r.Kind)?.Keeps;
            if (keeps != null) r.Status[keeps] = Math.Max(r.StatusOf(keeps), 2);
        }

        static bool InReach(int x0, int y0, int x1, int y1, int reach) => Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1)) <= reach;

        // The 5E attack roll a punch uses: a d20, the Strength modifier and the proficiency bonus against the wizard's AC, then its dice.
        bool BlowAtWizard(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2) return false;
            var s = Rules.Sim.Strike;
            var foe = stands.Values.FirstOrDefault(w => !w.Down && InReach(r.X, r.Y, w.X, w.Y, s.Reach) && !IsBlocking(At(w.X, w.Y)));
            if (foe == null) return false;
            var wizard = Rules.Entity(WizardEntity);
            int roll = 1 + (int)(Rand() * 20), total = roll + Modifier(def.Str) + s.Proficiency;
            bool hit = roll == 20 || (roll != 1 && total >= (wizard != null ? wizard.Ac : EntityDef.DefaultScore));
            var result = new StrikeHit { Tick = Tick, Attacker = def.Name, TargetPlayer = foe.Player, Roll = roll, Total = total, Hit = hit, ToX = foe.X, ToY = foe.Y };
            strikeHits.Add(result);
            r.Wait = def.Raider.AttackEvery;
            if (!hit) return true;
            result.Damage = Math.Max(1, Roll(dice) + Modifier(def.Str));
            foe.Hp = Math.Max(0, foe.Hp - result.Damage);
            AddLog($"The {def.Name.ToLowerInvariant()} hits {foe.Player}: {result.Damage} damage", LogKind.Hurt);
            if (!foe.Down) return true;
            result.Downed = true;
            AddLog($"{foe.Player} is downed", LogKind.Hurt);
            return true;
        }

        bool BlowAtHeart(Raider r, EntityDef def)
        {
            var dice = def.Raider?.AttackDice;
            if (dice == null || dice.Length != 2 || !InReach(r.X, r.Y, HoldSite.Heart.x, HoldSite.Heart.y, 1)) return false;
            r.Wait = def.Raider.AttackEvery;
            Hold.Health -= Roll(dice);
            LogOnce("heart#" + r.Id.ToString(CultureInfo.InvariantCulture), $"The {def.Name.ToLowerInvariant()} is at the keep!", LogKind.Hurt);
            if (Hold.Health <= 0) EndRun();
            return true;
        }

        void StepHold(HoldParams p, Raider r, EntityDef def)
        {
            int dir = HoldStep(p, r.X, r.Y);
            if (dir < 0) return;
            int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
            var to = At(nx, ny);
            if (Blows(p, to) > 0 || IsRaisedWall(to))
            {
                Batter(p, r, def, nx, ny, to);
                return;
            }
            if (RaiderAt(nx, ny) != null) return; // wait for the one ahead
            bool rough = Rough(Rules.Sim.Defense, At(r.X, r.Y), to);
            r.X = nx;
            r.Y = ny;
            r.Wait = PaceOf(def) - 1 + (rough ? Rules.Sim.Defense.RoughTicks : 0);
            Thorns(r, to);
            if (r.Alive && Rules.AdjectiveOf(r.Kind)?.Lights == true) Apply(r.X, r.Y, "fire", 1, -1, false);
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
            if (BreakWall(x, y, c)) AddLog($"The {was} gives way", LogKind.Notable);
        }

        // How many blows a blocking cell takes; 0 for anything else, or one that can't be battered.
        int Blows(HoldParams p, Cell c) =>
            HasTag(c, "blocking") && TerrainOf(c).BreaksTo != null && p.Strength.TryGetValue(c.T, out int blows) ? blows : 0;

        // ---------- the end ----------

        void EndRun()
        {
            Hold.Phase = HoldPhase.Lost;
            Hold.Health = 0;
            Hold.ToSpawn = 0;
            ClearRaiders();
            AddLog($"The keep falls in wave {Hold.Wave}", LogKind.Notable);
            MendCastle();
        }

        // The castle as the map has it: every cell that isn't, laid fresh, and the view told (ChunkFreshened).
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

        // ---------- the way to the heart ----------

        int HoldStepCost(HoldParams p, Cell from, Cell to)
        {
            if (to == null) return -1;
            int blows = Blows(p, to);
            if (blows > 0) return blows * p.BatterCost;
            if (HasTag(to, "blocking")) return -1;
            var d = Rules.Sim.Defense;
            if (IsRaisedWall(to)) return d.WallCost;
            return Rough(d, from, to) ? d.RoughCost : 1;
        }

        void HoldCostField(HoldParams p)
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
                    int step = HoldStepCost(p, At(nx, ny), here);
                    if (step < 0) continue;
                    int ni = FieldIndex(d, nx, ny), total = cost + step;
                    if (total >= pathCost[ni]) continue;
                    pathCost[ni] = total;
                    HeapPush(ni, n);
                }
            }
        }

        int HoldStep(HoldParams p, int x, int y)
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
                int on = pathCost[FieldIndex(d, nx, ny)];
                if (on == Unreached) continue;
                int step = HoldStepCost(p, here, At(nx, ny));
                if (step < 0 || (long)on + step >= bestCost) continue;
                bestCost = (long)on + step;
                best = dir;
            }
            return best;
        }

        // ---------- the checksum and the save ----------

        /// <summary>A run and its raiders as text, "" before any run and with no one on the plate, so a world without hashes as it did.</summary>
        public string HoldText()
        {
            if (Hold == null && holdPlate == 0) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("|H").Append(holdPlate.ToString(inv));
            if (Hold != null) sb.Append(':').Append((int)Hold.Phase).Append(':').Append(Hold.Wave).Append(':').Append(Hold.Health).Append(':').Append(Hold.ToSpawn).Append(':').Append(Hold.NextSpawn).Append(':').Append(Hold.Spawned);
            foreach (var b in battered) sb.Append("|b").Append(b.Key).Append(':').Append(b.Value);
            foreach (var r in raiders) sb.Append("|r").Append(r.Id).Append(':').Append(r.Kind).Append(':').Append(r.X).Append(':').Append(r.Y).Append(':').Append(r.Hp).Append(':').Append(r.Wait);
            return sb.ToString();
        }

        JObject SaveHold()
        {
            var save = new JObject { ["plate"] = holdPlate, ["battered"] = new JArray(battered.Select(b => new JArray(b.Key, b.Value))) };
            if (Hold != null) save["at"] = new JArray((int)Hold.Phase, Hold.Wave, Hold.Health, Hold.ToSpawn, Hold.NextSpawn, Hold.Spawned);
            return save;
        }

        void LoadHold(JToken s)
        {
            if (!(s is JObject o)) return;
            holdPlate = (int)o["plate"];
            if (o["at"] is JArray at) Hold = new HoldState { Phase = (HoldPhase)(int)at[0], Wave = (int)at[1], Health = (int)at[2], ToSpawn = (int)at[3], NextSpawn = (int)at[4], Spawned = (int)at[5] };
            foreach (var b in o["battered"] ?? new JArray()) battered[(int)b[0]] = (int)b[1];
        }
    }
}
