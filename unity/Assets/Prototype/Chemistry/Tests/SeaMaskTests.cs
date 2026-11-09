// The sea from a map's land (SeaMask, docs/design/maps-as-data.md phase 7): low land joined to the edge is sea, a hollow
// walled off is a basin, and the valley's are what its drawn sea and lake have always covered.
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
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
    }
}
