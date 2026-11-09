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
        static string Dir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "world-12x10");

        static GeneratedMap world;

        static GeneratedMap World => world ??= GeneratedMap.Load(Dir);

        static KnownChunk[] known;
        static double knownMs;

        // Every chunk known, row-major, on every core (what the known ring's worker does), and how long that took.
        static KnownChunk[] Known
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

        static StreamingParams Streaming => ChemistryTests.R.Sim.Streaming;

        // The plan's golden: a change that alters it is a new WorldPlan.Version, or a change to the files it reads.
        const uint PlanGolden = 0x3e847c28u;

        [Test]
        public void TheWorldFileSaysItsShape()
        {
            var m = World;
            Assert.That((m.SquaresW, m.SquaresH, m.Wrap, m.Bakes), Is.EqualTo((12, 10, PlanWrap.EastWest, false)));
            Assert.That((m.Plan.Grid.W, m.Plan.Grid.H), Is.EqualTo((12, 10)), "8 rows of land and the two wall rows");
            Assert.That((m.Plan.Inputs.Width, m.Plan.Inputs.Height), Is.EqualTo((12, 8)));
            Assert.That((m.Gen.ChunksW, m.Gen.ChunksH, m.Gen.WrapX), Is.EqualTo((108, 90, true)));
            Assert.That((m.Plan.Grid.X(m.Plan.Valley), m.Plan.Grid.Y(m.Plan.Valley)), Is.EqualTo(m.PinnedAt));
            var map = MapFile.Load(Dir);
            Assert.That((map.W, map.H), Is.EqualTo((m.Gen.CellsW, m.Gen.CellsH)));
            Assert.That(map.Spawn, Is.EqualTo((m.Gen.PinnedX + 292, m.Gen.PinnedY + 328)), "the valley's spawn, moved");
            Assert.That(m.Plan.Problems, Is.Empty);
            // It's never baked whole: too big.
            Assert.That(GeneratedMap.IsBaked(Dir), Is.False);
            Assert.That(() => GeneratedMap.EnsureBaked(Dir), Throws.InvalidOperationException.With.Message.Contains("too big to bake"));
            Assert.That(() => m.Bake(Path.Combine(Path.GetTempPath(), "never")), Throws.InvalidOperationException);
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
            // The wall rows stand high: every known sample in them is dry land above the sea.
            var gen = World.Gen;
            foreach (var k in Known.Where(k => k.Y < WorldPlan.ChunksPerSquare || k.Y >= gen.ChunksH - WorldPlan.ChunksPerSquare))
                Assert.That(k.Water.All(w => w == KnownChunk.Dry), Is.True, $"wall chunk ({k.X}, {k.Y})");
        }

        [Test]
        public void TheLandIsTheSameAWorldRound()
        {
            // Land is a function of position, and a position a world's width east is the same position.
            var gen = World.Gen;
            int w = gen.CellsW, cells = 0;
            for (int y = 3; y < gen.CellsH; y += 11)
                foreach (int x in new[] { -65, -64, -2, -1, 0, 1, 2, 63, 64 })
                {
                    Assert.That(gen.LandAt(x + w, y), Is.EqualTo(gen.LandAt(x, y)), $"cell ({x}, {y}) and a world east");
                    cells++;
                }
            Assert.That(cells, Is.GreaterThan(3000));
            // The known chunks either side of the seam share their samples there, as any two neighbours do.
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
            // The chunks either side of the seam, made: the step from the east column to the west is no bigger than the
            // land makes inside those chunks, and beside the valley (pinned at the west edge) no bigger than its own meet.
            // Under the sea it isn't held: where a coast's sea ramp meets an open-sea square the floor drops to the deep at
            // once, at every such border (about 170 land units between squares (6, 6) and (7, 6) too), seam or not.
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
                        // As the nine's valley sides: the step onto the valley less the valley's own next step.
                        valleyStep = Math.Max(valleyStep, step - Math.Abs(wl[j * n + 1] - wl[j * n]));
                        continue;
                    }
                    for (int i = 0; i + 1 < n; i++) inside = Math.Max(inside, Math.Abs(wl[j * n + i + 1] - wl[j * n + i]));
                    if (e[j * n + n - 1] < ChunkGen.SeaLevel && wl[j * n] < ChunkGen.SeaLevel) seaFloor = Math.Max(seaFloor, step);
                    else if (step > across) (across, worst) = (step, cy * n + j);
                }
            TestContext.Out.WriteLine($"SEAM the biggest step across it {across} land units at row {worst}; inside its chunks {inside}; " +
                $"onto the valley {valleyStep} more than the valley's own; on the sea floor {seaFloor}");
            Assert.That(west.Count(c => c.Pinned), Is.EqualTo(WorldPlan.ChunksPerSquare), "the valley's west side is on the seam");
            Assert.That(across, Is.LessThanOrEqualTo(inside), $"a step of {across} across the seam at row {worst}");
            Assert.That(valleyStep, Is.LessThanOrEqualTo(8));
        }

        [Test]
        [NonParallelizable] // it times itself: it needs the machine to itself
        public void TheKnownRingOverEveryChunk()
        {
            var gen = World.Gen;
            int count = gen.ChunksW * gen.ChunksH;
            Assert.That(Known.Length, Is.EqualTo(count));
            // One chunk at a time on one thread, a row of them, for the cost of a chunk.
            var one = Stopwatch.StartNew();
            for (int cx = 0; cx < gen.ChunksW; cx++) gen.Know(cx, 40);
            double perChunk = one.Elapsed.TotalMilliseconds / gen.ChunksW;
            // The ring itself, from one character knowing the whole world: every chunk but its own, less open sea away
            // from the coast.
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
            foreach (var k in ring.Chunks) Assert.That(k.Heights, Is.EqualTo(Known[k.Y * gen.ChunksW + k.X].Heights), $"chunk ({k.X}, {k.Y})");
            Assert.That(perChunk, Is.LessThan(5), "a known chunk costs well under a made one");
        }

        [Test]
        public void AKnownRingAtTheSeamSeesBothSides()
        {
            var gen = World.Gen;
            int w = gen.ChunksW, row = 30;
            var ring = World.KnownRing(Streaming);
            ring.Want(new[] { (0, row) });
            ring.Fill();
            var xs = ring.Chunks.Select(k => k.X).Distinct().ToList();
            Assert.That(xs.Any(x => x >= w - Streaming.Know), Is.True, "chunks west of the seam");
            Assert.That(xs.Any(x => x > Streaming.Sight && x <= Streaming.Know), Is.True, "and east of it");
            Assert.That(xs.All(x => x <= Streaming.Know || x >= w - Streaming.Know), Is.True, "nothing the long way round");
            foreach (var k in ring.Chunks)
            {
                int d = gen.ChunkDistance(0, row, k.X, k.Y);
                Assert.That(d, Is.GreaterThan(Streaming.Sight).And.LessThanOrEqualTo(Streaming.Know), $"chunk ({k.X}, {k.Y})");
            }
            // Every chunk past sight within know, the short way round, is shown unless it's open sea away from the coast.
            int wanted = 0;
            for (int cy = row - Streaming.Know; cy <= row + Streaming.Know; cy++)
                for (int cx = -Streaming.Know; cx <= Streaming.Know; cx++)
                {
                    if (Math.Max(Math.Abs(cx), Math.Abs(cy - row)) <= Streaming.Sight) continue;
                    wanted++;
                    bool shown = ring.TryGet(cx, cy, out var k);
                    Assert.That(shown || Known[cy * w + gen.WrapChunk(cx)].IsSea, Is.True, $"chunk ({cx}, {cy})");
                    if (shown) Assert.That(k.X, Is.EqualTo(gen.WrapChunk(cx)), "a chunk west of the seam is the map's east column");
                }
            Assert.That(wanted, Is.EqualTo(15 * 15 - 9 * 9));
            // A character a world east, or one column west of the seam named either way, knows the same chunks.
            var same = World.KnownRing(Streaming);
            same.Want(new[] { (w, row) });
            same.Fill();
            Assert.That(same.Chunks.Select(k => (k.X, k.Y)), Is.EqualTo(ring.Chunks.Select(k => (k.X, k.Y))));
            var a = World.KnownRing(Streaming);
            a.Want(new[] { (-1, row) });
            a.Fill();
            var b = World.KnownRing(Streaming);
            b.Want(new[] { (w - 1, row) });
            b.Fill();
            Assert.That(a.Chunks.Select(k => (k.X, k.Y)), Is.EqualTo(b.Chunks.Select(k => (k.X, k.Y))));
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
                // From just across the seam from it, it shows exactly when it would from as far off this side.
                int x = l.X < w / 2 ? w - 40 : 40;
                int dx = Math.Abs(l.DxTo(x));
                Assert.That(dx, Is.LessThanOrEqualTo(w / 2));
                Assert.That(l.SeenAt(x, l.Y), Is.EqualTo(dx <= l.SeenFrom * ChunkGen.Size), $"{l.Type} at ({l.X}, {l.Y}) from ({x}, {l.Y})");
                Assert.That(l.SeenAt(l.X + w, l.Y) && l.SeenAt(l.X - w, l.Y), Is.True, "a world round is where it stands");
            }
            // Something stands near the seam and is seen from the other side of it.
            Assert.That(far.Any(l => l.X >= w / 2 ? l.SeenAt(0, l.Y) : l.SeenAt(w - 1, l.Y)), Is.True, "a landmark seen across the seam");
        }

        [Test]
        public void ItsExpectsAreTheKnownLand()
        {
            // map.json's expects for a world that's never baked: the known samples' share above the sea, highest and lowest.
            var heights = Known.SelectMany(k => k.Heights).ToArray();
            var map = MapFile.Load(Dir);
            int share = (int)Math.Round(100.0 * heights.Count(h => h >= map.SeaLevel) / heights.Length);
            TestContext.Out.WriteLine($"EXPECTS landShare {share}, highest {heights.Max()}, lowest {heights.Min()}");
            Assert.That((map.LandShare, map.Highest, map.Lowest), Is.EqualTo((share, heights.Max(), heights.Min())));
            Picture();
        }

        // The plan, and the heights of the whole world from its known samples (a pixel per sample, 8 cells), with the
        // first two squares drawn again past the east edge so the seam shows in the middle of land, ticked above and below.
        static void Picture()
        {
            string folder = Environment.GetEnvironmentVariable("WORLD_12_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            var plan = World.Plan;
            var cv = WorldPlanPicture.Draw(plan, 48);
            File.WriteAllBytes(Path.Combine(folder, "plan.png"), LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
            var gen = World.Gen;
            int per = KnownChunk.Side - 1, w = gen.ChunksW * per, h = gen.ChunksH * per, extra = 2 * WorldPlan.ChunksPerSquare * per, margin = 6;
            int pw = w + extra, ph = h + 2 * margin;
            var rgb = new byte[pw * ph * 3];
            for (int k = 0; k < rgb.Length; k++) rgb[k] = 255;
            KnownChunk ChunkAt(int x, int y) => Known[(y / per) * gen.ChunksW + (x / per) % gen.ChunksW];
            int Sample(int x, int y) => (y % per) * KnownChunk.Side + x % per;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < pw; x++)
                {
                    var c = ChunkAt(x, y);
                    int v = c.Heights[Sample(x, y)];
                    int wall = plan.Wall[plan.Grid.Index(x / per / WorldPlan.ChunksPerSquare, y / per / WorldPlan.ChunksPerSquare)];
                    int px = x == 0 ? w - 1 : x - 1, py = Math.Max(0, y - 1);
                    int shade = v - ChunkAt(px, py).Heights[Sample(px, py)];
                    byte water = c.Water[Sample(x, y)];
                    var col = WorldPlanPicture.Height(v);
                    if (water == KnownChunk.River || water == KnownChunk.Lake) col = (90, 160, 230);
                    else if (water == KnownChunk.Dry && wall == 1) col = (215, 232, 245);
                    else if (water == KnownChunk.Dry && wall == 2) col = (222, 190, 120);
                    WorldPlanPicture.Put(rgb, (y + margin) * pw + x, col, Math.Max(-50, Math.Min(50, shade / 2)));
                }
            // The seam's ticks, red, in the margins.
            for (int y = 0; y < margin; y++)
                foreach (int yy in new[] { y, ph - 1 - y })
                    for (int x = w - 1; x <= w; x++)
                    {
                        int at = (yy * pw + x) * 3;
                        (rgb[at], rgb[at + 1], rgb[at + 2]) = ((byte)200, (byte)30, (byte)30);
                    }
            File.WriteAllBytes(Path.Combine(folder, "heights.png"), LandmarkPicture.Png(rgb, pw, ph));
            TestContext.Out.WriteLine($"PICTURE {folder}: plan.png {cv.W} x {cv.H}, heights.png {pw} x {ph} (the seam at x = {w})");
        }
    }
}
