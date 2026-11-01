using System;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>set_look and what describe and list say of looks (docs/RULES_MCP.md "Looks").</summary>
    public class LookToolsTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.RulesDir());

        static Draft Look(string id, string param, string value, string table = null, string draft = null) =>
            Bench.SetLook(draft, id, param, value, table, out _);

        static string Refusal(string id, string param, string value, string table = null) =>
            Assert.Throws<RulesToolException>(() => Look(id, param, value, table)).Message;

        static Rules With(Draft d) => Bench.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        [Test]
        public void SetLookOnASpellKeepsTheRestOfIt()
        {
            var d = Look("fireball", "color", "[0.2, 0.4, 1]");
            var rules = With(d);
            var fireball = rules.Spell("fireball");
            Assert.That((fireball.Look.Family, fireball.Look.B), Is.EqualTo(("blast", 1f)));
            Assert.That(fireball.Radius, Is.EqualTo(Bench.Base.Spell("fireball").Radius));
            Assert.That(fireball.Crater, Is.Not.Null, "the rest of the spell is kept");
            Assert.That(rules.Fingerprint, Is.EqualTo(Bench.Base.Fingerprint));
            Assert.That(Workbench.Report(Bench.Validate(d.Id)), Does.StartWith("No problems"));
        }

        [Test]
        public void SetLookOnATerrainTakesItsFamilysParameters()
        {
            var d = Look("stone", "color", "\"#4073ff\"");
            Look("stone", "height", "1.6", draft: d.Id);
            var stone = With(d).Terrain["stone"];
            Assert.That((stone.Look.Family, stone.Look.R, stone.Look.G, stone.Look.B, stone.Look.Height), Is.EqualTo(("block", 0.251f, 0.451f, 1f, 1.6f)));
            Assert.That(Workbench.Report(Bench.Validate(d.Id)), Does.StartWith("No problems"));
        }

        [Test]
        public void SetLookOnAGasGivesItAColourWithNoFamily()
        {
            var d = Look("steam", "colour", "[255, 128, 204]", "gas");
            var steam = With(d).Gases["steam"];
            Assert.That((steam.Look.Family, steam.Look.R, steam.Look.G), Is.EqualTo(((string)null, 1f, 0.502f)));
            Assert.That(steam.Scald, Is.EqualTo(Bench.Base.Gases["steam"].Scald), "the rest of the gas is kept");
            Assert.That(Workbench.Report(Bench.Validate(d.Id)), Does.StartWith("No problems"));
        }

        [Test]
        public void SetLookOnAnElementColoursEverythingItMakes()
        {
            var d = Look("fire", "color", "[0.25, 0.45, 1]", "element");
            Assert.That(d.Patch["elements"]?["fire"]?["look"]?["color"]?.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[0.25,0.45,1.0]"));
            var rules = With(d);
            Assert.That(rules.ElementLook("fire").B, Is.EqualTo(1f));
            Assert.That(rules.Fingerprint, Is.EqualTo(Bench.Base.Fingerprint));
            Assert.That(Workbench.Report(Bench.Validate(d.Id)), Does.StartWith("No problems"));
            StringAssert.Contains("fire: look.color from [0.95,0.42,0.12] to [0.25,0.45,1.0]", Bench.Changes(d));
        }

        [Test]
        public void AParameterTheFamilyDoesntTakeIsRefusedWithTheList()
        {
            Assert.That(Refusal("fireball", "height", "3"), Is.EqualTo("The spell fireball's look (family blast) takes color; height isn't one of them."));
            Assert.That(Refusal("fire", "glow", "1", "element"), Is.EqualTo("The element fire's look (an element's: only a colour) takes color; glow isn't one of them."));
            StringAssert.Contains("takes color, height, glow, roughness; flow isn't one of them", Refusal("stone", "flow", "1"));
            StringAssert.Contains("color is three numbers from 0 to 1", Refusal("fireball", "color", "\"blue\""));
        }

        [Test]
        public void AnIdInTwoTablesNeedsTheTable()
        {
            StringAssert.StartsWith("frost is both a spell and a cover; say which with table.", Refusal("frost", "color", "[1, 1, 1]"));
            StringAssert.StartsWith("steam is both a gas and an element", Refusal("steam", "color", "[1, 1, 1]"));
            Assert.That(Look("frost", "color", "[1, 1, 1]", "cover").Patch["covers"]?["frost"]?["look"], Is.Not.Null);
            StringAssert.StartsWith("Nothing called moonbeam has a look", Refusal("moonbeam", "color", "[1, 1, 1]"));
        }

        [Test]
        public void AnElementsLookSitsBesideANewElementOfTheDraft()
        {
            var d = Bench.AddReaction(null, "shadow-fire", "shadow", "[{ \"extinguish\": true }]");
            Look("shadow", "color", "[0.1, 0, 0.2]", "element", d.Id);
            Assert.That(d.Patch["elements"], Is.InstanceOf<JObject>());
            Assert.That(With(d).ElementLook("shadow").B, Is.EqualTo(0.2f));
            Bench.Remove(d.Id, "elements", "shadow");
            Assert.That(d.Patch["elements"], Is.Null, "taken back, and tidied");
        }

        [Test]
        public void DescribeShowsTheLookAndWhatItTakes()
        {
            StringAssert.Contains("Look: family blast", Bench.Describe("spells", "fireball"));
            StringAssert.Contains("set_look changes color.", Bench.Describe("spells", "fireball"));
            StringAssert.Contains("set_look changes color, height, baked, form (bush, tree, sapling, stump).", Bench.Describe("terrain", "tree"));
            var fire = Bench.Describe("elements", "fire");
            StringAssert.Contains("\"color\"", fire);
            StringAssert.Contains("Look: an element's: only a colour, the colour of what fire makes in the world", fire);
            StringAssert.Contains("Look: no family, only a colour (no gas family is drawn yet)", Bench.Describe("gases", "steam"));
        }

        [Test]
        public void ListLooksGivesTheFamiliesTheirParametersAndWhatTakesNoColour()
        {
            var list = Bench.List("looks");
            StringAssert.Contains("bare (covers, gases, elements): no family; takes color.", list);
            StringAssert.Contains("spells blast: takes color.", list);
            StringAssert.Contains("terrain plant: takes color, height, baked; forms bush, tree, sapling, stump.", list);
            StringAssert.Contains("A tree or sapling is the valley's own tree model, so it takes no colour there.", list);
            var none = list.Split('\n').Single(l => l.StartsWith("Takes no colour:", StringComparison.Ordinal));
            foreach (var f in new[] { "fire plain", "fire oil", "gases bank", "covers slick" }) StringAssert.Contains(f, none);
            StringAssert.DoesNotContain("spells blast", none);
            StringAssert.Contains("colour [0.95,0.42,0.12]", Bench.List("elements"));
        }

        [Test]
        public void TheInstructionsSendColoursToSetLook()
        {
            StringAssert.DoesNotContain("Numbers are enough", RulesTools.Instructions);
            StringAssert.Contains("set_look", RulesTools.Instructions);
            StringAssert.Contains("blue fire everywhere", RulesTools.Instructions);
        }
    }
}
