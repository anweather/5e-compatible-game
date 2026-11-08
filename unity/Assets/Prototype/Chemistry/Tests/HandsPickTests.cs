// The pad's hands menu's choosing (HandsPick): a stick points at an element by its direction alone, and letting go attunes
// only the hands that changed.
using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    public class HandsPickTests
    {
        [TestCase(0f, 1f, 0)]
        [TestCase(1f, 0f, 1)]
        [TestCase(0f, -1f, 2)]
        [TestCase(-1f, 0f, 3)]
        [TestCase(0.6f, 0.7f, 0)]
        [TestCase(0.7f, 0.6f, 1)]
        [TestCase(-0.7f, 0.6f, 3)]
        public void AStickPointsAtTheElementItLeansTo(float x, float y, int element) =>
            Assert.That(HandsPick.Pointed(x, y, 4), Is.EqualTo(element));

        [Test]
        public void ALightLeanPointsAtNothing()
        {
            Assert.That(HandsPick.Pointed(0f, 0f, 4), Is.EqualTo(-1));
            Assert.That(HandsPick.Pointed(0.2f, 0.3f, 4), Is.EqualTo(-1));
            Assert.That(HandsPick.Pointed(0f, 1f, 0), Is.EqualTo(-1));
        }

        [Test]
        public void EachElementSitsWhereItsStickPoints()
        {
            for (int i = 0; i < 4; i++)
            {
                var (x, y) = HandsPick.Direction(i, 4);
                Assert.That(HandsPick.Pointed(x, y, 4), Is.EqualTo(i));
            }
        }

        [Test]
        public void LettingGoAttunesOnlyTheHandsThatChanged()
        {
            var holds = new[] { "fire", "water" };
            Assert.That(HandsPick.Confirmed(holds, new string[] { null, null }), Is.Empty);
            Assert.That(HandsPick.Confirmed(holds, new[] { "fire", "water" }), Is.Empty);
            Assert.That(HandsPick.Confirmed(holds, new[] { "earth", null }), Is.EqualTo(new List<(int, string)> { (0, "earth") }));
            Assert.That(HandsPick.Confirmed(holds, new[] { "air", "fire" }), Is.EqualTo(new List<(int, string)> { (0, "air"), (1, "fire") }));
        }
    }
}
