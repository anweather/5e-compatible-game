using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>What the tools themselves tell a model, whichever harness it's in, and who may save (docs/telemetry/chat-context-2026-11-02.md, J-064).</summary>
    public class ChatKnowledgeTests
    {
        const string Blue = "[0.25, 0.45, 1]";

        static Workbench NewBench(GameFolders folders = null) => new Workbench(folders ?? ChemistryTests.Folders);

        static string[] FireSpells(Workbench b) => b.Base.Spells.Where(s => s.Element == "fire").Select(s => s.Id).ToArray();

        static float[] OwnColour(Rules rules, string spell) => ((JArray)JObject.Parse(rules.ToJson())["spells"].First(s => (string)s["id"] == spell)["look"]["color"]).Select(c => (float)c).ToArray();

        static Rules With(Workbench b, Draft d) => b.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        // A player's data folder in Temp, with their mods folder made, where a save goes.
        static void InATempFolder(Action<GameFolders> act)
        {
            string data = Path.Combine(Path.GetTempPath(), "chem-save-" + Guid.NewGuid().ToString("N"));
            var folders = new GameFolders(ChemistryTests.RulesDir(), data);
            Directory.CreateDirectory(folders.Mods);
            try
            {
                act(folders);
            }
            finally
            {
                Directory.Delete(data, true);
            }
        }

        // ---------- an element's colour ----------

        [Test]
        public void OneCallColoursAnElementAndAllItsSpells()
        {
            var b = NewBench();
            var d = b.SetLook(null, "fire", "color", Blue, "element", out string said);
            var fire = FireSpells(b);
            Assert.That(fire, Has.Length.EqualTo(7));
            foreach (var id in fire)
            {
                Assert.That(OwnColour(With(b, d), id), Is.EqualTo(new[] { 0.25f, 0.45f, 1f }), id);
                Assert.That(d.Changed, Does.Contain("spells " + id));
            }
            StringAssert.Contains("Fireball", said);
            StringAssert.Contains("set them too", said);
        }

        [Test]
        public void AColourFollowsAModsSpellOfThatElement()
        {
            var b = NewBench();
            var d = b.AddSpell(null, new Workbench.SpellArgs { Id = "ember-toss", Name = "Ember Toss", Group = "fire", Level = 0, Shape = "point", Element = "fire", Extra = "{\"look\": {\"family\": \"bolt\", \"color\": [1, 0.4, 0.1]}}" });
            b.SetLook(d.Id, "fire", "color", Blue, "element", out string said);
            StringAssert.Contains("Ember Toss", said);
            Assert.That(OwnColour(With(b, d), "ember-toss"), Is.EqualTo(new[] { 0.25f, 0.45f, 1f }));
        }

        [Test]
        public void SpellsFalseLeavesThemAndSaysWhich()
        {
            var b = NewBench();
            var d = b.SetLook(null, "fire", "color", Blue, "element", out string said, (null, null, false));
            Assert.That(d.Patch["spells"], Is.Null);
            StringAssert.Contains("keep their own colour: Fire Bolt", said);
        }

        [Test]
        public void AScopedColourStaysOffTheSpells()
        {
            var b = NewBench();
            var d = b.SetLook(null, "fire", "color", Blue, "element", out _, ("{\"at\": [10, 10], \"within\": 5}", null));
            Assert.That(d.Patch["spells"], Is.Null);
        }

        [Test]
        public void AMixCantGoOnASpellSoItsReplyNamesThem()
        {
            var b = NewBench();
            var d = b.SetLook(null, "fire", "mix", "[\"#ff4020\", \"#2050ff\"]", "element", out string said);
            Assert.That(d.Patch["spells"], Is.Null);
            StringAssert.Contains("keep their own colour", said);
        }

        [Test]
        public void AColourASpellCantShowIsNamed()
        {
            var b = NewBench();
            b.SetLook(null, "fire", "color", "[0.95, 0.95, 0.95]", "element", out string said);
            StringAssert.Contains("won't show on", said);
        }

        [Test]
        public void ADraftOfManyEntriesIsReadBackByName()
        {
            var b = NewBench();
            var d = b.SetLook(null, "fire", "color", Blue, "element", out _);
            StringAssert.Contains("spells fireball", d.Brief());
            StringAssert.DoesNotContain("\"color\"", d.Brief());
            var one = b.SetLook(null, "grass", "color", "[0.1, 0.1, 0.1]", null, out _);
            StringAssert.Contains("\"color\"", one.Brief());
        }

        // ---------- simulate before apply ----------

        [Test]
        public void ValidateAsksForASimulateOnlyWhenTheWorldBehavesDifferently()
        {
            var b = NewBench();
            StringAssert.Contains("run simulate", b.SimulateNote(b.SetMaterial(null, "grass", "burn.catch", "0.7").Id));
            Assert.That(b.SimulateNote(b.SetLook(null, "grass", "color", "[0.1, 0.1, 0.1]", null, out _).Id), Is.Empty);
            Assert.That(b.SimulateNote(b.SetLook(null, "fire", "color", Blue, "element", out _).Id), Is.Empty);
        }

        // ---------- who may save ----------

        [TestCase("save that as a mod", true)]
        [TestCase("Save it", true)]
        [TestCase("make it blue and saved for later", true)]
        [TestCase("make all fire blue", false)]
        [TestCase("add a cantrip that throws a small ball of frost", false)]
        [TestCase("savage fire please", false)]
        [TestCase(null, false)]
        public void OnlyThePlayersOwnWordAsksForASave(string message, bool asks) =>
            Assert.That(ClaudeRun.AsksToSave(message), Is.EqualTo(asks));

        [Test]
        public void ASaveTheChatWasntAskedForIsRefusedAndWritesNothing() =>
            InATempFolder(folders =>
            {
                string dir = folders.Mods;
                var b = NewBench(folders);
                b.SaveRefused = () => ClaudeRun.SaveNotAsked;
                var d = b.SetSpell(null, "fireball", "radius", "3");
                var tools = new RulesTools(b);
                var refused = Assert.Throws<RulesToolException>(() => tools.Save(d.Id, "Bigger", "A wider Fireball."));
                StringAssert.Contains("say save", refused.Message);
                Assert.That(Directory.GetFiles(dir), Is.Empty);
                b.SaveRefused = null;
                StringAssert.Contains("Saved", tools.Save(d.Id, "Bigger", "A wider Fireball."));
                Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
            });

        [Test]
        public void TheRefusalReachesTheModelAsAFailedCall() =>
            InATempFolder(folders =>
            {
                string dir = folders.Mods;
                var b = NewBench(folders);
                b.SaveRefused = () => ClaudeRun.SaveNotAsked;
                var rpc = new McpRpc(new RulesTools(b), "test", "1");
                var call = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = new JObject { ["name"] = "save", ["arguments"] = new JObject { ["draft"] = "d1", ["title"] = "x", ["idea"] = "y" } } };
                var said = JObject.Parse(rpc.Handle(call.ToString()));
                Assert.That((bool)said["result"]["isError"], Is.True);
                Assert.That(Directory.GetFiles(dir), Is.Empty);
            });

        [Test]
        public void AModThatsOnCanBeSavedInALaterMessage() =>
            InATempFolder(folders =>
            {
                string dir = folders.Mods;
                var b = NewBench(folders);
                var d = b.SetSpell(null, "fireball", "radius", "3");
                b.Apply(d.Id, null, "Wide fireball");
                // Each chat message is a new process that knows no draft ids, and may be a new server too: the room remembers.
                var later = new RulesTools(b);
                b.Refresh();
                string said = later.Save("wide-fireball", "Wide fireball", "A wider Fireball.");
                StringAssert.Contains("Saved", said);
                var saved = JObject.Parse(File.ReadAllText(Directory.GetFiles(dir).Single()));
                Assert.That(saved["patch"]["spells"], Is.Not.Null);
                Assert.That(ModPatch.Parse(saved.ToString()).Title, Is.EqualTo("Wide fireball"));
            });

        [Test]
        public void ASaveOfNothingKnownStillSaysSo()
        {
            var b = NewBench();
            StringAssert.Contains("no draft", Assert.Throws<RulesToolException>(() => b.Save("d99", "x", "y")).Message);
        }

        // ---------- where the words go ----------

        [Test]
        public void TheInstructionsStayShortAsEveryPromptWritesThem() =>
            Assert.That(RulesTools.Instructions, Has.Length.LessThan(1000));

        // Claude Code cuts an MCP tool's description at 2,048 characters.
        [Test]
        public void NoToolsDescriptionIsLongEnoughToBeCut()
        {
            var long_ = typeof(RulesTools).GetMethods().Where(m => m.GetCustomAttribute<RulesToolAttribute>() != null)
                .Where(m => m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description.Length > 2000).Select(m => m.Name);
            Assert.That(long_, Is.Empty);
        }
    }
}
