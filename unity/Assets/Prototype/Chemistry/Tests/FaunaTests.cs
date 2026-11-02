using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    // The creature tables (Fauna.cs): the view's, so out of the fingerprint; rows a mod
    // replaces whole; and refused here when a name in one doesn't hold.
    public class FaunaTests
    {
        static Rules Base() => Rules.Load(ChemistryTests.RulesDir());

        static Rules WithoutTheTables() => Rules.FromJson(Directory.GetFiles(ChemistryTests.RulesDir(), "*.json")
            .Where(f => Path.GetFileName(f) != "fauna.json" && Path.GetFileName(f) != "parts.json")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText));

        static string Row(Rules rules, string table, string id) => JObject.Parse(rules.ToJson())[table][id].ToString();

        static Rules With(Rules rules, string table, string id, string row) =>
            rules.WithMods(new[] { ModPatch.Parse($"{{ \"{table}\": {{ \"{id}\": {row} }} }}") });

        [Test]
        public void EverySpeciesReadsAndEveryNameInItHolds()
        {
            var fauna = Base().Fauna;
            Assert.That(fauna.SpeciesIds.Count(), Is.GreaterThanOrEqualTo(58));
            foreach (var id in fauna.SpeciesIds)
            {
                var body = fauna.BodyOf(id);
                Assert.That(body, Is.Not.Null, id);
                Assert.That(body.Slots, Is.Not.Empty, id);
                Assert.That(body.Slots.All(s => fauna.Part(s.Part) != null), Is.True, id);
            }
        }

        [Test]
        public void TheSpawnersComeFirstInTheirOldOrder()
        {
            var fauna = Base().Fauna;
            var spawners = fauna.SpeciesIds.Where(id => fauna.Species(id).Spawns).ToList();
            Assert.That(spawners, Is.EqualTo(new[] { "deer", "rabbit", "squirrel", "red-fox", "turkey", "quail", "marmot", "skunk", "garter-snake", "black-bear", "wolf", "frog", "heron", "ptarmigan", "roadrunner", "goblin", "ogre", "human-child", "dwarf" }));
            Assert.That(fauna.SpeciesIds.Take(spawners.Count), Is.EqualTo(spawners));
            Assert.That(fauna.Species("frog").NearWater, Is.True);
            Assert.That(fauna.Species("deer").NearWater, Is.False);
        }

        [Test]
        public void ColoursReadAsHexOrAsNumbers()
        {
            var plain = Base();
            var row = JObject.Parse(Row(plain, "fauna", "deer"));
            row["coat"]["base"] = "#ff8000";
            var coat = With(plain, "fauna", "deer", row.ToString()).Fauna.Species("deer").Coat;
            Assert.That(coat.Base, Is.EqualTo(new[] { 1.0, 128 / 255.0, 0.0 }));
            Assert.That(coat.Accent, Is.EqualTo(new[] { 0.92, 0.87, 0.8 }));
        }

        [Test]
        public void AVariantTakesItsBodyAndKeepsItsOwnCoat()
        {
            var fauna = Base().Fauna;
            Assert.That(fauna.BodyOf("arctic-fox").Id, Is.EqualTo("red-fox"));
            Assert.That(fauna.Species("arctic-fox").Coat.Base, Is.EqualTo(new[] { 0.9, 0.9, 0.88 }));
            Assert.That(fauna.Species("arctic-fox").Slots, Is.Empty);
        }

        [Test]
        public void CreatureRowsStayOutOfTheFingerprintAndTheCheckOfWholeNumbers()
        {
            var plain = Base();
            var row = JObject.Parse(Row(plain, "fauna", "deer"));
            row["body"]["radius"] = 0.33;
            var modded = With(plain, "fauna", "deer", row.ToString());
            Assert.That(modded.Fauna.Species("deer").Radius, Is.EqualTo(0.33));
            Assert.That(modded.Fauna.Species("rabbit").Radius, Is.EqualTo(plain.Fauna.Species("rabbit").Radius));
            Assert.That(modded.Fauna.Species("deer").Walk, Is.EqualTo(1.3), "a row comes in whole");
            Assert.That(modded.Fingerprint, Is.EqualTo(plain.Fingerprint));
            Assert.That(WithoutTheTables().Fingerprint, Is.EqualTo(plain.Fingerprint), "the tables add nothing to it");
        }

        [Test]
        public void AVariantTakesHowItsSpeciesVaries()
        {
            var fauna = Base().Fauna;
            Assert.That(fauna.Species("deer").Vary.Shade, Is.GreaterThan(0));
            Assert.That(fauna.Species("human").Vary.Mix, Is.True);
            Assert.That(fauna.Species("human-child").Vary, Is.Null, "the row says nothing, so the body takes the human's");
        }

        [Test]
        public void ChangedRowsAreFoundAndOnlyThose()
        {
            var plain = Base();
            var part = JObject.Parse(Row(plain, "parts", "deer-ear"));
            part["spring"] = 0.123;
            var modded = With(plain, "parts", "deer-ear", part.ToString());
            var changed = modded.Fauna.ChangedParts(plain.Fauna);
            Assert.That(changed, Is.EqualTo(new[] { "deer-ear" }));
            Assert.That(modded.Fauna.SameRows(plain.Fauna, "deer", changed), Is.False);
            Assert.That(modded.Fauna.SameRows(plain.Fauna, "rabbit", changed), Is.True);
        }

        [TestCase("fauna", "deer", "{ 'name': 'Deer', 'parts': { 'neck': { 'part': 'no-such-part' } } }", "species deer: part neck names unknown part no-such-part")]
        [TestCase("fauna", "deer", "{ 'parts': { 'head': { 'part': 'deer-head', 'on': 'neck' } } }", "species deer: part head hangs from neck, which isn't a part before it")]
        [TestCase("fauna", "deer", "{ 'variantOf': 'unicorn' }", "species deer: variantOf names unknown species unicorn")]
        [TestCase("fauna", "deer", "{ 'variantOf': 'deer' }", "species deer: variantOf goes round")]
        [TestCase("fauna", "deer", "{ 'behave': { 'hunts': ['unicorn'] } }", "species deer hunts unknown species unicorn")]
        [TestCase("fauna", "deer", "{ 'behave': { 'active': 'dusk' } }", "species deer: active must be one of day, night, always")]
        [TestCase("fauna", "deer", "{ 'hp': 12 }", "species deer: hp isn't a key the view reads")]
        [TestCase("fauna", "deer", "{ 'coat': { 'base': 'red' } }", "a coat's base must be")]
        [TestCase("fauna", "deer", "{ 'vary': { 'shade': 1.5 } }", "species deer: vary shade must be a share")]
        [TestCase("parts", "eye", "{ 'role': 'fin', 'segments': [{ 'length': 0.1 }] }", "part eye: role must be one of")]
        [TestCase("parts", "eye", "{ 'role': 'eye', 'segments': [] }", "part eye needs segments")]
        [TestCase("parts", "eye", "{ 'role': 'eye', 'segments': [{}], 'poses': { 'dance': [] } }", "part eye: dance isn't a pose")]
        public void ABadRowIsRefused(string table, string id, string row, string message)
        {
            var e = Assert.Throws<ModException>(() => With(Base(), table, id, row));
            Assert.That(e.Errors, Has.Some.Contains(message));
        }
    }
}
