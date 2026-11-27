// A graded road: cut into the land, level across, steady along, and kept when the stamps go (RoadGrade).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class RoadGradeTests
    {
        internal const int Size = 120;

        internal static int Land(int x, int y) => (Size - x) * 8;

        internal static BuildingRoad Road()
        {
            var road = new BuildingRoad { Id = "up", Terrain = "dirt", Width = 3 };
            road.Points.Add((60, 20));
            road.Points.Add((60, 200));
            return road;
        }

        static Dictionary<(int x, int y), int> Moved(ISet<(int x, int y)> keepOff = null) =>
            RoadGrade.Of(Road(), 0, 0, Land, keepOff ?? new HashSet<(int x, int y)>()).ToDictionary(c => (c.x, c.y), c => c.by);

        static int Surface(Dictionary<(int x, int y), int> moved, int x, int y) => Land(x, y) + (moved.TryGetValue((x, y), out int by) ? by : 0);

        [Test]
        public void TheRoadIsLevelAcrossAndCutIntoTheHill()
        {
            var moved = Moved();
            for (int y = 40; y < 80; y++)
            {
                Assert.That(Surface(moved, 29, y), Is.EqualTo(Surface(moved, 30, y)), $"across, at {y}");
                Assert.That(Surface(moved, 31, y), Is.EqualTo(Surface(moved, 30, y)), $"across, at {y}");
            }
            Assert.That(moved.Values.Any(by => by < 0), "cut from the uphill side");
            Assert.That(moved.Values.Any(by => by > 0), "banked on the downhill side");
        }

        [Test]
        public void TheLandLeansBackToWhereItWas()
        {
            var moved = Moved();
            Assert.That(moved.Keys.Max(c => c.x) - moved.Keys.Min(c => c.x), Is.LessThan(20), "a bench, not the whole hill");
            for (int x = 22; x < 38; x++)
                Assert.That(System.Math.Abs(Surface(moved, x + 1, 60) - Surface(moved, x, 60)), Is.LessThan(40), $"no cliff at {x}");
        }

        [Test]
        public void TheLandPastItsEndsStaysAsItIs()
        {
            var moved = Moved();
            Assert.That(moved.Keys.Where(c => c.y < 10), Is.Empty, "north of where it starts");
            Assert.That(moved.Keys.Where(c => c.y > 100), Is.Empty, "south of where it ends");
        }

        [Test]
        public void ItKeepsOffWhatTheRowBuilt()
        {
            var keep = new HashSet<(int x, int y)> { (30, 50), (28, 50) };
            var moved = Moved(keep);
            Assert.That(moved.Keys.Any(keep.Contains), Is.False);
        }

        [Test]
        public void TheSameRoadIsTheSameCut() => Assert.That(Moved(), Is.EqualTo(Moved()));
    }
}
