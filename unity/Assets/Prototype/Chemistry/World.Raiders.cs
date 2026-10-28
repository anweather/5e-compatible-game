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
        // Reused by TickRaiders so a tick allocates little.
        readonly List<Raider> raiderScratch = new List<Raider>();
        readonly List<string> statusScratch = new List<string>();

        /// <summary>The raiders on the board, oldest first (the sandbox's entities, for the defense field).</summary>
        public IReadOnlyList<Raider> Raiders => raiders;

        /// <summary>The next entity id (the sandbox's nextId).</summary>
        public int NextEntityId { get; private set; } = 1;

        /// <summary>The raider standing on a square, or null (entityAt in the TS).</summary>
        public Raider RaiderAt(int x, int y)
        {
            foreach (var r in raiders) if (r.X == x && r.Y == y) return r;
            return null;
        }

        /// <summary>
        /// Puts a raider of a kind on a square with the given hit points (spawn in the TS, minus its
        /// blocking check: the gate is never refused). Null if the kind isn't an entity or someone
        /// stands there.
        /// </summary>
        public Raider SpawnRaider(string kind, int x, int y, int hp)
        {
            if (!InBounds(x, y) || RaiderAt(x, y) != null || Rules.Entity(kind) == null) return null;
            var r = new Raider { Id = NextEntityId++, Kind = kind, X = x, Y = y, Hp = hp, MaxHp = hp };
            raiders.Add(r);
            return r;
        }

        /// <summary>
        /// An element arriving at a raider (applyEntityElement in the TS). Returns the damage
        /// multiplier its state adds (wet + lightning = 2).
        /// </summary>
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

        /// <summary>
        /// A status's ticks, but burning on a thing of a flammable material (a wooden crate) catches only
        /// with the material's flammability and lasts its fuel; 0 when it didn't catch. (The TS's
        /// materialBurn.)
        /// </summary>
        int MaterialBurn(EntityDef def, string status, int ticks)
        {
            var m = def.MaterialDef;
            if (status != "burning" || m == null || !m.Has("flammable")) return ticks;
            if (Rand() >= m.Flammability) return 0;
            return m.Fuel ?? ticks;
        }

        static bool Holds(EntityCond cond, Raider r, EntityDef def)
        {
            if (cond == null) return true;
            if (cond.Status != null && r.StatusOf(cond.Status) <= 0) return false;
            return def.Components.HasAll(cond.Has) && !def.Components.HasAny(cond.Lacks);
        }

        // Sets and clears the effect's statuses; returns its damage multiplier (1 for none).
        int RunEntityEffect(Raider r, EntityDef def, EntityEffect eff)
        {
            foreach (var kv in eff.Status)
            {
                int ticks = MaterialBurn(def, kv.Key, kv.Value);
                if (ticks > 0) r.Status[kv.Key] = Math.Max(r.StatusOf(kv.Key), ticks);
            }
            if (eff.Clear != null) r.Status.Remove(eff.Clear);
            return eff.DamageMul != 0 ? eff.DamageMul : 1;
        }

        /// <summary>
        /// Hurts a raider (damage in the TS): <paramref name="element"/> (or null) doubles the damage
        /// if it's weak to it and halves it (rounded up) if it resists. At 0 HP it's defeated.
        /// </summary>
        void DamageRaider(Raider r, int amount, string cause, string element)
        {
            if (amount <= 0 || !r.Alive) return;
            var def = Rules.Entity(r.Kind);
            if (def.Hp >= 999) return;
            amount = Scaled(def, element, amount);
            r.Hp -= amount;
            if (def.Creature)
                LogOnce($"hurt#{r.Id}#{cause}", $"{def.Name} takes {amount} {cause} damage ({Math.Max(0, r.Hp)}/{def.Hp} HP)", LogKind.Hurt);
            if (r.Hp <= 0) DefeatRaider(r);
        }

        /// <summary>Damage after a raider's weaknesses and resistances (scaleDamage in the TS).</summary>
        static int Scaled(EntityDef def, string element, int amount)
        {
            var raider = def.Raider;
            if (raider == null || element == null) return amount;
            if (Contains(raider.Weak, element)) return amount * 2;
            if (Contains(raider.Resist, element)) return amount - amount / 2;
            return amount;
        }

        void DefeatRaider(Raider r)
        {
            if (!raiders.Remove(r)) return;
            r.Alive = false;
            AddLog($"{Rules.Entity(r.Kind).Name} is defeated", LogKind.Hurt);
        }

        /// <summary>Takes every raider off the board (a lost defense, a restart), without a word.</summary>
        void ClearRaiders()
        {
            foreach (var r in raiders) r.Alive = false;
            raiders.Clear();
        }

        /// <summary>
        /// A spell landing (landSpell in the TS): a shoving spell's damage hits the raiders in its
        /// squares first, furthest first (they stand their ground); the rest hurt the creature in
        /// each square after their element lands there (see LandOnRaider).
        /// </summary>
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
                int da = (a.X - x) * (a.X - x) + (a.Y - y) * (a.Y - y), db = (b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y);
                return db != da ? db.CompareTo(da) : a.Id.CompareTo(b.Id);
            });
            foreach (var r in hit)
                if (s.Damage.Length == 2) DamageRaider(r, Roll(s.Damage), s.Name.ToLowerInvariant(), s.Element);
        }

        /// <summary>A non-shoving damage spell's hit on the creature in one square, after its element landed there.</summary>
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

        /// <summary>[n, sides] dice with the world's generator (roll in the TS).</summary>
        int Roll(int[] dice)
        {
            int total = 0;
            for (int i = 0; i < dice[0]; i++) total += 1 + (int)Math.Floor(Rand() * dice[1]);
            return total;
        }

        /// <summary>
        /// A creature stepping onto a cover that hurts (thorns) takes its damage, and one that slows is
        /// snagged for the rest of the step's time (thorns in the TS).
        /// </summary>
        void Thorns(Raider r, Cell c)
        {
            var cv = CoverOf(c);
            if (cv == null || !Rules.Entity(r.Kind).Creature) return;
            if (cv.Damage.Length == 2) DamageRaider(r, Roll(cv.Damage), cv.Name.ToLowerInvariant(), null);
            double speed = cv.Speed;
            if (speed > 0 && speed < 1 && r.Alive)
                r.Status["snagged"] = Math.Max(r.StatusOf("snagged"), (int)Math.Floor(Rules.Sim.TicksPerSecond * (1 / speed - 1) + 0.5));
        }

        /// <summary>Per-tick raider upkeep (tickEntities in the TS): statuses, burning, wading, zaps, falls.</summary>
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

        // What the raider's square does to it this tick: fire and water reach it, burning and steam hurt, a shock zaps.
        void Hazards(Raider r, Cell c, EntityDef def)
        {
            var s = Rules.Sim;
            if (c.Fire > 0) ApplyRaiderElement(r, "fire", false);
            if (c.Water > 0 && !c.Frozen) ApplyRaiderElement(r, "water", false);
            if (r.StatusOf("burning") > 0 && Tick % s.BurnDamageEvery == 0)
            {
                DamageRaider(r, 1, "fire", "fire");
                if (def.Flammable && r.Alive) Apply(r.X, r.Y, "fire", 1, -1, false);
            }
            if (c.Gas == "steam" && c.GasAmt >= 2 && def.Creature && Tick % s.ScaldEvery == 0)
                DamageRaider(r, Rules.Gases.TryGetValue("steam", out var steam) ? steam.Scald : 1, "scalding", null);
            if (c.Charge == 3 && r.Alive)
            {
                int mul = ApplyRaiderElement(r, "lightning", false);
                DamageRaider(r, s.ZapDamage * mul, "lightning", "lightning");
            }
        }

        // Into a dry pit: hurt once, until it's out again.
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

        // Every status but a fall ticks down and goes at 0 (in name order: the order only matters to the log).
        void CountDown(Raider r)
        {
            statusScratch.Clear();
            statusScratch.AddRange(r.Status.Keys);
            statusScratch.Sort(StringComparer.Ordinal);
            foreach (var k in statusScratch)
            {
                if (k == "fallen") continue;
                int left = r.Status[k] - 1;
                if (left <= 0) r.Status.Remove(k);
                else r.Status[k] = left;
            }
        }

        /// <summary>After a rule switch: raiders whose kind the rules no longer define go (sanitizeWorld in the TS).</summary>
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
