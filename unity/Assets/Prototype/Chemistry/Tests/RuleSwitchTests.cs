using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Switching rules in a running game: rule sets (base + mods), tick batches that carry a
    /// rule switch next to the casts, and a late joiner's replay of the host's history.
    /// </summary>
    public class RuleSwitchTests
    {
        static string ModsDir => Path.Combine(ChemistryTests.RulesDir(), "mods");

        static Rules Load() => Rules.Load(ChemistryTests.RulesDir());

        static ActiveMod Example(string id) => new ActiveMod(id, File.ReadAllText(Path.Combine(ModsDir, id + ".json")));

        // What ChemGrid passes for the valley: a longer fire fade and a far lightning reach.
        static ModPatch ValleyTuning() => ModPatch.Parse("{ \"sim\": { \"fire\": { \"fadeCells\": 50 }, \"conductLimit\": 12000 } }");

        static readonly ActiveMod Broken = new ActiveMod("broken",
            "{ \"reactions\": { \"add\": [{ \"id\": \"x\", \"element\": \"fire\", \"do\": [{ \"teleportEverything\": true }] }] } }");

        /// <summary>A grass field with dirt paths, so how far fire spreads depends on the rules.</summary>
        static World Field(Rules rules)
        {
            var w = new World(rules, 30, 12, 7, "grass");
            for (int x = 0; x < w.W; x += 6)
                for (int y = 0; y < w.H; y++) w.Paint(x, y, "dirt");
            return w;
        }

        // A host's session: casts at a few ticks, wildfire on at tick 10, damp world added at 25,
        // wildfire off at 40. Returns the batches as stamped, in order.
        static List<TickBatch> HostSession(int ticks)
        {
            var wildfire = Example("wildfire");
            var damp = Example("damp-world");
            var stamped = new List<TickBatch>();
            for (int t = 1; t <= ticks; t++)
            {
                var b = new TickBatch(t);
                if (t == 3) b.Casts.Add(new CastCommand("fire-bolt", 3, 5, 1));
                if (t == 18) b.Casts.Add(new CastCommand("fireball", 20, 6, 1));
                if (t == 10) b.Mods = new[] { wildfire };
                if (t == 25) b.Mods = new[] { wildfire, damp };
                if (t == 40) b.Mods = new[] { damp };
                stamped.Add(b);
            }
            return stamped;
        }

        static (World world, RuleSet rules) Run(IEnumerable<TickBatch> batches)
        {
            var rules = RuleSet.Plain(RuleSet.BaseOf(Load(), ValleyTuning()));
            var world = Field(rules.Effective);
            foreach (var b in batches)
            {
                rules = b.RunOn(world, rules, out var errors);
                Assert.That(errors, Is.Empty);
            }
            return (world, rules);
        }

        [Test]
        public void ALateJoinerReplayingTheHistoryWithRuleSwitchesMatchesTheLiveWorld()
        {
            const int ticks = 60;
            var live = HostSession(ticks);
            var (host, hostRules) = Run(live);

            // What the host sends a late joiner: every cast and rule switch with its tick.
            var casts = live.SelectMany(b => b.Casts.Select(c => (b.Tick, c))).ToList();
            var changes = live.Where(b => b.Mods != null).Select(b => new RuleChange(b.Tick, b.Mods)).ToList();
            var timeline = TickBatch.Timeline(casts, changes, ticks);
            var (joiner, joinerRules) = Run(Enumerable.Range(1, ticks).Select(t => timeline[t]));

            Assert.That(joiner.Tick, Is.EqualTo(host.Tick));
            Assert.That(joiner.CellHash(), Is.EqualTo(host.CellHash()));
            Assert.That(joinerRules.Fingerprint, Is.EqualTo(hostRules.Fingerprint));
            Assert.That(joiner.Rules.Fingerprint, Is.EqualTo(host.Rules.Fingerprint));
            Assert.That(joinerRules.Mods.Select(m => m.Id), Is.EqualTo(new[] { "damp-world" }));

            var (noSwitches, _) = Run(TickBatch.Timeline(casts, null, ticks).OrderBy(p => p.Key).Select(p => p.Value));
            Assert.That(noSwitches.CellHash(), Is.Not.EqualTo(host.CellHash()), "the rule switches made a difference");
        }

        [Test]
        public void ARuleSwitchTakesEffectAtItsTick()
        {
            var plain = HostSession(30).Select(b => new TickBatch(b.Tick, b.Casts)).ToList();
            var withMods = HostSession(30);
            var (a, _) = Run(plain.Take(9));
            var (b, rules) = Run(withMods.Take(9));
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()), "identical before the switch");
            Assert.That(rules.Mods, Is.Empty);
            var (_, after) = Run(withMods.Take(10));
            Assert.That(after.Effective.Sim.Fire.SpreadChance, Is.EqualTo(0.9), "wildfire from tick 10");
        }

        [Test]
        public void ABrokenModIsRefusedAndTheRulesStayAsTheyWere()
        {
            var rules = RuleSet.Plain(Load()).WithMods(new[] { Example("wildfire") });
            var e = Assert.Throws<ModException>(() => rules.WithMods(rules.Toggled(Broken)));
            Assert.That(e.Errors, Is.EqualTo(new[] { "rule x: unknown effect \"teleportEverything\" (new verbs need code; see docs/MODDING.md)" }));
            Assert.That(rules.Mods.Select(m => m.Id), Is.EqualTo(new[] { "wildfire" }), "the rule set is a value: unchanged");

            var bad = Assert.Throws<ModException>(() => rules.WithMods(new[] { new ActiveMod("typo", "{ \"sim\": ") }));
            Assert.That(bad.Errors[0], Does.StartWith("typo: That isn't valid JSON"), "says which mod");

            // A batch carrying a switch that doesn't check out keeps the old rules and still steps.
            var world = Field(rules.Effective);
            var before = world.Rules;
            var kept = new TickBatch(1, null, new[] { Broken }).RunOn(world, rules, out var errors);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(kept, Is.SameAs(rules));
            Assert.That(world.Rules, Is.SameAs(before));
            Assert.That(world.Tick, Is.EqualTo(1));
        }

        [Test]
        public void SwitchingAModOffRestoresTheBaseFingerprint()
        {
            var plain = RuleSet.Plain(RuleSet.BaseOf(Load(), ValleyTuning()));
            var wildfire = Example("wildfire");
            var on = plain.WithMods(plain.Toggled(wildfire));
            Assert.That(on.Fingerprint, Is.Not.EqualTo(plain.Fingerprint));
            Assert.That(on.IsOn("wildfire"));
            var off = on.WithMods(on.Toggled(wildfire));
            Assert.That(off.Mods, Is.Empty);
            Assert.That(off.Fingerprint, Is.EqualTo(plain.Fingerprint));
        }

        [Test]
        public void SceneTuningIsInTheFingerprintAndSurvivesSwitches()
        {
            var untuned = RuleSet.Plain(Load());
            var valley = RuleSet.Plain(RuleSet.BaseOf(Load(), ValleyTuning()));
            Assert.That(valley.Effective.Sim.Fire.FadeCells, Is.EqualTo(50));
            Assert.That(valley.Effective.Sim.ConductLimit, Is.EqualTo(12000));
            Assert.That(valley.Fingerprint, Is.Not.EqualTo(untuned.Fingerprint), "two scenes with different tuning are told apart");

            var wild = valley.WithMods(new[] { Example("wildfire") });
            Assert.That(wild.Effective.Sim.Fire.FadeCells, Is.EqualTo(120), "a mod applies over the tuning");
            Assert.That(wild.Effective.Sim.ConductLimit, Is.EqualTo(12000), "and keeps what it doesn't touch");
            Assert.That(wild.WithMods(System.Array.Empty<ActiveMod>()).Effective.Sim.Fire.FadeCells, Is.EqualTo(50), "switched off, the tuning is back");

            var reloaded = wild.WithBase(RuleSet.BaseOf(Load(), ValleyTuning()));
            Assert.That(reloaded.Fingerprint, Is.EqualTo(wild.Fingerprint), "reloading unchanged files changes nothing");
        }

        [Test]
        public void AnEditedActiveModKeepsItsPlace()
        {
            var rules = RuleSet.Plain(Load()).WithMods(new[] { Example("wildfire"), Example("damp-world") });
            var edited = new ActiveMod("wildfire", "{ \"sim\": { \"fire\": { \"spreadChance\": 0.7 } } }");
            var list = rules.Replaced(edited);
            Assert.That(list.Select(m => m.Id), Is.EqualTo(new[] { "wildfire", "damp-world" }));
            Assert.That(rules.WithMods(list).Effective.Sim.Fire.SpreadChance, Is.EqualTo(0.15), "damp world still applies last");
        }

        [Test]
        public void TheCatalogListsTheModsWithTitlesAndIdeas()
        {
            var catalog = new ModCatalog(ModsDir);
            Assert.That(catalog.Mods.Select(m => m.Id), Does.Contain("wildfire").And.Contain("damp-world"));
            var wildfire = catalog.Find("wildfire");
            Assert.That(wildfire.Title, Is.EqualTo("Wildfire"));
            Assert.That(wildfire.Idea, Does.StartWith("Fire catches easily"));
            Assert.That(wildfire.Error, Is.Null);
            Assert.That(catalog.Mods.Select(m => m.Id), Is.Ordered.Using((IComparer<string>)System.StringComparer.Ordinal));

            var bad = ModFile.FromJson("oops", "{ \"terrain\": [] }");
            Assert.That(bad.Error, Is.EqualTo("\"terrain\" must be an object of entries by id"));
            Assert.That(bad.Title, Is.EqualTo("oops"));
        }

        [Test]
        public void APropTakesNewNumbersAndKeepsItsDamage()
        {
            var crate = Load().Entity("crate");
            var p = new PropState(crate);
            p.Damage(2);
            var sturdy = new EntityDef { Id = "crate", Name = "Iron crate", Hp = 20, Mass = 5, Flammable = false };
            p.UseDef(sturdy);
            Assert.That(p.Def, Is.SameAs(sturdy));
            Assert.That(p.Hp, Is.EqualTo(18));
            Assert.That(p.Floats, Is.False, "mass 5 sinks");
            p.UseDef(new EntityDef { Id = "crate", Name = "Paper crate", Hp = 1, Mass = 1 });
            Assert.That(p.Hp, Is.EqualTo(1), "a rule change alone never breaks it");
            Assert.That(p.Destroyed, Is.False);
        }
    }
}
