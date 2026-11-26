// The rolling hills (world/maps/hills-12x10, docs/design/hills.md): a generated map of nothing but grass on gentle hills,
// wrapping both ways. Its land tiles round the torus (the same at x and x + W, y and y + H, and smooth over both seams),
// is the same on every load (a golden), is all grass (and its roads) with no water or things, and the map loads from its rows alone into
// a World of all its chunks that makes only what it lays on, and walks a character over the east seam with the chunks
// ahead made on the worker.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsTests
    {

        internal static string HillsDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");

        string root;
        GeneratedMap map;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsDir);
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void ItIsTheWideWorldsSizeWrappingBothWays()
        {
            var gen = map.Gen;
            Assert.That(map.Hills, Is.Not.Null);
            Assert.That(map.Plan, Is.Null, "no plan is solved");
            Assert.That(map.PinnedMap, Is.Null, "nothing is pinned");
            Assert.That((gen.CellsW, gen.CellsH, gen.ChunksW, gen.ChunksH), Is.EqualTo((6912, 5760, 108, 90)));
            Assert.That((gen.WrapX, gen.WrapY), Is.EqualTo((true, true)));
            var file = MapFile.Load(HillsDir);
            Assert.That((file.WrapX, file.WrapY), Is.EqualTo((true, true)));
            Assert.That(map.Bakes, Is.False);
            Assert.That(gen.ChunkDistance(107, 89, 0, 0), Is.EqualTo(1), "the corner chunks touch across both seams");
            Assert.That(gen.ChunkDistance(0, 0, 54, 45), Is.EqualTo(54));
        }

        [Test]
        public void TheLandTilesRoundTheTorus()
        {
            var h = map.Hills;
            int w = map.Gen.CellsW, ht = map.Gen.CellsH;
            for (int k = 0; k < 400; k++)
            {
                int x = (int)(WorldHash.Hash4(1, 2, k, 0) % (uint)w), y = (int)(WorldHash.Hash4(1, 3, k, 0) % (uint)ht);
                int at = h.At(x, y);
                Assert.That(h.At(x + w, y), Is.EqualTo(at));
                Assert.That(h.At(x - w, y), Is.EqualTo(at));
                Assert.That(h.At(x, y + ht), Is.EqualTo(at));
                Assert.That(h.At(x - 3 * w, y - 2 * ht), Is.EqualTo(at));
            }
            // Over each seam the land steps no more than it does anywhere else on the map: the seam is nowhere to be seen.
            int worst = 0;
            for (int y = 0; y < ht; y += 7)
                for (int x = 0; x < w; x += 7) worst = Math.Max(worst, Math.Max(Math.Abs(h.At(x + 1, y) - h.At(x, y)), Math.Abs(h.At(x, y + 1) - h.At(x, y))));
            int rolling = 0;
            for (int y = 0; y < ht; y += 7)
                for (int x = 0; x < w; x += 7) rolling = Math.Max(rolling, Math.Max(Math.Abs(h.Rolling(x + 1, y) - h.Rolling(x, y)), Math.Abs(h.Rolling(x, y + 1) - h.Rolling(x, y))));
            int seam = 0;
            for (int y = 0; y < ht; y++) seam = Math.Max(seam, Math.Abs(h.At(0, y) - h.At(w - 1, y)));
            for (int x = 0; x < w; x++) seam = Math.Max(seam, Math.Abs(h.At(x, 0) - h.At(x, ht - 1)));
            TestContext.Out.WriteLine($"HILLS the steepest step between neighbours {worst} land units ({rolling} without the massif); over the seams {seam}");
            Assert.That(seam, Is.LessThanOrEqualTo(2), "the massif is in the middle: over the seams the hills roll as anywhere");
            Assert.That(rolling, Is.LessThan(10), "rolling: nowhere as steep as a generated map's bare slopes (ChunkGen's SteepUnits)");
        }

        [Test]
        public void ChunksMeetOverTheSeams()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            var east = gen.Make(107, 89);
            var west = gen.Make(0, 89);
            var south = gen.Make(107, 0);
            for (int j = 0; j < s; j++)
                Assert.That(Math.Abs(east.Land[j * s + s - 1] - west.Land[j * s]), Is.LessThanOrEqualTo(2), $"row {j} over the east seam");
            for (int i = 0; i < s; i++)
                Assert.That(Math.Abs(east.Land[(s - 1) * s + i] - south.Land[i]), Is.LessThanOrEqualTo(2), $"column {i} over the south seam");
            // A known chunk's last samples are the next chunk's first, over the seams too.
            var k = gen.Know(107, 89);
            var kWest = gen.Know(0, 89);
            var kSouth = gen.Know(107, 0);
            for (int j = 0; j < KnownChunk.Side; j++) Assert.That(k.Heights[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(kWest.Heights[j * KnownChunk.Side]));
            for (int i = 0; i < KnownChunk.Side; i++) Assert.That(k.Heights[(KnownChunk.Side - 1) * KnownChunk.Side + i], Is.EqualTo(kSouth.Heights[i]));
        }

        [Test]
        public void ItIsAllGrassWithNoWaterOrThings()
        {
            var gen = map.Gen;
            foreach (var (cx, cy) in new[] { (0, 0), (107, 89), (54, 45), (12, 70), (99, 3) })
            {
                var c = gen.Make(cx, cy);
                Assert.That(c.Cells.All(ch => ch == ',' || ch == '.' || ch == ':' || ch == '^' || ch == 'r' || ch == 'w' || ch == 'T'), Is.True,
                    $"chunk ({cx}, {cy}) all grass, but its roads' dirt (HillsRoadsTests), the massif's scree and rock (HillsMassifTests) the rivers' water (HillsRiversTests) and the woods' trees (HillsForestTests)");
                Assert.That(c.Things, Is.Empty);
                Assert.That(c.Biome, Is.EqualTo("grassland"));
                Assert.That(c.Land.Min(), Is.GreaterThanOrEqualTo(map.Hills.Base));
                Assert.That(c.Land.Max(), Is.LessThanOrEqualTo(map.Hills.Highest));
            }
            // Over every known chunk: all dry land, and the map's expects as the known ring reads them.
            int lowest = int.MaxValue, highest = int.MinValue, wetChunks = 0;
            for (int cy = 0; cy < gen.ChunksH; cy++)
                for (int cx = 0; cx < gen.ChunksW; cx++)
                {
                    var k = gen.Know(cx, cy);
                    // Water only where the rivers and their lakes are (HillsRiversTests checks the samples cell by cell).
                    if (k.Water.Any(x => x != KnownChunk.Dry)) wetChunks++;
                    lowest = Math.Min(lowest, k.Heights.Min());
                    highest = Math.Max(highest, k.Heights.Max());
                }
            Assert.That(wetChunks, Is.InRange(1, 400), "a few chunks hold the rivers and lakes; the rest are dry");
            TestContext.Out.WriteLine($"HILLS {wetChunks} known chunks with water; known land from {lowest} to {highest} land units ({lowest * MapFile.LandUnitMetres:0.0} to {highest * MapFile.LandUnitMetres:0.0} m)");
            var file = MapFile.Load(HillsDir);
            Assert.That((file.LandShare, file.Lowest, file.Highest), Is.EqualTo((100, lowest, highest)), "map.json's expects");
        }

        [Test]
        public void TheSameHillsEveryTime()
        {
            var again = GeneratedMap.Load(HillsDir);
            foreach (var (cx, cy) in new[] { (0, 0), (107, 89), (54, 45) })
                Assert.That(again.Gen.Make(cx, cy).Hash, Is.EqualTo(map.Gen.Make(cx, cy).Hash));
            var other = GeneratedMap.Load(HillsDir, "hills-2");
            Assert.That(other.Gen.Make(54, 45).Hash, Is.Not.EqualTo(map.Gen.Make(54, 45).Hash), "another seed, other hills");
            // Chunk (54, 45) is the middle of the massif's meadow (it was 4ceac909 before the massif).
            Assert.That(map.Gen.Make(54, 45).Hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("8e975151"), "the hills' golden: a change to the land is a new generator version");
        }
    }
}
