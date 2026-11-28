// Lockstep on the chunked valley: a world the valley's size (TerrainGrid.Valley(), 200×175
// cells over 9×9 chunk terrains), the probe's online casts (fire, trenches, walls,
// Thunderwave) from two players as tick batches. A client fed the batches live, and a late
// joiner replaying the history, have the host's checksum at every 30th tick, and the terrain
// each draws (CellHeightfield) is the host's, whether eased in cell by cell as the cells
// changed (a player who watched) or computed at once after catching up (a late joiner). The three machines run side by
// side, each on its own world, as three peers would.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class OnlineValleyTests
    {
        const int HashEvery = 30, Ticks = 240, JoinAt = 150;

        static Rules R => ChemistryTests.R;
        static readonly TerrainGrid Grid = TerrainGrid.Valley();

        static List<TickBatch> HostSession()
        {
            var casts = new Dictionary<int, CastCommand>
            {
                [12] = new CastCommand("thunderwave", 98, 82, 1),
                [30] = new CastCommand("fire-bolt", 80, 90, 3),
                [36] = new CastCommand("fire-bolt", 91, 77, 0),
                [42] = new CastCommand("earthen-trench", 99, 81, 3),
                [48] = new CastCommand("earthen-trench", 99, 84, 3),
                [54] = new CastCommand("wall-of-stone", 95, 83, 0),
                [60] = new CastCommand("wall-of-stone", 90, 80, 0),
            };
            return Enumerable.Range(1, Ticks)
                .Select(t => casts.TryGetValue(t, out var c) ? new TickBatch(t, new[] { c }) : new TickBatch(t))
                .ToList();
        }

        sealed class Machine
        {
            public readonly World World = Valley();
            public readonly CellHeightfield Ground = new(Grid);
            public readonly Dictionary<int, string> Hashes = new();
            RuleSet rules = RuleSet.Plain(R);
            int[] elevation;

            static World Valley()
            {
                var w = new World(R, Grid.GridW, Grid.GridH, 1, "grass");
                for (int x = 100; x <= 104; x++)
                    for (int y = 80; y <= 84; y++)
                    {
                        w.Paint(x, y, "pit");
                        w.AddWater(x, y, 4);
                    }
                for (int x = 90; x <= 92; x++)
                    for (int y = 76; y <= 78; y++) w.SetCover(x, y, "oil");
                return w;
            }

            public Machine()
            {
                Ground.Reset(World);
                elevation = Elevations();
            }

            int[] Elevations() => World.AllCells().Select(c => World.ElevationUnits(c)).ToArray();

            public void Run(TickBatch batch, bool watch)
            {
                rules = batch.RunOn(World, rules, out _);
                if (World.Tick % HashEvery == 0) Hashes[World.Tick] = World.CellHash() + "/" + World.Rules.Fingerprint;
                if (!watch) return;
                var now = Elevations();
                var moved = new List<(int x, int y)>();
                for (int i = 0; i < now.Length; i++)
                    if (now[i] != elevation[i]) moved.Add((i % World.W, i / World.W));
                elevation = now;
                if (moved.Count > 0) Ground.Update(World, moved, snap: true);
            }

            public void CatchUpGround()
            {
                Ground.Reset(World);
                elevation = Elevations();
            }
        }

        static void AssertSameGround(CellHeightfield expected, CellHeightfield actual, string who)
        {
            foreach (var (i, j) in expected.Chunks)
                Assert.That(actual.Current(i, j), Is.EqualTo(expected.Current(i, j)), $"{who}: chunk ({i}, {j}) drawn as the host's");
        }

        [Test]
        public void AClientAndALateJoinerKeepTheHostsChecksumsAndTerrainOnTheChunkedValley()
        {
            var session = HostSession();
            var host = new Machine();
            var client = new Machine();
            var joiner = new Machine();
            Parallel.Invoke(
                () => session.ForEach(b => host.Run(b, watch: true)),
                () => session.ForEach(b => client.Run(b, watch: true)),
                () =>
                {
                    var history = session.Take(JoinAt).SelectMany(x => x.Casts.Select(c => (x.Tick, c)));
                    var timeline = TickBatch.Timeline(history, null, JoinAt);
                    for (int t = 1; t <= JoinAt; t++) joiner.Run(timeline[t], watch: false);
                    joiner.CatchUpGround();
                    foreach (var b in session.Where(b => b.Tick > JoinAt)) joiner.Run(b, watch: true);
                });

            Assert.That(host.Hashes.Keys, Is.EqualTo(Enumerable.Range(1, Ticks / HashEvery).Select(n => n * HashEvery)));
            Assert.That(client.Hashes, Is.EqualTo(host.Hashes), "the client's checksum at every 30th tick");
            Assert.That(joiner.Hashes, Is.EqualTo(host.Hashes), "the late joiner's, replayed and live");
            Assert.That(host.Hashes.Values.Distinct().Count(), Is.GreaterThan(4), "the world kept changing (fire, water)");

            AssertSameGround(host.Ground, client.Ground, "client");
            AssertSameGround(host.Ground, joiner.Ground, "late joiner");

            var start = new Machine();
            var (ci, cj) = Grid.ChunkOfCell(99, 81);
            Assert.That(host.Ground.Current(ci, cj), Is.Not.EqualTo(start.Ground.Current(ci, cj)), "the trench moved the ground");
            Assert.That(host.World.At(99, 81).Height, Is.LessThan(0), "dug");
            Assert.That(host.World.AllCells().Count(c => c.Water > 0 && c.T != "pit"), Is.GreaterThan(0), "the pond's water ran into a trench");
            Assert.That(host.World.AllCells().Count(c => host.World.IsRaisedWall(c)), Is.GreaterThan(0), "walls stand");
        }
    }
}
