using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The rules MCP server (core/Rules.Mcp, docs/RULES_MCP.md): validate, simulate, drafts and save.</summary>
    public class RulesMcpTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.Folders);

        static IEnumerable<string> Mods() =>
            Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal);

        static IReadOnlyList<ModProblem> Check(string patch) => Bench.Validate(patch.Replace('\'', '"'));

        // ---------- validate ----------

        [TestCaseSource(nameof(Mods))]
        public void EveryModInRulesModsPassesValidateUnchanged(string mod)
        {
            var problems = Bench.Validate(mod);
            Assert.That(problems, Is.Empty, Workbench.Report(problems));
        }

        // One bad patch per check, and the sentence it gets.
        [TestCase("{ 'spells': 3 }", ModCheck.Shape, "\"spells\" must be a list of spells.")]
        [TestCase("{ 'spels': [] }", ModCheck.Shape, "\"spels\" isn't a section of a mod patch")]
        [TestCase("{ 'reactions': { 'add': [ { 'element': 'fire', 'do': [{ 'extinguish': true }] } ] } }", ModCheck.Shape, "Every reaction rule needs an id, and one has none.")]
        [TestCase("{ 'spells': [ { 'id': 'ice-knife', 'group': 'water', 'name': 'Ice Knife', 'level': 1, 'shape': 'point', 'element': 'frost' } ] }", ModCheck.References, "Spell ice-knife: unknown element frost.")]
        [TestCase("{ 'reactions': { 'add': [ { 'id': 'fire-tar', 'element': 'fire', 'if': { 'cover': ['tar'] }, 'do': [{ 'ignite': 'cover' }] } ] } }", ModCheck.References, "tar")]
        [TestCase("{ 'spells': [ { 'id': 'ice-knife', 'name': 'Ice Knife', 'level': 1, 'shape': 'point', 'element': 'cold' } ] }", ModCheck.Design, "Spell ice-knife needs a group")]
        [TestCase("{ 'spells': [ { 'id': 'dud', 'group': 'arcane', 'name': 'Dud', 'level': 1, 'shape': 'point' } ] }", ModCheck.Design, "Spell dud has neither an element nor an effect")]
        [TestCase("{ 'spells': [ { 'id': 'wish-fire', 'group': 'fire', 'name': 'Wish Fire', 'level': 12, 'shape': 'point', 'element': 'fire' } ] }", ModCheck.Design, "Spell wish-fire is level 12; SRD 5.2.1 spells are level 0 (a cantrip) to 9.")]
        [TestCase("{ 'spells': [ { 'id': 'odd-bolt', 'group': 'fire', 'name': 'Odd Bolt', 'level': 1, 'shape': 'point', 'element': 'fire', 'damage': [3, 7] } ] }", ModCheck.Design, "Spell odd-bolt deals 3d7; damage dice are d4, d6, d8, d10, d12.")]
        [TestCase("{ 'elements': ['shadow'], 'reactions': { 'add': [ { 'id': 'shadow-fire', 'element': 'shadow', 'do': [{ 'extinguish': true }] } ] }, 'spells': [ { 'id': 'gloom', 'group': 'arcane', 'name': 'Gloom', 'level': 1, 'shape': 'burst', 'radius': 1, 'element': 'shadow' } ] }", ModCheck.Design, "The new element shadow is made for spell gloom")]
        [TestCase("{ 'elements': ['shadow'] }", ModCheck.Design, "The new element shadow has no reactions")]
        [TestCase("{ 'spells': [ { 'id': 'fireball', 'group': 'fire', 'name': 'Fireball', 'level': 3, 'shape': 'burst', 'radius': 2.5, 'element': 'fire' } ] }", ModCheck.Determinism, "Spell fireball's radius is 2.5, but the core keeps radius as a whole number (integer state); use a whole number.")]
        [TestCase("{ 'reactions': { 'add': [ { 'id': 'fire-gamble', 'element': 'fire', 'if': { 'chance': 1.5 }, 'do': [{ 'ignite': 2 }] } ] } }", ModCheck.Determinism, "Rule fire-gamble's if.chance is a chance of 1.5")]
        [TestCase("{ 'critters': [ { 'id': 'dice-imp', 'name': 'Dice imp', 'mode': 'emit', 'element': 'fire', 'every': 6, 'seed': 42 } ] }", ModCheck.Determinism, "Critter dice-imp's seed sets \"seed\"; all randomness comes from the world's seeded generator")]
        [TestCase("{ 'sim': { 'fire': { 'spreadChance': 0.95 } } }", ModCheck.Budget, "sim fire.spreadChance is 0.95; a mod may set it from 0 to 0.9.")]
        [TestCase("{ 'spells': [ { 'id': 'fireball', 'group': 'fire', 'name': 'Fireball', 'level': 3, 'shape': 'burst', 'radius': 20, 'element': 'fire' } ] }", ModCheck.Budget, "Spell fireball has a radius of 20; a mod's spell may have at most 6")]
        [TestCase("{ 'reactions': { 'add': [ { 'id': 'oil-flood', 'element': 'oil', 'do': [{ 'spreadCover': 9 }] } ] } }", ModCheck.Budget, "Rule oil-flood's spreadCover is 9; the most a mod's rule may give is 3")]
        [TestCase("{ 'critters': [ { 'id': 'ember-newt', 'name': 'Ember newt', 'mode': 'emit', 'element': 'fire', 'every': 1 } ] }", ModCheck.Budget, "Critter ember-newt acts every 1 ticks")]
        [TestCase("{ 'terrain': { 'grass': { 'name': 'Grass', 'tags': ['ground'], 'grow': { 'into': 'sprouts' } } } }", ModCheck.Budget, "terrain sprouts → terrain grass → terrain sprouts change into each other on their own forever")]
        [TestCase("{ 'terrain': { 'moon': { 'name': 'Moon', 'tags': ['ground'], 'look': { 'family': 'shimmer' } } } }", ModCheck.Looks, "Terrain moon names the look family \"shimmer\", which the view doesn't know; the families for it are block, debris, deck, ground, machine, plant, plate, wall (rules/looks.json).")]
        [TestCase("{ 'spells': [ { 'id': 'moon-ray', 'group': 'arcane', 'name': 'Moon Ray', 'level': 1, 'shape': 'point', 'element': 'cold', 'look': { 'family': 'laser' } } ] }", ModCheck.Looks, "Spell moon-ray names the look family \"laser\", which the view doesn't know")]
        [TestCase("{ 'terrain': { 'moon': { 'name': 'Moon', 'tags': ['ground'] } } }", ModCheck.Looks, "Terrain moon has no look, so the view can't draw it; give it a \"look\" naming a family")]
        [TestCase("{ 'terrain': { 'moon': { 'name': 'Moon', 'tags': ['ground'], 'look': { 'family': 'plant', 'form': 'cactus' } } } }", ModCheck.Looks, "Terrain moon's look family \"plant\" takes a form, one of bush, tree, sapling, stump.")]
        [TestCase("{ 'gases': { 'spores': { 'name': 'Spores', 'look': { 'family': 'bank' } } } }", ModCheck.Looks, "Gas spores names the look family \"bank\", which the view can't draw from data yet")]
        public void EachCheckRefusesItsBadPatchInOneSentence(string patch, ModCheck check, string sentence)
        {
            var problems = Check(patch);
            Assert.That(problems.Any(p => p.Check == check && p.Message.Contains(sentence, StringComparison.Ordinal)), Is.True, Workbench.Report(problems));
            Assert.That(problems.All(p => p.Message.EndsWith('.')), Is.True, "every problem is a sentence");
        }

        [Test]
        public void TooManyNewEntriesIsOverBudget()
        {
            var covers = string.Join(", ", Enumerable.Range(1, 13).Select(i => $"'tar{i}': {{ 'name': 'Tar {i}' }}"));
            var problems = Check("{ 'covers': { " + covers + " } }");
            Assert.That(problems.Select(p => p.Message), Has.Some.EqualTo("The mod adds 13 new entries; a mod may add at most 12, so keep it to one idea."));
        }

        [Test]
        public void ALooksFractionsAreNotIntegerState()
        {
            var problems = Check("{ 'terrain': { 'moon': { 'name': 'Moon', 'tags': ['ground'], 'look': { 'family': 'block', 'height': 1.25 } } } }");
            Assert.That(problems, Is.Empty, Workbench.Report(problems));
        }

        // ---------- simulate ----------

        [Test]
        public void SimulateIsDeterministic()
        {
            var (a, baseA) = Bench.SimulateRuns("wildfire", "fire-on-grass", 120);
            var (b, baseB) = Bench.SimulateRuns("wildfire", "fire-on-grass", 120);
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            Assert.That(b.Rng, Is.EqualTo(a.Rng));
            Assert.That(baseB.Hash, Is.EqualTo(baseA.Hash));
            Assert.That(a.Hash, Is.Not.EqualTo(baseA.Hash), "wildfire changes how the meadow burns");
            Assert.That(a.Peaks["fire"].count, Is.GreaterThan(0));
        }

        [Test]
        public void TheBigFireballModBurnsMoreInTheFireballScenario()
        {
            var (big, plain) = Bench.SimulateRuns("big-fireball", "fireball", 60);
            Assert.That(big.Hash, Is.Not.EqualTo(plain.Hash), "radius 2 to 4 changes the outcome");
            Assert.That(plain.CellsBurnt, Is.GreaterThan(0), "a Fireball sets the grass alight");
            Assert.That(big.CellsBurnt, Is.GreaterThan(plain.CellsBurnt));
            Assert.That(big.Peaks["fire"].count, Is.GreaterThan(plain.Peaks["fire"].count));
            StringAssert.Contains("burnt", Bench.Simulate("big-fireball", "fireball", 6));
        }

        [TestCase("fireball")]
        [TestCase("flood")]
        [TestCase("storm")]
        public void EveryScenarioRunsAndSaysWhatHappened(string scenario)
        {
            var text = Bench.Simulate("storm-season", scenario, 60);
            StringAssert.Contains("With the mod:", text);
            StringAssert.Contains("Without it:", text);
            StringAssert.Contains("Cell hash", text);
        }

        // ---------- drafts and save ----------

        [Test]
        public void ADraftSavesAndParsesBackToTheSamePatch()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rules-mcp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var b = new Workbench(new GameFolders(ChemistryTests.RulesDir(), dir));
                var d = b.AddReaction(null, "steam-damps-fire", "fire", "[{ \"extinguish\": true }]", "{ \"gas\": \"steam\" }", "The steam keeps everything too damp to burn", stop: true);
                b.SetSpell(d.Id, "fireball", "radius", "3");
                b.SetMaterial(d.Id, "grass", "burn.catch", "0.3");
                b.Tune(d.Id, "fire.spreadChance", "0.4");
                Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));

                var path = b.Save(d.Id, "Damp steam", "Steam keeps fire off, grass catches less, and Fireball is a step wider.");
                var saved = JObject.Parse(File.ReadAllText(path));
                Assert.That(JToken.DeepEquals(saved["patch"], d.Patch), Is.True);
                var parsed = ModPatch.Parse(File.ReadAllText(path));
                Assert.That(parsed.Title, Is.EqualTo("Damp steam"));
                var fromDraft = b.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });
                Assert.That(b.Base.WithMods(new[] { parsed }).Fingerprint, Is.EqualTo(fromDraft.Fingerprint));
                Assert.That(fromDraft.Spell("fireball").Radius, Is.EqualTo(3));
                Assert.That(fromDraft.Spell("fireball").Crater, Is.Not.Null, "set_spell keeps the rest of the spell");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void RemoveTakesBackOnlyWhatTheDraftHolds()
        {
            var d = Bench.Tune(null, "push.slideCells", "5");
            Assert.Throws<RulesToolException>(() => Bench.Remove(d.Id, "spells", "fireball"));
            Bench.Remove(d.Id, "sim", "push.slideCells");
            Assert.That(d.Patch.Count, Is.EqualTo(0));
            var e = Assert.Throws<RulesToolException>(() => Bench.Tune(d.Id, "fire.spreadChance", "2"));
            Assert.That(e.Message, Is.EqualTo("sim fire.spreadChance may be set from 0 to 0.9, not 2."));
        }

        [Test]
        public void ExplainNamesTheRuleThatDecides()
        {
            StringAssert.Contains("fire-oil decides it", Bench.Explain("fire", "oil"));
            StringAssert.Contains("fire-water decides it", Bench.Explain("fire", "water"));
            StringAssert.Contains("burn", Bench.Explain("grass", "fire"));
        }
    }
}
