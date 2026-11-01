using System.Collections.Generic;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The MCP protocol as the game serves it (McpRpc): what a client sends, and what comes back.</summary>
    public class McpRpcTests
    {
        sealed class Desk : IRoomDesk
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

        static Workbench Bench() => new Workbench(ChemistryTests.RulesDir());

        static McpRpc Rpc(Workbench bench) => new McpRpc(new RulesTools(bench), "test", "1");

        static JObject Ask(McpRpc rpc, string method, JObject given = null) =>
            JObject.Parse(rpc.Handle(new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = given ?? new JObject() }.ToString()));

        static JObject Call(McpRpc rpc, string tool, JObject arguments = null) =>
            (JObject)Ask(rpc, "tools/call", new JObject { ["name"] = tool, ["arguments"] = arguments ?? new JObject() })["result"];

        [Test]
        public void InitializeAnswersInTheClientsVersion()
        {
            var result = Ask(Rpc(Bench()), "initialize", new JObject { ["protocolVersion"] = "2025-03-26" })["result"];
            Assert.That((string)result["protocolVersion"], Is.EqualTo("2025-03-26"));
            Assert.That(result["capabilities"]["tools"], Is.Not.Null);
        }

        [Test]
        public void ItTellsAClientToWriteTheChangeItself()
        {
            var result = Ask(Rpc(Bench()), "initialize")["result"];
            Assert.That((string)result["instructions"], Does.Contain("build a new draft"));
        }

        [Test]
        public void ASpellDraftSaysFieldByField()
        {
            var bench = Bench();
            var draft = bench.SetSpell(null, "fireball", "radius", "4");
            Assert.That(bench.Changes(draft), Does.Contain("Fireball: radius from 2 to 4"));
            Assert.That(bench.Changes(bench.SetMaterial(null, "grass", "burn.fuel", "4")), Does.Contain("Grass: burn.fuel from 8 to 4"));
        }

        [Test]
        public void ANotificationHasNoAnswer()
        {
            Assert.That(Rpc(Bench()).Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"), Is.Null);
        }

        [Test]
        public void BadJsonIsAnError()
        {
            Assert.That(JObject.Parse(Rpc(Bench()).Handle("{ not json"))["error"], Is.Not.Null);
        }

        [Test]
        public void EveryToolIsListedWithItsSchema()
        {
            var tools = (JArray)Ask(Rpc(Bench()), "tools/list")["result"]["tools"];
            var names = tools.Select(t => (string)t["name"]).ToList();
            Assert.That(names, Is.SupersetOf(new[] { "list", "describe", "validate", "simulate", "save", "room", "apply", "revert", "add_spell", "set_spell", "tune" }));
            foreach (var tool in tools) Assert.That((string)tool["description"], Is.Not.Empty, (string)tool["name"]);
            var apply = tools.First(t => (string)t["name"] == "apply");
            Assert.That(apply["inputSchema"]["required"].Select(r => (string)r), Is.EqualTo(new[] { "mod" }));
            var spell = tools.First(t => (string)t["name"] == "add_spell");
            Assert.That((string)spell["inputSchema"]["properties"]["level"]["type"], Is.EqualTo("integer"));
        }

        [Test]
        public void AToolAnswersInItsResult()
        {
            var result = Call(Rpc(Bench()), "list", new JObject { ["table"] = "spells" });
            Assert.That((bool)result["isError"], Is.False);
            Assert.That((string)result["content"][0]["text"], Does.Contain("fireball"));
        }

        [Test]
        public void ARefusalIsOneSentenceNotAProtocolError()
        {
            var result = Call(Rpc(Bench()), "list", new JObject { ["table"] = "nonsense" });
            Assert.That((bool)result["isError"], Is.True);
            Assert.That((string)result["content"][0]["text"], Is.Not.Empty);
        }

        [Test]
        public void AMissingArgumentIsASentence()
        {
            var result = Call(Rpc(Bench()), "list");
            Assert.That((bool)result["isError"], Is.True);
            Assert.That((string)result["content"][0]["text"], Does.Contain("table"));
        }

        [Test]
        public void AnArgumentOfTheWrongKindIsASentence()
        {
            var result = Call(Rpc(Bench()), "simulate", new JObject { ["draft"] = "big-fireball", ["scenario"] = "flood", ["ticks"] = "many" });
            Assert.That((bool)result["isError"], Is.True);
            Assert.That((string)result["content"][0]["text"], Does.Contain("ticks"));
        }

        [Test]
        public void ADraftNeedsATitleAndItsAnswerSaysWhatChanged()
        {
            var bench = Bench();
            var desk = new Desk();
            bench.Desk = desk;
            var rpc = Rpc(bench);
            var text = (string)Call(rpc, "tune", new JObject { ["param"] = "fire.spreadChance", ["value"] = "0.2" })["content"][0]["text"];
            string draft = text.Split(' ', '\n', '.', ':', ',').First(w => w.Length > 1 && w[0] == 'd' && char.IsDigit(w[1]));
            var refused = Call(rpc, "apply", new JObject { ["mod"] = draft });
            Assert.That((bool)refused["isError"], Is.True);
            Assert.That(desk.Asked, Is.Empty);
            var said = (string)Call(rpc, "apply", new JObject { ["mod"] = draft, ["title"] = "Faster fire" })["content"][0]["text"];
            Assert.That(desk.Asked[0].Title, Is.EqualTo("Faster fire"));
            Assert.That(desk.Asked[0].Mod, Is.EqualTo("faster-fire"));
            Assert.That(said, Does.Contain("It changes fire.spreadChance from"));
            Assert.That(said, Does.Contain("to 0.2"));
        }

        [Test]
        public void TheRoomToolsGoToTheGamesDesk()
        {
            var bench = Bench();
            var desk = new Desk();
            bench.Desk = desk;
            var rpc = Rpc(bench);
            Assert.That((string)Call(rpc, "room")["content"][0]["text"], Is.EqualTo("the game's room"));
            Assert.That((string)Call(rpc, "apply", new JObject { ["mod"] = "big-fireball", ["title"] = "Bigger" })["content"][0]["text"], Is.EqualTo("decided"));
            Assert.That(desk.Asked[0].Mod, Is.EqualTo("big-fireball"));
            Assert.That(desk.Asked[0].Title, Is.EqualTo("Bigger"));
            Assert.That(desk.Asked[0].Json, Does.Contain("radius"));
            Call(rpc, "revert", new JObject { ["mod"] = "big-fireball" });
            Assert.That(desk.Asked[1].Change, Is.EqualTo(RoomChange.Revert));
        }
    }
}
