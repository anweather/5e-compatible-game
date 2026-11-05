using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// #79: a mod that changes the building tables, switched on through the room on a host and a peer. The
    /// world's own buildings are read over the mod's tables (World.SiteProblem, ApplyRules), so a table that
    /// would make one of them unreadable must be refused by the checks, the same on every peer, and never
    /// thrown out of ApplyRoom. One that reads is switched on, and both peers agree.
    /// </summary>
    public class RoomTablesTests
    {
        string data;
        GameFolders folders;
        JObject dock;

        [OneTimeSetUp]
        public void MakeFolder()
        {
            data = Path.Combine(Path.GetTempPath(), "room-tables-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(ChemistryTests.RulesDir(), data);
            Directory.CreateDirectory(folders.Mods);
            // A plain house on open ground near Hollowmere, so a mod with a row of its own reads the world's rows too.
            string sites = new HouseToolsTests.Client(new Workbench(folders)).Call("find_site", new { width = 6, depth = 6, near = "hollowmere", within = 60 });
            var at = Regex.Match(sites, @"\[(\d+), (\d+)\]");
            Assert.That(at.Success, sites);
            dock = BuildingRow.Box(6, 6, Side.South, "door", null);
            dock["at"] = new JArray(int.Parse(at.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(at.Groups[2].Value, CultureInfo.InvariantCulture));
            dock["style"] = "plain";
        }

        [OneTimeTearDown]
        public void RemoveFolder() => Directory.Delete(data, true);

        static string State(World w) => w.CellHash() + " " + w.CastHash() + " " + w.Rules.Fingerprint;

        // The patch: these tables, and with a row, the dock with these fields over it.
        string Patch(string tables, string row)
        {
            var patch = new JObject { ["buildings"] = JObject.Parse(tables) };
            if (row == null) return patch.ToString();
            var mine = (JObject)dock.DeepClone();
            foreach (var p in JObject.Parse(row).Properties()) mine[p.Name] = p.Value;
            patch["world"] = new JObject { ["buildings"] = new JObject { ["dock#1"] = mine } };
            return patch.ToString();
        }

        // Host and peer decide it at the same tick and step on, as the room does.
        (World host, World peer, RoomVerdict a, RoomVerdict b) Switch(string patch)
        {
            var host = new Workbench(folders).LiveWorld;
            var peer = new Workbench(folders).LiveWorld;
            var c = RoomCommand.Apply("Ana", "tables", patch, "New tables");
            RoomVerdict a = default, b = default;
            Assert.DoesNotThrow(() => a = host.ApplyRoom(c), "the host decides it without a throw");
            Assert.DoesNotThrow(() => b = peer.ApplyRoom(c), "the peer decides it without a throw");
            host.Step();
            peer.Step();
            Assert.That(b.Line, Is.EqualTo(a.Line), "every peer says the same");
            Assert.That(State(peer), Is.EqualTo(State(host)), "host and peer agree");
            return (host, peer, a, b);
        }

        // Each of these threw InvalidOperationException out of Rules.Check (Newtonsoft reading a field of a
        // string) and so out of ApplyRoom on every peer; a style's wall, floor or roof goes into each of the
        // world's buildings of that style. Now each is a sentence naming the part and the field.
        [TestCase("{\"styles\": {\"cabin\": {\"wall\": \"stone\"}}}", null, "Style cabin: wall must be an object of its own fields, not stone")]
        [TestCase("{\"styles\": {\"village\": {\"wall\": \"stone\"}}}", null, "Style village: wall must be an object of its own fields, not stone")]
        [TestCase("{\"styles\": {\"cabin\": {\"floor\": \"slab\"}}}", null, "Style cabin: floor must be an object of its own fields, not slab")]
        [TestCase("{\"styles\": {\"cabin\": {\"roof\": \"gable\"}}}", null, "Style cabin: roof must be an object of its own fields, not gable")]
        [TestCase("{\"styles\": {\"plain\": {\"wall\": \"stone\"}}}", "{}", "Style plain: wall must be an object of its own fields, not stone")]
        [TestCase("{}", "{\"wall\": \"stone\"}", "Building dock#1: wall must be an object of its own fields, not stone")]
        [TestCase("{}", "{\"floor\": \"slab\"}", "Building dock#1: floor must be an object of its own fields, not slab")]
        [TestCase("{}", "{\"roofs\": {\"main\": \"gable\"}}", "Building dock#1: roof main must be an object of its own fields, not gable")]
        [TestCase("{}", "{\"walls\": {\"north\": \"x\"}}", "Building dock#1: wall north must be an object of its own fields, not x")]
        // These were refused, but as "can't be read as a mod" (a FormatException from the checks or a row's read).
        [TestCase("{\"styles\": {\"plain\": {\"storeys\": \"two\", \"wall\": {\"material\": \"stone\"}}}}", "{}", "Style plain: storeys must be a whole number, 1 or more")]
        [TestCase("{}", "{\"storeys\": \"two\"}", "Building dock#1: storeys must be a whole number, 1 or more")]
        [TestCase("{}", "{\"built\": {\"stone\": [[0, \"a\", 2]]}}", "Building dock#1: built cells are spans")]
        public void ATableThatWouldNotReadIsRefusedOnEveryPeer(string tables, string row, string why)
        {
            var before = new Workbench(folders).LiveWorld;
            string was = State(before);
            var (host, _, a, _) = Switch(Patch(tables, row));
            Assert.That(a.Done, Is.False, a.Line);
            Assert.That(a.Line, Does.Contain(why), a.Line);
            Assert.That(a.Line, Does.Not.Contain("can't be read"), "a sentence that says what's wrong");
            before.Step();
            Assert.That(State(host), Is.EqualTo(State(before)), "the world and the rules are as if nothing was asked");
            Assert.That(host.Rules.Fingerprint, Is.EqualTo(was.Split(' ')[2]));
            Assert.That(host.Room.Mods, Is.Empty);
        }

        // What the checks let through, and why each is safe to read the world's rows over (BuildingRow.Read,
        // BuildingRaster.Cells): an opening longer than its wall only marks the cells it holds; a door that
        // becomes a gap, an empty style or roof only change what's drawn or what a row falls back to.
        [TestCase("{\"openings\": {\"drawn-door\": {\"cells\": 1, \"becomes\": \"open\"}}}")]
        [TestCase("{\"openings\": {\"window\": {\"cells\": 1000, \"becomes\": \"pane\"}}}")]
        [TestCase("{\"openings\": {\"window\": {\"cells\": 0, \"becomes\": \"pane\"}}}")]
        [TestCase("{\"styles\": {\"village\": {}, \"cabin\": {}}}")]
        [TestCase("{\"styles\": {\"village\": {\"floor\": {\"kind\": \"slab\"}, \"wall\": {\"material\": \"stone\"}}}}")]
        [TestCase("{\"styles\": {\"cabin\": {\"parts\": \"chimney\"}}}")]
        [TestCase("{\"roofs\": {\"gable\": {}}}")]
        public void ATableThatReadsSwitchesOnTheSameEverywhere(string tables)
        {
            foreach (var row in new[] { null, "{}" })
            {
                var (host, peer, a, _) = Switch(Patch(tables, row));
                Assert.That(a.Done, Is.True, a.Line);
                foreach (var w in new[] { host, peer })
                {
                    var rows = w.BuildingsNow;
                    Assert.That(rows.Count, Is.EqualTo(row == null ? 25 : 26), "the world's buildings, and the dock");
                    Assert.That(rows.All(r => w.BuildingCellsOf(r).Count > 0), "each row reads and rasterises over the new tables");
                }
                var off = RoomCommand.Revert("Ana", "tables");
                Assert.That(host.ApplyRoom(off).Line, Is.EqualTo(peer.ApplyRoom(off).Line));
                Assert.That(State(peer), Is.EqualTo(State(host)), "and agree once it's off");
            }
        }

        // A table can't take a row away: the shape check refuses a row that isn't an object, so a style or an
        // opening the world's buildings name is always there (ApplyBuildings only replaces rows).
        [TestCase("{\"styles\": {\"village\": null}}")]
        [TestCase("{\"styles\": {\"cabin\": \"plain\"}}")]
        [TestCase("{\"openings\": {\"drawn-door\": null}}")]
        [TestCase("{\"openings\": {\"window\": \"pane\"}}")]
        [TestCase("{\"floors\": {\"plinth\": null}}")]
        public void ARowCannotBeTakenAway(string tables)
        {
            var (_, _, a, _) = Switch(Patch(tables, null));
            Assert.That(a.Done, Is.False);
            Assert.That(a.Line, Does.Contain("\"buildings\" must be an object of tables"), a.Line);
        }
    }
}
