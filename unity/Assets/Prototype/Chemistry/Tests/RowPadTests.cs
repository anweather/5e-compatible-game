using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class RowPadTests
    {
        const int Margin = 2, Bank = 4;

        static int Slope(int x, int y) => x * 10 + y;

        static Dictionary<(int x, int y), int> Moved()
        {
            var cells = new List<(int x, int y)>();
            for (int y = 20; y < 30; y++)
                for (int x = 20; x < 30; x++) cells.Add((x, y));
            return RowPad.Of(cells, Margin, Bank, Slope, (x, y) => x >= 0 && y >= 0).ToDictionary(c => (c.x, c.y), c => c.by);
        }

        static int After(Dictionary<(int x, int y), int> moved, int x, int y) => Slope(x, y) + moved.GetValueOrDefault((x, y));

        [Test]
        public void TheRowAndItsMarginAreOneHeight()
        {
            var moved = Moved();
            int level = After(moved, 20, 20);
            for (int y = 20 - Margin; y < 30 + Margin; y++)
                for (int x = 20 - Margin; x < 30 + Margin; x++) Assert.That(After(moved, x, y), Is.EqualTo(level), $"{x},{y}");
        }

        [Test]
        public void ItIsTheMiddleOfTheLandUnderIt()
        {
            var heights = Enumerable.Range(20, 10).SelectMany(y => Enumerable.Range(20, 10).Select(x => Slope(x, y))).OrderBy(h => h).ToList();
            Assert.That(After(Moved(), 25, 25), Is.EqualTo(heights[50]));
        }

        [Test]
        public void TheLandPastTheBankStaysAsItIs()
        {
            var moved = Moved();
            Assert.That(moved.Keys.Where(c => c.x < 20 - Margin - Bank || c.x >= 30 + Margin + Bank), Is.Empty);
        }

        [Test]
        public void TheBankLeansBackWithoutAStep()
        {
            var moved = Moved();
            for (int x = 30; x < 30 + Margin + Bank + 1; x++)
                Assert.That(System.Math.Abs(After(moved, x + 1, 25) - After(moved, x, 25)), Is.LessThan(60), $"at {x}");
        }
    }
}
