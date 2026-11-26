// The rolling hills' roads (world/maps/hills-12x10, docs/design/hills.md "Roads"): made in the generator from map data
// alone. The network reaches every world square and runs on round both seams; a side is the same road seen from either
// square; a road runs on over chunk edges and both seams, level across and gentle along; a chunk is the same in any order
// on any thread; the spawn is on a road; loading lays nothing for them; and peers and late joiners agree on a road chunk.
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
    public class HillsRoadsTests
    {

        internal static string HillsDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");

        string root;
        GeneratedMap map;
        HillsRoads roads;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-roads-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsDir);
            roads = map.Hills.Roads;
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        int Squares => roads.SquaresW * roads.SquaresH;

        // The way a side faces, in world squares.
        static (int dx, int dy) Step(int side) => side switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

        int Neighbour(int i, int side)
        {
            int sx = i % roads.SquaresW, sy = i / roads.SquaresW;
            var (dx, dy) = Step(side);
            sx += dx;
            sy += dy;
            return PlanGrid.Mod(sy, roads.SquaresH) * roads.SquaresW + PlanGrid.Mod(sx, roads.SquaresW);
        }

        [Test]
        public void TheNetworkReachesEverySquareAndRunsRoundBothSeams()
        {
            Assert.That(roads, Is.Not.Null, "map.json asks for roads");
            var seen = new HashSet<int> { 0 };
            var open = new Queue<int>(new[] { 0 });
            while (open.Count > 0)
            {
                int i = open.Dequeue();
                for (int s = 0; s < 4; s++)
                    if (roads.SideOf(i, s).kind != HillsRoads.None && seen.Add(Neighbour(i, s))) open.Enqueue(Neighbour(i, s));
            }
            // Every world square but the massif's wild ones, which no lesser road reaches (HillsMassifTests).
            var massif = map.Hills.Massif;
            int wild = 0;
            for (int i = 0; i < Squares; i++)
            {
                if (seen.Contains(i)) continue;
                wild++;
                Assert.That(roads.ArmsOf(i), Is.Empty, $"square {i} is off the network and has no road");
                bool ring = false;
                int ox = i % roads.SquaresW * WorldPlan.SquareCells, oy = i / roads.SquaresW * WorldPlan.SquareCells;
                for (int y = 0; y < WorldPlan.SquareCells && !ring; y += 32)
                    for (int x = 0; x < WorldPlan.SquareCells && !ring; x += 32) ring = massif.InRing(ox + x, oy + y);
                Assert.That(ring, Is.True, $"square {i} off the network is in the massif's ring");
            }
            TestContext.Out.WriteLine($"ROADS {Squares - wild} world squares on the network, {wild} wild in the massif's ring");
            Assert.That(wild, Is.LessThanOrEqualTo(12));
            // Main roads: every side along their row (column), the seam's too.
            foreach (int r in roads.MainRows)
                for (int sx = 0; sx < roads.SquaresW; sx++) Assert.That(roads.SideOf(r * roads.SquaresW + sx, 1).kind, Is.EqualTo(HillsRoads.Main), $"row {r} at {sx}");
            foreach (int c in roads.MainColumns)
                for (int sy = 0; sy < roads.SquaresH; sy++) Assert.That(roads.SideOf(sy * roads.SquaresW + c, 2).kind, Is.EqualTo(HillsRoads.Main), $"column {c} at {sy}");
            int sides = 0, main = 0;
            for (int i = 0; i < Squares; i++)
                for (int s = 1; s <= 2; s++)
                {
                    var k = roads.SideOf(i, s).kind;
                    if (k != HillsRoads.None) sides++;
                    if (k == HillsRoads.Main) main++;
                }
            TestContext.Out.WriteLine($"ROADS {sides} of {2 * Squares} sides crossed, {main} by main roads");
            Assert.That(sides, Is.LessThan(2 * Squares * 3 / 4), "not every side: the network has a shape");
        }

        [Test]
        public void ASideIsTheSameRoadFromBothSquares()
        {
            const int sq = WorldPlan.SquareCells;
            for (int i = 0; i < Squares; i++)
                for (int s = 0; s < 4; s++)
                {
                    int j = Neighbour(i, s);
                    Assert.That(roads.SideOf(i, s), Is.EqualTo(roads.SideOf(j, (s + 2) % 4)), $"square {i} side {s}");
                    if (roads.SideOf(i, s).kind == HillsRoads.None) continue;
                    // The two arms start at the one point: the slot's middle, on the side between them.
                    var a = roads.ArmsOf(i).Single(x => x.Side == s);
                    var b = roads.ArmsOf(j).Single(x => x.Side == (s + 2) % 4);
                    long dx = (long)Step(s).dx * sq, dy = (long)Step(s).dy * sq;
                    Assert.That((a.X[0], a.Y[0]), Is.EqualTo((b.X[0] + dx * BiomeMath.One, b.Y[0] + dy * BiomeMath.One)), $"square {i} side {s}: the arms meet");
                    // Each leaves its side square on, so the road runs straight over it.
                    long across = s % 2 == 0 ? a.X[1] - a.X[0] : a.Y[1] - a.Y[0], inward = s % 2 == 0 ? a.Y[1] - a.Y[0] : a.X[1] - a.X[0];
                    Assert.That(Math.Abs(across) * 4, Is.LessThan(Math.Abs(inward)), $"square {i} side {s}: square on");
                }
        }

        // Every cell along a main road's line, at every cell step, once round the torus and a little past its start again (a
        // world on), so the walk goes over the seam.
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

        [Test]
        public void AMainRoadRunsOnRoundTheTorus()
        {
            var gen = map.Gen;
            foreach (var (alongRow, index) in roads.MainRows.Select(r => (true, r)).Concat(roads.MainColumns.Select(c => (false, c))))
            {
                int cells = 0, off = 0, steepest = 0, crossedSeam = 0, lastLand = int.MinValue;
                (int x, int y) last = default;
                foreach (var (x, y) in Along(alongRow, index))
                {
                    int land = gen.HillsLandAt(x, y, out bool road);
                    cells++;
                    if (!road) off++;
                    if (lastLand != int.MinValue) steepest = Math.Max(steepest, Math.Abs(land - lastLand));
                    if (cells > 1 && (Math.Abs(gen.WrapCellY(y) - gen.WrapCellY(last.y)) > gen.CellsH / 2 || Math.Abs(PlanGrid.Mod(x, gen.CellsW) - PlanGrid.Mod(last.x, gen.CellsW)) > gen.CellsW / 2)) crossedSeam++;
                    last = (x, y);
                    lastLand = land;
                }
                TestContext.Out.WriteLine($"ROADS main {(alongRow ? "row" : "column")} {index}: {cells} cells ({cells * 1.5 / 1000:0.0} km), {off} off the road, steepest {steepest} land units a cell, {crossedSeam} seam crossings");
                Assert.That(off, Is.Zero, "every cell on its line is road");
                // The grade limit is 12% (1.44 land units a cell); a diagonal step of the walk can move the line's nearest
                // point two cells, so up to 4 between neighbours where the land climbs at nearly the limit.
                Assert.That(steepest, Is.LessThanOrEqualTo(4), "within the grade along, through the massif's passes too");
                Assert.That(crossedSeam, Is.EqualTo(1), "once round: over its seam once");
            }
        }

        // The chunk a map cell is in, folded.
        (int cx, int cy) ChunkOf(int x, int y) => (PlanGrid.Mod(x, map.Gen.CellsW) / ChunkGen.Size, map.Gen.WrapCellY(y) / ChunkGen.Size);

        [Test]
        public void ChunksMeetOnTheRoadOverEdgesAndBothSeams()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            var made = new Dictionary<(int, int), GeneratedChunk>();
            GeneratedChunk Chunk((int cx, int cy) c)
            {
                if (!made.TryGetValue(c, out var m)) made[c] = m = gen.Make(c.cx, c.cy);
                return m;
            }
            int Local(int x, int y) => map.Gen.WrapCellY(y) % s * s + PlanGrid.Mod(x, gen.CellsW) % s;
            int edges = 0, seams = 0, worst = 0, disagree = 0;
            foreach (var (alongRow, index) in roads.MainRows.Select(r => (true, r)).Concat(roads.MainColumns.Select(c => (false, c))))
            {
                (int x, int y)? was = null;
                foreach (var p in Along(alongRow, index))
                {
                    var c = ChunkOf(p.x, p.y);
                    if (was is { } q && ChunkOf(q.x, q.y) != c)
                    {
                        // The line steps from one chunk into the next: both cells are road in their own chunk, and level.
                        var a = Chunk(ChunkOf(q.x, q.y));
                        var b = Chunk(c);
                        edges++;
                        if (Math.Abs(a.X - b.X) > 1 || Math.Abs(a.Y - b.Y) > 1) seams++;
                        // Road, or a ford's water where it crosses a river (HillsRiversTests).
                        Assert.That(a.Cells[Local(q.x, q.y)] is '.' or 'r', Is.True, $"({q.x}, {q.y}) in chunk ({a.X}, {a.Y})");
                        Assert.That(b.Cells[Local(p.x, p.y)] is '.' or 'r', Is.True, $"({p.x}, {p.y}) in chunk ({b.X}, {b.Y})");
                        worst = Math.Max(worst, Math.Abs(a.Land[Local(q.x, q.y)] - b.Land[Local(p.x, p.y)]));
                        // Every cell of both chunks' facing edges is what the land is there, cell by cell: a chunk reads
                        // every road that reaches it, from its own side and across the seam.
                        for (int k = 0; k < s; k++)
                            foreach (var (ch, i, j) in new[] { (a, 0, k), (a, s - 1, k), (a, k, 0), (a, k, s - 1), (b, 0, k), (b, s - 1, k), (b, k, 0), (b, k, s - 1) })
                            {
                                int land = gen.HillsCellAt(ch.X * s + i, ch.Y * s + j, out char kind, out _);
                                if (land != ch.Land[j * s + i] || (kind != '\0' && kind != ch.Cells[j * s + i])) disagree++;
                            }
                    }
                    was = p;
                }
            }
            TestContext.Out.WriteLine($"ROADS the main roads cross {edges} chunk edges, {seams} of them seams; the land steps at most {worst} units over them");
            Assert.That(disagree, Is.Zero, "a chunk's edges are the land as a cell alone works it out");
            Assert.That(seams, Is.EqualTo(roads.MainRows.Count + roads.MainColumns.Count), "each main road over its seam once");
            Assert.That(worst, Is.LessThanOrEqualTo(2), "within the grade over an edge");
        }

        [Test]
        public void TheRoadIsLevelAcrossAndTheLandAroundItGentle()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size, roadCells = 0, worstRoad = 0, worst = 0;
            // The chunks round the spawn's junction and its two seams.
            for (int cy = 84; cy < 90; cy++)
                for (int cx = 100; cx < 108; cx++)
                {
                    var c = gen.Make(cx, cy);
                    for (int j = 0; j < s; j++)
                        for (int i = 0; i < s; i++)
                        {
                            int k = j * s + i;
                            bool road = c.Cells[k] == '.';
                            if (road) roadCells++;
                            Assert.That(c.Cells[k] == '.' || c.Cells[k] == ',' || c.Cells[k] == 'T', Is.True, "grass, a tree or road");
                            foreach (int n in new[] { i + 1 < s ? k + 1 : -1, j + 1 < s ? k + s : -1 })
                            {
                                if (n < 0) continue;
                                int step = Math.Abs(c.Land[n] - c.Land[k]);
                                worst = Math.Max(worst, step);
                                if (road && c.Cells[n] == '.') worstRoad = Math.Max(worstRoad, step);
                            }
                        }
                }
            TestContext.Out.WriteLine($"ROADS round the spawn: {roadCells} road cells in 48 chunks; road to road steps at most {worstRoad}, anywhere {worst}");
            Assert.That(roadCells, Is.GreaterThan(1000));
            Assert.That(worstRoad, Is.LessThanOrEqualTo(1), "level across, gentle along");
            Assert.That(worst, Is.LessThanOrEqualTo(2), "the banks lean back gently: nowhere a step");
        }

        [Test]
        public void AChunkIsTheSameInAnyOrderOnAnyThread()
        {
            var chunks = new List<(int, int)>();
            foreach (var (x, y) in Along(true, roads.MainRows[1]).Where((_, k) => k % 97 == 0)) chunks.Add(ChunkOf(x, y));
            chunks = chunks.Distinct().ToList();
            var first = chunks.ToDictionary(c => c, c => map.Gen.Make(c.Item1, c.Item2).Hash);
            var other = GeneratedMap.Load(HillsDir);
            var again = new uint[chunks.Count];
            Parallel.For(0, chunks.Count, k =>
            {
                var (cx, cy) = chunks[chunks.Count - 1 - k];
                again[chunks.Count - 1 - k] = other.Gen.Make(cx, cy).Hash;
            });
            for (int k = 0; k < chunks.Count; k++) Assert.That(again[k], Is.EqualTo(first[chunks[k]]), $"chunk {chunks[k]}");
            // Known chunks sample the same land, roads in it.
            var (kx, ky) = ChunkOf(6880, 5601);
            var known = map.Gen.Know(kx, ky);
            var made = map.Gen.Make(kx, ky);
            for (int j = 0; j < KnownChunk.Side - 1; j++)
                for (int i = 0; i < KnownChunk.Side - 1; i++)
                    Assert.That(known.Heights[j * KnownChunk.Side + i], Is.EqualTo(made.Land[j * KnownChunk.Step * ChunkGen.Size + i * KnownChunk.Step]));
        }

        [Test]
        public void TheRoadsGolden()
        {
            string Hex(int cx, int cy) => map.Gen.Make(cx, cy).Hash.ToString("x8", CultureInfo.InvariantCulture);
            var (sx, sy) = ChunkOf(6880, 5601);
            TestContext.Out.WriteLine($"ROADS golden: spawn chunk ({sx}, {sy}) {Hex(sx, sy)}, the east seam's other side (0, {sy}) {Hex(0, sy)}, (102, 89) {Hex(102, 89)}");
            Assert.That(map.Gen.Make(sx, sy).Cells.Count(c => c == '.'), Is.GreaterThan(100), "the spawn's chunk has a road in it");
            Assert.That((Hex(sx, sy), Hex(0, sy), Hex(102, 89)), Is.EqualTo(("9c6c171b", "5f49a928", "fa458485")), "a change to the roads is a change to the map");
        }

        [Test]
        public void TheSpawnIsOnARoad()
        {
            var file = MapFile.Load(HillsDir);
            map.Gen.HillsLandAt(file.Spawn.x, file.Spawn.y, out bool road);
            Assert.That(road, Is.True, $"the spawn {file.Spawn}");
        }
    }
}
