// WorldTwelveTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in WorldTwelveTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class WorldTwelveWorldTests
    {

        static StreamingParams Streaming => ChemistryTests.R.Sim.Streaming;

        [Test]
        public void TheWorldFileSaysItsShape()
        {
            var m = WorldTwelveTests.World;
            Assert.That((m.SquaresW, m.SquaresH, m.Wrap, m.Bakes), Is.EqualTo((12, 10, PlanWrap.EastWest, false)));
            Assert.That((m.Plan.Grid.W, m.Plan.Grid.H), Is.EqualTo((12, 10)), "8 rows of land and the two wall rows");
            Assert.That((m.Plan.Inputs.Width, m.Plan.Inputs.Height), Is.EqualTo((12, 8)));
            Assert.That((m.Gen.ChunksW, m.Gen.ChunksH, m.Gen.WrapX), Is.EqualTo((108, 90, true)));
            Assert.That((m.Plan.Grid.X(m.Plan.Valley), m.Plan.Grid.Y(m.Plan.Valley)), Is.EqualTo(m.PinnedAt));
            var map = MapFile.Load(WorldTwelveTests.Dir);
            Assert.That((map.W, map.H), Is.EqualTo((m.Gen.CellsW, m.Gen.CellsH)));
            Assert.That(map.Spawn, Is.EqualTo((m.Gen.PinnedX + 292, m.Gen.PinnedY + 328)), "the valley's spawn, moved");
            Assert.That(m.Plan.Problems, Is.Empty);
            // It's never baked whole: too big.
            Assert.That(GeneratedMap.IsBaked(WorldTwelveTests.Dir), Is.False);
            Assert.That(() => MapBake.EnsureBaked(WorldTwelveTests.Dir), Throws.InvalidOperationException.With.Message.Contains("too big to bake"));
            Assert.That(() => m.Bake(Path.Combine(Path.GetTempPath(), "never")), Throws.InvalidOperationException);
        }

        // The plan, and the heights of the whole world from its known samples (a pixel per sample, 8 cells), with the
        // first two squares drawn again past the east edge so the seam shows in the middle of land, ticked above and below.
        static void Picture()
        {
            string folder = Environment.GetEnvironmentVariable("WORLD_12_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            var plan = WorldTwelveTests.World.Plan;
            var cv = WorldPlanPicture.Draw(plan, 48);
            File.WriteAllBytes(Path.Combine(folder, "plan.png"), LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
            var gen = WorldTwelveTests.World.Gen;
            int per = KnownChunk.Side - 1, w = gen.ChunksW * per, h = gen.ChunksH * per, extra = 2 * WorldPlan.ChunksPerSquare * per, margin = 6;
            int pw = w + extra, ph = h + 2 * margin;
            var rgb = new byte[pw * ph * 3];
            for (int k = 0; k < rgb.Length; k++) rgb[k] = 255;
            KnownChunk ChunkAt(int x, int y) => WorldTwelveTests.Known[(y / per) * gen.ChunksW + (x / per) % gen.ChunksW];
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

        [Test]
        public void AKnownRingAtTheSeamSeesBothSides()
        {
            var gen = WorldTwelveTests.World.Gen;
            int w = gen.ChunksW, row = 30;
            var ring = WorldTwelveTests.World.KnownRing(Streaming);
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
                    Assert.That(shown || WorldTwelveTests.Known[cy * w + gen.WrapChunk(cx)].IsSea, Is.True, $"chunk ({cx}, {cy})");
                    if (shown) Assert.That(k.X, Is.EqualTo(gen.WrapChunk(cx)), "a chunk west of the seam is the map's east column");
                }
            Assert.That(wanted, Is.EqualTo(15 * 15 - 9 * 9));
            // A character a world east, or one column west of the seam named either way, knows the same chunks.
            var same = WorldTwelveTests.World.KnownRing(Streaming);
            same.Want(new[] { (w, row) });
            same.Fill();
            Assert.That(same.Chunks.Select(k => (k.X, k.Y)), Is.EqualTo(ring.Chunks.Select(k => (k.X, k.Y))));
            var a = WorldTwelveTests.World.KnownRing(Streaming);
            a.Want(new[] { (-1, row) });
            a.Fill();
            var b = WorldTwelveTests.World.KnownRing(Streaming);
            b.Want(new[] { (w - 1, row) });
            b.Fill();
            Assert.That(a.Chunks.Select(k => (k.X, k.Y)), Is.EqualTo(b.Chunks.Select(k => (k.X, k.Y))));
        }

        [Test]
        public void ItsExpectsAreTheKnownLand()
        {
            // map.json's expects for a world that's never baked: the known samples' share above the sea, highest and lowest.
            var heights = WorldTwelveTests.Known.SelectMany(k => k.Heights).ToArray();
            var map = MapFile.Load(WorldTwelveTests.Dir);
            int share = (int)Math.Round(100.0 * heights.Count(h => h >= map.SeaLevel) / heights.Length);
            TestContext.Out.WriteLine($"EXPECTS landShare {share}, highest {heights.Max()}, lowest {heights.Min()}");
            Assert.That((map.LandShare, map.Highest, map.Lowest), Is.EqualTo((share, heights.Max(), heights.Min())));
            Picture();
        }
    }
}
