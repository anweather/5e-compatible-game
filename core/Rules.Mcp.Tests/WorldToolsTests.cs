using System;
using System.Collections.Generic;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>places(), here(), and set_look's where and mix (docs/design/world-tools.md G1 and "Scoped and mixed looks").</summary>
    [Category("Simulation")]
    public class WorldToolsTests
    {
        static Workbench Bench(HereSpot? spot = null, bool standIn = true)
        {
            var bench = new Workbench(ChemistryTests.Folders);
            if (spot != null || !standIn) bench.Here = new StandInHere(spot);
            return bench;
        }

        static Draft Look(Workbench b, string id, string param, string value, string where = null, string draft = null, string table = null) =>
            b.SetLook(draft, id, param, value, table, out _, (where, null));

        static Draft By(Workbench b, string id, string param, string value, string by, string draft = null, string table = null) =>
            b.SetLook(draft, id, param, value, table, out _, (null, by));

        static string Refusal(Workbench b, string id, string param, string value, string where, string table = null) =>
            Assert.Throws<RulesToolException>(() => Look(b, id, param, value, where, table: table)).Message;

        static Rules With(Workbench b, Draft d) => b.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        static (int x, int y) Village(Workbench b) => b.Places.All.First(p => p.Kind == "village").Middle;

        [Test]
        public void PlacesListsEveryPlaceWithItsCells()
        {
            var b = Bench();
            string said = new RulesTools(b).Places();
            foreach (var p in b.Places.All) StringAssert.Contains($"- {p.Id}: {p.Name}", said);
            StringAssert.Contains("lake: the lake, a lake,", said);
            StringAssert.Contains("(from villages.json)", said);
            StringAssert.Contains("river: the river, a river,", said);
            StringAssert.Contains("(by hand)", said);
        }

        [Test]
        public void HereSaysWhereThePlayerIsAndWhatsNear()
        {
            var village = Village(Bench());
            var b = Bench(new HereSpot("host", village.x, village.y, (village.x + 1, village.y)));
            var name = b.Places.All.First(p => p.Kind == "village").Name;
            string said = new RulesTools(b).Here();
            StringAssert.StartsWith($"You are host (slot 0), at cell [{village.x}, {village.y}] (a stand-in", said);
            StringAssert.Contains($"In {name} (", said);
            StringAssert.Contains("Near, within 120 cells:", said);
            StringAssert.Contains("the lake (lake, a lake):", said);
            StringAssert.Contains($"Looking at cell [{village.x + 1}, {village.y}], in {name}.", said);
            Assert.That(Bench(standIn: false).DescribeHere(), Does.StartWith("No player is in the game"));
        }

        [Test]
        public void TheFireNearMeTurnsGreenAndFollowsMe()
        {
            var b = Bench(new HereSpot("host", 100, 100));
            var d = Look(b, "fire", "color", "\"#33e64d\"", "{\"around\": \"me\", \"within\": 10}");
            var scoped = (JObject)d.Patch["elements"]["fire"]["look"]["scoped"][0];
            Assert.That(scoped["where"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"around\":\"host\",\"within\":10}"), "me is the player asking");
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            var rules = With(b, d);
            Assert.That(rules.Fingerprint, Is.EqualTo(b.Base.Fingerprint), "looks stay out of the fingerprint");
            var fire = LookScopes.Compile(rules, b.Places).Of("elements", "fire");
            var me = new List<PlayerSpot> { new PlayerSpot("host", 100, 100) };
            Assert.That(fire.At(105, 100, me).G, Is.EqualTo(0.902f).Within(0.001), "green near me");
            Assert.That(fire.At(150, 100, me).G, Is.EqualTo(b.Base.ElementLook("fire").G).Within(0.001), "plain fire away from me");
            me[0] = new PlayerSpot("host", 150, 100);
            Assert.That(fire.At(150, 100, me).G, Is.EqualTo(0.902f).Within(0.001), "and it follows me");
            StringAssert.Contains("Scoped, within 10 cells of the player host: [0.2, 0.902, 0.302].", b.Describe("elements", "fire", d.Id));
        }

        [Test]
        public void FireRoundTheVillageInThreeColours()
        {
            var b = Bench();
            var village = b.Places.All.First(p => p.Kind == "village");
            var d = Look(b, "fire", "mix", "[\"#ff4020\", \"#2050ff\", {\"color\": \"#30d040\", \"weight\": 2}]", $"{{\"place\": \"{village.Name}\", \"within\": 20}}");
            var scoped = d.Patch["elements"]["fire"]["look"]["scoped"][0];
            Assert.That((string)scoped["where"]["place"], Is.EqualTo(village.Id), "the place by its id");
            Assert.That(scoped["mix"].Count(), Is.EqualTo(3));
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            var fire = LookScopes.Compile(With(b, d), b.Places).Of("elements", "fire");
            var seen = new HashSet<string>();
            foreach (var (x, y) in village.Cells()) seen.Add(fire.At(x, y, null).ToString());
            Assert.That(seen.Count, Is.EqualTo(3), "three colours over the village");
            StringAssert.Contains($"Scoped, within 20 cells of the place {village.Id}: [1, 0.251, 0.125] 25%, [0.125, 0.314, 1] 25%, [0.188, 0.816, 0.251] 50%, a patchwork.", b.Describe("elements", "fire", d.Id));
        }

        [Test]
        public void SetLookAgainWithTheSameWhereChangesItAndNullTakesItBack()
        {
            var b = Bench();
            var d = Look(b, "fire", "color", "[0, 1, 0]", "{\"at\": [10, 10], \"within\": 4}");
            Look(b, "fire", "color", "[0, 0, 1]", "{\"at\": [30, 30], \"within\": 4}", d.Id);
            Look(b, "fire", "mix", "[[1, 0, 0], [1, 1, 0]]", "{\"at\": [10, 10], \"within\": 4}", d.Id);
            var scoped = (JArray)d.Patch["elements"]["fire"]["look"]["scoped"];
            Assert.That(scoped.Count, Is.EqualTo(2));
            Assert.That(scoped[1]["mix"], Is.Not.Null, "changed again, it's the latest");
            Assert.That(scoped[1]["color"], Is.Null, "a mix in place of its colour");
            Look(b, "fire", "color", "null", "{\"at\": [10, 10], \"within\": 4}", d.Id);
            Assert.That(((JArray)d.Patch["elements"]["fire"]["look"]["scoped"]).Count, Is.EqualTo(1));
            Look(b, "fire", "color", "null", "{\"at\": [30, 30], \"within\": 4}", d.Id);
            Assert.That(d.Patch["elements"]["fire"]["look"]["scoped"], Is.Null);
        }

        [Test]
        public void AMixEverywhereAndAColourAfterIt()
        {
            var b = Bench();
            var d = Look(b, "steam", "mix", "[[1, 1, 1], [0.5, 0.5, 0.5]]", table: "gas");
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            Assert.That(With(b, d).Gases["steam"].Look.Mix.Colours.Count, Is.EqualTo(2));
            Look(b, "steam", "color", "[1, 0, 1]", draft: d.Id, table: "gas");
            Assert.That(d.Patch["gases"]["steam"]["look"]["mix"], Is.Null, "a colour replaces the mix");
            var t = Look(b, "stone", "mix", "[[0.4, 0.4, 0.4], [0.5, 0.45, 0.4]]", "{\"at\": [5, 5], \"within\": 3}");
            Assert.That(Workbench.Report(b.Validate(t.Id)), Does.StartWith("No problems"), "a terrain's look scopes too");
        }

        [Test]
        public void AWhereThatCantHoldIsRefused()
        {
            var b = Bench();
            string missing = Refusal(b, "fire", "color", "[0, 1, 0]", "{\"place\": \"atlantis\"}");
            StringAssert.StartsWith("There is no place called atlantis; the places are ", missing);
            StringAssert.Contains("lake", missing);
            StringAssert.Contains("can't hold in one place", Refusal(b, "fireball", "color", "[0, 1, 0]", "{\"place\": \"lake\"}"));
            StringAssert.Contains("only color, mix or blend", Refusal(b, "stone", "height", "2", "{\"place\": \"lake\"}"));
            StringAssert.Contains("within is at most 64 cells", Refusal(b, "fire", "color", "[0, 1, 0]", "{\"place\": \"lake\", \"within\": 500}"));
            StringAssert.Contains("names one of place, at or around", Refusal(b, "fire", "color", "[0, 1, 0]", "{\"within\": 5}"));
            StringAssert.Contains("has no one to follow", Refusal(Bench(standIn: false), "fire", "color", "[0, 1, 0]", "{\"around\": \"me\", \"within\": 5}"));
            StringAssert.Contains("mix is a list of colours", Refusal(b, "fire", "mix", "\"green\"", null));
            StringAssert.Contains("isn't one of them", Refusal(b, "fireball", "mix", "[[1, 0, 0]]", null));
        }

        [Test]
        public void ValidateRefusesAPlaceTheWorldDoesntHaveAndListsThem()
        {
            var b = Bench();
            string said = Workbench.Report(b.Validate("{\"elements\": {\"fire\": {\"look\": {\"color\": [1, 0, 0], \"scoped\": [{\"where\": {\"place\": \"atlantis\"}, \"color\": [0, 1, 0]}]}}}}"));
            StringAssert.Contains("[references] Element fire's scoped look names the place \"atlantis\", which the world doesn't have; the places are ", said);
            StringAssert.Contains("river", said);
            said = Workbench.Report(b.Validate("{\"elements\": {\"fire\": {\"look\": {\"color\": [1, 0, 0], \"scoped\": [{\"where\": {\"at\": [1, 1], \"within\": 99}, \"color\": [0, 1, 0]}]}}}}"));
            StringAssert.Contains("[budget] Element fire's scoped look reaches 99 cells; a scope reaches at most 64 cells.", said);
            said = Workbench.Report(b.Validate("{\"spells\": [{\"id\": \"fireball\", \"look\": {\"family\": \"blast\", \"color\": [1, 0, 0], \"mix\": [[1, 0, 0]]}}]}"));
            StringAssert.Contains("gives mix, which the look family \"blast\" doesn't take", said);
        }

        [Test]
        public void TheFireICreateTurnsBlue()
        {
            var b = Bench(new HereSpot("guest", 10, 10, slot: 2));
            var d = By(b, "fire", "color", "[0.25, 0.45, 1]", "me");
            Assert.That(d.Patch["elements"]["fire"]["look"]["scoped"][0].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"by\":2,\"color\":[0.25,0.45,1.0]}"), "me is the asking player's slot");
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            var rules = With(b, d);
            Assert.That(rules.Fingerprint, Is.EqualTo(b.Base.Fingerprint));
            var players = new List<PlayerSpot> { new PlayerSpot("host", 0, 0, 0), new PlayerSpot("guest", 10, 10, 2) };
            var fire = LookScopes.Compile(rules, b.Places).Of("elements", "fire");
            Assert.That(fire.At(300, 300, players, null, 2).B, Is.EqualTo(1f), "the guest's fire, anywhere");
            Assert.That(fire.At(300, 300, players, null, 0).B, Is.EqualTo(b.Base.ElementLook("fire").B), "the host's as it was");
            StringAssert.Contains("By player, by the player in slot 2: [0.25, 0.45, 1].", b.Describe("elements", "fire", d.Id));
            var spell = By(b, "fireball", "color", "\"#4073ff\"", "me");
            Assert.That(Workbench.Report(b.Validate(spell.Id)), Does.StartWith("No problems"), "a spell's own show by player");
        }

        [Test]
        public void EachPlayerGetsTheirOwnFireColour()
        {
            var b = Bench();
            var d = By(b, "fire", "mix", "[\"#ff4020\", \"#2050ff\", \"#30d040\", \"#f0d020\"]", "each");
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            var fire = LookScopes.Compile(With(b, d), b.Places).Of("elements", "fire");
            var players = Enumerable.Range(0, 4).Select(i => new PlayerSpot("p" + i, i, i, i)).ToList();
            var colours = Enumerable.Range(0, 4).Select(i => fire.At(50, 50, players, null, i).ToString()).ToList();
            Assert.That(colours.Distinct().Count(), Is.EqualTo(4), "four players, four colours");
            StringAssert.Contains("By player, by each player, one colour per slot in join order:", b.Describe("elements", "fire", d.Id));
            var smoke = By(b, "smoke", "color", "[0.2, 0.2, 0.6]", "1", table: "gas");
            Assert.That(Workbench.Report(b.Validate(smoke.Id)), Does.StartWith("No problems"), "a gas by slot");
        }

        [Test]
        public void ALookByPlayerThatCantHoldIsRefused()
        {
            var b = Bench();
            StringAssert.Contains("A terrain isn't cast by anyone", Assert.Throws<RulesToolException>(() => By(b, "stone", "color", "[1, 0, 0]", "me")).Message);
            StringAssert.Contains("by each gives a mix", Assert.Throws<RulesToolException>(() => By(b, "fire", "color", "[1, 0, 0]", "each")).Message);
            StringAssert.Contains("by is \"me\"", Assert.Throws<RulesToolException>(() => By(b, "fire", "color", "[1, 0, 0]", "everyone")).Message);
            StringAssert.Contains("either a where or a by", Assert.Throws<RulesToolException>(() => b.SetLook(null, "fire", "color", "[1, 0, 0]", null, out _, ("{\"place\": \"lake\"}", "me"))).Message);
            StringAssert.Contains("has no one to colour for", Assert.Throws<RulesToolException>(() => By(Bench(standIn: false), "fire", "color", "[1, 0, 0]", "me")).Message);
            string said = Workbench.Report(b.Validate("{\"terrain\": {\"stone\": {\"look\": {\"family\": \"block\", \"color\": [1, 0, 0], \"scoped\": [{\"by\": 0, \"color\": [0, 1, 0]}]}}}}"));
            StringAssert.Contains("is given by player, which only what a cast makes", said);
            var fireball = JObject.Parse(b.Describe("spells", "fireball").Split('\n', 2)[1].Split("\nReferred to by")[0].Split("\nNothing in the rules")[0]);
            fireball["look"]["scoped"] = JArray.Parse("[{\"where\": {\"place\": \"lake\"}, \"color\": [0, 1, 0]}]");
            said = Workbench.Report(b.Validate(new JObject { ["spells"] = new JArray(fireball) }.ToString()));
            StringAssert.Contains("is scoped to a place, but a spell's look colours its own show", said);
        }

        [Test]
        public void AMixCanBlendLikePaint()
        {
            var b = Bench();
            var d = Look(b, "fire", "mix", "[\"#0000ff\", \"#ffff00\"]");
            b.SetLook(d.Id, "fire", "blend", "true", null, out var said);
            Assert.That((bool)d.Patch["elements"]["fire"]["look"]["blend"], Is.True);
            Assert.That(Workbench.Report(b.Validate(d.Id)), Does.StartWith("No problems"));
            var fire = LookScopes.Compile(With(b, d), b.Places).Of("elements", "fire").At(3, 4, null);
            Assert.That(fire.G, Is.GreaterThan(fire.R).And.GreaterThan(fire.B), "blue and yellow blend green");
            StringAssert.Contains("blended like paint into [0, 1, 0]", b.Describe("elements", "fire", d.Id));
            StringAssert.Contains("give the look a mix first", Assert.Throws<RulesToolException>(() => Look(b, "smoke", "blend", "true", table: "gas")).Message);
            var s = Look(b, "fire", "mix", "[[0, 0, 1], [1, 1, 0]]", "{\"place\": \"lake\"}");
            Look(b, "fire", "blend", "true", "{\"place\": \"lake\"}", s.Id);
            Assert.That((bool)s.Patch["elements"]["fire"]["look"]["scoped"][0]["blend"], Is.True, "a scoped mix blends too");
            StringAssert.Contains("can't blend", Assert.Throws<RulesToolException>(() => b.SetLook(null, "fire", "blend", "true", null, out _, (null, "each"))).Message);
            said = Workbench.Report(b.Validate("{\"elements\": {\"fire\": {\"look\": {\"color\": [1, 0, 0], \"blend\": true}}}}"));
            StringAssert.Contains("look.blend needs a mix", said);
        }

        [Test]
        public void SetLookSaysWhenAMixOnFlamesIsPale()
        {
            var b = Bench();
            b.SetLook(null, "fire", "mix", "[[1, 0.9, 0.85], [0.85, 0.9, 1]]", null, out var said);
            StringAssert.Contains("In the mix, [1, 0.9, 0.85] and [0.85, 0.9, 1] are pale: fire can only turn its hue", said);
            var d = b.SetLook(null, "fire", "mix", "[[1, 0.55, 0.45], [0.45, 0.6, 1]]", null, out said);
            StringAssert.DoesNotContain("pale", said, "each colour shows as a patchwork");
            b.SetLook(d.Id, "fire", "blend", "true", null, out said);
            StringAssert.Contains("The mix blends to", said);
            StringAssert.Contains("which is pale: fire can only turn its hue, so it shows as the nearest hue", said);
            b.SetLook(null, "stone", "mix", "[[0.5, 0.5, 0.5], [0.6, 0.6, 0.6]]", null, out said);
            StringAssert.DoesNotContain("pale", said, "stone is painted, not turned");
        }

        [Test]
        public void TheToolsMakeScopedAndMixedAsksClear()
        {
            var listed = new McpRpc(new RulesTools(Bench()), "test", "1").Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
            string tools = Newtonsoft.Json.Linq.JObject.Parse(listed).ToString();
            foreach (var said in new[] { "The fire near me green", "Fire round the village in three colours", "places (or here)", "blue and yellow giving green", "each player gets their own fire colour", "The fire I create blue" })
                StringAssert.Contains(said, tools);
        }
    }
}
