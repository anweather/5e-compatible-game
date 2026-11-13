// Lockstep in the core (D19, LockstepSession.cs) over the loopback: peers in one process, the host's clock a tick a
// frame. Two peers stay equal while both cast; a joiner by snapshot, a desync put right by one, input delay, an online
// reset, a refused command and messages split small all come back to the host's checksum (NetChem.Checksum's parts),
// tick for tick.
using System;
using System.Collections.Generic;
using System.Linq;
using Chem.Lockstep;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class LockstepTests
    {
        static Rules R => ChemistryTests.R;

        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        // A meadow with trees, and a pond to pour into and boil.
        static World Meadow()
        {
            var w = new World(R, 64, 64, 5, "grass");
            for (int y = 0; y < w.H; y += 6)
                for (int x = (y * 5) % 13; x < w.W; x += 13) w.Paint(x, y, "tree");
            for (int x = 40; x < 48; x++)
                for (int y = 40; y < 46; y++)
                {
                    w.Paint(x, y, "pit");
                    w.AddWater(x, y, 4);
                }
            w.KeepStart();
            return w;
        }

        static World LockedRoom()
        {
            var w = Meadow();
            w.OpenRoom(ChangeMode.Locked, LockstepChannels.PeerName(new PeerId(0)), R, Budgets);
            return w;
        }

        /// <summary>One peer: its session over its end of the loopback, its channels, and its checksum at every tick it ran.</summary>
        sealed class Peer
        {
            public LockstepSession Session;
            public LockstepWorld Port;
            public LoopbackPeer Link;
            public LockstepChannel<CastCommand> Casts;
            public LockstepChannel<EffectCommand> Effects;
            public LockstepChannel<RoomCommand> Room;
            public readonly Dictionary<int, string> Sums = new Dictionary<int, string>();
            public readonly Dictionary<int, TickBatch> Ran = new Dictionary<int, TickBatch>();
            public readonly List<LockstepDesync> Desyncs = new List<LockstepDesync>();
            public readonly List<LockstepRefusal> Refusals = new List<LockstepRefusal>();
            public readonly List<string> Failures = new List<string>();
            public readonly List<int> Joins = new List<int>(), ResetsComing = new List<int>();

            public World World => Port.World;
            public int Tick => Session.NetTick;
        }

        /// <summary>The loopback, its host and clients, run a frame at a time.</summary>
        sealed class Net
        {
            public readonly LockstepLoopback Loop;
            public readonly Peer Host;
            public readonly List<Peer> Clients = new List<Peer>();
            readonly Func<World> build;

            public Net(Func<World> build, int maxMessageBytes = 1024)
            {
                this.build = build;
                Loop = new LockstepLoopback(maxMessageBytes);
                Host = Make(Loop.AddHost());
            }

            public Peer Join(int delay = 0)
            {
                var client = Make(Loop.AddClient("client " + (Clients.Count + 1), delay));
                Clients.Add(client);
                Loop.Connect(client.Link);
                return client;
            }

            Peer Make(LoopbackPeer link)
            {
                var p = new Peer { Link = link, Port = new LockstepWorld(R, build, budgets: Budgets) };
                var s = p.Session = new LockstepSession(link, p.Port, link.Id);
                p.Casts = s.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
                p.Effects = s.Register(LockstepChannels.Effects());
                s.Register(LockstepChannels.Strikes(LockstepChannels.PlayerName));
                p.Room = s.Register(LockstepChannels.Room(LockstepChannels.PeerName));
                s.TickApplied += b =>
                {
                    p.Ran[b.Tick] = b;
                    p.Sums[b.Tick] = s.Checksum();
                };
                s.Desynced += p.Desyncs.Add;
                s.Refused += p.Refusals.Add;
                s.Failed += p.Failures.Add;
                s.Joined += p.Joins.Add;
                s.ResetComing += p.ResetsComing.Add;
                link.Session = s;
                return p;
            }

            /// <summary>One frame: the host's clock moves a tick on (none, <paramref name="hostClock"/> false), every peer runs, the loopback delivers.</summary>
            public void Frame(bool hostClock = true)
            {
                Host.Session.Update(hostClock ? 1.0 / Math.Max(1, R.Sim.TicksPerSecond) : 0);
                foreach (var c in Clients) c.Session.Update(0.5);
                Loop.Pump();
            }

            /// <summary>Frames until the host has run <paramref name="tick"/>, with <paramref name="each"/> before every frame.</summary>
            public void RunTo(int tick, Action<int> each = null)
            {
                int guard = 0;
                while (Host.Tick < tick)
                {
                    Assert.That(guard++, Is.LessThan(tick * 3 + 100), "the host's clock moves");
                    each?.Invoke(Host.Tick);
                    Frame();
                }
            }

            /// <summary>The host's clock stopped, frames until every client has run all the host stamped.</summary>
            public void Settle()
            {
                int guard = 0;
                while (Clients.Any(c => c.Tick < Host.Session.HostTick) || Host.Tick < Host.Session.HostTick)
                {
                    Assert.That(guard++, Is.LessThan(500), "the clients catch up");
                    Frame(false);
                }
            }
        }

        /// <summary>The first tick from <paramref name="from"/> on where a peer's checksum isn't the host's, or null.</summary>
        static string FirstDifference(Net net, Peer peer, int from)
        {
            var ticks = peer.Sums.Keys.Where(t => t >= from).OrderBy(t => t).ToList();
            Assert.That(ticks, Is.Not.Empty, "the peer ran ticks");
            foreach (int t in ticks)
                if (peer.Sums[t] != net.Host.Sums[t]) return $"tick {t}: host {net.Host.Sums[t]}, peer {peer.Sums[t]}";
            return null;
        }

        static CastCommand Cast(string spell, int x, int y) => new CastCommand(spell, x, y, 1);

        [Test]
        public void TwoPeersStayEqualFor600TicksWithCastsFromBoth()
        {
            var net = new Net(Meadow);
            var client = net.Join();
            net.RunTo(600, t =>
            {
                if (t % 37 == 5) net.Host.Session.Request(net.Host.Casts, Cast("fire-bolt", 5 + t % 50, 10 + t % 40));
                if (t % 53 == 11) client.Session.Request(client.Casts, Cast(t % 2 == 0 ? "fireball" : "create-water", 40 + t % 9, 38 + t % 7));
            });
            net.Settle();

            Assert.That(client.Joins.Single(), Is.InRange(0, 3), "joined in the host's first ticks, a hello's round trip in");
            Assert.That(client.Session.IsCaughtUp, Is.True);
            Assert.That(client.Tick, Is.EqualTo(net.Host.Tick));
            Assert.That(FirstDifference(net, client, 1), Is.Null);
            Assert.That(client.Sums.Keys.OrderBy(t => t), Is.EqualTo(Enumerable.Range(client.Joins[0] + 1, net.Host.Tick - client.Joins[0])), "the client ran every tick after it joined");
            Assert.That(client.Desyncs, Is.Empty);
            Assert.That(client.Failures.Concat(net.Host.Failures), Is.Empty);
            var casters = net.Host.Ran.Values.SelectMany(b => b.Casts).Select(c => c.By).Distinct().OrderBy(b => b).ToList();
            Assert.That(casters, Is.EqualTo(new[] { "p0", "p1" }), "both cast, each named by the host");
            Assert.That(client.World.CellHash(), Is.Not.EqualTo(Meadow().CellHash()), "the casts did something");
        }

        [Test]
        public void AJoinerAtTick300BySnapshotReachesTheSameHash()
        {
            var net = new Net(Meadow);
            var first = net.Join();
            void Casting(int t)
            {
                if (t % 41 == 3) first.Session.Request(first.Casts, Cast("fireball", 10 + t % 30, 50));
                if (t % 29 == 7) net.Host.Session.Request(net.Host.Casts, Cast("create-water", 42, 41));
            }
            net.RunTo(300, Casting);
            var late = net.Join();
            net.RunTo(420, Casting);
            net.Settle();

            Assert.That(late.Joins.Count, Is.EqualTo(1));
            int joinedAt = late.Joins[0];
            Assert.That(joinedAt, Is.InRange(300, 303), "it starts from the host's world as it stood");
            Assert.That(late.Sums.Keys.Min(), Is.EqualTo(joinedAt + 1), "no tick before the snapshot is run again");
            Assert.That(FirstDifference(net, late, joinedAt + 1), Is.Null);
            Assert.That(FirstDifference(net, first, 1), Is.Null);
            Assert.That(late.Session.IsCaughtUp, Is.True);
            Assert.That(late.Desyncs.Concat(first.Desyncs), Is.Empty);
            Assert.That(late.Session.Checksum(), Is.EqualTo(net.Host.Session.Checksum()));
        }

        [Test]
        public void ADesyncIsCaughtWithinAWindowAndPutRightByASnapshot()
        {
            var net = new Net(Meadow);
            var client = net.Join();
            net.RunTo(100);
            net.Settle();
            int corrupted = client.Tick;
            client.World.Paint(20, 30, "stone");
            net.RunTo(300);
            net.Settle();

            int window = RollingHash.WindowFor(client.World);
            Assert.That(client.Desyncs.Count, Is.EqualTo(1), "seen once, then put right");
            var desync = client.Desyncs[0];
            // The first window to hash that cell after it changed: the one it's in, or the next if its slice had gone by.
            Assert.That(desync.Tick, Is.InRange(corrupted + 1, corrupted + 2 * window));
            // The dragon's part counts the cells changed since the start (World.DragonHash), so it differs too.
            Assert.That(desync.Parts, Is.EqualTo(new[] { "cells", "dragon" }));
            StringAssert.Contains("no longer matches the host's", desync.Text);
            Assert.That(client.Joins.Count, Is.EqualTo(2), "the snapshot it joined with, and the one that put it right");
            int recovered = client.Joins[1];
            Assert.That(recovered, Is.GreaterThanOrEqualTo(desync.Tick));
            Assert.That(client.Session.DesyncSeen, Is.False);
            Assert.That(FirstDifference(net, client, recovered + 1), Is.Null);
            Assert.That(client.Sums[corrupted + 1], Is.Not.EqualTo(net.Host.Sums[corrupted + 1]), "it had gone its own way");
        }

        [Test]
        public void ACastAskedThreeTicksAheadLandsOnTheSameTickOnEveryPeer()
        {
            var net = new Net(Meadow);
            var client = net.Join(delay: 2);
            var taken = new List<(int tick, int hostTick)>();
            net.Host.Session.Taken += t => taken.Add((t.Tick, net.Host.Session.HostTick));
            net.RunTo(40);
            client.Session.Request(client.Casts, Cast("fire-bolt", 30, 30), 3);
            client.Session.Request(client.Casts, Cast("fire-bolt", 32, 30), 99);
            net.RunTo(120);
            net.Settle();

            Assert.That(taken.Count, Is.EqualTo(2));
            Assert.That(taken[0].tick, Is.EqualTo(taken[0].hostTick + 1 + 3), "stamped three ticks past the host's next");
            Assert.That(taken[1].tick, Is.EqualTo(taken[1].hostTick + 1 + LockstepSession.MaxDelayTicks), "a delay past the most is held to it");
            foreach (var (tick, _) in taken)
            {
                Assert.That(net.Host.Ran[tick].Casts.Count, Is.EqualTo(1), $"the host ran it at {tick}");
                Assert.That(client.Ran[tick].Casts.Single().By, Is.EqualTo("p1"), $"the client ran it at {tick}");
            }
            Assert.That(FirstDifference(net, client, 1), Is.Null);
        }

        [Test]
        public void AnOnlineResetRebuildsTheWorldOnEveryPeerAtItsTick()
        {
            var net = new Net(Meadow);
            var client = net.Join();
            net.RunTo(60, t =>
            {
                if (t == 20) client.Session.Request(client.Casts, Cast("fireball", 30, 30));
            });
            net.Host.Session.StampReset(18);
            int resetAt = net.Host.ResetsComing.Single();
            net.RunTo(150);
            var late = net.Join();
            net.RunTo(200);
            net.Settle();

            Assert.That(resetAt, Is.InRange(78, 80));
            Assert.That(client.ResetsComing, Is.EqualTo(new[] { resetAt }), "the client heard it was coming");
            Assert.That(net.Host.Ran[resetAt].Reset && client.Ran[resetAt].Reset, Is.True);
            foreach (var p in new[] { net.Host, client, late })
            {
                Assert.That(p.Session.LastResetTick, Is.EqualTo(resetAt));
                Assert.That(p.World.Tick, Is.EqualTo(p.Tick - (resetAt - 1)), "the rebuilt world counts from the reset");
            }
            // Right after the reset the world is the fresh one, run a tick.
            var fresh = Meadow();
            fresh.Step();
            Assert.That(net.Host.Sums[resetAt].Split('/')[0], Is.EqualTo(fresh.CellHash()), "the fireball's burn is gone");
            Assert.That(FirstDifference(net, client, 1), Is.Null);
            Assert.That(FirstDifference(net, late, late.Joins[0] + 1), Is.Null);
        }

        [Test]
        public void ACommandTheHostWontTakeIsRefusedEverywhereAndNeverRuns()
        {
            var net = new Net(LockedRoom);
            var client = net.Join();
            net.RunTo(10);
            client.Session.Request(client.Room, RoomCommand.Apply("anyone", "big", "{ \"spells\": { } }"));
            client.Session.Request(client.Effects, new EffectCommand(EffectKind.Explode, 5, 5, "barrel"));
            net.RunTo(40);
            net.Settle();

            var room = net.Host.Refusals.Single();
            Assert.That(room.Channel.Name, Is.EqualTo("room"));
            Assert.That(room.Peer, Is.EqualTo(client.Link.Id));
            Assert.That(((RoomCommand)room.Command).Peer, Is.EqualTo("Player 2"), "named for who sent it, whatever it said");
            StringAssert.Contains("locked", room.Why);
            Assert.That(client.Refusals.Select(r => (r.Channel.Name, r.Why)), Is.EquivalentTo(new[]
            {
                ("effects", "only the host sends effects"),
                ("room", room.Why),
            }), "the client hears the host's refusal, and refuses its own host-only ask");
            Assert.That(net.Host.Ran.Values.Concat(client.Ran.Values).All(b => b.Room.Count == 0 && b.Effects.Count == 0), Is.True, "nothing was stamped");
            Assert.That(client.World.Room.Mods, Is.Empty);
            Assert.That(FirstDifference(net, client, 1), Is.Null);
        }

        [Test]
        public void MessagesSplitAt256BytesComeBackWhole()
        {
            var net = new Net(LockedRoom, 256);
            var client = net.Join();
            net.RunTo(60, t =>
            {
                if (t % 7 == 0) client.Session.Request(client.Casts, Cast("fireball", 5 + t % 50, 20));
            });
            Assert.That(net.Host.Port.Snapshot().Text().Length, Is.GreaterThan(4 * 256), "a snapshot many sends long");
            var late = net.Join();
            net.RunTo(120);
            net.Settle();

            Assert.That(net.Loop.LongestSend, Is.LessThanOrEqualTo(256));
            Assert.That(late.Failures, Is.Empty);
            Assert.That(late.Joins.Count, Is.EqualTo(1));
            Assert.That(FirstDifference(net, late, late.Joins[0] + 1), Is.Null);
            Assert.That(FirstDifference(net, client, 1), Is.Null);
        }

        [Test]
        public void SoloRunsThroughTheLoopbackLikeAHost()
        {
            var loop = new LockstepLoopback();
            var port = new LockstepWorld(R, Meadow);
            var solo = new LockstepSession(loop.AddHost(solo: true), port);
            var casts = solo.Register(LockstepChannels.Casts(_ => "solo"));
            var ran = new List<TickBatch>();
            solo.TickApplied += ran.Add;
            solo.Request(casts, Cast("fire-bolt", 10, 10));
            for (int f = 0; f < 12; f++)
            {
                solo.Update(1.0 / Math.Max(1, R.Sim.TicksPerSecond));
                loop.Pump();
            }
            Assert.That(solo.NetTick, Is.EqualTo(12));
            Assert.That(ran[0].Casts.Single().By, Is.EqualTo("solo"), "the cast lands on the next tick");
            Assert.That(loop.Sends, Is.Zero, "solo sends nothing");
        }

        [Test]
        public void APeerWithOtherChannelsIsToldWhyItCantJoin()
        {
            var loop = new LockstepLoopback();
            var host = new LockstepSession(loop.AddHost(), new LockstepWorld(R, Meadow));
            host.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
            host.MapId = "valley";
            loop.Host.Session = host;
            var link = loop.AddClient("other");
            var client = new LockstepSession(link, new LockstepWorld(R, Meadow), link.Id);
            client.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
            client.Register(LockstepChannels.Dragon());
            link.Session = client;
            var failed = new List<string>();
            client.Failed += failed.Add;
            loop.Connect(link);
            for (int f = 0; f < 5; f++)
            {
                host.Update(0.2);
                client.Update(0);
                loop.Pump();
            }
            Assert.That(failed.Single(), Does.Contain("channels (casts) aren't this peer's (casts, dragon)"));
            Assert.That(client.Synced, Is.False);
            Assert.That(client.Hello?.Map, Is.EqualTo("valley"), "the hello is kept, so a peer on another map can go to the host's");
        }
    }
}
