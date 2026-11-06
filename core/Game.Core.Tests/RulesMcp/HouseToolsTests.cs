using System;
using System.IO;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Building part of the game by asking and keeping it (J-059, J-062): the two asks from a headless
    /// client, over the MCP's own messages, ending as one mod that survives save and reload.
    /// </summary>
    public class HouseToolsTests
    {
        string data, dir;
        GameFolders folders;

        // A player's data folder; dir is their mods in it, where a save goes.
        [SetUp]
        public void MakeFolder()
        {
            data = Path.Combine(Path.GetTempPath(), "house-tools-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(ChemistryTests.RulesDir(), data);
            dir = Directory.CreateDirectory(folders.Mods).FullName;
        }

        [TearDown]
        public void RemoveFolder() => Directory.Delete(data, true);

        // A headless client: JSON-RPC to the tools, as a player's Claude talks to the game.
        internal sealed class Client
        {
            readonly McpRpc rpc;
            int next = 1;

            public Client(Workbench bench)
            {
                rpc = new McpRpc(new RulesTools(bench), "test", "1");
            }

            JObject Send(string tool, object arguments)
            {
                var message = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = next++,
                    ["method"] = "tools/call",
                    ["params"] = new JObject { ["name"] = tool, ["arguments"] = JObject.FromObject(arguments) },
                };
                return JObject.Parse(rpc.Handle(message.ToString()));
            }

            public string Call(string tool, object arguments)
            {
                var answer = Send(tool, arguments);
                var text = (string)answer["result"]?["content"]?[0]?["text"];
                Assert.That(text, Is.Not.Null, answer.ToString());
                Assert.That((bool?)answer["result"]["isError"] ?? false, Is.False, $"{tool}: {text}");
                return text;
            }

            public string Refused(string tool, object arguments)
            {
                var answer = Send(tool, arguments);
                Assert.That((bool?)answer["result"]?["isError"], Is.True, answer.ToString());
                return (string)answer["result"]["content"][0]["text"];
            }
        }

        static string DraftId(string shown) => shown.Substring(shown.IndexOf("Draft ", StringComparison.Ordinal) + 6).Split(':')[0];

        static string FirstId(string found) => found.Split('\n')[1].Substring(2).Split(':')[0];

        static string FirstSite(string sites) => sites.Split('\n')[1].Substring(2).Split(',')[0] + "," + sites.Split('\n')[1].Substring(2).Split(',')[1];

        // Both asks, as Claude would answer them, in one draft. Returns the saved mod's name and the world's state.
        (string mod, string text) BothAsks(Workbench bench)
        {
            var client = new Client(bench);

            // "Add a wing to the house by the lake."
            string found = client.Call("find", new { kind = "building", near = "lake" });
            string house = FirstId(found);
            Assert.That(found, Does.Contain("cabin"));
            string d = DraftId(client.Call("add_wing", new { building = house }));

            // "Build a house on the beach", of stucco, with its roof tile in the same mod.
            client.Call("add_material", new { kind = "terrain", id = "clay-tile", name = "Clay tile", tags = new[] { "roof" }, verbs = "{\"look\": {\"family\": \"ground\", \"color\": [0.66, 0.3, 0.2]}}", draft = d });
            client.Call("add_building_part", new { table = "styles", id = "southwest", row = BuildingTests.SouthwestStyle, draft = d });
            string sites = client.Call("find_site", new { width = 8, depth = 6, near = "sea", edge = "shore" });
            Assert.That(sites, Does.Contain("Sand"));
            string at = FirstSite(sites);
            client.Call("add_building", new { at, width = 8, depth = 6, style = "southwest", id = "beach-house#1", front = "south", draft = d });

            Assert.That(client.Call("validate", new { draft = d }), Does.StartWith("No problems"));
            string saved = client.Call("save", new { draft = d, title = "A wing and a beach house", idea = "The house by the lake gets a wing, and a stucco house goes up on the beach.", name = "wing-and-beach" });
            Assert.That(saved, Does.Contain("wing-and-beach"));
            string kept = File.ReadAllText(Path.Combine(dir, "wing-and-beach.json"));
            return ("wing-and-beach", kept);
        }

        [Test]
        public void BothAsksEndAsOneModThatSurvivesSaveAndReload()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            var (mod, kept) = BothAsks(bench);

            var rows = JObject.Parse(kept)["patch"]["world"]["buildings"];
            Assert.That(((JObject)rows).Count, Is.EqualTo(2), "one edit by id and one new building, in one mod");
            Assert.That(rows["beach-house#1"], Is.Not.Null);
            Assert.That(rows.Children<JProperty>().Select(p => p.Name), Has.Some.StartWith("lake-house#"), "the house by the lake");

            // Switched on from the kept file, it builds both; nothing but the row names anything.
            var world = bench.LiveWorld;
            string before = world.CellHash();
            var plain = new Workbench(folders).LiveWorld;
            string on = client.Call("apply", new { mod });
            plain.Step();
            Assert.That(on, Does.Contain("switched on"), on);
            Assert.That(world.CellHash(), Is.Not.EqualTo(before));
            Assert.That(world.CellHash(), Is.Not.EqualTo(plain.CellHash()), "it's in the cell hash");
            var house = world.BuildingRowOf("beach-house#1");
            Assert.That(world.BuildingCellsOf(house).Count(c => c.Terrain == "stucco-wall"), Is.GreaterThan(0));
            Assert.That(world.BuildingCellsOf(house).Count(c => c.Terrain == "door"), Is.EqualTo(1));
            Assert.That(world.BuildingCellsOf(house).Count(c => c.Terrain == "rock"), Is.GreaterThan(0), "new houses are hollow");
            string wing = ((JObject)rows).Properties().Select(p => p.Name).Single(n => n.StartsWith("lake-house#", StringComparison.Ordinal));
            Assert.That(world.BuildingRowOf(wing).Walls.Any(w => w.Id.StartsWith("wing1", StringComparison.Ordinal)));
            Assert.That(client.Call("find", new { kind = "building", near = "lake", within = 12 }), Does.Contain(wing));
            Assert.That(client.Call("whats_on", new { }), Does.Contain("the building beach-house#1"));
            Assert.That(client.Call("describe", new { table = "buildings", id = "beach-house#1" }), Does.Contain("stucco-wall"), "the tools read what's on");

            // The world's save keeps it: a load has the same cells and room, and reverting then puts all back.
            var source = bench.LiveSource;
            string save = world.Save(source);
            var loaded = World.Load(save, bench.Base, source, bench.Budgets);
            Assert.That(loaded.CellHash(), Is.EqualTo(world.CellHash()));
            Assert.That(loaded.CastHash(), Is.EqualTo(world.CastHash()));
            var built = world.BuildingCellsOf(house).Concat(world.BuildingCellsOf(world.BuildingRowOf(wing))).ToList();
            Assert.That(loaded.ApplyRoom(RoomCommand.Revert(Workbench.HostName, mod)).Done, Is.True);
            Assert.That(built.All(c => loaded.At(c.X, c.Y).T == plain.At(c.X, c.Y).T), "reverting puts back what stood there");

            // A new session reads the kept mod and gets the same world.
            var again = new Workbench(folders);
            Assert.That(new Client(again).Call("apply", new { mod }), Does.Contain("switched on"));
            Assert.That(again.LiveWorld.CellHash(), Is.EqualTo(world.CellHash()));
        }

        [Test]
        public void TwoRunsAreIdentical()
        {
            var first = BothAsks(new Workbench(folders));
            File.Delete(Path.Combine(dir, first.mod + ".json"));
            var second = BothAsks(new Workbench(folders));
            Assert.That(second.text, Is.EqualTo(first.text), "the same asks make the same mod");
            var a = new Workbench(folders);
            var b = new Workbench(folders);
            a.Apply(first.mod);
            b.Apply(first.mod);
            Assert.That(b.LiveWorld.CellHash(), Is.EqualTo(a.LiveWorld.CellHash()));
            Assert.That(b.LiveWorld.CastHash(), Is.EqualTo(a.LiveWorld.CastHash()));
        }

        [Test]
        public void FindNamesBuildingsAndThings()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            Assert.That(client.Call("find", new { kind = "building", near = "hollowmere", within = 20 }), Does.Contain("Hollowmere#1"));
            var thing = bench.Budgets.Things.All[0];
            string near = $"[{thing.CellX}, {thing.CellY}]";
            Assert.That(client.Call("find", new { kind = "thing", near, within = 2 }), Does.Contain(thing.Kind).And.Contain(thing.Id.ToString()));
            Assert.That(client.Call("find", new { kind = thing.Kind, near, within = 2 }), Does.Contain(thing.Id.ToString()));
            Assert.That(client.Refused("find", new { near = "nowhere-at-all" }), Does.Contain("neither a place nor a building"));
            Assert.That(client.Call("list", new { table = "buildings" }), Does.Contain("cabin#2"));
            Assert.That(client.Call("describe", new { table = "buildings", id = "cabin#2" }), Does.Contain("built"));
            Assert.That(client.Call("list", new { table = "openings" }), Does.Contain("double-garage"));
        }

        [Test]
        public void FindSiteReadsTheMapItWasGiven()
        {
            string repo = Path.Combine(Path.GetTempPath(), "map-site-" + Guid.NewGuid().ToString("N"));
            try
            {
                var valley = new GameFolders(Path.Combine(repo, "rules"));
                var isle = new GameFolders(valley.Rules, null, "isle");
                CopyFiles(ChemistryTests.RulesDir(), valley.Rules);
                CopyFiles(ChemistryTests.WorldDir(), valley.World);
                CopyFiles(ChemistryTests.WorldDir(), isle.World);
                File.WriteAllText(Path.Combine(isle.World, WorldPlaces.FileName), @"{ ""places"": [ { ""id"": ""dock"", ""name"": ""the dock"", ""kind"": ""village"", ""area"": [10, 10, 20, 20] } ] }");

                Assert.That(new Client(new Workbench(valley)).Call("find_site", new { width = 6, depth = 6, near = "lake" }), Does.Contain("lake"), "the valley's lake");
                var client = new Client(new Workbench(isle));
                Assert.That(() => client.Call("find_site", new { width = 6, depth = 6, near = "lake" }), Throws.Exception.With.Message.Contain("dock"), "no lake on this map");
            }
            finally
            {
                Directory.Delete(repo, true);
            }
        }

        static void CopyFiles(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        [Test]
        public void FindSiteUsesAShoreAndASide()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            var lake = bench.Places.Find("lake");
            string north = client.Call("find_site", new { width = 6, depth = 6, near = "lake", side = "north" });
            var (x, y) = Parse(north);
            Assert.That(north, Does.Contain("north shore"));
            Assert.That(y, Is.LessThan(lake.Middle.y), "north of the lake's middle");
            for (int dy = 0; dy < 6; dy++)
                for (int dx = 0; dx < 6; dx++)
                    Assert.That(bench.LiveWorld.CellProblem(x + dx, y + dy), Is.Null, $"a free cell at {x + dx}, {y + dy}");
            Assert.That(client.Refused("find_site", new { width = 6, depth = 6, near = "lake", side = "up" }), Does.Contain("isn't a side"));
            Assert.That(client.Refused("find_site", new { width = 40, depth = 6, near = "lake" }), Does.Contain("at most 24"));
        }

        static (int x, int y) Parse(string sites)
        {
            var parts = sites.Split('\n')[1].Substring(2).TrimStart('[').Split(']')[0].Split(',');
            return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        [Test]
        public void ABadSiteIsRefusedAtOnceAndAtTheTick()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            var lake = bench.Places.Find("lake");
            string shown = client.Call("add_building", new { at = $"[{lake.Middle.x}, {lake.Middle.y}]", width = 4, depth = 4 });
            Assert.That(shown, Does.Contain("won't stand").And.Contain("water"));
            string d = DraftId(shown);
            Assert.That(client.Call("validate", new { draft = d }), Does.Contain("[site]").And.Contain("water"));
            string said = client.Call("apply", new { mod = d, title = "A house in the lake" });
            Assert.That(said, Does.Contain("refused").And.Contain("water"));
        }

        [Test]
        public void AWingOnAHollowHouseGoesThroughItsWall()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            string sites = client.Call("find_site", new { width = 8, depth = 6, near = "hollowmere", within = 60 });
            string at = FirstSite(sites);
            string d = DraftId(client.Call("add_building", new { at, width = 8, depth = 6, id = "t#1", front = "south" }));
            string edit = client.Call("add_wing", new { building = "t#1", side = "north", width = 4, depth = 3, draft = d });
            Assert.That(edit, Does.Not.Contain("won't stand"));
            var row = JObject.Parse(edit.Substring(edit.IndexOf('{')))["world"]["buildings"]["t#1"];
            Assert.That(row["walls"]["wing1-far"], Is.Not.Null);
            Assert.That(row["roofs"]["main"]["over"].Count(), Is.EqualTo(8), "the roof's outline goes round the wing");
            Assert.That(row["walls"]["north"]["openings"].Single()["kind"].ToString(), Is.EqualTo("door"), "its window under the wing goes, and a door takes it through");
            Assert.That(client.Call("apply", new { mod = d, title = "House and wing" }), Does.Contain("switched on"));
        }

        [Test]
        public void AVillageBuildingEditedByIdChangesOnlyItsCellsAndComesBack()
        {
            var bench = new Workbench(folders);
            var world = bench.LiveWorld;
            var row = world.BuildingRowOf("Hollowmere#1");
            var was = world.BuildingCellsOf(row).Select(c => (c.X, c.Y, c.Terrain)).ToList();
            string other = "Hollowmere#2";
            var others = world.BuildingCellsOf(world.BuildingRowOf(other)).Select(c => (c.X, c.Y, c.Terrain)).ToList();
            int x = row.X, y = row.Y;

            // A smaller building in the same place: what it gives up goes to bare ground.
            var patch = new JObject { ["world"] = new JObject { ["buildings"] = new JObject { ["Hollowmere#1"] = JObject.Parse("{ \"at\": [" + x + ", " + y + "], \"style\": \"village\", \"joins\": { \"a\": [0, 0], \"b\": [6, 0], \"c\": [6, 6], \"d\": [0, 6] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" }, \"e\": { \"from\": \"b\", \"to\": \"c\" }, \"s\": { \"from\": \"c\", \"to\": \"d\" }, \"w\": { \"from\": \"d\", \"to\": \"a\" } } }") } } };
            Assert.That(world.ApplyRoom(RoomCommand.Apply("host", "smaller", patch.ToString())).Done, Is.True);
            Assert.That(world.At(x + 3, y).T, Is.EqualTo("dirt"), "a cell it gave up");
            Assert.That(others.All(c => world.At(c.X, c.Y).T == c.Terrain), "the next house stands as it did");
            Assert.That(world.ApplyRoom(RoomCommand.Revert("host", "smaller")).Done, Is.True);
            Assert.That(was.All(c => world.At(c.X, c.Y).T == c.Terrain), "the revert lays it as it was");
        }

        [Test]
        public void AModsBuiltCellsStayABuildingsSize()
        {
            var bench = new Workbench(folders);
            var problems = bench.Validate("{ \"world\": { \"buildings\": { \"t#1\": { \"at\": [10, 10], \"style\": \"cabin\", \"built\": { \"logwall\": [[0, 0, 2000000]] } } } } }");
            Assert.That(problems.Select(p => p.Message), Has.Some.Contains("built cells"));
        }

        [Test]
        public void SetBuildingAndRemoveTakeARowWhole()
        {
            var bench = new Workbench(folders);
            var client = new Client(bench);
            string shown = client.Call("set_building", new { id = "cabin#2", field = "wall.finish", value = "{\"pattern\": \"worn\", \"color\": \"#8a6a4a\"}" });
            string d = DraftId(shown);
            Assert.That(client.Call("validate", new { draft = d }), Does.StartWith("No problems"));
            var cells = bench.LiveWorld.BuildingCellsOf(bench.LiveWorld.BuildingRowOf("cabin#2")).Select(c => (c.X, c.Y, c.Terrain)).ToList();
            Assert.That(client.Call("apply", new { mod = d, title = "Worn cabin" }), Does.Contain("switched on"));
            Assert.That(bench.LiveWorld.BuildingRowOf("cabin#2").Json["wall"]["finish"]["pattern"].ToString(), Is.EqualTo("worn"));
            Assert.That(cells.All(c => bench.LiveWorld.At(c.X, c.Y).T == c.Terrain), "a finish is only drawn");
            Assert.That(client.Call("remove", new { draft = d, table = "buildings", id = "cabin#2" }), Does.Not.Contain("cabin#2"));
        }
    }
}
