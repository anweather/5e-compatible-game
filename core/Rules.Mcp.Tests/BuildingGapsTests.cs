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
    /// #85: three quiet gaps in a mod's building tables, each switched through the room on a host and a peer.
    /// A table-only change that redraws the world's own buildings lays their cells again (and back on revert);
    /// an opening's cells too many for its span to be an int, or an at near int's top, are refused; a table a
    /// mod's "buildings" can't add rows to is refused by name.
    /// </summary>
    [Category("Simulation")]
    public class BuildingGapsTests
    {
        string data;
        GameFolders folders;
        JObject dock;

        [OneTimeSetUp]
        public void MakeFolder()
        {
            data = Path.Combine(Path.GetTempPath(), "building-gaps-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(ChemistryTests.RulesDir(), data);
            Directory.CreateDirectory(folders.Mods);
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

        (World host, World peer, RoomVerdict a) Switch(string patch)
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
            return (host, peer, a);
        }

        // Every cell the buildings in force rasterise to is the terrain on the world's cell.
        static void CellsMatchTheRaster(World w, string when)
        {
            foreach (var row in w.BuildingsNow)
                foreach (var c in w.BuildingCellsOf(row).Where(c => w.InBounds(c.X, c.Y)))
                    Assert.That(w.At(c.X, c.Y).T, Is.EqualTo(c.Terrain), $"{when}: {row.Id} at ({c.X}, {c.Y})");
        }

        // Gap 1: no mod row, only the drawn-door's row: the world's own drawn doors become gaps, so their
        // cells are laid again, and laid back when it's off.
        [Test]
        public void ATableOnlyChangeLaysTheWorldsOwnBuildingsAgain()
        {
            var untouched = new Workbench(folders).LiveWorld;
            untouched.Step();
            var (host, peer, a) = Switch("{\"buildings\": {\"openings\": {\"drawn-door\": {\"cells\": 1, \"becomes\": \"open\"}}}}");
            Assert.That(a.Done, Is.True, a.Line);
            Assert.That(host.CellHash(), Is.Not.EqualTo(untouched.CellHash()), "the doors' cells changed");
            foreach (var w in new[] { host, peer }) CellsMatchTheRaster(w, "on");
            var off = RoomCommand.Revert("Ana", "tables");
            Assert.That(host.ApplyRoom(off).Line, Is.EqualTo(peer.ApplyRoom(off).Line));
            Assert.That(State(peer), Is.EqualTo(State(host)), "and agree once it's off");
            foreach (var w in new[] { host, peer }) CellsMatchTheRaster(w, "off");
            Assert.That(host.CellHash(), Is.EqualTo(untouched.CellHash()), "the doors are laid back as the world was made");
        }

        // Gap 2: cells * 2 past int wrapped into a negative span that passed "runs past the wall".
        [TestCase(1 << 30)]
        [TestCase(2000000000)]
        [TestCase(OpeningDef.MaxCells + 1)]
        public void AnOpeningWhoseSpanWouldWrapIsRefused(int cells)
        {
            foreach (var row in new[] { null, "{}" })
            {
                var patch = new JObject { ["buildings"] = new JObject { ["openings"] = new JObject { ["door"] = new JObject { ["cells"] = cells, ["becomes"] = "open" } } } };
                if (row != null) patch["world"] = new JObject { ["buildings"] = new JObject { ["dock#1"] = dock.DeepClone() } };
                var (host, _, a) = Switch(patch.ToString());
                Assert.That(a.Done, Is.False, a.Line);
                Assert.That(a.Line, Does.Contain($"pening door: cells must be a whole number, 0 to {OpeningDef.MaxCells}"), a.Line);
                Assert.That(host.Room.Mods, Is.Empty);
            }
        }

        // And at + span, near int's top, wrapped the same way.
        [Test]
        public void AnOpeningAtNearIntsTopIsRefused()
        {
            var mine = (JObject)dock.DeepClone();
            mine["walls"]["south"]["openings"][0]["at"] = int.MaxValue;
            var patch = new JObject { ["world"] = new JObject { ["buildings"] = new JObject { ["dock#1"] = mine } } };
            var (host, _, a) = Switch(patch.ToString());
            Assert.That(a.Done, Is.False, a.Line);
            Assert.That(a.Line, Does.Contain($"at {int.MaxValue} is 2 half cells long and runs past the wall's 12"), a.Line);
            Assert.That(host.Room.Mods, Is.Empty);
        }

        // Gap 3: a table ApplyBuildings never reads was dropped without a word.
        [TestCase("{\"windows\": {\"pane\": {\"cells\": 1}}}", "\"buildings\" has no table windows; a mod adds rows to openings, floors, roofs, details, styles")]
        [TestCase("{\"ground\": \"stone\"}", "\"buildings\" can't change ground, the terrain the world's buildings stand on")]
        [TestCase("{\"drawn\": {}}", "\"buildings\" has no table drawn")]
        public void ATableAModCannotAddToIsRefusedByName(string tables, string why)
        {
            var (host, _, a) = Switch(new JObject { ["buildings"] = JObject.Parse(tables) }.ToString());
            Assert.That(a.Done, Is.False, a.Line);
            Assert.That(a.Line, Does.Contain(why), a.Line);
            Assert.That(host.Room.Mods, Is.Empty);
        }
    }
}
