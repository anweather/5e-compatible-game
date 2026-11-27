// The 12 x 10 world (docs/design/world-12x8-scope.md, J-083, J-084), the first proof that the far field scales: its world
// file (world/maps/world-12x10/map.json) solves a plan 12 squares round and 8 rows of land between an ice row and a desert
// row, east joined to west, and ChunkGen and the known ring wrap with it (generator 6). Nothing bakes it: every cell of it
// would be about 9-10 GB. The land is the same a world round (x and x + the width, bit for bit) and runs on unbroken
// across the seam, the known ring and far landmarks reach across it, and the known ring over every chunk is timed.
// WORLD_12_PNG_OUT=<folder> dotnet test core --filter "FullyQualifiedName~WorldTwelveTests" draws the plan and the
// heights of the whole world (docs/design/world-12x10/).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class WorldTwelveTests
    {
        internal static string Dir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "world-12x10");

        static GeneratedMap world;

        internal static GeneratedMap World => world ??= GeneratedMap.Load(Dir);

        [OneTimeTearDown]
        public void DropTheMap()
        {
            world = null;
            known = null;
        }

        static KnownChunk[] known;
        static double knownMs;

        internal static KnownChunk[] Known
        {
            get
            {
                if (known != null) return known;
                var gen = World.Gen;
                var all = new KnownChunk[gen.ChunksW * gen.ChunksH];
                var watch = Stopwatch.StartNew();
                Parallel.For(0, all.Length, k => all[k] = gen.Know(k % gen.ChunksW, k / gen.ChunksW));
                knownMs = watch.Elapsed.TotalMilliseconds;
                return known = all;
            }
        }

        const uint PlanGolden = 0x9a65cab9u;

        [Test]
        public void NoPlaceIdIsTakenTwice()
        {
            var ids = ScaleIslandTests.PlaceIds(World);
            Assert.That(ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "place ids taken twice (#203)");
            Assert.That(World.Gen.PlaceIds, Is.EquivalentTo(ids));
        }

        [Test]
        public void ItsPlanIsGolden()
        {
            TestContext.Out.WriteLine($"PLAN GOLDEN world-12x10 0x{World.Plan.Hash:x8}");
            Assert.That(World.Plan.Hash, Is.EqualTo(PlanGolden));
            Assert.That(GeneratedMap.Load(Dir).Plan.Hash, Is.EqualTo(PlanGolden), "the same on every load");
        }

        [Test]
        public void TheNorthRowIsIceAndTheSouthRowDesert()
        {
            var p = World.Plan;
            int last = p.Grid.H - 1;
            for (int x = 0; x < p.Grid.W; x++)
            {
                int north = p.Grid.Index(x, 0), south = p.Grid.Index(x, last);
                Assert.That((p.Wall[north], p.TemplateAt(north).Id), Is.EqualTo(((byte)1, "ice-wall")), $"square ({x}, 0)");
                Assert.That((p.Wall[south], p.TemplateAt(south).Id), Is.EqualTo(((byte)2, "dune-sea")), $"square ({x}, {last})");
                Assert.That((p.Cold[north], p.Cold[south]), Is.EqualTo((BiomeMath.One, 0L)), "coldest north, hottest south");
                for (int y = 1; y < last; y++) Assert.That(p.Wall[p.Grid.Index(x, y)], Is.Zero, $"square ({x}, {y}) is land or sea");
            }
            var gen = World.Gen;
            foreach (var k in Known.Where(k => k.Y < WorldPlan.ChunksPerSquare || k.Y >= gen.ChunksH - WorldPlan.ChunksPerSquare))
                Assert.That(k.Water.All(w => w == KnownChunk.Dry), Is.True, $"wall chunk ({k.X}, {k.Y})");
        }

        [Test]
        public void TheLandIsTheSameAWorldRound()
        {
            var gen = World.Gen;
            int w = gen.CellsW, cells = 0;
            for (int y = 3; y < gen.CellsH; y += 11)
                foreach (int x in new[] { -65, -64, -2, -1, 0, 1, 2, 63, 64 })
                {
                    Assert.That(gen.LandAt(x + w, y), Is.EqualTo(gen.LandAt(x, y)), $"cell ({x}, {y}) and a world east");
                    cells++;
                }
            Assert.That(cells, Is.GreaterThan(3000));
            for (int cy = 0; cy < gen.ChunksH; cy++)
            {
                var east = Known[cy * gen.ChunksW + gen.ChunksW - 1];
                var west = Known[cy * gen.ChunksW];
                for (int j = 0; j < KnownChunk.Side; j++)
                    Assert.That(east.Heights[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(west.Heights[j * KnownChunk.Side]), $"chunk row {cy} sample {j}");
            }
        }

        [Test]
        public void TheLandRunsOnUnbrokenAcrossTheSeam()
        {
            var gen = World.Gen;
            int n = ChunkGen.Size, rows = gen.ChunksH;
            var east = new GeneratedChunk[rows];
            var west = new GeneratedChunk[rows];
            Parallel.For(0, rows * 2, k =>
            {
                if (k < rows) east[k] = gen.Make(gen.ChunksW - 1, k);
                else west[k - rows] = gen.Make(0, k - rows);
            });
            int across = 0, inside = 0, worst = 0, valleyStep = 0, seaFloor = 0;
            for (int cy = 0; cy < rows; cy++)
                for (int j = 0; j < n; j++)
                {
                    var e = east[cy].Land;
                    var wl = west[cy].Land;
                    int step = Math.Abs(e[j * n + n - 1] - wl[j * n]);
                    for (int i = 0; i + 1 < n; i++) inside = Math.Max(inside, Math.Abs(e[j * n + i + 1] - e[j * n + i]));
                    if (west[cy].Pinned)
                    {
                        valleyStep = Math.Max(valleyStep, step - Math.Abs(wl[j * n + 1] - wl[j * n]));
                        continue;
                    }
                    for (int i = 0; i + 1 < n; i++) inside = Math.Max(inside, Math.Abs(wl[j * n + i + 1] - wl[j * n + i]));
                    if (e[j * n + n - 1] < ChunkGen.SeaLevel && wl[j * n] < ChunkGen.SeaLevel) seaFloor = Math.Max(seaFloor, step);
                    if (step > across) (across, worst) = (step, cy * n + j);
                }
            TestContext.Out.WriteLine($"SEAM the biggest step across it {across} land units at row {worst}; inside its chunks {inside}; " +
                $"onto the valley {valleyStep} more than the valley's own; on the sea floor {seaFloor}");
            Assert.That(west.Count(c => c.Pinned), Is.EqualTo(WorldPlan.ChunksPerSquare), "the valley's west side is on the seam");
            Assert.That(across, Is.LessThanOrEqualTo(inside), $"a step of {across} across the seam at row {worst}");
            Assert.That(valleyStep, Is.LessThanOrEqualTo(8));
            Assert.That(seaFloor, Is.LessThanOrEqualTo(4), "the sea floor across the seam");
        }

        [Test]
        public void TheSeaFloorShelvesIntoTheOpenSea()
        {
            var gen = World.Gen;
            var plan = World.Plan;
            var grid = plan.Grid;
            int sq = ChunkGen.Square;
            var sides = new System.Collections.Generic.List<(int i, int s)>();
            for (int i = 0; i < grid.W * grid.H; i++)
                for (int s = 1; s <= 2; s++)
                {
                    int j = grid.Neighbour(i, s);
                    if (j < 0 || i == plan.Valley || j == plan.Valley || plan.Wall[i] != 0 || plan.Wall[j] != 0) continue;
                    sides.Add((i, s));
                }
            bool CoastFacing((int i, int s) p)
            {
                int j = grid.Neighbour(p.i, p.s);
                return (plan.Band[j] == 0 && plan.Band[p.i] != 0 && plan.Coasts[p.i].Open[p.s]) || (plan.Band[p.i] == 0 && plan.Band[j] != 0 && plan.Coasts[j].Open[(p.s + 2) % 4]);
            }
            var worst = new (int step, int x, int y)[sides.Count];
            Parallel.For(0, sides.Count, k =>
            {
                var (i, s) = sides[k];
                int bx = (grid.X(i) - gen.OriginX + (s == 1 ? 1 : 0)) * sq, by = (grid.Y(i) - gen.OriginY + (s == 2 ? 1 : 0)) * sq;
                var best = (0, 0, 0);
                for (int t = 0; t < sq; t += 4)
                {
                    int prev = int.MaxValue;
                    for (int o = -16; o < 16; o++)
                    {
                        int x = gen.WrapCell(s == 1 ? bx + o : bx + t), y = s == 1 ? by + t : by + o;
                        int h = gen.LandAt(x, y);
                        if (h < ChunkGen.SeaLevel && prev < ChunkGen.SeaLevel && Math.Abs(h - prev) > best.Item1) best = (Math.Abs(h - prev), x, y);
                        prev = h;
                    }
                }
                worst[k] = best;
            });
            var top = worst.OrderByDescending(w => w.step).FirstOrDefault();
            var facing = Enumerable.Range(0, sides.Count).Where(k => CoastFacing(sides[k])).ToList();
            TestContext.Out.WriteLine($"SHELF {sides.Count} sides, {facing.Count} where a coast's sea meets an open-sea square; the sea floor's biggest step " +
                $"across one {top.step} land units at ({top.x}, {top.y}), across those {facing.Max(k => worst[k].step)}");
            Assert.That(facing, Is.Not.Empty, "a coast meets the open sea");
            Assert.That(top.step, Is.LessThanOrEqualTo(4), $"a step of {top.step} on the sea floor at ({top.x}, {top.y})");
        }

        [Test]
        [NonParallelizable]
        public void TheKnownRingOverEveryChunk()
        {
            if (Type.GetType("Mono.Runtime") != null)
                Assert.Ignore("The whole 12 x 10's known ring is proved in the .NET suite (dotnet test core, the landing gates): under Mono it takes minutes (#187).");
            var gen = World.Gen;
            int count = gen.ChunksW * gen.ChunksH;
            Assert.That(Known.Length, Is.EqualTo(count));
            var one = Stopwatch.StartNew();
            for (int cx = 0; cx < gen.ChunksW; cx++) gen.Know(cx, 40);
            double perChunk = one.Elapsed.TotalMilliseconds / gen.ChunksW;
            var ring = new KnownRing(gen, 0, gen.ChunksW);
            ring.Want(new[] { (0, 45) });
            var watch = Stopwatch.StartNew();
            ring.Fill();
            double ringMs = watch.Elapsed.TotalMilliseconds;
            int seaOnly = Known.Count(k => k.IsSea);
            TestContext.Out.WriteLine($"KNOWN every chunk of the 12 x 10 ({count}): {knownMs:F0} ms on {Environment.ProcessorCount} threads; " +
                $"{perChunk:F2} ms a chunk on one; the ring from one character {ringMs:F0} ms, {ring.Chunks.Count} shown ({seaOnly} all sea)");
            Assert.That(ring.Chunks.Count, Is.GreaterThan(count * 3 / 4).And.LessThan(count));
            Assert.That(ring.Chunks.Select(k => (k.X, k.Y)), Is.Unique);
            var differ = ring.Chunks.Where(k => !k.Heights.AsSpan().SequenceEqual(Known[k.Y * gen.ChunksW + k.X].Heights)).Select(k => (k.X, k.Y)).Take(5).ToList();
            Assert.That(differ, Is.Empty, "chunks whose ring heights aren't their known heights");
            Assert.That(perChunk, Is.LessThan(5), "a known chunk costs well under a made one");
        }

        [Test]
        public void FarLandmarksAreSeenAcrossTheSeam()
        {
            var gen = World.Gen;
            int w = gen.CellsW;
            var far = World.Far.All;
            Assert.That(far, Is.Not.Empty);
            foreach (var l in far)
            {
                Assert.That(l.WrapCells, Is.EqualTo(w));
                int x = l.X < w / 2 ? w - 40 : 40;
                int dx = Math.Abs(l.DxTo(x));
                Assert.That(dx, Is.LessThanOrEqualTo(w / 2));
                Assert.That(l.SeenAt(x, l.Y), Is.EqualTo(dx <= l.SeenFrom * ChunkGen.Size), $"{l.Type} at ({l.X}, {l.Y}) from ({x}, {l.Y})");
                Assert.That(l.SeenAt(l.X + w, l.Y) && l.SeenAt(l.X - w, l.Y), Is.True, "a world round is where it stands");
            }
            Assert.That(far.Any(l => l.X >= w / 2 ? l.SeenAt(0, l.Y) : l.SeenAt(w - 1, l.Y)), Is.True, "a landmark seen across the seam");
        }
    }
}
