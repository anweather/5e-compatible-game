// The sea from a map's land (SeaMask, docs/design/maps-as-data.md phase 7): low land joined to the edge is sea, a hollow
// walled off is a basin, and the valley's are what its drawn sea and lake have always covered.
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class SeaMaskTests
    {
        const int Side = 12, Level = -4;

        static int[] Flat(int height = 8) => Enumerable.Repeat(height, Side * Side).ToArray();

        [Test]
        public void LowLandJoinedToTheEdgeIsSeaAndAHollowIsABasin()
        {
            var land = Flat();
            // A cove cut in from the west edge, two cells wide and five deep, and a hollow in the middle of the land.
            for (int x = 0; x < 5; x++)
                for (int y = 2; y < 4; y++) land[y * Side + x] = -10;
            for (int x = 7; x < 10; x++)
                for (int y = 7; y < 10; y++) land[y * Side + x] = -10;
            var mask = SeaMask.Of(land, Side, Side, Level);
            Assert.That(mask.SeaCells, Is.EqualTo(10), "the cove is sea");
            Assert.That(mask.BasinCells, Is.EqualTo(9), "the hollow is a basin");
            Assert.That(mask.IsSea(4, 3), Is.True);
            Assert.That(mask.IsBasin(8, 8), Is.True);
            Assert.That(mask.IsSea(8, 8), Is.False);
            Assert.That(mask.IsSea(-1, 5), Is.True, "off the map is sea when an edge is");
            Assert.That(SeaMask.Of(Flat(), Side, Side, Level).IsSea(-1, 5), Is.False, "and dry land when none is");
        }

        [Test]
        public void LandAtTheLevelIsDry()
        {
            var land = Flat(Level);
            land[0] = Level - 1;
            var mask = SeaMask.Of(land, Side, Side, Level);
            Assert.That(mask.SeaCells, Is.EqualTo(1));
            Assert.That(mask.BasinCells, Is.EqualTo(0));
        }

        [Test]
        public void NearReachesASquareRoundTheWater()
        {
            var land = Flat();
            land[6 * Side + 6] = -10;
            var mask = SeaMask.Of(land, Side, Side, Level);
            Assert.That(mask.Near(2, basins: false).Count(w => w), Is.EqualTo(0), "the sea's: there is none");
            var near = mask.Near(2, basins: true);
            Assert.That(near.Count(w => w), Is.EqualTo(25));
            Assert.That(near[4 * Side + 4] && near[8 * Side + 8] && !near[3 * Side + 6], Is.True);
        }

        // The valley's sea is the water its coast has always drawn, and the lake and the generated land's lakes are basins.
        [Test]
        public void TheValleysSeaAndBasins()
        {
            string dir = ChemistryTests.WorldDir();
            var map = MapFile.Load(dir);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            var mask = SeaMask.Of(land, map.W, map.H, map.SeaLevel);
            Assert.That(mask.SeaCells, Is.EqualTo(35770));
            Assert.That(mask.BasinCells, Is.EqualTo(6078));
            Assert.That((float)map.WaterMetres, Is.EqualTo(-0.6f), "the lake's and the sea's level, as drawn");
            Assert.That(map.SeaShore, Is.EqualTo(((double, double)?)(225.0, -160.0)));
        }

        // island-startup-2: Near a rectangle or a chunk at a time is Near, cell for cell (the valley's sea and lake).
        [TestCase(3, true)]
        [TestCase(12, false)]
        public void NearByPartsIsNear(int reach, bool basins)
        {
            string dir = ChemistryTests.WorldDir();
            var map = MapFile.Load(dir);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            var mask = SeaMask.Of(land, map.W, map.H, map.SeaLevel);
            var whole = mask.Near(reach, basins);
            var byChunk = mask.NearByChunk(reach, basins);
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                    if (byChunk[x, y] != whole[y * map.W + x]) Assert.Fail($"({x}, {y}): by chunk {byChunk[x, y]}, whole {whole[y * map.W + x]}");
            // Only chunks with a coast in or round them are worked out: open sea and dry land answer at once.
            Assert.That(byChunk.Chunks, Is.LessThan((map.W / Chunks.Size) * (map.H / Chunks.Size)));
            foreach (var (x0, y0, w, h) in new[] { (0, 0, 7, 9), (130, 300, 40, 3), (map.W - 5, map.H - 11, 5, 11), (200, 0, 1, map.H) })
            {
                var part = mask.NearIn(x0, y0, w, h, reach, basins);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Assert.That(part[y * w + x], Is.EqualTo(whole[(y0 + y) * map.W + x0 + x]), $"({x0 + x}, {y0 + y})");
            }
            Assert.That(byChunk[-1, 3], Is.False);
        }

        // core-mem-scale: land.json held a chunk at a time (LandChunks) reads as LandFile.Read does, cell for cell, by At, by
        // band and by row; and its sea mask is the one the whole array makes.
        [Test]
        public void TheValleysLandAChunkAtATimeIsItsLand()
        {
            string dir = ChemistryTests.WorldDir();
            var map = MapFile.Load(dir);
            string json = File.ReadAllText(Path.Combine(dir, MapFile.LandName));
            var whole = LandFile.Read(json, map.W, map.H);
            var land = LandChunks.Read(json, map.W, map.H);
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                    if (land.At(x, y) != whole[y * map.W + x]) Assert.Fail($"({x}, {y}): {land.At(x, y)}, not {whole[y * map.W + x]}");
            var band = new int[Chunks.Size * map.W];
            land.Band(3, band);
            Assert.That(band.Take(Chunks.Size * map.W), Is.EqualTo(whole.Skip(3 * Chunks.Size * map.W).Take(Chunks.Size * map.W)));
            var row = new int[map.W];
            land.Row(301, row);
            Assert.That(row, Is.EqualTo(whole.Skip(301 * map.W).Take(map.W)));
            Assert.That(land.Bytes, Is.LessThan(whole.Length * 4L / 2), "smaller than an int a cell");
            Assert.That(LandChunks.Read(json, map.W + 64, map.H), Is.Null, "another size isn't this land");
            var reader = land.NewReader();
            Assert.That(reader.At(300, 200), Is.EqualTo(whole[200 * map.W + 300]));
            Assert.That(reader.At(5, 571), Is.EqualTo(whole[571 * map.W + 5]));
            var sea = LandChunks.Uniform(map.W, map.H, map.SeaLevel);
            Assert.That((sea.At(0, 0), sea.At(map.W - 1, map.H - 1)), Is.EqualTo((map.SeaLevel, map.SeaLevel)));
            Assert.That(LandCheck.Disagreements((x, y) => 0f, land, 0.125, 1e9), Is.EqualTo(LandCheck.Disagreements((x, y) => 0f, whole, map.W, map.H, 0.125, 1e9)));

            var fromArray = SeaMask.Of(whole, map.W, map.H, map.SeaLevel);
            var fromChunks = SeaMask.Of(land, map.SeaLevel);
            Assert.That((fromChunks.SeaCells, fromChunks.BasinCells), Is.EqualTo((fromArray.SeaCells, fromArray.BasinCells)));
            Assert.That(fromChunks.Near(5, true), Is.EqualTo(fromArray.Near(5, true)));
            // Read with the sea from the same pass (sea-land-speed): the same land and the same mask.
            var both = LandChunks.Read(json, map.W, map.H, map.SeaLevel, out var seaRead);
            Assert.That(both.At(300, 200), Is.EqualTo(whole[200 * map.W + 300]));
            Assert.That((seaRead.SeaCells, seaRead.BasinCells), Is.EqualTo((fromArray.SeaCells, fromArray.BasinCells)));
            Assert.That(seaRead.Near(5, true), Is.EqualTo(fromArray.Near(5, true)));
            Assert.That(seaRead.Near(9, false), Is.EqualTo(fromArray.Near(9, false)));
        }

        // A map that isn't a whole number of chunks, with NoLand and big steps: the same land back, by Of and by Read.
        [Test]
        public void LandNotAWholeNumberOfChunksComesBackTheSame()
        {
            const int w = 100, h = 70;
            var units = new int[w * h];
            var rng = new System.Random(5);
            for (int i = 0; i < units.Length; i++)
            {
                int step = i % 3 == 0 ? rng.Next(-200, 3000) : 0;
                units[i] = i % 97 == 0 ? World.NoLand : step + i / w;
            }
            var of = LandChunks.Of(units, w, h);
            var read = LandChunks.Read(LandFile.Write(units, w, h, 0.125f), w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Assert.That(of.At(x, y), Is.EqualTo(units[y * w + x]), $"Of ({x}, {y})");
                    Assert.That(read.At(x, y), Is.EqualTo(units[y * w + x]), $"Read ({x}, {y})");
                }
        }
    }
}
