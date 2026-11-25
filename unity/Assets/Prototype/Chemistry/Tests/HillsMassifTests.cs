// The rolling hills' alpine massif (world/maps/hills-12x10, docs/design/alpine.md): a high meadow ringed by mountains,
// passes where the main roads cross the ring, and foothills down to the hills.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsMassifTests
    {
        static string HillsDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");

        GeneratedMap map;

        [OneTimeSetUp]
        public void LoadTheHills() => map = GeneratedMap.Load(HillsDir);

        HillsMassif Massif => map.Hills.Massif;

        [Test]
        public void AMeadowRingedByMountainsInTheMiddle()
        {
            var m = Massif;
            var land = map.Hills;
            Assert.That(m, Is.Not.Null, "map.json asks for a massif");
            Assert.That((m.CentreX, m.CentreY), Is.EqualTo((map.Gen.CellsW / 2, map.Gen.CellsH / 2)), "in the middle of the map");
            // Round the centre: the meadow's land, the ring's highest and the foothills', by radius.
            int meadowLow = int.MaxValue, meadowHigh = int.MinValue, ringHigh = int.MinValue;
            for (int a = 0; a < 360; a += 3)
            {
                double ang = a * Math.PI / 180;
                int Cell(int r, bool y) => (y ? m.CentreY + (int)Math.Round(r * Math.Sin(ang)) : m.CentreX + (int)Math.Round(r * Math.Cos(ang)));
                for (int r = 0; r < m.Meadow - m.Warp; r += 16)
                {
                    int h = land.At(Cell(r, false), Cell(r, true));
                    meadowLow = Math.Min(meadowLow, h);
                    meadowHigh = Math.Max(meadowHigh, h);
                }
                for (int r = m.Meadow; r < m.Foot; r += 8) ringHigh = Math.Max(ringHigh, land.At(Cell(r, false), Cell(r, true)));
                // Past its reach (and its wander) the land is the hills' own.
                for (int r = m.Reach + m.Warp + 2; r < m.Reach + m.Warp + 200; r += 20)
                    Assert.That(land.At(Cell(r, false), Cell(r, true)), Is.EqualTo(land.Rolling(Cell(r, false), Cell(r, true))));
            }
            TestContext.Out.WriteLine($"MASSIF meadow {meadowLow / 8.0:0} to {meadowHigh / 8.0:0} m, the ring's highest {ringHigh / 8.0:0} m");
            Assert.That(meadowLow, Is.GreaterThanOrEqualTo(land.Base + m.MeadowHeight), "the meadow stands high over the hills");
            Assert.That(ringHigh - meadowHigh, Is.GreaterThan(800), "peaks stand over 100 m above the meadow");
            Assert.That(land.Highest, Is.LessThanOrEqualTo(MapFile.Load(HillsDir).LandRange.high), "map.json's drawable range holds the peaks");
        }

        [Test]
        public void EachMainRoadCrossesTheRingThroughAPass()
        {
            var m = Massif;
            var gen = map.Gen;
            var roads = map.Hills.Roads;
            int passes = 0;
            foreach (var (x, y) in roads.MainLines())
            {
                // The line's points in the ring: the road there, and the ring's land beside it.
                int roadTop = int.MinValue, sideTop = int.MinValue;
                for (int k = 0; k < x.Length; k++)
                {
                    int cx = (int)BiomeMath.FloorDiv(x[k], BiomeMath.One), cy = (int)BiomeMath.FloorDiv(y[k], BiomeMath.One);
                    if (!m.InRing(cx, cy) || m.Ring(m.Radius(cx, cy)) < BiomeMath.One / 2) continue;
                    roadTop = Math.Max(roadTop, gen.HillsLandAt(cx, cy, out bool road));
                    Assert.That(road, Is.True);
                    // Off the road either way, a pass's walls: the land well off the line stands higher.
                    for (int off = m.PassWidth; off <= 2 * m.PassWidth; off += 40)
                        sideTop = Math.Max(sideTop, Math.Max(map.Hills.At(cx + off, cy), Math.Max(map.Hills.At(cx - off, cy), Math.Max(map.Hills.At(cx, cy + off), map.Hills.At(cx, cy - off)))));
                }
                if (roadTop == int.MinValue) continue;
                passes++;
                // Where the pass's col is: the line's point nearest the crest.
                int col = 0;
                long crest = -1;
                for (int k = 0; k < x.Length; k++)
                {
                    int cx = (int)BiomeMath.FloorDiv(x[k], BiomeMath.One), cy = (int)BiomeMath.FloorDiv(y[k], BiomeMath.One);
                    long w = m.Touches(cx, cy) ? m.Ring(m.Radius(cx, cy)) : 0;
                    if (w > crest) (crest, col) = (w, k);
                }
                TestContext.Out.WriteLine($"MASSIF a pass's col near cell ({BiomeMath.FloorDiv(x[col], BiomeMath.One)}, {BiomeMath.FloorDiv(y[col], BiomeMath.One)})");
                TestContext.Out.WriteLine($"MASSIF a main road's arm over the ring tops at {roadTop / 8.0:0} m, its pass's walls {sideTop / 8.0:0} m");
                Assert.That(sideTop - roadTop, Is.GreaterThan(400), "the road goes through a pass, the mountains 50 m and more above it");
            }
            Assert.That(passes, Is.GreaterThanOrEqualTo(4), "the main row and column through the middle each cross the ring twice");
        }

        [Test]
        public void NoLesserRoadReachesTheRing()
        {
            var m = Massif;
            var roads = map.Hills.Roads;
            int lesser = 0;
            for (int i = 0; i < roads.SquaresW * roads.SquaresH; i++)
            {
                int ox = i % roads.SquaresW * WorldPlan.SquareCells, oy = i / roads.SquaresW * WorldPlan.SquareCells;
                foreach (var a in roads.ArmsOf(i))
                {
                    if (a.Kind != HillsRoads.Lesser) continue;
                    lesser++;
                    for (int k = 0; k < a.X.Length; k++)
                    {
                        int x = ox + (int)BiomeMath.FloorDiv(a.X[k], BiomeMath.One), y = oy + (int)BiomeMath.FloorDiv(a.Y[k], BiomeMath.One);
                        Assert.That(m.InRing(x, y), Is.False, $"square {i}'s lesser road at ({x}, {y})");
                    }
                }
            }
            Assert.That(lesser, Is.GreaterThan(100));
        }

        // A cell's ground worked out from the land at it and its four neighbours alone, as a chunk makes it (or what's laid on it).
        char GroundAt(int x, int y)
        {
            int h = map.Gen.HillsCellAt(x, y, out char kind, out _);
            if (kind != '\0') return kind;
            int slope = 0;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) slope = Math.Max(slope, Math.Abs(map.Gen.HillsCellAt(x + dx, y + dy, out _, out _) - h));
            if (slope >= ChunkGen.HillsRock) return '^';
            return slope >= ChunkGen.HillsScree ? ':' : ',';
        }

        // Chunks of the ring, the meadow and the foothills along a line from the centre east, and its pass's chunks.
        List<(int cx, int cy)> Sample()
        {
            var m = Massif;
            var list = new List<(int, int)>();
            for (int r = 0; r < m.Reach; r += 192) list.Add(((m.CentreX + r) / ChunkGen.Size, (m.CentreY + r / 3) / ChunkGen.Size));
            for (int r = m.Meadow; r < m.Foot; r += 128) list.Add(((m.CentreX - r / 2) / ChunkGen.Size, (m.CentreY - r) / ChunkGen.Size));
            return list.Distinct().ToList();
        }

        [Test]
        public void RockAndScreeWhereItsSteepAndChunksMeetAtTheirEdges()
        {
            int s = ChunkGen.Size, rock = 0, scree = 0, grass = 0, edges = 0;
            foreach (var (cx, cy) in Sample())
            {
                var c = map.Gen.Make(cx, cy);
                rock += c.Cells.Count(ch => ch == '^');
                scree += c.Cells.Count(ch => ch == ':');
                grass += c.Cells.Count(ch => ch == ',');
                // Every edge cell is what the cell alone works out: the land and the ground, so the chunk east of it meets it.
                for (int k = 0; k < s; k++)
                    foreach (var (i, j) in new[] { (0, k), (s - 1, k), (k, 0), (k, s - 1) })
                    {
                        int x = cx * s + i, y = cy * s + j;
                        Assert.That(c.Land[j * s + i], Is.EqualTo(map.Gen.HillsLandAt(x, y, out _)), $"({x}, {y})");
                        Assert.That(c.Cells[j * s + i], Is.EqualTo(GroundAt(x, y)), $"({x}, {y})");
                        edges++;
                    }
            }
            TestContext.Out.WriteLine($"MASSIF sampled chunks: {rock} rock, {scree} scree, {grass} grass cells; {edges} edge cells as a cell alone works them out");
            Assert.That(rock, Is.GreaterThan(1000), "bare rock on the mountains");
            Assert.That(scree, Is.GreaterThan(1000), "scree below it");
            Assert.That(grass, Is.GreaterThan(rock), "the meadow and the foothills are grass");
        }

        [Test]
        public void TheMassifIsTheSameInAnyOrderOnAnyThread()
        {
            var chunks = Sample();
            var first = chunks.ToDictionary(c => c, c => map.Gen.Make(c.cx, c.cy).Hash);
            var other = GeneratedMap.Load(HillsDir);
            var again = new uint[chunks.Count];
            Parallel.For(0, chunks.Count, k =>
            {
                var (cx, cy) = chunks[chunks.Count - 1 - k];
                again[chunks.Count - 1 - k] = other.Gen.Make(cx, cy).Hash;
            });
            for (int k = 0; k < chunks.Count; k++) Assert.That(again[k], Is.EqualTo(first[chunks[k]]), $"chunk {chunks[k]}");
            // A known chunk samples the same land as the made one.
            var (kx, ky) = chunks[chunks.Count / 2];
            var known = map.Gen.Know(kx, ky);
            var made = map.Gen.Make(kx, ky);
            for (int j = 0; j < KnownChunk.Side - 1; j++)
                for (int i = 0; i < KnownChunk.Side - 1; i++)
                    Assert.That(known.Heights[j * KnownChunk.Side + i], Is.EqualTo(made.Land[j * KnownChunk.Step * ChunkGen.Size + i * KnownChunk.Step]));
        }

        [Test]
        public void TheMassifsGolden()
        {
            var m = Massif;
            string Hex(int cx, int cy) => map.Gen.Make(cx, cy).Hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            int ring = (m.CentreX + m.Crest) / ChunkGen.Size, foot = (m.CentreX + (m.Foot + m.Reach) / 2) / ChunkGen.Size, row = m.CentreY / ChunkGen.Size;
            TestContext.Out.WriteLine($"MASSIF golden: the ring's chunk ({ring}, {row}) {Hex(ring, row)}, the foothills' ({foot}, {row}) {Hex(foot, row)}");
            Assert.That((Hex(ring, row), Hex(foot, row)), Is.EqualTo(("9061c639", "9c15349d")), "a change to the massif is a change to the map");
        }

        [Test, Explicit("where a spring's water would go, for the rivers' design")]
        public void WhereASpringsWaterGoes()
        {
            const int g = 16;
            var gen = map.Gen;
            int w = gen.CellsW / g, h = gen.CellsH / g;
            var land = new int[w * h];
            var sw = Stopwatch.StartNew();
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++) land[j * w + i] = map.Hills.At(i * g, j * g);
            TestContext.Out.WriteLine($"SPRING sampled {w}x{h} in {sw.ElapsedMilliseconds} ms");
            int Id(int i, int j) => PlanGrid.Mod(j, h) * w + PlanGrid.Mod(i, w);
            var m = Massif;
            foreach (var (name, r, deg) in new[] { ("inner flank NE", 880, 45), ("inner flank SW", 880, 225), ("outer flank E", 1300, 0), ("outer flank N", 1300, 270) })
            {
                double a = deg * Math.PI / 180;
                int at = Id((m.CentreX + (int)(r * Math.Cos(a))) / g, (m.CentreY + (int)(r * Math.Sin(a))) / g);
                TestContext.Out.WriteLine($"SPRING {name} at ({at % w * g}, {at / w * g}) {land[at] / 8.0:0} m");
                for (int basin = 0; basin < 6; basin++)
                {
                    // Down the steepest way to a pit.
                    int steps = 0;
                    while (true)
                    {
                        int best = at;
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int n = Id(at % w + di, at / w + dj);
                                if (land[n] < land[best]) best = n;
                            }
                        if (best == at) break;
                        at = best;
                        steps++;
                    }
                    // Fill the pit until it spills: the lowest rim first, until a cell lower than the water is reached.
                    var seen = new HashSet<int> { at };
                    var open = new SortedSet<(int h, int id)> { (land[at], at) };
                    int level = land[at], spill = -1;
                    while (open.Count > 0 && spill < 0 && seen.Count < 200000)
                    {
                        var (lh, id) = open.Min;
                        open.Remove(open.Min);
                        if (lh < level) { spill = id; break; }
                        level = Math.Max(level, lh);
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int n = Id(id % w + di, id / w + dj);
                                if (seen.Add(n)) open.Add((land[n], n));
                            }
                    }
                    TestContext.Out.WriteLine($"SPRING   {steps} steps down to a pit at ({at % w * g}, {at / w * g}) {land[at] / 8.0:0.0} m; it fills to {level / 8.0:0.0} m over {seen.Count - open.Count} samples ({(seen.Count - open.Count) * g * g * 2.25 / 1e6:0.00} km2) and spills at ({spill % w * g}, {spill / w * g})");
                    if (spill < 0) break;
                    at = spill;
                }
            }
            Assert.Pass();
        }

        [Test, Explicit("a picture of the map, for looking at")]
        public void DrawTheMap()
        {
            var sw = Stopwatch.StartNew();
            var again = GeneratedMap.Load(HillsDir);
            TestContext.Out.WriteLine($"MASSIF load {sw.ElapsedMilliseconds} ms");
            var gen = map.Gen;
            const int step = 8;
            int w = gen.CellsW / step, h = gen.CellsH / step;
            var px = new byte[w * h * 3];
            int lo = int.MaxValue, hi = int.MinValue;
            var land = new int[w * h];
            var road = new bool[w * h];
            var wet = new bool[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    land[j * w + i] = gen.HillsCellAt(i * step, j * step, out char kind, out _);
                    road[j * w + i] = kind == '.';
                    wet[j * w + i] = kind == 'r' || kind == 'w';
                    lo = Math.Min(lo, land[j * w + i]);
                    hi = Math.Max(hi, land[j * w + i]);
                }
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int v = land[j * w + i];
                    int sl = 0;
                    if (i > 0 && j > 0) sl = Math.Max(Math.Abs(v - land[j * w + i - 1]), Math.Abs(v - land[(j - 1) * w + i])) / step;
                    double t = (v - lo) / (double)(hi - lo);
                    double shade = 1.0 - Math.Min(0.6, sl * 0.08);
                    (double r, double g, double b) c = (0.25 + 0.4 * t, 0.5 + 0.3 * t, 0.2 + 0.4 * t);
                    if (sl >= ChunkGen.HillsScree) c = (0.6, 0.58, 0.5);
                    if (sl >= ChunkGen.HillsRock) c = (0.5, 0.48, 0.45);
                    if (road[j * w + i]) c = (0.9, 0.6, 0.2);
                    if (wet[j * w + i]) c = (0.1, 0.3, 0.9);
                    px[(j * w + i) * 3] = (byte)(255 * c.r * shade);
                    px[(j * w + i) * 3 + 1] = (byte)(255 * c.g * shade);
                    px[(j * w + i) * 3 + 2] = (byte)(255 * c.b * shade);
                }
            string path = Environment.GetEnvironmentVariable("MASSIF_PPM") ?? Path.Combine(Path.GetTempPath(), "massif.ppm");
            using (var f = File.Create(path))
            {
                var head = System.Text.Encoding.ASCII.GetBytes($"P6 {w} {h} 255\n");
                f.Write(head, 0, head.Length);
                f.Write(px, 0, px.Length);
            }
            TestContext.Out.WriteLine($"MASSIF land {lo}..{hi} units ({lo / 8.0:0} to {hi / 8.0:0} m), picture {path}");
            var roads = map.Hills.Roads;
            for (int i = 0; i < roads.SquaresW * roads.SquaresH; i++)
                foreach (var a in roads.ArmsOf(i))
                    if (a.Profile != null) TestContext.Out.WriteLine($"MASSIF arm square ({i % roads.SquaresW}, {i / roads.SquaresW}) side {a.Side} kind {a.Kind}: cut {a.Cut / 8.0:0.0} m fill {a.Fill / 8.0:0.0} m");
            var rv = map.Hills.Rivers;
            if (rv != null)
            {
                var m = map.Hills.Massif;
                TestContext.Out.WriteLine($"MASSIF gorge way {m.GorgeIndex} ({m.GorgeWay.x / 65536.0:0.00}, {m.GorgeWay.y / 65536.0:0.00}) floor {m.GorgeLevel / 8.0:0.0} m");
                foreach (var l in rv.Lakes) TestContext.Out.WriteLine($"MASSIF lake {l.Id} at ({l.X}, {l.Y}) level {l.Level / 8.0:0.0} m, {l.Nodes} nodes ({l.Nodes * 576 / 1e6:0.000} km2){(l.End ? " END" : "")}");
                foreach (var r in rv.Rivers) TestContext.Out.WriteLine($"MASSIF river from ({r.X[0] / 65536}, {r.Y[0] / 65536}) {r.Surface[0] / 8.0:0} m to ({r.X[r.X.Length - 1] / 65536}, {r.Y[r.Y.Length - 1] / 65536}) {r.Surface[r.Surface.Length - 1] / 8.0:0} m, {r.Length * 1.5 / 1000:0.00} km, catchment {r.Catchment[0]}..{r.Catchment[r.Catchment.Length - 1]}, half {r.Half[0]}..{r.Half[r.Half.Length - 1]}{(r.Joins ? " joins" : "")}");
            }
            sw.Restart();
            int n = 0;
            for (int cy = 40; cy < 50; cy++)
                for (int cx = 49; cx < 59; cx++, n++) gen.Make(cx, cy);
            TestContext.Out.WriteLine($"MASSIF make {sw.ElapsedMilliseconds / (double)n:0.00} ms a chunk in the massif");
            sw.Restart();
            n = 0;
            for (int cy = 80; cy < 90; cy++)
                for (int cx = 98; cx < 108; cx++, n++) gen.Make(cx, cy);
            TestContext.Out.WriteLine($"MASSIF make {sw.ElapsedMilliseconds / (double)n:0.00} ms a chunk at the spawn");
            Assert.That(again, Is.Not.Null);
        }
    }
}
