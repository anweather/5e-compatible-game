// The hills' rivers (world/maps/hills-12x10, docs/design/alpine.md "Rivers"): worked out from map data on a coarse grid
// when the generator is made (priority-flood, D8, flow accumulation), and carved by each chunk as it's made. The flood
// leaves no pit and its flow lines only go down; the catchment only grows downstream; a river's surface never rises from
// its spring to its end lake, and every river cell along it is water; the meadow's lake spills through the gorge, clear of
// the passes; no lake covers a road and a road crossing a river is a ford; chunks along the rivers meet at their edges
// (and a river over a seam is the same either side); the rivers are the same on every load and every thread; and peers
// and late joiners agree on a river's chunks while its water runs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsRiversTests
    {
        static Rules R => ChemistryTests.R;

        static string HillsDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");

        string root;
        GeneratedMap map;
        HillsRivers rivers;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-rivers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsDir);
            rivers = map.Hills.Rivers;
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        HillsRivers.River Main => rivers.Rivers.Single(r => !r.Joins);

        [Test]
        public void TheFloodLeavesNoPitAndItsFlowOnlyGrowsDownstream()
        {
            Assert.That(rivers, Is.Not.Null, "map.json asks for rivers");
            int lowest = 0, nodes = rivers.GridW * rivers.GridH;
            for (int j = 0; j < rivers.GridH; j++)
                for (int i = 0; i < rivers.GridW; i++)
                {
                    var (land, fill, down, catchment) = rivers.Node(i, j);
                    Assert.That(fill, Is.GreaterThanOrEqualTo(land), $"node ({i}, {j}) is filled, never lowered");
                    if (down < 0)
                    {
                        lowest++;
                        Assert.That(catchment, Is.EqualTo(nodes), "the lowest node drains the whole map");
                        continue;
                    }
                    var below = rivers.Node(down % rivers.GridW, down / rivers.GridW);
                    Assert.That(below.fill, Is.LessThanOrEqualTo(fill), $"node ({i}, {j}) drains down, never up");
                    Assert.That(below.catchment, Is.GreaterThan(catchment), $"node ({i}, {j}): the catchment grows downstream");
                    int di = Math.Abs(down % rivers.GridW - i), dj = Math.Abs(down / rivers.GridW - j);
                    di = Math.Min(di, rivers.GridW - di);
                    dj = Math.Min(dj, rivers.GridH - dj);
                    Assert.That(Math.Max(di, dj), Is.EqualTo(1), $"node ({i}, {j}) drains to one of its 8 neighbours");
                }
            Assert.That(lowest, Is.EqualTo(1), "one lowest node: every flow line ends there, none in a pit");
        }

        [Test]
        public void ARiverFromEachSpringFallsAllTheWayToItsEndLake()
        {
            var massif = map.Hills.Massif;
            Assert.That(rivers.Rivers.Count, Is.EqualTo(2), "two springs");
            Assert.That(rivers.Rivers.Count(r => r.Joins), Is.EqualTo(1), "the second spring's river joins the first's");
            foreach (var r in rivers.Rivers)
            {
                for (int k = 1; k < r.Surface.Length; k++)
                {
                    Assert.That(r.Surface[k], Is.LessThanOrEqualTo(r.Surface[k - 1]), $"its surface never rises (point {k})");
                    Assert.That(r.Catchment[k], Is.GreaterThanOrEqualTo(r.Catchment[k - 1]), $"its catchment never falls (point {k})");
                    Assert.That(r.Half[k], Is.GreaterThanOrEqualTo(r.Half[k - 1]), $"it never narrows (point {k})");
                }
                int sx = (int)BiomeMath.FloorDiv(r.X[0], BiomeMath.One), sy = (int)BiomeMath.FloorDiv(r.Y[0], BiomeMath.One);
                Assert.That(massif.InRing(sx, sy), Is.True, $"spring ({sx}, {sy}) on the ring");
                Assert.That(r.Surface[0], Is.GreaterThan(massif.GorgeLevel + 400), "a spring well up the flank, 50 m over the meadow's lake or more");
            }
            var main = Main;
            var end = rivers.Lakes.Single(l => l.End);
            int last = main.X.Length - 1;
            Assert.That(main.Surface[last], Is.EqualTo(end.Level), "the main river ends at its end lake's level");
            int ex = (int)BiomeMath.FloorDiv(main.X[last], BiomeMath.One), ey = (int)BiomeMath.FloorDiv(main.Y[last], BiomeMath.One);
            Assert.That(massif.Touches(ex, ey) && massif.Ring(massif.Radius(ex, ey)) > 0, Is.False, "the end lake is past the mountains");
            TestContext.Out.WriteLine($"RIVERS main {main.Length * 1.5 / 1000:0.00} km from {main.Surface[0] / 8.0:0} m to {main.Surface[last] / 8.0:0} m; " +
                $"lakes: {string.Join(", ", rivers.Lakes.Select(l => $"{l.Nodes * 576 / 1e6:0.000} km2 at {l.Level / 8.0:0.0} m{(l.End ? " (end)" : "")}"))}");
            Assert.That(main.Length * 3 / 2, Is.InRange(4000, 9000), "4 to 9 km from the spring to the end lake (3 to 5 of it past the gorge)");
            Assert.That(end.Nodes * 576, Is.InRange(50_000, 400_000), "an end lake of 5 to 40 hectares");
        }

        static IEnumerable<(int x, int y)> Along(HillsRivers.River r)
        {
            for (int k = 0; k + 1 < r.X.Length; k++)
            {
                long ax = r.X[k], ay = r.Y[k], bx = r.X[k + 1], by = r.Y[k + 1];
                long len = Math.Max(1, BiomeMath.Length(bx - ax, by - ay) / BiomeMath.One);
                for (long t = 0; t < len; t++)
                    yield return ((int)BiomeMath.FloorDiv(ax + (bx - ax) * t / len, BiomeMath.One), (int)BiomeMath.FloorDiv(ay + (by - ay) * t / len, BiomeMath.One));
            }
        }

        [Test]
        public void EveryCellAlongARiverIsWaterAndNoneIsAPit()
        {
            var gen = map.Gen;
            foreach (var r in rivers.Rivers)
            {
                int last = int.MaxValue, cells = 0, lake = 0, rises = 0;
                foreach (var (x, y) in Along(r))
                {
                    int land = gen.HillsCellAt(x, y, out char kind, out _);
                    Assert.That(kind == 'r' || kind == 'w', Is.True, $"({x}, {y}) on the river is water, not '{kind}'");
                    cells++;
                    if (kind == 'w') lake++;
                    if (last != int.MaxValue && land > last + 1) rises++;
                    last = Math.Min(last, land);
                }
                TestContext.Out.WriteLine($"RIVERS {(r.Joins ? "the tributary" : "the main river")}: {cells} cells along it, {lake} of them lake; {rises} rising");
                Assert.That(rises, Is.Zero, "the water only goes down along it");
            }
            int s = ChunkGen.Size, pits = 0, riverCells = 0;
            foreach (var (cx, cy) in Along(Main).Where((_, k) => k % 40 == 0).Select(p => (PlanGrid.Mod(p.x, gen.CellsW) / s, gen.WrapCellY(p.y) / s)).Distinct())
            {
                var c = gen.Make(cx, cy);
                for (int j = 0; j < s; j++)
                    for (int i = 0; i < s; i++)
                    {
                        if (c.Cells[j * s + i] != 'r') continue;
                        riverCells++;
                        int x = cx * s + i, y = cy * s + j, here = c.Land[j * s + i];
                        bool lower = false;
                        for (int dj = -1; dj <= 1 && !lower; dj++)
                            for (int di = -1; di <= 1 && !lower; di++)
                            {
                                if (di == 0 && dj == 0) continue;
                                int land = gen.HillsCellAt(x + di, y + dj, out char kind, out _);
                                lower = (kind == 'r' || kind == 'w') && land <= here;
                            }
                        if (!lower) pits++;
                    }
            }
            TestContext.Out.WriteLine($"RIVERS {riverCells} river cells in the main river's chunks, {pits} with no water beside them as low");
            Assert.That(riverCells, Is.GreaterThan(2000));
            Assert.That(pits, Is.Zero);
        }

        [Test]
        public void TheMeadowsLakeSpillsThroughTheGorgeClearOfThePasses()
        {
            var massif = map.Hills.Massif;
            Assert.That(massif.HasGorge, Is.True);
            var meadow = rivers.Lakes.Where(l => !l.End).OrderByDescending(l => l.Nodes).First();
            Assert.That(massif.Ring(massif.Radius(meadow.X, meadow.Y)), Is.Zero, "the biggest lake on the way is in the meadow");
            Assert.That(meadow.Level - massif.GorgeLevel, Is.InRange(0, 8), "it stands at the gorge's floor: that's where it spills");
            var (gx, gy) = massif.GorgeWay;
            var (ox, oy) = massif.GorgeStart;
            int crossings = 0;
            foreach (var (x, y) in Along(Main))
            {
                if (!massif.Touches(x, y) || massif.Ring(massif.Radius(x, y)) < BiomeMath.One * 9 / 10) continue;
                crossings++;
                long dx = (long)(x - ox) * BiomeMath.One, dy = (long)(y - oy) * BiomeMath.One;
                long across = Math.Abs(dx * gy - dy * gx) / BiomeMath.One / BiomeMath.One;
                Assert.That(across, Is.LessThan(massif.GorgeWidth + HillsMassif.GorgeWander), $"({x}, {y}) on the crest is in the gorge");
                Assert.That(massif.PassAt(x, y), Is.Zero, $"({x}, {y}): no pass there");
            }
            Assert.That(crossings, Is.GreaterThan(0), "the river crosses the ring's crest");
        }

        [Test]
        public void NoLakeOnARoadAndRoadsCrossRiversByFords()
        {
            var gen = map.Gen;
            var roads = map.Hills.Roads;
            int fords = 0, mainFords = 0, flooded = 0;
            for (int sq = 0; sq < roads.SquaresW * roads.SquaresH; sq++)
            {
                int ox = sq % roads.SquaresW * WorldPlan.SquareCells, oy = sq / roads.SquaresW * WorldPlan.SquareCells;
                foreach (var a in roads.ArmsOf(sq))
                    for (int k = 0; k + 1 < a.X.Length; k++)
                        for (int t = 0; t < 4; t++)
                        {
                            long x = a.X[k] + (a.X[k + 1] - a.X[k]) * t / 4, y = a.Y[k] + (a.Y[k + 1] - a.Y[k]) * t / 4;
                            gen.HillsCellAt(ox + (int)BiomeMath.FloorDiv(x, BiomeMath.One), oy + (int)BiomeMath.FloorDiv(y, BiomeMath.One), out char kind, out bool ford);
                            if (kind == 'w') flooded++;
                            if (!ford) continue;
                            fords++;
                            if (a.Kind == HillsRoads.Main) mainFords++;
                        }
            }
            TestContext.Out.WriteLine($"RIVERS road line points at a ford: {fords} ({mainFords} on main roads); under a lake: {flooded}");
            Assert.That(flooded, Is.Zero, "no lake covers a road: a road crosses one on a causeway");
            Assert.That(fords, Is.GreaterThan(0), "the river crosses roads by fords");
        }

        List<(int cx, int cy)> RiverChunks()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            var list = new List<(int, int)>();
            foreach (var r in rivers.Rivers)
                list.AddRange(Along(r).Where((_, k) => k % 32 == 0).Select(p => (PlanGrid.Mod(p.x, gen.CellsW) / s, gen.WrapCellY(p.y) / s)));
            return list.Distinct().ToList();
        }

        [Test]
        public void ChunksAlongTheRiversMeetAtTheirEdges()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size, edges = 0, water = 0;
            foreach (var (cx, cy) in RiverChunks())
            {
                var c = gen.Make(cx, cy);
                water += c.Cells.Count(ch => ch == 'r' || ch == 'w');
                for (int k = 0; k < s; k++)
                    foreach (var (i, j) in new[] { (0, k), (s - 1, k), (k, 0), (k, s - 1) })
                    {
                        int x = cx * s + i, y = cy * s + j;
                        int land = gen.HillsCellAt(x, y, out char kind, out _);
                        Assert.That(c.Land[j * s + i], Is.EqualTo(land), $"({x}, {y})");
                        if (kind != '\0') Assert.That(c.Cells[j * s + i], Is.EqualTo(kind), $"({x}, {y})");
                        edges++;
                    }
                var known = gen.Know(cx, cy);
                for (int j = 0; j < KnownChunk.Side - 1; j++)
                    for (int i = 0; i < KnownChunk.Side - 1; i++)
                    {
                        int q = j * KnownChunk.Step * s + i * KnownChunk.Step;
                        Assert.That(known.Heights[j * KnownChunk.Side + i], Is.EqualTo(c.Land[q]));
                        byte wet = KnownChunk.Dry;
                        if (c.Cells[q] == 'r') wet = KnownChunk.River;
                        if (c.Cells[q] == 'w') wet = KnownChunk.Lake;
                        Assert.That(known.Water[j * KnownChunk.Side + i], Is.EqualTo(wet));
                    }
            }
            TestContext.Out.WriteLine($"RIVERS {edges} edge cells of the river chunks as a cell alone works them out; {water} water cells");
            Assert.That(water, Is.GreaterThan(5000));
        }

        [Test]
        public void ARiverOverASeamIsTheSameEitherSide()
        {
            const int w = 1152, h = 1152;
            static int Land(int x, int y)
            {
                x = PlanGrid.Mod(x, w);
                y = PlanGrid.Mod(y, h);
                int dx = Math.Abs(x - 40), dy = Math.Abs(y - 576);
                dx = Math.Min(dx, w - dx);
                dy = Math.Min(dy, h - dy);
                return 40 + dx / 2 + dy / 3 + (int)(WorldHash.Hash4(3, 4, x / 16, y / 16) % 3);
            }
            var set = new HillsRivers.Settings { MinLake = 4, MinEndLake = 4, EndLake = 12 };
            var hr = new HillsRivers(Land, w, h, new[] { (1040, 600) }, set, (0, 0, 0));
            var r = hr.Rivers[0];
            Assert.That(r.X.Max(), Is.GreaterThan((long)w * BiomeMath.One), "its line runs on past the east seam, unwrapped");
            int seen = 0;
            for (int y = 0; y < h; y++)
            {
                var a = HillsRivers.Nearest(hr.Near(w - 1, y, w - 1, y), w - 1, y, hr.Reach);
                var b = HillsRivers.Nearest(hr.Near(0, y, 0, y), 0, y, hr.Reach);
                var a2 = HillsRivers.Nearest(hr.Near(-1, y, -1, y), -1, y, hr.Reach);
                Assert.That((a2.Distance, a2.Surface, a2.Half), Is.EqualTo((a.Distance, a.Surface, a.Half)), $"row {y}: cell {w - 1} and its copy -1");
                if (a.Distance >= (long)a.Half * BiomeMath.One || b.Distance >= (long)b.Half * BiomeMath.One) continue;
                seen++;
                Assert.That(Math.Abs(a.Surface - b.Surface), Is.LessThanOrEqualTo(2), $"row {y}: the surface steps over the seam as anywhere");
            }
            Assert.That(seen, Is.GreaterThan(0), "the river is water either side of the seam");
        }

        [Test]
        public void TheRiversAreTheSameEveryLoadAndThread()
        {
            var other = GeneratedMap.Load(HillsDir).Hills.Rivers;
            Assert.That(other.Rivers.Count, Is.EqualTo(rivers.Rivers.Count));
            for (int k = 0; k < rivers.Rivers.Count; k++)
            {
                Assert.That(other.Rivers[k].X, Is.EqualTo(rivers.Rivers[k].X));
                Assert.That(other.Rivers[k].Y, Is.EqualTo(rivers.Rivers[k].Y));
                Assert.That(other.Rivers[k].Surface, Is.EqualTo(rivers.Rivers[k].Surface));
            }
            var chunks = RiverChunks();
            var first = chunks.ToDictionary(c => c, c => map.Gen.Make(c.cx, c.cy).Hash);
            var again = new uint[chunks.Count];
            var gen = GeneratedMap.Load(HillsDir).Gen;
            Parallel.For(0, chunks.Count, k =>
            {
                var (cx, cy) = chunks[chunks.Count - 1 - k];
                again[chunks.Count - 1 - k] = gen.Make(cx, cy).Hash;
            });
            for (int k = 0; k < chunks.Count; k++) Assert.That(again[k], Is.EqualTo(first[chunks[k]]), $"chunk {chunks[k]}");
        }

        [Test, Explicit("places along the rivers, for stills and walks")]
        public void Places()
        {
            var gen = map.Gen;
            var main = Main;
            var cells = Along(main).ToList();
            for (int k = 0; k < 10; k++)
            {
                var (x, y) = cells[k * cells.Count / 10];
                TestContext.Out.WriteLine($"PLACES main river {k * 10}%: ({PlanGrid.Mod(x, gen.CellsW)}, {gen.WrapCellY(y)})");
            }
            var roads = map.Hills.Roads;
            for (int sq = 0; sq < roads.SquaresW * roads.SquaresH; sq++)
            {
                int ox = sq % roads.SquaresW * WorldPlan.SquareCells, oy = sq / roads.SquaresW * WorldPlan.SquareCells;
                foreach (var a in roads.ArmsOf(sq))
                    for (int k = 0; k + 1 < a.X.Length; k++)
                    {
                        int x = ox + (int)BiomeMath.FloorDiv(a.X[k], BiomeMath.One), y = oy + (int)BiomeMath.FloorDiv(a.Y[k], BiomeMath.One);
                        gen.HillsCellAt(x, y, out _, out bool ford);
                        if (ford) TestContext.Out.WriteLine($"PLACES ford on a {(a.Kind == HillsRoads.Main ? "main" : "lesser")} road at ({PlanGrid.Mod(x, gen.CellsW)}, {gen.WrapCellY(y)})");
                    }
            }
            foreach (var l in rivers.Lakes) TestContext.Out.WriteLine($"PLACES lake {l.Id}{(l.End ? " (end)" : "")} at ({l.X}, {l.Y})");
            Assert.Pass();
        }

        [Test]
        public void TheRiversGolden()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            string Hex((int cx, int cy) c) => gen.Make(c.cx, c.cy).Hash.ToString("x8", CultureInfo.InvariantCulture);
            var main = Main;
            var spring = ((int)(main.X[0] / BiomeMath.One) / s, (int)(main.Y[0] / BiomeMath.One) / s);
            var end = rivers.Lakes.Single(l => l.End);
            var lake = (end.X / s, end.Y / s);
            var reach = Along(main).Select(p => (PlanGrid.Mod(p.x, gen.CellsW) / s, gen.WrapCellY(p.y) / s)).Distinct().ElementAt(40);
            TestContext.Out.WriteLine($"RIVERS golden: the spring's chunk {spring} {Hex(spring)}, a reach's {reach} {Hex(reach)}, the end lake's {lake} {Hex(lake)}");
            Assert.That((Hex(spring), Hex(reach), Hex(lake)), Is.EqualTo(("4b8df59b", "ec5db057", "0281e057")), "a change to the rivers is a change to the map");
        }

        [Test]
        public void PeersAndLateJoinersAgreeOnARiver()
        {
            string dir = Path.Combine(root, "hills-12x10");
            GeneratedMap.EnsureRows(HillsDir, dir);
            GeneratedSource Source() => new GeneratedSource(GeneratedMap.Load(HillsDir), R);
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
            var path = Along(Main).Select(p => (x: PlanGrid.Mod(p.x, host.W), y: gen.WrapCellY(p.y))).ToList();
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
