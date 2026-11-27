// HillsRiversTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in HillsRiversTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsRiversWorldTests
    {
        HillsRivers rivers;

        HillsRivers.River Main => rivers.Rivers.Single(r => !r.Joins);

        string root;
        GeneratedMap map;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-rivers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsRiversTests.HillsDir);
            rivers = map.Hills.Rivers;
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }
        static Rules R => ChemistryTests.R;

        [Test]
        public void PeersAndLateJoinersAgreeOnARiver()
        {
            string dir = Path.Combine(root, "hills-12x10");
            MapBake.EnsureRows(HillsRiversTests.HillsDir, dir);
            GeneratedSource Source() => new GeneratedSource(GeneratedMap.Load(HillsRiversTests.HillsDir), R);
            World Peer(GeneratedSource source)
            {
                var w = GeneratedSource.MapWorld(source, dir, R, 7, out var start);
                w.KeepStart();
                ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
                return w;
            }
            var host = Peer(Source());
            var other = Peer(Source());
            var gen = map.Gen;
            var path = HillsRiversTests.Along(Main).Select(p => (x: PlanGrid.Mod(p.x, host.W), y: gen.WrapCellY(p.y))).ToList();
            int from = path.FindIndex(p => gen.HillsCellAt(p.x, p.y, out char kind, out _) > 0 && kind == 'r' && map.Hills.Massif.Ring(map.Hills.Massif.Radius(p.x, p.y)) > 0);
            Assert.That(from, Is.GreaterThanOrEqualTo(0));
            var planner = new ChunkPlanner();
            var history = new List<(List<ChunkSwitch> plan, (int x, int y)? cast)>();
            var peers = new List<(World world, string name)> { (host, "the host"), (other, "the other peer") };
            const int ticks = 400, joinAt = 240;
            void Step(World w, List<ChunkSwitch> plan, (int x, int y)? cast)
            {
                ChunkStreaming.Apply(w, plan);
                if (cast is { } c) w.Cast("fire-bolt", c.x, c.y, 1);
                w.Step();
            }
            int wet = 0;
            for (int t = 0; t < ticks; t++)
            {
                var me = new[] { path[Math.Min(path.Count - 1, from + t)] };
                var plan = planner.Plan(host, me);
                (int x, int y)? cast = null;
                if (t % 60 == 30)
                {
                    cast = path[Math.Min(path.Count - 1, from + t + 6)];
                    if (host.At(cast.Value.x, cast.Value.y).Water > 0) wet++;
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
            Assert.That(wet, Is.GreaterThan(2), "the bolts land in the river's water");
            foreach (var (w, name) in peers)
            {
                Assert.That(w.CellHash(), Is.EqualTo(host.CellHash()), name);
                foreach (var (cx, cy) in host.ActiveChunks) Assert.That(w.ChunkHash(cx, cy), Is.EqualTo(host.ChunkHash(cx, cy)), $"{name}: chunk ({cx}, {cy})");
            }
        }
    }
}
