// SeaMaskTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in SeaMaskTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class SeaMaskWorldTests
    {

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
