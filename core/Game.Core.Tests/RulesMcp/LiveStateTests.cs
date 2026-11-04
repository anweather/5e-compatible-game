using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>What the first play with Claude found (docs/playtests/2026-11-01-mcp.md): the tools read what's in force, refuse at once and say their limits.</summary>
    public class LiveStateTests
    {
        static Workbench NewBench() => new Workbench(ChemistryTests.Folders);

        static void Bigger(Workbench b) => b.Apply(b.SetSpell(null, "fireball", "radius", "4").Id, "Ana", "Bigger fireball");

        // ---------- live state ----------

        [Test]
        public void ADraftBuildsOnWhatIsInForce()
        {
            var b = NewBench();
            Bigger(b);
            var d = b.SetSpell(null, "fireball", "push", "3");
            var spell = (JObject)d.Patch["spells"][0];
            Assert.That(spell["radius"], Is.Null, "the draft holds only what it names");
            Assert.That(b.Changes(d), Is.EqualTo("It changes Fireball: push from 2 to 3."));
            b.Apply(d.Id, "Bo", "Harder fireball");
            Assert.That(b.InForce.Spell("fireball").Radius, Is.EqualTo(4));
            Assert.That(b.InForce.Spell("fireball").Push, Is.EqualTo(3));
        }

        [Test]
        public void ApplySaysWhatChangedFromWhatWasInForce()
        {
            var b = NewBench();
            Bigger(b);
            var d = b.SetSpell(null, "fireball", "radius", "6");
            StringAssert.Contains("It changes Fireball: radius from 4 to 6.", b.Apply(d.Id, "Bo", "Biggest fireball"));
        }

        [Test]
        public void ALookDraftKeepsWhatAModGave()
        {
            var b = NewBench();
            b.Apply(b.SetLook(null, "grass", "color", "[0.1, 0.1, 0.1]", null, out _).Id, "Ana", "Black grass");
            var d = b.SetMaterial(null, "grass", "burn.fuel", "4");
            Assert.That(d.Patch["terrain"]["grass"]["look"], Is.Null, "the draft holds only what it names");
            b.Apply(d.Id, "Bo", "Thin grass");
            Assert.That(b.InForce.Terrain["grass"].Look.R, Is.EqualTo(0.1f).Within(0.001f), "and the look the mod gave stays");
        }

        [Test]
        public void DescribeSaysWhichModsTouchedAValue()
        {
            var b = NewBench();
            StringAssert.Contains("No mods are on", b.Describe("spells", "fireball"));
            Bigger(b);
            b.Apply(b.SetSpell(null, "fireball", "radius", "5").Id, "Bo", "Bigger still");
            var text = b.Describe("spells", "fireball");
            StringAssert.Contains("\"radius\": 5", text);
            StringAssert.Contains("Bigger fireball (bigger-fireball), by Ana: radius from 2 to 4", text);
            StringAssert.Contains("Bigger still (bigger-still), by Bo: radius from 4 to 5", text);
            StringAssert.Contains("No mod on changes it", b.Describe("spells", "meteor"));
        }

        [Test]
        public void ARevertTakesTheValueBackAndSaysWhatStillHoldsIt()
        {
            var b = NewBench();
            Bigger(b);
            Assert.That(b.Revert("bigger-fireball"), Does.Not.Contain("sets some of the same"));
            Assert.That(b.InForce.Spell("fireball").Radius, Is.EqualTo(2));
            StringAssert.Contains("\"radius\": 2", b.Describe("spells", "fireball"));

            Bigger(b);
            b.Apply(b.SetSpell(null, "fireball", "push", "3").Id, "Bo", "Harder fireball");
            Assert.That(b.Revert("bigger-fireball"), Does.Not.Contain("sets some of the same"), "built while it was on, but it names another field");
            Assert.That(b.InForce.Spell("fireball").Radius, Is.EqualTo(2), "a revert undoes its own");
            Assert.That(b.InForce.Spell("fireball").Push, Is.EqualTo(3), "and only its own");

            Bigger(b);
            b.Apply(b.SetSpell(null, "fireball", "radius", "5").Id, "Bo", "Bigger still");
            StringAssert.Contains("Bigger still sets some of the same values", b.Revert("bigger-fireball"));
            Assert.That(b.InForce.Spell("fireball").Radius, Is.EqualTo(5));
        }

        [Test]
        public void TheLayersReadAsTheRoomComposesThem()
        {
            var b = NewBench();
            foreach (var mod in new[] { "big-fireball", "winter", "moonstone" }) b.Apply(mod, "Ana");
            Assert.That(b.RoomNow.Mods.Count, Is.EqualTo(3), b.Room());
            var room = b.RoomNow.Mods.Select(m => ModPatch.Parse(m.Json));
            Assert.That(b.InForce.Fingerprint, Is.EqualTo(b.Base.WithMods(room).Fingerprint));
            // A look-free read of a value the written layer has: it agrees with the engine's in every spell.
            foreach (var s in b.InForce.Spells)
                StringAssert.Contains($"\"id\": \"{s.Id}\"", b.Describe("spells", s.Id));
        }

        [Test]
        public void WhatsOnSaysItInPlainWords()
        {
            var b = NewBench();
            var empty = b.WhatsOn();
            StringAssert.Contains("No mods on", empty);
            StringAssert.Contains("Time: 09:00 on day 1, bright light.", empty);
            StringAssert.Contains("Weather: clear", empty);
            Bigger(b);
            var d = b.AddMaterial(null, "cover", "tar", "Tar", new[] { "slippery" });
            d = b.Tune(d.Id, "push.slideCells", "5");
            b.Apply(d.Id, "Bo", "Tar pits");
            var text = b.WhatsOn();
            StringAssert.Contains("1. Bigger fireball: Fireball: radius from 2 to 4.", text);
            StringAssert.Contains("2. Tar pits: a new cover, Tar; push.slideCells from", text);
        }

        [Test]
        public void SimulateAndValidateReadTheRulesInForce()
        {
            var b = NewBench();
            var said = b.Apply(b.AddMaterial(null, "cover", "tar", "Tar").Id, "Ana", "Tar");
            StringAssert.Contains("switched on", said);
            var d = b.AddReaction(null, "fire-tar", "fire", "[{\"ignite\": 2}]", "{\"cover\": [\"tar\"]}");
            // Tar is on, as a temporary material of its own mod (World.Lapse.cs): another mod may name it, and goes when it does (#86).
            Assert.That(b.Validate(d.Id), Is.Empty, Workbench.Report(b.Validate(d.Id)));
            Assert.That(b.Simulate(d.Id, "fire-on-grass", 6), Does.Contain("Scenario fire-on-grass"));
        }

        // ---------- a game's desk ----------

        sealed class OldDesk : IRoomDesk
        {
            public readonly List<RoomCommand> Asked = new List<RoomCommand>();
            public string Room(string mode) => "the game's room";
            public void Saved(string path) { }

            public string Decide(RoomCommand command)
            {
                Asked.Add(command);
                return "decided";
            }
        }

        // A game that says what's on: its own world, stepped as a solo game does.
        sealed class LiveDesk : ILiveDesk
        {
            public World World;
            public string Room(string mode) => Workbench.Describe(World.Room, World.Tick);
            public void Saved(string path) { }
            public WorldNow Now() => World == null ? null : WorldNow.Of(World);

            public string Decide(RoomCommand command)
            {
                var verdict = World.ApplyRoom(command.As("Player 1"));
                World.Step();
                return verdict.Line + ".";
            }
        }

        static LiveDesk Game(Workbench b)
        {
            var world = new World(b.Base, 64, 64, 1);
            world.OpenRoom(ChangeMode.Open, "Player 1", b.Base, b.Budgets);
            return new LiveDesk { World = world };
        }

        [Test]
        public void AGameThatSaysWhatIsOnIsReadThroughItsDesk()
        {
            var b = NewBench();
            var desk = Game(b);
            b.Desk = desk;
            var tools = new RulesTools(b);
            tools.Apply(b.SetSpell(null, "fireball", "radius", "4").Id, null, "Bigger fireball");
            // Someone else's change in the game shows at the next call.
            desk.World.ApplyRoom(RoomCommand.Apply("Player 2", "push", "{\"sim\": {\"push\": {\"slideCells\": 5}}}", "Longer slides"));
            StringAssert.Contains("Bigger fireball (bigger-fireball), by Player 1: radius from 2 to 4", tools.Describe("spells", "fireball"));
            StringAssert.Contains("Longer slides", tools.WhatsOn());
            StringAssert.Contains("set the time to 21:00", tools.SetTime("21"));
            Assert.That(desk.World.Minutes % Sky.MinutesPerDay, Is.EqualTo(21 * 60));
            StringAssert.Contains("Time: 21:00", tools.Time());
            StringAssert.Contains("set the weather to fog", tools.SetWeather("fog"));
            Assert.That(desk.World.WeatherSet.Value.kind, Is.EqualTo(WeatherKind.Fog));
        }

        [Test]
        public void AGameBetweenScenesSaysSoAndKeepsTheLastItKnew()
        {
            var b = NewBench();
            var desk = Game(b);
            b.Desk = desk;
            var tools = new RulesTools(b);
            tools.Apply(b.SetSpell(null, "fireball", "radius", "4").Id, null, "Bigger fireball");
            tools.List("spells");
            desk.World = null;
            StringAssert.Contains("between scenes", tools.WhatsOn());
            Assert.Throws<RulesToolException>(() => tools.Time());
            Assert.That(b.InForce.Spell("fireball").Radius, Is.EqualTo(4));
        }

        [Test]
        public void AGameThatCannotSayYetIsHonestAndGetsNoCommandItWouldMisread()
        {
            var b = NewBench();
            var desk = new OldDesk();
            b.Desk = desk;
            var tools = new RulesTools(b);
            StringAssert.Contains("can't tell the tools", tools.Describe("spells", "fireball"));
            StringAssert.Contains("the game's room", tools.WhatsOn());
            StringAssert.Contains("the base rules", tools.WhatsOn());
            Assert.Throws<RulesToolException>(() => tools.Time());
            Assert.Throws<RulesToolException>(() => tools.SetTime("noon"));
            Assert.Throws<RulesToolException>(() => tools.SetWeather("rain"));
            Assert.That(desk.Asked, Is.Empty, "it would send a clock command on as a mod");
        }

        // ---------- refuse at once ----------

        [TestCase("burn.colour", "burn takes")]
        [TestCase("burnn.catch", "has no field")]
        [TestCase("glow", "use set_look")]
        [TestCase("flammability", "burn.catch")]
        public void SetMaterialRefusesAnUnknownField(string field, string says)
        {
            var e = Assert.Throws<RulesToolException>(() => NewBench().SetMaterial(null, "grass", field, "1"));
            StringAssert.Contains(says, e.Message);
        }

        [Test]
        public void ARefusedFieldLeavesTheDraftAsItWas()
        {
            var b = NewBench();
            var d = b.SetMaterial(null, "grass", "burn.fuel", "4");
            string before = d.Show();
            Assert.Throws<RulesToolException>(() => b.SetMaterial(d.Id, "oil", "nope", "1"));
            Assert.Throws<RulesToolException>(() => b.SetSpell(d.Id, "fireball", "radius", "40"));
            Assert.That(d.Show(), Is.EqualTo(before));
        }

        [Test]
        public void TheOtherToolsRefuseAtOnceToo()
        {
            var b = NewBench();
            Assert.Throws<RulesToolException>(() => b.SetMaterial(null, "steam", "scaldd", "1"));
            Assert.Throws<RulesToolException>(() => b.SetMaterial(null, "steam", "sheds.nope", "1"));
            Assert.Throws<RulesToolException>(() => b.SetMaterial(null, "oil", "burn", "{\"nope\": 1}"));
            Assert.Throws<RulesToolException>(() => b.SetSpell(null, "fireball", "radiuss", "2"));
            Assert.Throws<RulesToolException>(() => b.SetSpell(null, "fireball", "crater.nope", "2"));
            Assert.Throws<RulesToolException>(() => b.SetReaction(null, "fire-oil", "if.nope", "2"));
            Assert.Throws<RulesToolException>(() => b.SetReaction(null, "fire-oil", "nope", "2"));
            Assert.Throws<RulesToolException>(() => b.SetReaction(null, "fire-oil", "do", "[{\"nope\": 1}]"));
            Assert.Throws<RulesToolException>(() => b.AddReaction(null, "r", "fire", "[{\"nope\": 1}]"));
            Assert.Throws<RulesToolException>(() => b.AddReaction(null, "r", "fire", "[{\"ignite\": 1}]", "{\"nope\": 1}"));
            Assert.Throws<RulesToolException>(() => b.AddMaterial(null, "terrain", "t", "T", null, "{\"burn\": {\"nope\": 1}}"));
            Assert.Throws<RulesToolException>(() => b.AddMaterial(null, "cover", "c", "C", null, "{\"lapse\": {\"until\": 60}}"));
            Assert.DoesNotThrow(() => b.AddMaterial(null, "cover", "c", "C", null, "{\"lapse\": {\"after\": 60}}"));
            Assert.DoesNotThrow(() => b.AddMaterial(null, "gas", "g", "G", null, "{\"lapse\": {\"after\": 60}}"));
            Assert.Throws<RulesToolException>(() => b.AddSpell(null, new Workbench.SpellArgs { Id = "s", Name = "S", Group = "fire", Shape = "burst", Extra = "{\"nope\": 1}" }));
            Assert.Throws<RulesToolException>(() => b.AddSpell(null, new Workbench.SpellArgs { Id = "s", Name = "S", Group = "fire", Shape = "burst", Radius = 40 }));
        }

        [Test]
        public void AddMaterialMakesALiquid()
        {
            var b = NewBench();
            Assert.Throws<RulesToolException>(() => b.AddMaterial(null, "liquid", "tar", "Tar", null, "{\"sets\": {\"into\": \"rock\"}}"));
            var d = b.AddMaterial(null, "liquid", "honey", "Honey", new[] { "sticky" }, "{\"thick\": 4, \"heap\": 6, \"soaks\": false, \"lapse\": {\"after\": 600}}");
            d = b.SetMaterial(d.Id, "honey", "floats", "false");
            StringAssert.Contains("switched on", b.Apply(d.Id, "Bo", "Honey"));
            StringAssert.Contains("a new liquid, Honey", b.WhatsOn());
            StringAssert.Contains("honey (liquid): Honey [sticky]", b.List("liquids"));
        }

        [Test]
        public void EveryFieldInTheRulesIsKnown()
        {
            var dir = ChemistryTests.RulesDir();
            var materials = JObject.Parse(File.ReadAllText(Path.Combine(dir, "materials.json")));
            foreach (var kind in Workbench.MaterialKinds)
                foreach (var entry in (materials[kind] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    Assert.DoesNotThrow(() => RuleFields.CheckMaterial(kind, (JObject)entry.Value), entry.Name);
            foreach (var spell in JArray.Parse(File.ReadAllText(Path.Combine(dir, "spells.json"))).OfType<JObject>())
                foreach (var p in spell.Properties())
                    Assert.DoesNotThrow(() => RuleFields.CheckSpell(p.Name, p.Value), $"{spell["id"]} {p.Name}");
            var reactions = JObject.Parse(File.ReadAllText(Path.Combine(dir, "reactions.json")));
            foreach (var rule in reactions["cell"].Concat(reactions["entity"]).OfType<JObject>())
                foreach (var p in rule.Properties())
                    Assert.DoesNotThrow(() => RuleFields.CheckRule(p.Name, p.Value), $"{rule["id"]} {p.Name}");
            foreach (var item in JObject.Parse(File.ReadAllText(Path.Combine(dir, "items.json"))).Properties())
                Assert.DoesNotThrow(() => RuleFields.CheckThing("items", (JObject)item.Value), item.Name);
            foreach (var entity in JObject.Parse(File.ReadAllText(Path.Combine(dir, "entities.json"))).Properties().Where(p => p.Value["player"] == null))
                Assert.DoesNotThrow(() => RuleFields.CheckThing("entities", (JObject)entity.Value), entity.Name);
        }

        [Test]
        public void AnUnknownThingFieldNamesTheSchemasFields()
        {
            var e = Assert.Throws<RulesToolException>(() => RuleFields.CheckThing("items", new JObject { ["name"] = "N", ["glow"] = 1 }));
            Assert.AreEqual("An item has no field glow; its fields are name, material, components, use, message, srd, mass, light, zoom, reach, strength, breaks, look.", e.Message);
            e = Assert.Throws<RulesToolException>(() => RuleFields.CheckThing("entities", new JObject { ["name"] = "N", ["player"] = true }));
            Assert.AreEqual("An entity has no field player; its fields are name, material, components, hp, mass, str, ac, explodes, raider, look.", e.Message);
        }

        // ---------- limits up front ----------

        static string Description(string tool) =>
            typeof(RulesTools).GetMethods().Where(m => m.GetCustomAttribute<RulesToolAttribute>()?.Name == tool).Select(m => m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description).Single();

        [Test]
        public void TheCapsToolsStateAreTheBudgets()
        {
            var budgets = NewBench().Budgets;
            foreach (var (word, cap) in new[] { ("radius", "maxRadius"), ("length", "maxLength"), ("size", "maxSize"), ("power", "maxPower"), ("push", "maxPush"), ("raise", "maxRaise"), ("dig", "maxDig") })
            {
                string said = $"{word} {budgets.Number("spells." + cap, -1)}";
                StringAssert.Contains(said, Description("add_spell"));
                StringAssert.Contains(said, Description("set_spell"));
            }
            StringAssert.Contains($"{budgets.Number("spells.maxDice", -1)} dice", Description("add_spell"));
            StringAssert.Contains($"at most {budgets.Number("mod.maxNewEntries", -1)} new entries", Description("add_spell"));
        }

        [Test]
        public void TheHueOnlyRuleIsSaidUpFront()
        {
            StringAssert.Contains("only turn their art's hue", Description("set_look"));
            StringAssert.Contains("only turns its art's hue", Description("add_spell"));
        }

        [Test]
        public void OutsideTheRulesIsInOnePlaceAndTheInstructionsPointThere()
        {
            var text = Workbench.Outside();
            foreach (var what in new[] { "Heat glow", "Player size", "Slippery terrain", "Weather's cover", "Area fill", "A building's rooms" })
                StringAssert.Contains(what, text);
            StringAssert.Contains("outside_rules", RulesTools.Instructions);
            Assert.That(new RulesTools(NewBench()).OutsideRules(), Is.EqualTo(text));
        }

        [Test]
        public void TheGameServesTheNewTools()
        {
            var rpc = new McpRpc(new RulesTools(NewBench()), "test", "1");
            var listed = JObject.Parse(rpc.Handle("{\"jsonrpc\": \"2.0\", \"id\": 1, \"method\": \"tools/list\"}"))["result"]["tools"].Select(t => (string)t["name"]);
            Assert.That(listed, Is.SupersetOf(new[] { "whats_on", "outside_rules", "time", "weather", "set_time", "set_weather" }));
        }

        // ---------- a new spell's place ----------

        static Workbench.SpellArgs Deluge(string extra = null) =>
            new Workbench.SpellArgs { Id = "oil-deluge", Name = "Oil Deluge", Group = "fire", Level = 3, Shape = "burst", Element = "fire", Radius = 6, Extra = extra };

        [Test]
        public void ANewSpellSaysHowAPlayerReachesIt()
        {
            var b = NewBench();
            var d = b.AddSpell(null, Deluge("{\"look\": {\"family\": \"blast\", \"color\": [1, 0.4, 0.1]}}"));
            StringAssert.Contains("on the spell radial: hold Tab (LB on a pad), the Fire wedge, then Oil Deluge", b.ReachOf(d, "oil-deluge"));
            StringAssert.Contains("A player reaches Oil Deluge", b.Apply(d.Id, "Ana", "Oil Deluge"));
            StringAssert.Contains("the Fire wedge", b.Describe("spells", "oil-deluge"));
        }

        [Test]
        public void ASpellOffTheRadialSaysSo()
        {
            var b = NewBench();
            var d = b.AddSpell(null, Deluge("{\"radial\": false, \"look\": {\"family\": \"blast\", \"color\": [1, 0.4, 0.1]}}"));
            StringAssert.StartsWith("No player can reach Oil Deluge yet", b.ReachOf(d, "oil-deluge"));
        }

        [Test]
        public void ASpellInAGroupThatIsNotThereIsRefusedAndOneWithNoGroupGoesLast()
        {
            var b = NewBench();
            var e = Assert.Throws<RulesToolException>(() => b.AddSpell(null, new Workbench.SpellArgs { Id = "s", Name = "S", Group = "nope", Level = 1, Shape = "burst", Element = "fire", Radius = 1 }));
            StringAssert.Contains("the radial's groups are fire, water", e.Message);
            Assert.Throws<RulesToolException>(() => b.SetSpell(null, "fireball", "group", "nope"));
            var d = b.SetSpell(null, "fireball", "group", "null");
            StringAssert.Contains("it names no group the radial has, so it goes in the last wedge", b.ReachOf(d, "fireball"));
        }

        [Test]
        public void ASpellOnlyACombosReachSaysWhichCombo()
        {
            var b = NewBench();
            StringAssert.Contains("by the combo", b.ReachOf(null, "scalding-steam"));
        }

        // ---------- the clock and the weather ----------

        [Test]
        public void SetTimeMovesTheClockForwardOnly()
        {
            var b = NewBench();
            StringAssert.Contains("host set the time to 06:30", b.SetTime("06:30"));
            StringAssert.Contains("Time: 06:30 on day 2, dim light.", b.TimeNow());
            b.SetTime("noon");
            StringAssert.Contains("Time: 12:00 on day 2, bright light.", b.TimeNow());
            Assert.Throws<RulesToolException>(() => b.SetTime("half past"));
            Assert.Throws<RulesToolException>(() => b.SetTime("25"));
        }

        [Test]
        public void SetWeatherHoldsItAndNaturalLetsItGo()
        {
            var b = NewBench();
            StringAssert.Contains("host set the weather to rain for 2 minutes", b.SetWeather("rain", 2));
            StringAssert.Contains("Weather: rain held", b.WeatherNow());
            StringAssert.Contains("host let the weather go its own way", b.SetWeather("natural"));
            StringAssert.Contains("as the world has it", b.WeatherNow());
            Assert.Throws<RulesToolException>(() => b.SetWeather("plague"));
            Assert.Throws<RulesToolException>(() => b.SetWeather("rain", 61));
        }

        [Test]
        public void TheRoomDecidesTheClockAndTheWeather()
        {
            var b = NewBench();
            b.Room("host-only");
            StringAssert.StartsWith("Set time refused: only the host can change this room", b.SetTime("noon", "Bo"));
            StringAssert.StartsWith("Set weather refused: only the host can change this room", b.SetWeather("rain", null, "Bo"));
            StringAssert.Contains("Time: 09:", b.TimeNow());
            b.Room("locked");
            StringAssert.StartsWith("Set weather refused: this room is locked", b.SetWeather("rain"));
        }
    }
}
