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
    public class HiddenThingTests
    {
        string dir;

        [SetUp]
        public void MakeDir()
        {
            dir = Path.Combine(Path.GetTempPath(), "hidden-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void RemoveDir() => Directory.Delete(dir, true);

        // What the asking player's Claude calls, in order. Returns the kept mod's text.
        string Ask(Workbench bench, string file = "golden-squirrel")
        {
            var client = new HouseToolsTests.Client(bench);
            Assert.That(client.Call("places", new { }), Does.Contain("hollowmere"));
            Assert.That(client.Call("list", new { table = "looks" }), Does.Contain("species"));
            string sites = client.Call("find_site", new { width = 1, depth = 1, near = "hollowmere", within = 40 });
            var spot = Regex.Match(sites, @"\[(\d+), (\d+)\], on");
            Assert.That(spot.Success, sites);
            string made = client.Call("add_item", new
            {
                id = "golden-squirrel",
                name = "Golden squirrel",
                mass = 0.3,
                material = "flesh",
                look = "{\"family\": \"creature\", \"species\": \"Squirrel\", \"color\": [1, 0.78, 0.2], \"scale\": 1}",
            });
            string draft = Regex.Match(made, @"Draft (d\d+)").Groups[1].Value;
            client.Call("set_look", new { id = "golden-squirrel", param = "glow", value = "0.4", table = "item", draft });
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
            var bench = new Workbench(ChemistryTests.RulesDir(), dir);
            string kept = Ask(bench);

            // One mod: an item with a creature look, added at the spot the tools gave. Nothing else.
            var patch = (JObject)JObject.Parse(kept)["patch"];
            Assert.That(patch.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "items", "world" }));
            Assert.That((string)patch["items"]["golden-squirrel"]["look"]["family"], Is.EqualTo("creature"));
            Assert.That((double)patch["items"]["golden-squirrel"]["look"]["glow"], Is.EqualTo(0.4));
            Assert.That(((JArray)patch["world"]["add"]).Single()["kind"].ToString(), Is.EqualTo("golden-squirrel"));

            // Switched on, it stands in the world; the tools see it; it leaves the cells alone.
            var plain = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
            int things = plain.ThingsNow.Count;
            var (thing, world) = On(bench);
            plain.Step();
            var client = new HouseToolsTests.Client(bench);
            Assert.That(thing.FromMod);
            Assert.That(world.ThingsNow.Count, Is.EqualTo(things + 1));
            Assert.That(world.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(world.Rules.Item("golden-squirrel").Look.Species, Is.EqualTo("Squirrel"));
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
            var (again, _) = On(new Workbench(ChemistryTests.RulesDir(), dir));
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
                string own = Path.Combine(dir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(own);
                var bench = new Workbench(ChemistryTests.RulesDir(), own);
                var client = new HouseToolsTests.Client(bench);
                string sites = client.Call("find_site", new { width = 1, depth = 1, near = "hollowmere", within = 40 });
                var spot = Regex.Match(sites, @"\[(\d+), (\d+)\], on");
                string draft = Regex.Match(client.Call("add_item", new { id = "golden-squirrel", name = "Golden squirrel", mass = 0.3, material = "flesh", look = "{\"species\": \"Squirrel\", \"color\": [1, 0.78, 0.2]}" }), @"Draft (d\d+)").Groups[1].Value;
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
            string kept = Ask(new Workbench(ChemistryTests.RulesDir(), dir));
            // Candidates at a chance as well, which is the "rare": each peer decides them alone.
            var mod = JObject.Parse(kept);
            var rows = (JArray)mod["patch"]["world"]["add"];
            var at = (JArray)rows[0]["cell"];
            for (int i = 1; i <= 5; i++) rows.Add(new JObject { ["kind"] = "golden-squirrel", ["cell"] = new JArray((int)at[0] + i, (int)at[1]), ["chance"] = 0.2 });
            string json = mod.ToString();

            var a = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
            var b = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
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
            var late = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
            var batches = TickBatch.Timeline(Array.Empty<(int, CastCommand)>(), null, 30);
            TickBatch.AddRoom(batches, new[] { (3, RoomCommand.Apply("Ana", "golden-squirrel", json)), (9, RoomCommand.Found("Bo", found)) });
            var rules = late.RoomRules;
            for (int t = 1; t <= 30; t++) rules = batches[t].RunOn(late, rules, out _);
            Assert.That(late.ThingsNow.Fingerprint, Is.EqualTo(a.ThingsNow.Fingerprint));
            Assert.That(late.Room.Found, Is.EqualTo(a.Room.Found));
        }

        [Test]
        public void NoneOfItIsInTheCodeOrTheBaseRules()
        {
            string root = Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\'));
            var code = Directory.GetFiles(Path.Combine(root, "unity", "Assets", "Prototype"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            var rules = Directory.GetFiles(Path.Combine(root, "rules"), "*.json", SearchOption.AllDirectories);
            var named = code.Concat(rules).Where(f => Regex.IsMatch(File.ReadAllText(f), "golden.?squirrel", RegexOptions.IgnoreCase)).ToList();
            Assert.That(named, Is.Empty);
        }
    }
}
