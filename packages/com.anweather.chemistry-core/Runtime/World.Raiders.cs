// Raiders as entities in the core: the parts of sandbox/src/entities.ts and
// react.ts (applyEntityElement) that the defense field's raiders need, line
// for line: elements arriving at a raider (reactions.json "entity"), damage
// (with the raider's weaknesses and resistances), death, and the per-tick
// upkeep (burning, soaking, shocks, scalding, pits, statuses). Spells reach
// raiders through Land and Apply, as the sandbox's landSpell and applyElement
// reach its entities. Raiders are planted: a spell's shove doesn't move them
// (push in entities.ts leaves them be too).
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        readonly List<Raider> raiders = new List<Raider>();
        readonly List<Raider> raiderScratch = new List<Raider>();
        readonly List<string> statusScratch = new List<string>();

        public IReadOnlyList<Raider> Raiders => raiders;

        public int NextEntityId { get; private set; } = 1;

        public Raider RaiderAt(int x, int y)
        {
            x = FoldX(x);
            y = FoldY(y);
            foreach (var r in raiders) if (r.X == x && r.Y == y) return r;
            return null;
        }

        public Raider SpawnRaider(string kind, int x, int y, int hp)
        {
            if (!InBounds(x, y) || RaiderAt(x, y) != null || Rules.Entity(kind) == null) return null;
            var r = new Raider { Id = NextEntityId++, Kind = kind, X = FoldX(x), Y = y, Hp = hp, MaxHp = hp };
            raiders.Add(r);
            return r;
        }

        int ApplyRaiderElement(Raider r, string element, bool direct)
        {
            var def = Rules.Entity(r.Kind);
            int mul = 1;
            foreach (var rule in Rules.EntityRulesFor(element))
            {
                if (!Holds(rule.If, r, def)) continue;
                foreach (var eff in rule.Do) mul *= RunEntityEffect(r, def, eff);
                if (rule.Log != null && (direct || rule.Notable))
                    LogOnce(rule.Id + "#" + r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ReplaceFirst(rule.Log, "{name}", def.Name), rule.Notable ? LogKind.Notable : LogKind.Info);
                if (rule.Stop) break;
            }
            return mul;
        }

        int MaterialBurn(Raider r, EntityDef def, string status, int ticks)
        {
            var m = def.MaterialDef;
            if (status != "burning" || m == null || !m.Burns) return ticks;
            double dryness = r.StatusOf("wet") > 0 ? 1 - m.Dampens : 1;
            if (Rand() >= m.Flammability * dryness) return 0;
            return DampFuel(m.Fuel ?? ticks, dryness);
        }

        static void MaterialStatus(Raider r, EntityDef def, string verb)
        {
            int ticks = def.MaterialDef?.StatusTicks(verb) ?? 0;
            var status = TerrainDef.StatusOfVerb(verb);
            if (ticks > 0 && status != null) r.Status[status] = Math.Max(r.StatusOf(status), ticks);
        }

        static bool Holds(EntityCond cond, Raider r, EntityDef def)
        {
            if (cond == null) return true;
            if (cond.Status != null && r.StatusOf(cond.Status) <= 0) return false;
            if (cond.Verb != null && (def.MaterialDef?.StatusTicks(cond.Verb) ?? 0) <= 0) return false;
            return def.Components.HasAll(cond.Has) && !def.Components.HasAny(cond.Lacks);
        }

        int RunEntityEffect(Raider r, EntityDef def, EntityEffect eff)
        {
            foreach (var kv in eff.Status)
            {
                int ticks = MaterialBurn(r, def, kv.Key, kv.Value);
                if (ticks > 0) r.Status[kv.Key] = Math.Max(r.StatusOf(kv.Key), ticks);
            }
            if (eff.StatusOf != null) MaterialStatus(r, def, eff.StatusOf);
            if (eff.Clear != null) r.Status.Remove(eff.Clear);
            return eff.DamageMul != 0 ? eff.DamageMul : 1;
        }

        void DamageRaider(Raider r, int amount, string cause, string element)
        {
            amount = amount * castPower / 100;
            if (amount <= 0 || !r.Alive) return;
            var def = Rules.Entity(r.Kind);
            if (def.Hp >= 999) return;
            amount = Scaled(def, element, amount);
            var sim = Rules.Sim;
            if (sim.ShatterWhen != null && r.StatusOf(sim.ShatterWhen) > 0 && (sim.ShatterNotBy == null || element != sim.ShatterNotBy))
            {
                if (CracksFirst(r, sim.ShatterWhen)) return;
                r.Shattered = true;
                DefeatRaider(r, "shatters");
                return;
            }
            r.Hp -= amount;
            if (def.Creature)
                LogOnce($"hurt#{r.Id}#{cause}", $"{def.Name} takes {amount} {cause} damage ({Math.Max(0, r.Hp)}/{def.Hp} HP)", LogKind.Hurt);
            if (r.Hp <= 0) DefeatRaider(r);
        }

        bool CracksFirst(Raider r, string frozen)
        {
            if (r.StatusOf(CrackedStatus) > 0 || !(FrozenWater() is TerrainDef ice) || !ice.Cracks) return false;
            r.Status[CrackedStatus] = r.StatusOf(frozen);
            LogOnce($"cracked#{r.Id}", $"{Rules.Entity(r.Kind).Name} cracks", LogKind.Notable);
            return true;
        }

        static int Scaled(EntityDef def, string element, int amount)
        {
            var raider = def.Raider;
            if (raider == null || element == null) return amount;
            if (Contains(raider.Weak, element)) return amount * 2;
            if (Contains(raider.Resist, element)) return amount - amount / 2;
            return amount;
        }

        void DefeatRaider(Raider r, string how = "is defeated")
        {
            if (!raiders.Remove(r)) return;
            r.Alive = false;
            AddLog($"{Rules.Entity(r.Kind).Name} {how}", LogKind.Hurt);
        }

        void ClearRaiders()
        {
            foreach (var r in raiders) r.Alive = false;
            raiders.Clear();
        }

        void BlastRaiders(SpellDef s, List<(int x, int y)> cells, int x, int y)
        {
            if (s.Push <= 0 || raiders.Count == 0) return;
            var hit = new List<Raider>();
            foreach (var (cx, cy) in cells)
            {
                var r = RaiderAt(cx, cy);
                if (r != null) hit.Add(r);
            }
            hit.Sort((a, b) =>
            {
                int ax = DeltaX(x, a.X), bx = DeltaX(x, b.X), ay = DeltaY(y, a.Y), by = DeltaY(y, b.Y);
                int da = ax * ax + ay * ay, db = bx * bx + by * by;
                return db != da ? db.CompareTo(da) : a.Id.CompareTo(b.Id);
            });
            foreach (var r in hit)
                if (s.Damage.Length == 2) DamageRaider(r, Roll(s.Damage), s.Name.ToLowerInvariant(), s.Element);
        }

        void LandOnRaider(SpellDef s, int cx, int cy)
        {
            if (s.Push > 0 || s.Damage.Length != 2) return;
            var r = RaiderAt(cx, cy);
            if (r == null) return;
            var def = Rules.Entity(r.Kind);
            if (def.Creature)
            {
                int mul = s.Element == "lightning" ? ApplyRaiderElement(r, "lightning", false) : 1;
                DamageRaider(r, Roll(s.Damage) * mul, s.Name.ToLowerInvariant(), s.Element);
            }
            else if (s.Element == "fire" && def.Flammable)
            {
                DamageRaider(r, Roll(s.Damage), "fire", "fire");
            }
        }

        static int AwayDir(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return 0;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx > 0 ? 1 : 3;
            return dy > 0 ? 2 : 0;
        }

        void ShoveRaider(Raider r, int dir, int force)
        {
            int dist = force - Rules.Entity(r.Kind).Mass + 1;
            for (int k = 0; k < dist; k++)
            {
                int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) return;
                r.X = FoldX(nx);
                r.Y = FoldY(ny);
            }
        }

        void Force(SpellDef s, int x, int y, int dir, HashSet<int> once = null, int charge = 0)
        {
            if (s.ForcePush <= 0 && s.ForceThrow <= 0) return;
            HitBall(s, x, y, dir);
            var r = RaiderAt(x, y);
            if (r == null || !r.Alive || (once != null && !once.Add(r.Id))) return;
            var def = Rules.Entity(r.Kind);
            string by = s.Name.ToLowerInvariant();
            if (charge > 0 && s.ForcePush > 0 && dir >= 0 && def.Mass <= s.MaxMass) CastShove(s, r, def, dir, charge, by);
            else if (s.ForcePush > 0 && dir >= 0 && def.Mass <= s.MaxMass)
            {
                for (int k = 0; k < s.ForcePush; k++)
                {
                    int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                    if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) break;
                    r.X = FoldX(nx);
                    r.Y = FoldY(ny);
                }
                LogOnce($"pushed#{r.Id}", $"The {by} knocks {def.Name} back", LogKind.Notable);
            }
            if (s.ForceThrow <= 0) return;
            r.Status[ThrownStatus] = Math.Max(r.StatusOf(ThrownStatus), s.ForceThrow);
            LogOnce($"thrown#{r.Id}", $"The {by} throws {def.Name} off its feet", LogKind.Notable);
        }

        public const string ShovedStatus = "shoved";

        void CastShove(SpellDef s, Raider r, EntityDef def, int dir, int charge, string by)
        {
            int shoved = r.StatusOf(ShovedStatus);
            if (shoved >= Rules.Sim.MostShoves)
            {
                LogOnce($"braced#{r.Id}", $"The {def.Name.ToLowerInvariant()} braces against the {by}", LogKind.Notable);
                return;
            }
            r.Status[ShovedStatus] = shoved + 1;
            int cells = s.ForcePush * (ClampCharge(charge) + 1);
            if (shoved > 0) cells = (cells + 1) / 2;
            Hurl(r, def, dir, cells, by);
            if (r.Alive) r.Status[ThrownStatus] = Math.Max(r.StatusOf(ThrownStatus), Rules.Sim.ShoveStaggerTicks);
            LogOnce($"pushed#{r.Id}", $"The {by} knocks {def.Name} back", LogKind.Notable);
        }

        void Hurl(Raider r, EntityDef def, int dir, int cells, string by)
        {
            int from = GroundUnits(r.X, r.Y), step = Rules.Sim.WaterUnitsPerStep;
            for (int k = 0; k < cells; k++)
            {
                int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) return;
                bool over = GroundUnits(r.X, r.Y) - GroundUnits(nx, ny) >= Rules.Sim.CliffSteps * step;
                r.X = FoldX(nx);
                r.Y = FoldY(ny);
                if (!over) continue;
                int dropped = (from - GroundUnits(nx, ny)) / step;
                AddLog($"The {by} throws {def.Name} over the edge", LogKind.Notable);
                DamageRaider(r, Rules.Sim.FallDamage * Math.Max(1, dropped / Rules.Sim.FallStepsPerHurt), "the fall", null);
                return;
            }
        }

        int Roll(int[] dice)
        {
            int total = 0;
            for (int i = 0; i < dice[0]; i++) total += 1 + (int)Math.Floor(Rand() * dice[1]);
            return total;
        }

        void Thorns(Raider r, Cell c)
        {
            if (!Rules.Entity(r.Kind).Creature) return;
            var cv = CoverOf(c);
            if (cv != null && cv.Damage.Length == 2) DamageRaider(r, Roll(cv.Damage), cv.Name.ToLowerInvariant(), null);
            double speed = WalkSpeed(c);
            if (speed > 0 && speed < 1 && r.Alive)
                r.Status["snagged"] = Math.Max(r.StatusOf("snagged"), (int)Math.Floor(Rules.Sim.TicksPerSecond * (1 / speed - 1) + 0.5));
        }

        void TickRaiders()
        {
            if (raiders.Count == 0) return;
            raiderScratch.Clear();
            raiderScratch.AddRange(raiders);
            foreach (var r in raiderScratch)
            {
                if (!r.Alive) continue;
                var def = Rules.Entity(r.Kind);
                var c = At(r.X, r.Y);
                Hazards(r, c, def);
                if (!r.Alive) continue;
                Fall(r, c, def);
                CountDown(r);
            }
        }

        void HeatHazards(Raider r, Cell c, EntityDef def)
        {
            var s = Rules.Sim;
            if (r.StatusOf("burning") > 0 && Tick % s.BurnDamageEvery == 0)
            {
                DamageRaider(r, 1, "fire", "fire");
                if (def.Flammable && r.Alive) Apply(r.X, r.Y, "fire", 1, -1, false);
            }
            if (c.Heat > 0 && def.Creature && Tick % s.BurnDamageEvery == 0) DamageRaider(r, 1, "hot metal", "fire");
            if (c.Gas != null && c.Gas == Rules.Sim.WaterBoils && c.GasAmt >= 2 && def.MaterialDef?.Has("scalds") == true && Tick % s.ScaldEvery == 0)
                DamageRaider(r, Rules.Gases.TryGetValue(c.Gas, out var steam) ? steam.Scald : 1, "scalding", "fire");
        }

        void Hazards(Raider r, Cell c, EntityDef def)
        {
            var s = Rules.Sim;
            if (c.Fire > 0) ApplyRaiderElement(r, "fire", false);
            if (IsOpenWater(c)) ApplyRaiderElement(r, "water", false);
            HeatHazards(r, c, def);
            if (Shocked(r, c, def) && r.Alive)
            {
                int mul = ApplyRaiderElement(r, "lightning", false);
                DamageRaider(r, s.ZapDamage * mul, "lightning", "lightning");
            }
        }

        bool Shocked(Raider r, Cell c, EntityDef def) => c.Charge == 3 || ShockedFromBeside(r, def);

        bool ShockedFromBeside(Raider r, EntityDef def)
        {
            if (r.StatusOf("wet") == 0 || def.MaterialDef == null || !def.MaterialDef.ChainsWet) return false;
            foreach (var (nx, ny, _) in ActiveNeighbours(r.X, r.Y))
            {
                var n = At(nx, ny);
                if (n.Charge == 3 && IsOpenWater(n)) return true;
            }
            return false;
        }

        void Fall(Raider r, Cell c, EntityDef def)
        {
            bool inPit = HasTag(c, "low") && !(c.Frozen && c.Water > 0);
            if (inPit && c.Water == 0 && r.StatusOf("fallen") == 0)
            {
                r.Status["fallen"] = int.MaxValue;
                AddLog($"{def.Name} falls into the pit", LogKind.Notable);
                DamageRaider(r, Rules.Sim.FallDamage, "falling", null);
            }
            else if (!inPit && r.StatusOf("fallen") != 0)
            {
                r.Status.Remove("fallen");
            }
        }

        void CountDown(Raider r)
        {
            statusScratch.Clear();
            statusScratch.AddRange(r.Status.Keys);
            statusScratch.Sort(StringComparer.Ordinal);
            foreach (var k in statusScratch)
            {
                if (k == "fallen" || k == ShovedStatus) continue;
                int left = r.Status[k] - 1;
                if (left <= 0) r.Status.Remove(k);
                else r.Status[k] = left;
            }
        }

        void SanitizeRaiders()
        {
            for (int i = raiders.Count - 1; i >= 0; i--)
            {
                if (Rules.Entity(raiders[i].Kind) != null) continue;
                raiders[i].Alive = false;
                raiders.RemoveAt(i);
            }
        }
    }
}
