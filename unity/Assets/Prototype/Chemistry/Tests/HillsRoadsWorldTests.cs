// HillsRoadsTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in HillsRoadsTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsRoadsWorldTests
    {
        GeneratedMap map;
        HillsRoads roads;

        string root;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-roads-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsRoadsTests.HillsDir);
            roads = map.Hills.Roads;
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        IEnumerable<(int x, int y)> Along(bool alongRow, int index)
        {
            var path = roads.Route(alongRow, index);
            int wx = alongRow ? map.Gen.CellsW : 0, wy = alongRow ? 0 : map.Gen.CellsH;
            path.AddRange(path.Take(8).Select(p => (p.x + wx, p.y + wy)).ToList());
            for (int k = 0; k + 1 < path.Count; k++)
            {
                var (ax, ay) = path[k];
                var (bx, by) = path[k + 1];
                int n = Math.Max(1, Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)));
                for (int t = 0; t < n; t++) yield return (ax + (bx - ax) * t / n, ay + (by - ay) * t / n);
            }
        }
        static Rules R => ChemistryTests.R;

        [Test]
        public void PeersAndLateJoinersAgreeOnARoad()
        {
            string dir = Path.Combine(root, "hills-12x10");
            MapBake.EnsureRows(HillsRoadsTests.HillsDir, dir);
            GeneratedSource Source() => new GeneratedSource(GeneratedMap.Load(HillsRoadsTests.HillsDir), R);
            World Peer(GeneratedSource source)
            {
                var w = GeneratedSource.MapWorld(source, dir, R, 7, out var start);
                w.KeepStart();
                ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
                return w;
            }
            var hostSource = Source();
            var host = Peer(hostSource);
            Assert.That(hostSource.Made, Is.LessThan(60), "loading lays nothing for the roads: only the spawn's reach is made");
            var other = Peer(Source());
            var path = Along(true, roads.MainRows[1]).Select(p => (x: PlanGrid.Mod(p.x, host.W), y: map.Gen.WrapCellY(p.y))).ToList();
            int from = path.FindIndex(p => p.x == 6880);
            Assert.That(from, Is.GreaterThanOrEqualTo(0));
            var planner = new ChunkPlanner();
            var history = new List<(List<ChunkSwitch> plan, (int x, int y)? cast)>();
            var peers = new List<(World world, string name)> { (host, "the host"), (other, "the other peer") };
            const int ticks = 500, joinAt = 300;
            void Step(World w, List<ChunkSwitch> plan, (int x, int y)? cast)
            {
                ChunkStreaming.Apply(w, plan);
                if (cast is { } c) w.Cast("fire-bolt", c.x, c.y, 1);
                w.Step();
            }
            int roadsCast = 0;
            for (int t = 0; t < ticks; t++)
            {
                var me = new[] { path[(from + t / 2) % path.Count] };
                var plan = planner.Plan(host, me);
                (int x, int y)? cast = null;
                if (t % 60 == 30)
                {
                    var ahead = path[(from + t / 2 + 8) % path.Count];
                    cast = ahead;
                    if (host.At(ahead.x, ahead.y).T == "dirt") roadsCast++;
                }
                history.Add((plan, cast));
                if (t == joinAt)
                {
                    var replayed = Peer(Source());
                    foreach (var (p, c) in history.Take(t)) Step(replayed, p, c);
                    var loaded = World.Load(host.Save(host.Source), R, Source());
                    loaded.LandFromSource();
                    Assert.That(replayed.CellHash(), Is.EqualTo(host.CellHash()), "the joiner replaying the history");
                    Assert.That(loaded.CellHash(), Is.EqualTo(host.CellHash()), "the joiner loading the save");
                    peers.Add((replayed, "the joiner replaying"));
                    peers.Add((loaded, "the joiner loading"));
                }
                foreach (var (w, _) in peers) Step(w, plan, cast);
            }
            Assert.That(roadsCast, Is.GreaterThan(3), "the bolts land on the road");
            Assert.That(path[(from + ticks / 2) % path.Count].x, Is.LessThan(host.W / 2), "the walk crossed the east seam");
            foreach (var (w, name) in peers)
            {
                Assert.That(w.CellHash(), Is.EqualTo(host.CellHash()), name);
                foreach (var (cx, cy) in host.ActiveChunks) Assert.That(w.ChunkHash(cx, cy), Is.EqualTo(host.ChunkHash(cx, cy)), $"{name}: chunk ({cx}, {cy})");
            }
            TestContext.Out.WriteLine($"ROADS peers: the host made {hostSource.Made} chunks ({hostSource.MadeOnRead} on a read, {hostSource.MadeAhead} ahead)");
        }
    }
}
