using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// J-055's test of the tools: "put a rare golden squirrel somewhere in the woods for us to find", from a client
    /// of the MCP, ends as one mod. The row lives here and in the mod it makes, nowhere in the code or the base rules.
    /// </summary>
    [Category("Simulation")]
    public class HiddenThingTests
    {
        // Family creature's note (rules/looks.json), which is all a model hears of what a glow means.
        const string GlowHint = "glow 0.1 is a soft glow";

        string data, dir;
        GameFolders folders;

        // A player's data folder; dir is their mods in it, where a save goes.
        [SetUp]
        public void MakeDir()
        {
            data = Path.Combine(Path.GetTempPath(), "hidden-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(ChemistryTests.RulesDir(), data);
            dir = Directory.CreateDirectory(folders.Mods).FullName;
        }

        [TearDown]
        public void RemoveDir() => Directory.Delete(data, true);

        // What the asking player's Claude calls, in order. Returns the kept mod's text.
        string Ask(Workbench bench, string file = "golden-squirrel")
        {
            var client = new HouseToolsTests.Client(bench);
            Assert.That(client.Call("places", new { }), Does.Contain("hollowmere"));
            string looks = client.Call("list", new { table = "looks" });
            Assert.That(looks, Does.Contain("species deer, rabbit, squirrel"), "or a client has to guess, and be refused");
            Assert.That(looks, Does.Contain(GlowHint), "read before a client picks a glow");
            Assert.That(client.Call("describe", new { table = "items", id = "ball" }), Does.Contain("baked model"));
            string sites = client.Call("find_site", new { width = 1, depth = 1, near = "hollowmere", within = 40 });
            var spot = Regex.Match(sites, @"\[(\d+), (\d+)\], on");
            Assert.That(spot.Success, sites);
            string made = client.Call("add_item", new
            {
                id = "golden-squirrel",
                name = "Golden squirrel",
                mass = 0.3,
                material = "flesh",
                look = "{\"family\": \"creature\", \"species\": \"squirrel\", \"color\": [1, 0.78, 0.2], \"scale\": 1}",
            });
            string draft = Regex.Match(made, @"Draft (d\d+)").Groups[1].Value;
            Assert.That(client.Call("set_look", new { id = "golden-squirrel", param = "glow", value = "0.1", table = "item", draft }), Does.Contain(GlowHint), "the reply says what the number means");
            string put = client.Call("add_thing", new { kind = "golden-squirrel", at = $"[{spot.Groups[1].Value}, {spot.Groups[2].Value}]", draft });
            Assert.That(put, Does.Not.Contain("won't stand"));
            Assert.That(client.Call("validate", new { draft }), Does.StartWith("No problems"));
            Assert.That(client.Call("save", new { draft, title = "A rare golden squirrel", idea = "Somewhere near the village sits a golden squirrel, for whoever finds it.", name = file }), Does.Contain("Saved"));
            return File.ReadAllText(Path.Combine(dir, file + ".json"));
        }

        static (ThingSpawn thing, World world) On(Workbench bench)
        {
            string said = new HouseToolsTests.Client(bench).Call("apply", new { mod = "golden-squirrel" });
            Assert.That(said, Does.Contain("switched on"), said);
            var world = bench.LiveWorld;
            return (world.ThingsNow.All.Single(t => t.Kind == "golden-squirrel"), world);
        }

        [Test]
        public void TheAskEndsAsOneModThatAnyPeerAgreesOn()
        {
            var bench = new Workbench(folders);
            string kept = Ask(bench);

            // One mod: an item with a creature look, added at the spot the tools gave. Nothing else.
            var patch = (JObject)JObject.Parse(kept)["patch"];
            Assert.That(patch.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "items", "world" }));
            Assert.That((string)patch["items"]["golden-squirrel"]["look"]["family"], Is.EqualTo("creature"));
            Assert.That((double)patch["items"]["golden-squirrel"]["look"]["glow"], Is.EqualTo(0.1));
            Assert.That(((JArray)patch["world"]["add"]).Single()["kind"].ToString(), Is.EqualTo("golden-squirrel"));

            // Switched on, it stands in the world; the tools see it; it leaves the cells alone.
            var plain = new Workbench(folders).LiveWorld;
            int things = plain.ThingsNow.Count;
            var (thing, world) = On(bench);
            plain.Step();
            var client = new HouseToolsTests.Client(bench);
            Assert.That(thing.FromMod);
            Assert.That(world.ThingsNow.Count, Is.EqualTo(things + 1));
            Assert.That(world.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(world.Rules.Item("golden-squirrel").Look.Species, Is.EqualTo("squirrel"));
            Assert.That(client.Call("find", new { kind = "golden-squirrel", near = $"[{thing.CellX}, {thing.CellY}]" }), Does.Contain(thing.Id.ToString()));
            Assert.That(client.Call("whats_on", new { }), Does.Contain("a golden-squirrel at"));
            Assert.That(client.Call("find_site", new { width = 1, depth = 1, near = $"[{thing.CellX}, {thing.CellY}]", within = 0 }), Does.Contain("No site"), "nothing else may stand on it");

            // Save and reload: the world's save, then a new session from the kept file.
            var source = bench.LiveSource;
            string save = world.Save(source);
            var loaded = World.Load(save, bench.Base, source, bench.Budgets);
            Assert.That(loaded.ThingsNow.Find(thing.Id)?.Kind, Is.EqualTo("golden-squirrel"));
            Assert.That(loaded.ThingsNow.Fingerprint, Is.EqualTo(world.ThingsNow.Fingerprint));
            Assert.That(loaded.CastHash(), Is.EqualTo(world.CastHash()));
            var (again, _) = On(new Workbench(folders));
            Assert.That(again.Id, Is.EqualTo(thing.Id));

            // A revert takes it all away.
            Assert.That(client.Call("revert", new { mod = "golden-squirrel" }), Does.Contain("switched off"));
            Assert.That(world.ThingsNow.Find(thing.Id), Is.Null);
            Assert.That(world.Rules.Item("golden-squirrel"), Is.Null);
            Assert.That(world.Rules.Fingerprint, Is.EqualTo(bench.Base.Fingerprint));
        }

        [Test]
        public void TwoRunsOfTheAskAreTheSame()
        {
            (string, string, string, ulong) Run()
            {
                var ownFolders = new GameFolders(ChemistryTests.RulesDir(), Path.Combine(data, Guid.NewGuid().ToString("N")));
                string own = Directory.CreateDirectory(ownFolders.Mods).FullName;
                var bench = new Workbench(ownFolders);
                var client = new HouseToolsTests.Client(bench);
                string sites = client.Call("find_site", new { width = 1, depth = 1, near = "hollowmere", within = 40 });
                var spot = Regex.Match(sites, @"\[(\d+), (\d+)\], on");
                string draft = Regex.Match(client.Call("add_item", new { id = "golden-squirrel", name = "Golden squirrel", mass = 0.3, material = "flesh", look = "{\"species\": \"squirrel\", \"color\": [1, 0.78, 0.2]}" }), @"Draft (d\d+)").Groups[1].Value;
                client.Call("add_thing", new { kind = "golden-squirrel", at = $"[{spot.Groups[1].Value}, {spot.Groups[2].Value}]", draft });
                client.Call("save", new { draft, title = "A rare golden squirrel", idea = "One to find.", name = "golden-squirrel" });
                var (thing, world) = On(bench);
                return (File.ReadAllText(Path.Combine(own, "golden-squirrel.json")), world.Rules.Fingerprint, world.CastHash() + world.CellHash(), thing.Id.Value);
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }

        [Test]
        public void TwoPeersAndALateJoinerAgreeOnWhereItIsAndWhoFoundIt()
        {
            string kept = Ask(new Workbench(folders));
            // Candidates at a chance as well, which is the "rare": each peer decides them alone.
            var mod = JObject.Parse(kept);
            var rows = (JArray)mod["patch"]["world"]["add"];
            var at = (JArray)rows[0]["cell"];
            for (int i = 1; i <= 5; i++) rows.Add(new JObject { ["kind"] = "golden-squirrel", ["cell"] = new JArray((int)at[0] + i, (int)at[1]), ["chance"] = 0.2 });
            string json = mod.ToString();

            var a = new Workbench(folders).LiveWorld;
            var b = new Workbench(folders).LiveWorld;
            (World peer, RuleSet rules)[] peers = { (a, a.RoomRules), (b, b.RoomRules) };
            ThingId found = ThingId.None;
            for (int t = 1; t <= 30; t++)
            {
                for (int p = 0; p < peers.Length; p++)
                {
                    var batch = new TickBatch(t);
                    if (t == 3) batch.Room.Add(RoomCommand.Apply("Ana", "golden-squirrel", json));
                    if (t == 9)
                    {
                        found = peers[p].peer.ThingsNow.All.First(x => x.Kind == "golden-squirrel").Id;
                        batch.Room.Add(RoomCommand.Found("Bo", found));
                    }
                    if (t == 12) batch.Room.Add(RoomCommand.Found("Ana", found));
                    peers[p].rules = batch.RunOn(peers[p].peer, peers[p].rules, out _);
                }
                Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()), $"tick {t}");
                Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()), $"tick {t}");
                Assert.That(b.ThingsNow.Fingerprint, Is.EqualTo(a.ThingsNow.Fingerprint), $"tick {t}");
            }
            Assert.That(a.Log.Count(l => l.Text == "Bo found the golden squirrel"), Is.EqualTo(1), "the row's name, the finder's, once");
            Assert.That(b.Log.Count(l => l.Text == "Bo found the golden squirrel"), Is.EqualTo(1));
            Assert.That(a.Log.Any(l => l.Text.StartsWith("Ana found", StringComparison.Ordinal)), Is.False);

            // A late joiner: the room as its history starts, then the commands since.
            var late = new Workbench(folders).LiveWorld;
            var batches = TickBatch.Timeline(Array.Empty<(int, CastCommand)>(), null, 30);
            TickBatch.AddRoom(batches, new[] { (3, RoomCommand.Apply("Ana", "golden-squirrel", json)), (9, RoomCommand.Found("Bo", found)) });
            var rules = late.RoomRules;
            for (int t = 1; t <= 30; t++) rules = batches[t].RunOn(late, rules, out _);
            Assert.That(late.ThingsNow.Fingerprint, Is.EqualTo(a.ThingsNow.Fingerprint));
            Assert.That(late.Room.Found, Is.EqualTo(a.Room.Found));
        }

        // What a real Claude wrote, with only the game's tools, in a headless run (docs/RULES_MCP.md "Things"): kept as it came, but for its glow.
        static string WrittenByAClient() =>
            File.ReadAllText(Path.Combine(ChemistryTests.Folders.Repo, "core", "Rules.Mcp.Tests", "golden-squirrel.mod.json"));

        [Test]
        public void TheModAHeadlessClientWroteHoldsThroughRunsPeersAndASave()
        {
            string json = WrittenByAClient();
            var patch = (JObject)JObject.Parse(json)["patch"];
            Assert.That(patch.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "items", "world" }), "one mod: a kind and where it stands");
            Assert.That((string)patch["items"]["golden-squirrel"]["look"]["family"], Is.EqualTo("creature"));
            Assert.That(((JArray)patch["world"]["add"]).All(t => (string)t["kind"] == "golden-squirrel"));

            (World world, Workbench bench) Run()
            {
                var bench = new Workbench(folders);
                var world = bench.LiveWorld;
                var rules = world.RoomRules;
                for (int t = 1; t <= 12; t++)
                {
                    var batch = new TickBatch(t);
                    if (t == 3) batch.Room.Add(RoomCommand.Apply("Ana", "golden-squirrel", json));
                    if (t == 9) batch.Room.Add(RoomCommand.Found("Bo", world.ThingsNow.All.First(x => x.Kind == "golden-squirrel").Id));
                    rules = batch.RunOn(world, rules, out _);
                }
                return (world, bench);
            }

            var (a, bench) = Run();
            var (b, _) = Run();
            Assert.That(a.Room.Mods.Select(m => m.Id), Is.EqualTo(new[] { "golden-squirrel" }), "every peer took it");
            Assert.That(b.ThingsNow.Fingerprint, Is.EqualTo(a.ThingsNow.Fingerprint));
            Assert.That(b.ThingsNow.All.Where(t => t.FromMod).Select(t => t.Id), Is.EqualTo(a.ThingsNow.All.Where(t => t.FromMod).Select(t => t.Id)));
            Assert.That(b.Rules.Fingerprint, Is.EqualTo(a.Rules.Fingerprint));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(a.Log.Count(l => l.Text == "Bo found the golden squirrel"), Is.EqualTo(1));

            var loaded = World.Load(a.Save(bench.LiveSource), bench.Base, bench.LiveSource, bench.Budgets);
            Assert.That(loaded.ThingsNow.Fingerprint, Is.EqualTo(a.ThingsNow.Fingerprint));
            Assert.That(loaded.Room.Found, Is.EqualTo(a.Room.Found));
            Assert.That(loaded.CastHash(), Is.EqualTo(a.CastHash()));
        }

        // ---------- a draft's own thing, once it's on ----------

        // A golden squirrel on a clear cell, drafted and switched on. Returns the draft and the cell.
        static (HouseToolsTests.Client client, string draft, string at) Applied(Workbench bench, string title = "A squirrel to find")
        {
            var client = new HouseToolsTests.Client(bench);
            string at = ClearCell(client, 0);
            string draft = Regex.Match(client.Call("add_item", new { id = "golden-squirrel", name = "Golden squirrel", mass = 0.3, material = "flesh", look = "{\"species\": \"squirrel\", \"color\": [1, 0.78, 0.2]}" }), @"Draft (d\d+)").Groups[1].Value;
            client.Call("add_thing", new { kind = "golden-squirrel", at, draft });
            string said = client.Call("apply", new { mod = draft, title });
            Assert.That(said, Does.Contain("switched on"), said);
            return (client, draft, at);
        }

        static string ClearCell(HouseToolsTests.Client client, int skip)
        {
            string sites = client.Call("find_site", new { width = 1, depth = 1, near = "hollowmere", within = 40 });
            var spot = Regex.Matches(sites, @"\[(\d+), (\d+)\], on")[skip];
            return $"[{spot.Groups[1].Value}, {spot.Groups[2].Value}]";
        }

        const string Kept = "One to find.";

        [Test]
        public void AnAppliedDraftSavesByItsId()
        {
            var bench = new Workbench(folders);
            var (client, draft, _) = Applied(bench);

            Assert.That(client.Call("validate", new { draft }), Does.StartWith("No problems"));
            Assert.That(client.Call("save", new { draft, title = "A squirrel to find", idea = Kept }), Does.Contain("Saved"));
            var patch = JObject.Parse(File.ReadAllText(Path.Combine(dir, "a-squirrel-to-find.json")))["patch"];
            Assert.That(patch["world"]["add"].Count(), Is.EqualTo(1), "the draft's row, not the draft's and the mod's");
            Assert.That(bench.LiveWorld.ThingsNow.All.Count(t => t.Kind == "golden-squirrel"), Is.EqualTo(1), "a save changes nothing in the world");
        }

        [Test]
        public void AnAppliedDraftChangedSinceSavesToo()
        {
            var bench = new Workbench(folders);
            var (client, draft, at) = Applied(bench);

            client.Call("add_thing", new { kind = "golden-squirrel", at = ClearCell(client, 1), draft });
            Assert.That(client.Call("validate", new { draft }), Does.StartWith("No problems"), "a new row on a clear cell");
            Assert.That(client.Call("add_thing", new { kind = "ball", at, draft }), Does.Not.Contain("won't stand"), "its own thing is what stands there");
            Assert.That(client.Call("save", new { draft, title = "Two to find", idea = Kept }), Does.Contain("Saved"));
        }

        [Test]
        public void AnAppliedDraftStillRefusesAnotherModsThing()
        {
            var bench = new Workbench(folders);
            var (client, draft, _) = Applied(bench);
            string there = ClearCell(client, 1);
            string other = Regex.Match(client.Call("add_thing", new { kind = "ball", at = there }), @"Draft (d\d+)").Groups[1].Value;
            Assert.That(client.Call("apply", new { mod = other, title = "A ball" }), Does.Contain("switched on"));

            client.Call("add_thing", new { kind = "golden-squirrel", at = there, draft });
            string said = client.Refused("save", new { draft, title = "Over the ball", idea = Kept });
            Assert.That(said, Does.Contain("a ball (").And.Contain("stands there"), said);
            Assert.That(Directory.GetFiles(dir), Is.Empty);
        }

        [Test]
        public void AnAppliedDraftStillRefusesABaseThing()
        {
            var bench = new Workbench(folders);
            var (client, draft, _) = Applied(bench);
            var based = bench.LiveWorld.ThingsNow.All.First(t => !t.FromMod);

            client.Call("add_thing", new { kind = "golden-squirrel", at = $"[{based.CellX}, {based.CellY}]", draft });
            string said = client.Refused("save", new { draft, title = "Over a base thing", idea = Kept });
            Assert.That(said, Does.Contain($"a {based.Kind} (").And.Contain("stands there"), said);
            Assert.That(Directory.GetFiles(dir), Is.Empty);
        }

        [Test]
        public void ASecondDraftOnTheSameCellIsStillRefused()
        {
            var bench = new Workbench(folders);
            var (client, _, at) = Applied(bench);

            string other = Regex.Match(client.Call("add_thing", new { kind = "golden-squirrel", at }), @"Draft (d\d+)").Groups[1].Value;
            string said = client.Refused("save", new { draft = other, title = "The same again", idea = Kept });
            Assert.That(said, Does.Contain("golden-squirrel (").And.Contain("stands there"), said);
        }

        [Test]
        public void AnotherModUnderTheSameIdIsNotTheDraftsOwn()
        {
            var bench = new Workbench(folders);
            var (client, draft, at) = Applied(bench);
            Assert.That(client.Call("revert", new { mod = "a-squirrel-to-find" }), Does.Contain("switched off"));
            string other = Regex.Match(client.Call("add_item", new { id = "silver-squirrel", name = "Silver squirrel", mass = 0.3, material = "flesh", look = "{\"species\": \"squirrel\", \"color\": [0.8, 0.8, 0.8]}" }), @"Draft (d\d+)").Groups[1].Value;
            client.Call("add_thing", new { kind = "silver-squirrel", at, draft = other });
            Assert.That(client.Call("apply", new { mod = other, title = "A squirrel to find" }), Does.Contain("switched on"));

            string said = client.Refused("save", new { draft, title = "Mine", idea = Kept });
            Assert.That(said, Does.Contain("silver-squirrel (").And.Contain("stands there"), said);
        }

        [Test]
        public void TheChatsRefusalStillComesFirstAndAModOnStillSavesByItsId()
        {
            var bench = new Workbench(folders);
            var (client, draft, _) = Applied(bench);

            bench.SaveRefused = () => ClaudeRun.SaveNotAsked;
            Assert.That(client.Refused("save", new { draft, title = "Not asked", idea = Kept }), Does.Contain(ClaudeRun.SaveNotAsked));
            Assert.That(client.Refused("save", new { draft = "a-squirrel-to-find", title = "Not asked", idea = Kept }), Does.Contain(ClaudeRun.SaveNotAsked));
            Assert.That(Directory.GetFiles(dir), Is.Empty);

            bench.SaveRefused = null;
            Assert.That(client.Call("save", new { draft = "a-squirrel-to-find", title = "By its id", idea = Kept }), Does.Contain("Saved"));
        }

        [Test]
        public void NoneOfItIsInTheCodeOrTheBaseRules()
        {
            string root = ChemistryTests.Folders.Repo;
            var code = Directory.GetFiles(Path.Combine(root, "unity", "Assets", "Prototype"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            var rules = Directory.GetFiles(Path.Combine(root, "rules"), "*.json", SearchOption.AllDirectories);
            var named = code.Concat(rules).Where(f => Regex.IsMatch(File.ReadAllText(f), "golden.?squirrel", RegexOptions.IgnoreCase)).ToList();
            Assert.That(named, Is.Empty);
        }
    }
}
