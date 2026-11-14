// The open world (D20, World.OpenWorld) over the lockstep loopback: chunks filled as reaches need them, by the host's plan
// (OpenWorldPlanner) from a made-up host game's ground. Two peers far apart, a teleport, two areas meeting, a joiner
// mid-fire, a cave under a burning meadow, and negative chunks, each checked against the host's checksum tick for tick.
using System;
using System.Collections.Generic;
using System.Linq;
using Chem.Lockstep;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class OpenWorldTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A host game: a grass meadow on every layer with a tree here and there, its characters set by the test.</summary>
        sealed class Ground : IOpenGround
        {
            public readonly Dictionary<string, (int layer, int x, int y)> Where = new Dictionary<string, (int, int, int)>();
            // Every sample asked for, and the character nearest each chunk when it was.
            public readonly List<ChunkKey> Samples = new List<ChunkKey>();
            public readonly Dictionary<ChunkKey, string> Grown = new Dictionary<ChunkKey, string>();

            public IEnumerable<(string who, int layer, int x, int y)> Characters() =>
                Where.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => (w.Key, w.Value.layer, w.Value.x, w.Value.y));

            public ChunkGround Sample(ChunkKey key)
            {
                Samples.Add(key);
                Grown[key] = Where.OrderBy(w => ChunkKey.OfCell(w.Value.layer, w.Value.x, w.Value.y).DistanceTo(key)).ThenBy(w => w.Key, StringComparer.Ordinal).First().Key;
                return Meadow(key);
            }
        }

        static ChunkGround Meadow(ChunkKey key)
        {
            var g = new ChunkGround("grass");
            for (int i = 0; i < Chunks.Area; i++)
            {
                uint h = unchecked((uint)(key.X * 73856093) ^ (uint)(key.Y * 19349663) ^ (uint)(key.Layer * 83492791) ^ (uint)(i * 2654435761));
                if (h % 97 == 0) g.SetAt(i, "tree");
            }
            return g;
        }

        sealed class Peer
        {
            public LockstepSession Session;
            public LockstepWorld Port;
            public LockstepChannel<CastCommand> Casts;
            public readonly Dictionary<int, string> Sums = new Dictionary<int, string>();
            public readonly List<LockstepDesync> Desyncs = new List<LockstepDesync>();
            public readonly List<string> Failures = new List<string>();
            public readonly List<int> Joins = new List<int>();
            public readonly List<(int tick, ChunkKey key)> Fills = new List<(int, ChunkKey)>();

            public World World => Port.World;
            public int Tick => Session.NetTick;
        }

        /// <summary>The loopback, its host (which plans, from the ground) and clients, a frame at a time.</summary>
        sealed class Net
        {
            public readonly LockstepLoopback Loop;
            public readonly Ground Ground = new Ground();
            public readonly Peer Host;
            public readonly List<Peer> Clients = new List<Peer>();

            public Net(bool solo = false)
            {
                Loop = new LockstepLoopback(1024);
                Host = Make(Loop.AddHost(solo: solo), true);
            }

            public Peer Join()
            {
                var link = Loop.AddClient("client " + (Clients.Count + 1));
                var client = Make(link, false);
                Clients.Add(client);
                Loop.Connect(link);
                return client;
            }

            Peer Make(LoopbackPeer link, bool host)
            {
                var p = new Peer { Port = new LockstepWorld(R, () => World.OpenWorld(R, 7)) };
                var s = p.Session = new LockstepSession(link, p.Port, link.Id);
                p.Casts = s.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
                var fills = s.Register(LockstepChannels.Fills());
                var chunks = s.Register(LockstepChannels.Chunks());
                if (host) OpenWorldPlanner.Attach(s, Ground, fills, chunks);
                s.TickApplied += b =>
                {
                    p.Sums[b.Tick] = s.Checksum();
                    foreach (var f in b.Fills) p.Fills.Add((b.Tick, f.Key));
                };
                s.Desynced += p.Desyncs.Add;
                s.Failed += p.Failures.Add;
                s.Joined += p.Joins.Add;
                link.Session = s;
                return p;
            }

            public void Frame()
            {
                Host.Session.Update(1.0 / Math.Max(1, R.Sim.TicksPerSecond));
                foreach (var c in Clients) c.Session.Update(0.5);
                Loop.Pump();
            }

            public void RunFor(int ticks, Action<int> each = null)
            {
                int to = Host.Tick + ticks, guard = 0;
                while (Host.Tick < to)
                {
                    Assert.That(guard++, Is.LessThan(ticks * 3 + 100), "the host's clock moves");
                    each?.Invoke(Host.Tick);
                    Frame();
                }
            }

            public void Settle()
            {
                int guard = 0;
                while (Clients.Any(c => c.Tick < Host.Session.HostTick) || Host.Tick < Host.Session.HostTick)
                {
                    Assert.That(guard++, Is.LessThan(500), "the clients catch up");
                    foreach (var c in Clients) c.Session.Update(0.5);
                    Host.Session.Update(0);
                    Loop.Pump();
                }
            }

            /// <summary>Casts as a peer at a layer's cell.</summary>
            public static void Cast(Peer p, string spell, int layer, int x, int y) =>
                p.Session.Request(p.Casts, new CastCommand(spell, x, ChunkKey.Row(layer, y), 1));
        }

        // Every tick a peer ran from `from` on agrees with the host's checksum; the first that doesn't, or null.
        static string FirstDifference(Net net, Peer peer, int from)
        {
            var ticks = peer.Sums.Keys.Where(t => t >= from).OrderBy(t => t).ToList();
            Assert.That(ticks, Is.Not.Empty, "the peer ran ticks");
            foreach (int t in ticks)
                if (peer.Sums[t] != net.Host.Sums[t]) return $"tick {t}: host {net.Host.Sums[t]}, peer {peer.Sums[t]}";
            return null;
        }

        static void AssertInStep(Net net, Peer peer)
        {
            Assert.That(peer.Failures.Concat(net.Host.Failures), Is.Empty);
            Assert.That(peer.Desyncs, Is.Empty);
            Assert.That(peer.Tick, Is.EqualTo(net.Host.Tick));
            Assert.That(FirstDifference(net, peer, peer.Joins.Count > 0 ? peer.Joins[0] + 1 : 1), Is.Null);
            Assert.That(peer.World.CellHash(), Is.EqualTo(net.Host.World.CellHash()));
        }

        // Cells of a chunk that aren't their ground any more (burnt, burning, wet...), read without unpacking it.
        static int Changed(World w, ChunkKey key)
        {
            var ground = w.GroundOf(key);
            Assert.That(ground, Is.Not.Null, $"chunk {key} is filled");
            var (cx, cy) = key.Chunk;
            var cells = w.ChunkCells(cx, cy);
            int n = 0;
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = cells[i];
                if (c.T != ground.TerrainAt(i) || c.Fire > 0 || c.Cover != null) n++;
            }
            return n;
        }

        static int BurntOrBurning(World w, ChunkKey key)
        {
            var (cx, cy) = key.Chunk;
            var cells = w.ChunkCells(cx, cy);
            int n = 0;
            for (int i = 0; i < Chunks.Area; i++) if (cells[i].Fire > 0 || cells[i].T == "dirt" || cells[i].T == "stump") n++;
            return n;
        }

        const int Far = 1000 * Chunks.Size;

        [Test]
        public void TwoPeersFarApartEachBurnTheirOwnMeadowAndItStaysBurnt()
        {
            var net = new Net();
            net.Ground.Where["p0"] = (0, 32, 32);
            net.Ground.Where["p1"] = (0, Far + 32, 32);
            var client = net.Join();
            net.RunFor(20);
            var mine = ChunkKey.OfCell(0, 32, 32);
            var theirs = ChunkKey.OfCell(0, Far + 32, 32);
            Assert.That(net.Host.World.IsActive(mine) && net.Host.World.IsActive(theirs), Is.True, "both reaches are active");

            Net.Cast(net.Host, "fireball", 0, 32, 32);
            Net.Cast(client, "fireball", 0, Far + 32, 32);
            net.RunFor(150);
            Assert.That(BurntOrBurning(net.Host.World, mine), Is.GreaterThan(20), "the host's meadow burns");
            Assert.That(BurntOrBurning(net.Host.World, theirs), Is.GreaterThan(20), "the client's meadow burns, a thousand chunks off");
            int between = net.Host.World.Filled.Count(k => k.X > 10 && k.X < 990);
            Assert.That(between, Is.Zero, "nothing between the two is filled");

            // The client walks 20 chunks south: its meadow goes inactive and stays as it was, then it walks back.
            net.Ground.Where["p1"] = (0, Far + 32, 20 * Chunks.Size + 32);
            net.RunFor(30);
            Assert.That(net.Host.World.IsActive(theirs), Is.False, "its reach left the meadow");
            string left = net.Host.World.ChunkHash(theirs.Chunk.cx, theirs.Chunk.cy);
            int burnt = Changed(net.Host.World, theirs);
            net.RunFor(200);
            Assert.That(net.Host.World.ChunkHash(theirs.Chunk.cx, theirs.Chunk.cy), Is.EqualTo(left), "an inactive chunk keeps what burned, as it was");
            net.Ground.Where["p1"] = (0, Far + 32, 32);
            net.RunFor(20);
            Assert.That(net.Host.World.IsActive(theirs), Is.True, "its reach came back");
            Assert.That(Changed(net.Host.World, theirs), Is.GreaterThanOrEqualTo(burnt), "the burnt ground is still there");
            Assert.That(net.Ground.Samples.Count(k => k == theirs), Is.EqualTo(1), "filled once, never sampled again");

            net.Settle();
            AssertInStep(net, client);
            Assert.That(client.World.ChunkHash(theirs.Chunk.cx, theirs.Chunk.cy), Is.EqualTo(net.Host.World.ChunkHash(theirs.Chunk.cx, theirs.Chunk.cy)));
        }

        [Test]
        public void ATeleportFillsWhereItLandsAndTheChemistryRunsThere()
        {
            var net = new Net(solo: true);
            net.Ground.Where["p0"] = (0, 10, 10);
            net.RunFor(30);
            var home = ChunkKey.OfCell(0, 10, 10);
            Assert.That(net.Host.World.IsActive(home), Is.True);

            // A jump of 500 chunks east and 300 north (negative rows).
            int x = 500 * Chunks.Size + 20, y = -300 * Chunks.Size + 20;
            var there = ChunkKey.OfCell(0, x, y);
            net.Ground.Where["p0"] = (0, x, y);
            int jumped = net.Host.Tick, active = -1;
            net.RunFor(10, t => { if (active < 0 && net.Host.World.IsActive(there)) active = t; });
            Assert.That(active, Is.GreaterThan(0).And.LessThanOrEqualTo(jumped + 3), "the chunk it landed in is active within a few ticks");
            var reach = R.Sim.Streaming.Reach;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                    Assert.That(net.Host.World.IsActive(new ChunkKey(0, there.X + dx, there.Y + dy)), Is.True, $"its reach is active, ({dx}, {dy})");
            Assert.That(net.Host.World.IsActive(home), Is.False, "the reach it jumped from is let go");
            Assert.That(net.Host.World.IsFilled(home), Is.True, "and kept");

            Net.Cast(net.Host, "fire-bolt", 0, x, y);
            net.RunFor(3);
            Assert.That(net.Host.World.At(x, ChunkKey.Row(0, y)).Fire, Is.GreaterThan(0), "a fire bolt lights the grass where it landed");
            net.RunFor(60);
            Assert.That(BurntOrBurning(net.Host.World, there), Is.GreaterThan(5), "and the fire spreads");
        }

        [Test]
        public void TwoAreasGrowTowardEachOtherAndFireCrossesWhereTheyMeet()
        {
            var net = new Net();
            var client = net.Join();
            net.Ground.Where["a"] = (0, 32, 32);
            net.Ground.Where["b"] = (0, 14 * Chunks.Size + 32, 32);
            net.RunFor(10);
            // They walk toward each other a chunk at a time until their active reaches touch.
            int a = 0, b = 14;
            int reach = R.Sim.Streaming.Reach;
            while (b - a > 2 * reach + 1)
            {
                if ((b - a) % 2 == 0) a++;
                else b--;
                net.Ground.Where["a"] = (0, a * Chunks.Size + 32, 32);
                net.Ground.Where["b"] = (0, b * Chunks.Size + 32, 32);
                net.RunFor(8);
            }
            net.RunFor(5);

            // The seam: a chunk grown from a's side beside one grown from b's, both active.
            var w = net.Host.World;
            int seam = Enumerable.Range(a, b - a).First(cx => net.Ground.Grown[new ChunkKey(0, cx, 0)] == "a" && net.Ground.Grown[new ChunkKey(0, cx + 1, 0)] == "b");
            var west = new ChunkKey(0, seam, 0);
            var east = new ChunkKey(0, seam + 1, 0);
            Assert.That(w.IsActive(west) && w.IsActive(east), Is.True, "both sides of the seam are active");
            Assert.That(BurntOrBurning(w, east), Is.Zero);

            // A line of fire just west of the seam: it crosses into b's chunk.
            int edge = (seam + 1) * Chunks.Size - 2;
            for (int y = 8; y < 56; y += 8) Net.Cast(net.Host, "fire-bolt", 0, edge, y);
            net.RunFor(80);
            Assert.That(BurntOrBurning(net.Host.World, east), Is.GreaterThan(5), "the fire crossed the join");
            net.Settle();
            AssertInStep(net, client);
        }

        [Test]
        public void AJoinerMidFireReachesTheHostsHash()
        {
            var net = new Net();
            net.Ground.Where["p0"] = (0, -40, -40);
            net.RunFor(20);
            Net.Cast(net.Host, "fireball", 0, -40, -40);
            net.RunFor(60);
            Assert.That(BurntOrBurning(net.Host.World, ChunkKey.OfCell(0, -40, -40)), Is.GreaterThan(10), "the fire is going");

            var joiner = net.Join();
            net.RunFor(90);
            net.Settle();
            Assert.That(joiner.Joins, Has.Count.EqualTo(1));
            Assert.That(joiner.World.Filled, Is.EqualTo(net.Host.World.Filled), "the joiner has every chunk the host filled");
            AssertInStep(net, joiner);
            Assert.That(joiner.Sums.Keys.Min(), Is.EqualTo(joiner.Joins[0] + 1));
        }

        [Test]
        public void ACaveUnderABurningMeadowStaysUntouched()
        {
            var net = new Net(solo: true);
            net.Ground.Where["up"] = (0, 32, 32);
            net.Ground.Where["down"] = (1, 32, 32);
            net.RunFor(20);
            var meadow = ChunkKey.OfCell(0, 32, 32);
            var cave = ChunkKey.OfCell(1, 32, 32);
            Assert.That(net.Host.World.IsActive(meadow) && net.Host.World.IsActive(cave), Is.True, "both layers are active at the same x, y");

            Net.Cast(net.Host, "fireball", 0, 32, 32);
            net.RunFor(200);
            var w = net.Host.World;
            Assert.That(BurntOrBurning(w, meadow), Is.GreaterThan(20), "the meadow burns");
            var caves = w.Filled.Where(k => k.Layer == 1).ToList();
            Assert.That(caves, Has.Count.GreaterThan(9));
            foreach (var k in caves) Assert.That(Changed(w, k), Is.Zero, $"cave chunk {k} is as it was filled");
            Assert.That(w.Filled.Where(k => k.Layer == 0).Select(k => (k.X, k.Y)), Is.EquivalentTo(caves.Select(k => (k.X, k.Y))), "each layer filled its own reach");
        }

        [Test]
        public void NegativeCellsAndTheChunkAtZeroMinusOne()
        {
            var w = World.OpenWorld(R, 3);
            var north = new ChunkKey(0, 0, -1);
            var south = new ChunkKey(0, 0, 0);
            Assert.That(ChunkKey.OfCell(0, 5, -1), Is.EqualTo(north), "cell row -1 is in chunk -1");
            Assert.That(w.FillChunk(north, Meadow(north)), Is.True);
            Assert.That(w.FillChunk(south, Meadow(south)), Is.True);
            Assert.That(w.FillChunk(south, new ChunkGround("dirt")), Is.False, "a chunk filled already keeps its ground");
            foreach (var k in new[] { north, south }) w.SetActive(k.Chunk.cx, k.Chunk.cy, true);

            Assert.That(w.At(5, -1)?.T, Is.EqualTo(Meadow(north).TerrainAt(63 * Chunks.Size + 5)));
            Assert.That(w.At(5, -65), Is.Null, "a chunk not filled is no cell");
            Assert.That(w.At(-1, 5), Is.Null);
            Assert.That(w.InBounds(-1, 5), Is.False);

            // Fire lit on row -1 crosses into row 0 and stops at the edge of what's filled.
            for (int x = 2; x < 62; x += 4) w.Cast(new CastCommand("fire-bolt", x, -1, 2));
            for (int t = 0; t < 120; t++) w.Step();
            Assert.That(BurntOrBurning(w, south), Is.GreaterThan(5), "the fire crossed from chunk (0, -1) into (0, 0)");
            Assert.That(w.At(-1, 0), Is.Null, "and made nothing past the filled edge");
            Assert.That(w.Filled, Is.EqualTo(new[] { north, south }), "in key order");
        }

        [Test]
        public void EverySpellCastsInAnOpenWorld()
        {
            var w = World.OpenWorld(R, 11);
            for (int y = -2; y <= 1; y++)
                for (int x = -2; x <= 1; x++)
                {
                    var k = new ChunkKey(0, x, y);
                    var g = Meadow(k);
                    for (int i = 0; i < Chunks.Area; i += 211) g.SetAt(i, "pit", 4);
                    w.FillChunk(k, g);
                }
            for (int y = -1; y <= 0; y++)
                for (int x = -1; x <= 0; x++) w.SetActive(x, y, true);
            var failed = new List<string>();
            foreach (var s in R.Spells)
            {
                try
                {
                    w.Cast(new CastCommand(s.Id, -3, -3, 1, 2, 1, 1, "p0"));
                    for (int t = 0; t < 10; t++) w.Step();
                }
                catch (Exception e) when (!(e is AssertionException))
                {
                    failed.Add($"{s.Id}: {e.GetType().Name} {e.Message}");
                }
            }
            Assert.That(failed, Is.Empty);
            var saved = World.Load(w.Save(w.Source), R, w.Source);
            Assert.That(saved.CellHash(), Is.EqualTo(w.CellHash()), "and it saves and loads as it was");
        }
    }
}
