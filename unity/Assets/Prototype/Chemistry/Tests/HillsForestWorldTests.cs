// HillsForestTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in HillsForestTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsForestWorldTests
    {
        GeneratedMap map;
        HillsForest forest;
        (int cx, int cy) heart;


        string root;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-forest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsForestTests.HillsDir);
            forest = map.Hills.Forest;
            heart = forest.Heart(map.Hills.At);
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }
        static Rules R => ChemistryTests.R;

        [Test]
        public void PeersAndLateJoinersAgreeInABurningWood()
        {
            string dir = Path.Combine(root, "hills-12x10");
            MapBake.EnsureRows(HillsForestTests.HillsDir, dir);
            GeneratedSource Source() => new GeneratedSource(GeneratedMap.Load(HillsForestTests.HillsDir), R);
            World Peer(GeneratedSource source)
            {
                var w = GeneratedSource.MapWorld(source, dir, R, 7, out var start);
                w.KeepStart();
                ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
                return w;
            }
            var host = Peer(Source());
            var other = Peer(Source());
            int s = ChunkGen.Size, y0 = heart.cy * s + s / 2, x0 = (heart.cx - 2) * s;
            var path = Enumerable.Range(0, 5 * s).Select(k => (x: PlanGrid.Mod(x0 + k, host.W), y: y0)).ToList();
            var planner = new ChunkPlanner();
            var history = new List<(List<ChunkSwitch> plan, (int x, int y)? cast)>();
            var peers = new List<(World world, string name)> { (host, "the host"), (other, "the other peer") };
            const int ticks = 320, joinAt = 200;
            void Step(World w, List<ChunkSwitch> plan, (int x, int y)? cast)
            {
                ChunkStreaming.Apply(w, plan);
                if (cast is { } c) w.Cast("fire-bolt", c.x, c.y, 1);
                w.Step();
            }
            int atTrees = 0;
            for (int t = 0; t < ticks; t++)
            {
                var me = new[] { path[Math.Min(path.Count - 1, t)] };
                var plan = planner.Plan(host, me);
                (int x, int y)? cast = null;
                if (t % 40 == 20)
                {
                    var (px, py) = path[Math.Min(path.Count - 1, t + 8)];
                    for (int d = 0; d < 12 && cast == null; d++)
                        foreach (var (dx, dy) in new[] { (0, d), (0, -d), (d, 0), (-d, 0) })
                            if (cast == null && host.At(PlanGrid.Mod(px + dx, host.W), py + dy).T == "tree") cast = (PlanGrid.Mod(px + dx, host.W), py + dy);
                    if (cast != null) atTrees++;
                    else cast = (px, py);
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
            Assert.That(atTrees, Is.GreaterThanOrEqualTo(4), "the bolts go into the wood's trees");
            foreach (var (w, name) in peers)
            {
                Assert.That(w.CellHash(), Is.EqualTo(host.CellHash()), name);
                foreach (var (cx, cy) in host.ActiveChunks) Assert.That(w.ChunkHash(cx, cy), Is.EqualTo(host.ChunkHash(cx, cy)), $"{name}: chunk ({cx}, {cy})");
            }
        }
    }
}
