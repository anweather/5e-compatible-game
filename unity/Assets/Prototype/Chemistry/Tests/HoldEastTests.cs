// The Hold's second road (hold-east-path, playtest 2026-11-08 C3): a spawner by the lake south-east of the castle, opposite the
// stable, whose road comes in at a south gate, so a wave comes at the castle from two sides.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public partial class HoldTests
    {
        // The castle's open gates, each as its cells: the east gate first, then the south.
        static List<List<(int x, int y)>> Gates(World w)
        {
            var row = BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings);
            var open = BuildingRaster.Cells(row, R.Buildings).Where(c => c.Terrain == "timber-gate-open").Select(c => (c.X, c.Y)).ToList();
            var gates = new List<List<(int x, int y)>>();
            foreach (var c in open)
            {
                var near = gates.FirstOrDefault(g => g.Any(o => Math.Abs(o.x - c.Item1) + Math.Abs(o.y - c.Item2) <= 2));
                if (near != null) near.Add(c);
                else gates.Add(new List<(int x, int y)> { c });
            }
            return gates.OrderByDescending(g => g.Max(c => c.x)).ToList();
        }

        // Where one raider of this kind put down here walks, until it strikes the keep.
        static List<(int x, int y)> Walk(World w, (int x, int y) from, string kind = "bone-hound")
        {
            int full = w.Hold.Health;
            var r = w.SpawnRaider(kind, from.x, from.y, 100000);
            Assert.That(r, Is.Not.Null, $"a {kind} at {from}");
            var way = new List<(int x, int y)> { from };
            for (int i = 0; i < MostTicks && w.Hold.Health == full; i++)
            {
                w.Step();
                if (way[way.Count - 1] != (r.X, r.Y)) way.Add((r.X, r.Y));
            }
            Assert.That(w.Hold.Health, Is.LessThan(full), $"the {kind} from {from} strikes the keep");
            return way;
        }

        [Test]
        public void TheCastleHasTwoGatesAndTwoSpawners()
        {
            var w = Running();
            Assert.That(Gates(w).Count, Is.EqualTo(2), "the east gate and the south gate");
            Assert.That(Site.Spawners.Select(s => s.Id), Is.EqualTo(new[] { "foot", "east" }));
            var (east, foot) = (Site.Spawners[1], Site.Spawners[0]);
            var (hx, hy) = Site.Heart;
            Assert.That(east.Cells.Average(c => c.y), Is.GreaterThan(hy), "the east road's spawner is south of the heart");
            Assert.That(foot.Cells.Min(c => c.y), Is.LessThan(east.Cells.Min(c => c.y)), "and south of the foot's");
            Assert.That(east.Cells.Min(c => c.x), Is.GreaterThan(hx), "and east of it");
        }

        [Test]
        public void TheEastRoadComesInAtTheSouthGate()
        {
            var w = Running();
            Away(w);
            w.Hold.ToSpawn = 0;
            var south = Gates(w)[1];
            var way = Walk(w, Site.Spawners[1].Cells[Site.Spawners[1].Cells.Count / 2]);
            Assert.That(way.Any(south.Contains), $"through the south gate: {string.Join(" ", way.Where((_, i) => i % 10 == 0))}");
        }

        [Test]
        public void TheFootsRoadStillComesInAtTheEastGate()
        {
            var w = Running();
            Away(w);
            w.Hold.ToSpawn = 0;
            var east = Gates(w)[0];
            var way = Walk(w, Site.Spawners[0].Cells[Site.Spawners[0].Cells.Count / 2]);
            Assert.That(way.Any(east.Contains), "through the east gate");
        }

        [Test]
        public void AWaveComesByBothRoads()
        {
            var w = Running();
            Away(w);
            w.Hold.Health = int.MaxValue / 2;
            w.Hold.Wave = 2;
            w.Hold.InWave = 0;
            w.Hold.ToSpawn = World.HoldWaveSize(R.Sim.Hold, 2);
            var seen = new HashSet<int>();
            var from = new List<(int x, int y)>();
            for (int i = 0; i < MostTicks && w.Hold.ToSpawn > 0; i++)
            {
                w.Step();
                foreach (var r in w.Raiders)
                    if (seen.Add(r.Id)) from.Add((r.X, r.Y));
            }
            bool By(HoldSpawner s, (int x, int y) c) => s.Cells.Any(o => Math.Abs(o.x - c.x) + Math.Abs(o.y - c.y) <= 2);
            int foot = from.Count(c => By(Site.Spawners[0], c)), east = from.Count(c => By(Site.Spawners[1], c));
            Assert.That(foot, Is.GreaterThan(0), "some by the foot's road");
            Assert.That(east, Is.GreaterThan(0), "some by the east road");
            Assert.That(Math.Abs(foot - east), Is.LessThanOrEqualTo(1), $"about half each: {foot} and {east}");
        }
    }
}
