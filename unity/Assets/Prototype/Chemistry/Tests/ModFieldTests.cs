using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>A mod patches only the fields it names (Rules.MergedEntry), so mods stack and a revert undoes only its own.</summary>
    public class ModFieldTests
    {
        static readonly Rules Base = Rules.Load(ChemistryTests.RulesDir());

        static ModPatch P(string json) => ModPatch.Parse(json.Replace('\'', '"'));

        static Rules With(params string[] mods) => Base.WithMods(mods.Select(P));

        static ModPatch Shipped(string name) => ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", name + ".json")));

        [TestCase("big-fireball", "blue-fireballs")]
        [TestCase("blue-fireballs", "big-fireball")]
        public void ShippedModsStackEitherWayRound(string first, string second)
        {
            var r = Base.WithMods(new[] { Shipped(first), Shipped(second) });
            Assert.That(r.Spell("fireball").Radius, Is.EqualTo(4), "the blue one no longer loses the radius");
            Assert.That(r.Spell("fireball").Look.B, Is.EqualTo(1f), "the big one no longer loses the blue");
            Assert.That(r.Fingerprint, Is.EqualTo(Base.WithMods(new[] { Shipped("big-fireball") }).Fingerprint), "a look is out of the fingerprint");
        }

        [Test]
        public void TakingAModOffLeavesTheOthersOwnValues()
        {
            var both = Base.WithMods(new[] { Shipped("big-fireball"), Shipped("blue-fireballs") });
            Assert.That(both.Spell("fireball").Radius, Is.EqualTo(4));
            var blueOnly = Base.WithMods(new[] { Shipped("blue-fireballs") });
            Assert.That(blueOnly.Spell("fireball").Radius, Is.EqualTo(Base.Spell("fireball").Radius), "big's radius goes with big");
            Assert.That(blueOnly.Spell("fireball").Look.B, Is.EqualTo(1f));
            Assert.That(blueOnly.Fingerprint, Is.EqualTo(Base.Fingerprint));
        }

        [Test]
        public void GroupsMergeAndNullTakesAFieldOut()
        {
            var r = With("{ 'spells': [{ 'id': 'fireball', 'crater': { 'depth': 5 } }] }");
            Assert.That(r.Spell("fireball").Crater.Depth, Is.EqualTo(5));
            Assert.That(r.Spell("fireball").Crater.Radius, Is.EqualTo(Base.Spell("fireball").Crater.Radius), "a group's other keys stay");
            Assert.That(With("{ 'spells': [{ 'id': 'fireball', 'crater': null }] }").Spell("fireball").Crater, Is.Null);
        }

        [Test]
        public void AMaterialsVerbsMergeAsWritten()
        {
            var grass = Base.Terrain["grass"];
            var r = With("{ 'terrain': { 'grass': { 'burn': { 'fuel': 99 } } } }").Terrain["grass"];
            Assert.That(r.Fuel, Is.EqualTo(99));
            Assert.That(r.Flammability, Is.EqualTo(grass.Flammability), "the verb's other keys stay");
            Assert.That(r.Has("flammable"));
            var flat = With("{ 'terrain': { 'grass': { 'flammability': 0.9 } } }").Terrain["grass"];
            Assert.That(flat.Flammability, Is.EqualTo(0.9), "a flat key still wins over the group under it");
            var none = With("{ 'terrain': { 'grass': { 'burn': null } } }").Terrain["grass"];
            Assert.That(none.Has("flammable"), Is.False);
            Assert.That(With("{ 'terrain': { 'grass': { 'tags': ['ground', 'slippery'] } } }").Terrain["grass"].Has("flammable"), "tags don't take the verb away");
        }

        [Test]
        public void TakingAKeyOutOfNothingDoesntSwitchAVerbOn()
        {
            Assert.That(Base.Terrain["bedrock"].Has("flammable"), Is.False);
            Assert.That(With("{ 'terrain': { 'bedrock': { 'burn': { 'catch': null } } } }").Terrain["bedrock"].Has("flammable"), Is.False);
        }

        [Test]
        public void AReactionRuleKeepsItsPlaceAndItsOtherFields()
        {
            var rule = JObject.Parse(Base.ToJson())["cellRules"].First(t => t["if"]?["chance"] != null);
            string id = (string)rule["id"], element = (string)rule["element"];
            var r = JObject.Parse(With($"{{ 'reactions': {{ 'add': [{{ 'id': '{id}', 'if': {{ 'chance': 0.123 }} }}] }} }}").ToJson());
            var after = r["cellRules"].First(t => (string)t["id"] == id);
            Assert.That((double)after["if"]["chance"], Is.EqualTo(0.123));
            Assert.That((string)after["element"], Is.EqualTo(element));
            Assert.That(JToken.DeepEquals(after["do"], rule["do"]));
            Assert.That(r["cellRules"].Select(t => (string)t["id"]), Is.EqualTo(JObject.Parse(Base.ToJson())["cellRules"].Select(t => (string)t["id"])));
        }

        [Test]
        public void ANewEntryIsThePatchWithoutItsNulls()
        {
            var r = With("{ 'items': { 'pebble': { 'name': 'Pebble', 'mass': 1, 'use': null } } }");
            Assert.That(r.Item("pebble").Use, Is.Null);
            Assert.That(JObject.Parse(r.ToJson())["items"]["pebble"]["use"], Is.Null);
            Assert.Throws<ModException>(() => With("{ 'spells': [{ 'id': 'nothing-here', 'radius': 3 }] }"), "naming fields of an entry nobody has isn't an entry");
        }

        [Test]
        public void ALooksFieldsMergeButAColourReplacesAMixAndAFamilyStartsAfresh()
        {
            var look = With("{ 'terrain': { 'moss': { 'name': 'Moss', 'tags': ['ground'], 'look': { 'family': 'block', 'color': [0, 1, 0], 'height': 2, 'glow': 0.5 } } } }",
                "{ 'terrain': { 'moss': { 'look': { 'glow': 0.1 } } } }").Terrain["moss"].Look;
            Assert.That(look.Height, Is.EqualTo(2f), "the family's other numbers stay");
            Assert.That(look.Glow, Is.EqualTo(0.1f));
            Assert.That(With("{ 'elements': { 'fire': { 'look': { 'mix': [{ 'color': [0, 1, 0], 'weight': 1 }] } } } }",
                "{ 'elements': { 'fire': { 'look': { 'color': [0, 0, 1] } } } }").ElementLook("fire").Mix, Is.Null, "as set_look's colour drops a mix");
            var other = With("{ 'terrain': { 'moss': { 'name': 'Moss', 'tags': ['ground'], 'look': { 'family': 'block', 'color': [0, 1, 0], 'height': 2 } } } }",
                "{ 'terrain': { 'moss': { 'look': { 'family': 'ground', 'color': [0, 0, 1] } } } }").Terrain["moss"].Look;
            Assert.That(other.Height, Is.EqualTo(0f), "another family's numbers don't carry over");
        }

        [Test]
        public void ScopedLooksStillAddUpAcrossMods()
        {
            string Scoped(int x) => "{ 'elements': { 'fire': { 'look': { 'scoped': [{ 'where': { 'at': [" + x + ", 1], 'within': 3 }, 'color': [0, 1, 0] }] } } } }";
            Assert.That(With(Scoped(1), Scoped(9)).ElementLook("fire").Scoped.Count, Is.EqualTo(2));
            Assert.That(With(Scoped(1), Scoped(1)).ElementLook("fire").Scoped.Count, Is.EqualTo(1), "one given again isn't two");
        }

        [Test]
        public void ThePatchChecksReadTheEntryAsItComposes()
        {
            var budgets = ModBudgets.Load(ChemistryTests.RulesDir());
            string Problems(string mod) => string.Join("\n", ModValidator.Validate(Base, budgets, mod.Replace('\'', '"')));
            Assert.That(Problems("{ 'spells': [{ 'id': 'fireball', 'radius': 3 }] }"), Is.Empty, "it keeps the spell's element and group");
            Assert.That(Problems("{ 'spells': [{ 'id': 'fireball', 'radius': 30 }] }"), Does.Contain("radius of 30"));
            Assert.That(Problems("{ 'spells': [{ 'id': 'fireball', 'look': { 'wobble': 3 } }] }"), Does.Contain("gives wobble"), "its family is read from the entry under it");
            Assert.That(Problems("{ 'spells': [{ 'id': 'fireball', 'element': null }] }"), Does.Contain("neither an element nor an effect"));
        }

        [Test]
        public void AFaunaRowPatchesLikeAnyOther()
        {
            var squirrel = Base.Fauna.Species("squirrel");
            string bigger = "{ 'fauna': { 'squirrel': { 'size': [2, 3], 'move': { 'run': 9 } } } }";
            var row = With(bigger).Fauna.Species("squirrel");
            Assert.That(row.Size, Is.EqualTo(new double[] { 2, 3 }));
            Assert.That(row.Run, Is.EqualTo(9));
            Assert.That(row.Walk, Is.EqualTo(squirrel.Walk), "a group's other keys stay");
            Assert.That(row.Name, Is.EqualTo("Squirrel"));
            Assert.That(row.Slots.Count, Is.EqualTo(squirrel.Slots.Count), "and so does the rest of the row");
            var stacked = With(bigger, "{ 'fauna': { 'squirrel': { 'move': { 'walk': 5 } } } }").Fauna.Species("squirrel");
            Assert.That((stacked.Run, stacked.Walk), Is.EqualTo((9d, 5d)), "two mods stack");
            Assert.That(With(bigger).Fingerprint, Is.EqualTo(Base.Fingerprint), "the fauna's the view's");

            var budgets = ModBudgets.Load(ChemistryTests.RulesDir());
            Assert.That(ModValidator.Validate(Base, budgets, bigger.Replace('\'', '"')), Is.Empty, "a row that names only what it changes passes");
        }

        [Test]
        public void EveryShippedModPatchesOnlyWhatItChanges()
        {
            foreach (var path in Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json"))
            {
                var doc = JObject.Parse(Base.ToJson());
                foreach (var spell in (JObject.Parse(File.ReadAllText(path))["patch"]["spells"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var under = doc["spells"].FirstOrDefault(s => (string)s["id"] == (string)spell["id"]);
                    if (under == null) continue;
                    foreach (var p in spell.Properties().Where(p => p.Name != "id"))
                        Assert.That(JToken.DeepEquals(p.Value, under[p.Name]), Is.False, $"{Path.GetFileName(path)} restates {spell["id"]}.{p.Name}");
                }
            }
        }
    }
}
