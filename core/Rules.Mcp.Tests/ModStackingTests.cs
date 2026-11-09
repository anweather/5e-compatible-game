using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// #86: a mod may name a material another mod on adds (a wing naming its house's clay tile, as add_wing
    /// writes it), and goes with that mod when it goes, reverted or lapsed, at the same tick on every peer
    /// (World.Lapse.cs). A mod naming a material whose owner went is refused, naming the owner.
    /// </summary>
    [Category("Simulation")]
    public class ModStackingTests
    {
        const string House = "lakeside#1";

        string data;
        GameFolders folders;
        int x, y;

        [OneTimeSetUp]
        public void MakeFolder()
        {
            data = Path.Combine(Path.GetTempPath(), "mod-stacking-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(ChemistryTests.RulesDir(), data);
            Directory.CreateDirectory(folders.Mods);
            // The rowhouses probe's site: the lake's north shore.
            string sites = new HouseToolsTests.Client(new Workbench(folders)).Call("find_site", new { width = 14, depth = 8, near = "lake", side = "north", edge = "shore" });
            var at = Regex.Match(sites, @"\[(\d+), ?(\d+)\]");
            Assert.That(at.Success, sites);
            x = int.Parse(at.Groups[1].Value, CultureInfo.InvariantCulture);
            y = int.Parse(at.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        [OneTimeTearDown]
        public void RemoveFolder() => Directory.Delete(data, true);

        static string State(World w) =>
            $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {string.Join(",", w.Room.Mods.Select(m => m.Id))}";

        // The house, its clay tile lapsing after this many ticks (0: while the house is on).
        string HouseMod(int lapse = 0)
        {
            string tile = lapse == 0 ? BuildingTests.TileTerrain : BuildingTests.TileTerrain.Replace("\"tags\"", "\"lapse\": { \"after\": " + lapse + " }, \"tags\"");
            return BuildingTests.StuccoMod(x, y).Replace(BuildingTests.TileTerrain, tile);
        }

        sealed class Peers
        {
            public Workbench Bench;
            public World Host, Peer;

            // Host and peer decide it at the same tick, and step on.
            public RoomVerdict Both(RoomCommand c)
            {
                var a = Host.ApplyRoom(c);
                var b = Peer.ApplyRoom(c);
                Assert.That(b.Line, Is.EqualTo(a.Line), "every peer says the same");
                Step();
                return a;
            }

            public void Step()
            {
                Host.Step();
                Peer.Step();
                Assert.That(State(Peer), Is.EqualTo(State(Host)), "host and peer agree");
            }
        }

        // The house on, then the wing add_wing drafts over it, on a host and a peer. Returns the wing's patch too.
        (Peers p, string wing) HouseAndWing(int lapse = 0)
        {
            var p = new Peers { Bench = new Workbench(folders) };
            p.Host = p.Bench.LiveWorld;
            p.Peer = new Workbench(folders).LiveWorld;
            var on = p.Both(RoomCommand.Apply("Ana", "house", HouseMod(lapse), "A stucco house"));
            Assert.That(on.Done, Is.True, on.Line);
            int walls = p.Host.BuildingRowOf(House).Walls.Count;
            string shown = new HouseToolsTests.Client(p.Bench).Call("add_wing", new { building = House, width = 4, depth = 4 });
            string draft = Regex.Match(shown, @"Draft (\w[\w-]*)").Groups[1].Value;
            string wing = p.Bench.PatchJson(draft);
            Assert.That(wing, Does.Contain("clay-tile"), "the wing names the house's tile");
            var verdict = p.Both(RoomCommand.Apply("Bo", "wing", wing, "A wing"));
            Assert.That(verdict.Done, Is.True, verdict.Line);
            foreach (var w in new[] { p.Host, p.Peer })
            {
                Assert.That(w.Room.Mods.Select(m => m.Id), Is.EqualTo(new[] { "house", "wing" }));
                Assert.That(w.BuildingRowOf(House).Walls.Count, Is.GreaterThan(walls), "the wing's walls go in");
            }
            return (p, wing);
        }

        static void BothGone(Peers p, string line)
        {
            foreach (var w in new[] { p.Host, p.Peer })
            {
                Assert.That(w.Room.Mods, Is.Empty, "the wing went with the house");
                Assert.That(w.BuildingRowOf(House), Is.Null);
                Assert.That(w.Rules.HasMaterial("terrain", "clay-tile"), Is.False);
                Assert.That(w.Log.Any(l => l.Text == line), Is.True, string.Join("\n", w.Log.Select(l => l.Text).TakeLast(4)));
            }
        }

        [Test]
        public void AWingNamingTheHousesTileGoesOnOverIt()
        {
            var (p, _) = HouseAndWing();
            Assert.That(p.Host.TemporaryMaterials.Single(t => t.Id == "clay-tile").Mod, Is.EqualTo("house"), "the house still owns its tile");
        }

        [Test]
        public void RevertingTheHouseTakesTheWingOnBothPeers()
        {
            var (p, _) = HouseAndWing();
            var heard = new System.Collections.Generic.List<string>();
            p.Host.RoomDecided += (c, v) => heard.Add(c.Change + " " + c.Mod + " by " + c.Peer);
            var off = p.Both(RoomCommand.Revert("Ana", "house"));
            Assert.That(off.Done, Is.True, off.Line);
            Assert.That(heard, Is.EqualTo(new[] { "Revert house by Ana", "Revert wing by Bo" }), "the view hears each go, the owner first");
            BothGone(p, "Ana switched off A stucco house, and A wing with it (Open room)");
            Assert.That(p.Host.Room.Retired.Select(r => r.Id + ":" + r.Mod), Has.Member("clay-tile:house"));
        }

        [Test]
        public void TheHouseLapsingTakesTheWingOnBothPeers()
        {
            const int lapse = 6;
            var (p, _) = HouseAndWing(lapse);
            int at = p.Host.LapsesAt(p.Host.Room.Mods[0]);
            Assert.That(at, Is.GreaterThan(p.Host.Tick));
            while (p.Host.Tick < at - 1) p.Step();
            Assert.That(p.Host.Room.Mods.Count, Is.EqualTo(2), "both stand until the lapse");
            p.Step();
            Assert.That(p.Host.Tick, Is.EqualTo(at));
            BothGone(p, "A stucco house lapsed, and A wing with it (Open room)");
        }

        [Test]
        public void AWingWithoutItsHouseIsRefusedNamingTheHouse()
        {
            var (p, wing) = HouseAndWing();
            p.Both(RoomCommand.Revert("Ana", "house"));
            string was = State(p.Host);
            var again = p.Both(RoomCommand.Apply("Bo", "wing", wing, "A wing"));
            Assert.That(again.Done, Is.False);
            Assert.That(again.Reason, Is.EqualTo("A wing names clay-tile, a material of the mod house, which isn't on; switch house on first"));
            Assert.That(p.Host.Room.Mods, Is.Empty);
            Assert.That(State(p.Host).Split(' ')[2], Is.EqualTo(was.Split(' ')[2]), "the rules are as they were");
        }

        [Test]
        public void AWingNeverOverAHouseIsRefused()
        {
            var (p, wing) = HouseAndWing();
            var fresh = new Workbench(folders).LiveWorld;
            var v = fresh.ApplyRoom(RoomCommand.Apply("Bo", "wing", wing, "A wing"));
            Assert.That(v.Done, Is.False, "no house, no wing: " + v.Line);
            // The house was never on here, so nothing knows whose southwest and clay-tile are: the checks say what is missing.
            Assert.That(v.Reason, Does.StartWith("A wing doesn't pass the checks: Building lakeside#1: unknown style southwest"));
            Assert.That(fresh.Room.Mods, Is.Empty);
            Assert.That(p.Host.Room.Mods.Count, Is.EqualTo(2));
        }

        // A late joiner opens the room with the mods on, a reload composes them again: either way the wing
        // stands on the house, and reverting the house takes it the same as on the host.
        [Test]
        public void ALateJoinerAndAReloadTakeTheWingWithTheHouse()
        {
            var (p, _) = HouseAndWing();
            var late = new World(p.Host.Rules, 16, 16, 3, "grass");
            late.OpenRoom(ChangeMode.Open, "Ana", p.Host.RoomRules.Base, p.Bench.Budgets, p.Host.Room.Mods.ToList());
            Assert.That(late.Rules.Fingerprint, Is.EqualTo(p.Host.Rules.Fingerprint), "the joiner composes the same rules");

            var loaded = World.Load(p.Host.Save(p.Bench.LiveSource), p.Bench.Base, p.Bench.LiveSource, p.Bench.Budgets);
            Assert.That(State(loaded), Is.EqualTo(State(p.Host)));
            var off = RoomCommand.Revert("Ana", "house");
            foreach (var w in new[] { p.Host, loaded, late })
            {
                var v = w.ApplyRoom(off);
                Assert.That(v.Line, Is.EqualTo("Ana switched off A stucco house, and A wing with it (Open room)"));
                Assert.That(w.Room.Mods, Is.Empty);
            }
            p.Host.Step();
            loaded.Step();
            Assert.That(State(loaded), Is.EqualTo(State(p.Host)), "the reload goes the same way as the host");
            Assert.That(late.Rules.Fingerprint, Is.EqualTo(p.Host.Rules.Fingerprint));
        }
    }
}
