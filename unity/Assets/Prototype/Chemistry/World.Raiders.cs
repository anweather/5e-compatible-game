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
        /// with the material's flammability and lasts its fuel, both less its soak.dampens while the thing
        /// is wet; 0 when it didn't catch. (The TS's materialBurn.)
        /// </summary>
        int MaterialBurn(Raider r, EntityDef def, string status, int ticks)
        {
            var m = def.MaterialDef;
            if (status != "burning" || m == null || !m.Burns) return ticks;
            double dryness = r.StatusOf("wet") > 0 ? 1 - m.Dampens : 1;
            if (Rand() >= m.Flammability * dryness) return 0;
            return DampFuel(m.Fuel ?? ticks, dryness);
        }

        /// <summary>
        /// A statusOf effect (react.ts materialStatus): the status the verb gives (burn burning, freeze frozen,
        /// soak wet), for as many ticks as the thing's material says (flesh burns 9, bone not at all).
        /// </summary>
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

        // Sets and clears the effect's statuses; returns its damage multiplier (1 for none).
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

        /// <summary>
        /// Hurts a raider (damage in the TS): <paramref name="element"/> (or null) doubles the damage
        /// if it's weak to it and halves it (rounded up) if it resists. At 0 HP it's defeated; with the
        /// sim's status.shatter status (frozen), a hurt of any element but its notBy (cold) shatters it; if the
        /// material ice cracks, only the second: the first leaves it cracked for as long as it stays frozen.
        /// </summary>
        void DamageRaider(Raider r, int amount, string cause, string element)
        {
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

        // The first hit on a frozen raider of cracking ice: it cracks, and takes no harm yet.
        bool CracksFirst(Raider r, string frozen)
        {
            if (r.StatusOf(CrackedStatus) > 0 || !Rules.Terrain.TryGetValue(IceMaterial, out var ice) || !ice.Cracks) return false;
            r.Status[CrackedStatus] = r.StatusOf(frozen);
            LogOnce($"cracked#{r.Id}", $"{Rules.Entity(r.Kind).Name} cracks", LogKind.Notable);
            return true;
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

        void DefeatRaider(Raider r, string how = "is defeated")
        {
            if (!raiders.Remove(r)) return;
            r.Alive = false;
            AddLog($"{Rules.Entity(r.Kind).Name} {how}", LogKind.Hurt);
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

        /// <summary>The way from a blast's middle out through (dx, dy) off it, along the longer axis, east or west on a tie; north at the middle (awayDir in the TS).</summary>
        static int AwayDir(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return 0;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx > 0 ? 1 : 3;
            return dy > 0 ? 2 : 0;
        }

        /// <summary>
        /// A blast's shove on a raider (shoveRaider in the TS: a dust flash): force less its mass, plus 1, cells along dir,
        /// stopping at a wall, an inactive chunk or another raider.
        /// </summary>
        void ShoveRaider(Raider r, int dir, int force)
        {
            int dist = force - Rules.Entity(r.Kind).Mass + 1;
            for (int k = 0; k < dist; k++)
            {
                int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) return;
                r.X = nx;
                r.Y = ny;
            }
        }

        /// <summary>
        /// A spell's force on the raider at (x, y) (force in the TS; spells.json force): pushed ForcePush cells along dir
        /// where that's open, if it's MaxMass or lighter (a wave knocks it back), and thrown off its feet for ForceThrow
        /// ticks (a geyser's jet, a boulder), so it can't walk. A cast forces each raider once (<paramref name="once"/>
        /// holds those it has), so one pushed along a gust's line isn't pushed again on the cells it lands on.
        /// </summary>
        void Force(SpellDef s, int x, int y, int dir, HashSet<int> once = null)
        {
            if (s.ForcePush <= 0 && s.ForceThrow <= 0) return;
            HitBall(s, x, y, dir);
            var r = RaiderAt(x, y);
            if (r == null || !r.Alive || (once != null && !once.Add(r.Id))) return;
            var def = Rules.Entity(r.Kind);
            string by = s.Name.ToLowerInvariant();
            if (s.ForcePush > 0 && dir >= 0 && def.Mass <= s.MaxMass)
            {
                for (int k = 0; k < s.ForcePush; k++)
                {
                    int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                    if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) break;
                    r.X = nx;
                    r.Y = ny;
                }
                LogOnce($"pushed#{r.Id}", $"The {by} knocks {def.Name} back", LogKind.Notable);
            }
            if (s.ForceThrow <= 0) return;
            r.Status[ThrownStatus] = Math.Max(r.StatusOf(ThrownStatus), s.ForceThrow);
            LogOnce($"thrown#{r.Id}", $"The {by} throws {def.Name} off its feet", LogKind.Notable);
        }

        /// <summary>[n, sides] dice with the world's generator (roll in the TS).</summary>
        int Roll(int[] dice)
        {
            int total = 0;
            for (int i = 0; i < dice[0]; i++) total += 1 + (int)Math.Floor(Rand() * dice[1]);
            return total;
        }

        /// <summary>
        /// A creature stepping onto a cover that hurts (thorns) takes its damage, and one that slows (or
        /// ground that does: mud) is snagged for the rest of the step's time (thorns in the TS).
        /// </summary>
        void Thorns(Raider r, Cell c)
        {
            if (!Rules.Entity(r.Kind).Creature) return;
            var cv = CoverOf(c);
            if (cv != null && cv.Damage.Length == 2) DamageRaider(r, Roll(cv.Damage), cv.Name.ToLowerInvariant(), null);
            double speed = WalkSpeed(c);
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

        // The heat that hurts a raider this tick: burning, a hot cell, thick steam (the TS's heatHazards).
        void HeatHazards(Raider r, Cell c, EntityDef def)
        {
            var s = Rules.Sim;
            if (r.StatusOf("burning") > 0 && Tick % s.BurnDamageEvery == 0)
            {
                DamageRaider(r, 1, "fire", "fire");
                if (def.Flammable && r.Alive) Apply(r.X, r.Y, "fire", 1, -1, false);
            }
            if (c.Heat > 0 && def.Creature && Tick % s.BurnDamageEvery == 0) DamageRaider(r, 1, "hot metal", "fire");
            // Thick steam scalds what is made of a material that scalds (flesh; not bone or chitin).
            if (c.Gas == "steam" && c.GasAmt >= 2 && def.MaterialDef?.Has("scalds") == true && Tick % s.ScaldEvery == 0)
                DamageRaider(r, Rules.Gases.TryGetValue("steam", out var steam) ? steam.Scald : 1, "scalding", "fire");
        }

        // What the raider's square does to it this tick: fire and water reach it, burning and steam hurt, a shock zaps.
        void Hazards(Raider r, Cell c, EntityDef def)
        {
            var s = Rules.Sim;
            if (c.Fire > 0) ApplyRaiderElement(r, "fire", false);
            if (c.Water > 0 && !c.Frozen) ApplyRaiderElement(r, "water", false);
            HeatHazards(r, c, def);
            if (Shocked(r, c, def) && r.Alive)
            {
                int mul = ApplyRaiderElement(r, "lightning", false);
                DamageRaider(r, s.ZapDamage * mul, "lightning", "lightning");
            }
        }

        // Freshly charged water (or metal) under it, or, wet, beside it.
        bool Shocked(Raider r, Cell c, EntityDef def) => c.Charge == 3 || ShockedFromBeside(r, def);

        // Shock-water (conduct.chainsWet): a wet creature of a material that chains wet is shocked by
        // freshly charged water beside it too, not only under it (the goblin on the bank of a struck pond).
        bool ShockedFromBeside(Raider r, EntityDef def)
        {
            if (r.StatusOf("wet") == 0 || def.MaterialDef == null || !def.MaterialDef.ChainsWet) return false;
            foreach (var (nx, ny, _) in ActiveNeighbours(r.X, r.Y))
            {
                var n = At(nx, ny);
                if (n.Charge == 3 && n.Water > 0 && !n.Frozen) return true;
            }
            return false;
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
