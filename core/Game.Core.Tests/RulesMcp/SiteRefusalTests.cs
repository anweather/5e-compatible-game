using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// #60: a draft whose rows don't fit the rules (a wall of wood, which doesn't block) is an ordinary refusal
    /// through a running game's desk, as the tools reach it, never an exception thrown on the game's main thread.
    /// The world is left as it was, and the same on every peer.
    /// </summary>
    public class SiteRefusalTests
    {
        const string TimedOut = "The game didn't answer in time; ask for the room to see whether it went in.";

        string dir;

        [SetUp]
        public void MakeFolder()
        {
            dir = Path.Combine(Path.GetTempPath(), "site-refusal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void RemoveFolder() => Directory.Delete(dir, true);

        // A running game, as GameRoomDesk is: a read runs on the main thread, where only a RulesToolException comes
        // back to the tool; anything else escapes into RulesMcpServer.Update, and the tool hears nothing until it gives up.
        // A room command is decided by the host and by a peer, each on its own world, at the same tick.
        sealed class GameDesk : ILiveDesk, IWorldDesk
        {
            public World Host, Peer;
            public readonly List<Exception> Escaped = new List<Exception>();
            public readonly List<(RoomVerdict host, RoomVerdict peer)> Verdicts = new List<(RoomVerdict, RoomVerdict)>();

            public T Read<T>(Func<World, T> read)
            {
                T value = default;
                Exception failed = null;
                string said = "";
                try
                {
                    try
                    {
                        value = read(Host);
                    }
                    catch (RulesToolException e)
                    {
                        failed = e;
                    }
                }
                catch (Exception e)
                {
                    Escaped.Add(e);
                    said = TimedOut;
                }
                if (failed != null) throw failed;
                if (said.Length > 0) throw new RulesToolException(said);
                return value;
            }

            public WorldNow Now() => WorldNow.Of(Host);
            public string Room(string mode) => Workbench.Describe(Host.Room, Host.Tick);
            public void Saved(string path) { }

            public string Decide(RoomCommand command)
            {
                var host = Host.ApplyRoom(command);
                var peer = Peer.ApplyRoom(command);
                Host.Step();
                Peer.Step();
                Verdicts.Add((host, peer));
                return host.Line + ".";
            }
        }

        (Workbench bench, GameDesk desk, HouseToolsTests.Client client) Game()
        {
            var bench = new Workbench(ChemistryTests.RulesDir(), dir);
            var desk = new GameDesk { Host = bench.LiveWorld, Peer = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld };
            bench.Desk = desk;
            bench.WorldDesk = desk;
            return (bench, desk, new HouseToolsTests.Client(bench));
        }

        static string State(World w) => w.CellHash() + " " + w.CastHash() + " " + w.Rules.Fingerprint;

        static string WoodenHouse(HouseToolsTests.Client client)
        {
            string sites = client.Call("find_site", new { width = 6, depth = 6, near = "hollowmere", within = 60 });
            var at = Regex.Match(sites, @"\[\d+, \d+\]");
            Assert.That(at.Success, sites);
            string shown = client.Call("add_building", new { at = at.Value, width = 6, depth = 6, id = "dock#1" });
            return Regex.Match(shown, @"Draft (d\d+)").Groups[1].Value;
        }

        [Test]
        public void AWallThatDoesNotBlockIsARefusalThroughTheGame()
        {
            var (_, desk, client) = Game();
            string before = State(desk.Host);
            string d = WoodenHouse(client);

            string said = client.Call("set_building", new { id = "dock#1", field = "wall.material", value = "\"wood\"", draft = d });
            Assert.That(desk.Escaped, Is.Empty, "nothing escapes on the game's main thread");
            Assert.That(said, Does.Contain("won't stand").And.Contain("doesn't block"), said);
            Assert.That(client.Call("validate", new { draft = d }), Does.Contain("doesn't block"));
            Assert.That(client.Refused("save", new { draft = d, title = "A wooden dock", idea = "A dock of planks." }), Does.Contain("doesn't block"));

            string applied = client.Call("apply", new { mod = d, title = "A wooden dock" });
            Assert.That(applied, Does.Contain("refused").And.Contain("doesn't block"), applied);
            Assert.That(desk.Escaped, Is.Empty);

            var (host, peer) = desk.Verdicts[desk.Verdicts.Count - 1];
            Assert.That(host.Done, Is.False);
            Assert.That(peer.Line, Is.EqualTo(host.Line), "every peer refuses it with the same sentence");
            desk.Host.Step();
            desk.Peer.Step();
            var plain = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
            for (int i = 0; i < desk.Host.Tick; i++) plain.Step();
            Assert.That(State(desk.Host), Is.EqualTo(State(desk.Peer)), "host and peer agree");
            Assert.That(desk.Host.CellHash(), Is.EqualTo(plain.CellHash()), "the cells are as if nothing was asked");
            Assert.That(desk.Host.Rules.Fingerprint, Is.EqualTo(before.Split(' ')[2]), "the rules are as they were");
            Assert.That(desk.Host.Room.Mods, Is.Empty);
        }

        [Test]
        public void AWingOnAWoodenHouseSaysWhyEachSideFails()
        {
            var (_, desk, client) = Game();
            string d = WoodenHouse(client);
            client.Call("set_building", new { id = "dock#1", field = "wall.material", value = "\"wood\"", draft = d });
            string refused = client.Refused("add_wing", new { building = "dock#1", draft = d });
            Assert.That(desk.Escaped, Is.Empty);
            Assert.That(refused, Does.Contain("doesn't block").And.Not.Contain("in time"), refused);
        }

        [Test]
        public void AWingInADraftWhoseStyleDoesNotFitIsARefusal()
        {
            var (_, desk, client) = Game();
            string d = WoodenHouse(client);
            client.Call("add_building_part", new { table = "styles", id = "planks", row = "{\"wall\": {\"material\": \"wood\"}}", draft = d });
            string refused = client.Refused("add_wing", new { building = "dock#1", draft = d });
            Assert.That(desk.Escaped, Is.Empty);
            Assert.That(refused, Does.Contain("doesn't block"), refused);
        }

        [Test]
        public void SiteProblemOfAPatchThatDoesNotComposeIsASentence()
        {
            var bench = new Workbench(ChemistryTests.RulesDir(), dir);
            var world = bench.LiveWorld;
            string before = State(world);
            const string patch = "{ \"world\": { \"buildings\": { \"dock#1\": { \"at\": [10, 10], \"style\": \"plain\", \"wall\": { \"material\": \"wood\" } } } } }";
            string why = null;
            Assert.DoesNotThrow(() => why = world.SiteProblemOf(patch, "The draft"));
            Assert.That(why, Does.StartWith("The draft").And.Contain("has no walls"), why);
            string unread = world.SiteProblemOf("{ \"world\": 3 }");
            Assert.That(unread, Does.StartWith("that mod doesn't fit the rules: \"world\" must be"), "a patch that can't be read says so too");
            Assert.That(unread.IndexOf('\0'), Is.EqualTo(-1), "without the stand-in's id");
            Assert.That(State(world), Is.EqualTo(before));

            // A peer asking the same of its own world hears the same sentence.
            var peer = new Workbench(ChemistryTests.RulesDir(), dir).LiveWorld;
            Assert.That(peer.SiteProblemOf(patch, "The draft"), Is.EqualTo(why));
            Assert.That(State(peer), Is.EqualTo(before));
        }
    }
}
