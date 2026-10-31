using System;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>A swimmer's breath and drowning (docs/BACKLOG.md 13; Breath.cs, sim.json breath).</summary>
    public class BreathTests
    {
        static BreathParams P => ChemistryTests.R.Sim.Breath;

        // A seeded die of the given sides.
        static Func<int, int> Dice(int seed)
        {
            var r = new Random(seed);
            return sides => r.Next(1, sides + 1);
        }

        // Steps the breath in frames of dt for this many seconds; the damage taken in all.
        static int Run(Breath b, double seconds, bool under, double dt = 0.02)
        {
            int damage = 0;
            for (double t = 0; t < seconds - 1e-9; t += dt) damage += b.Step(dt, under);
            return damage;
        }

        [Test]
        public void TheNumbersComeFromSimJson()
        {
            Assert.That(P.Seconds, Is.EqualTo(30));
            Assert.That(P.RefillSeconds, Is.LessThan(P.Seconds / 5), "it fills again quickly");
            Assert.That(P.DrownDice, Is.EqualTo(1));
            Assert.That(P.DrownSides, Is.EqualTo(6));
        }

        [Test]
        public void ItRunsDownOnlyWithTheHeadUnder()
        {
            var b = new Breath(P, Dice(0));
            Assert.That(Run(b, 10, under: false), Is.Zero);
            Assert.That(b.Left, Is.EqualTo(P.Seconds), "out of the water it stays full");
            Assert.That(b.Full, Is.True);
            Assert.That(Run(b, 10, under: true), Is.Zero);
            Assert.That(b.Left, Is.EqualTo(P.Seconds - 10).Within(0.05));
            Assert.That(!b.Full && b.Under && !b.Drowning, Is.True);
        }

        [Test]
        public void ItFillsAgainQuicklyAtTheSurface()
        {
            var b = new Breath(P, Dice(0));
            Run(b, P.Seconds + 1, under: true);
            Assert.That(b.Left, Is.Zero);
            Run(b, P.RefillSeconds / 2, under: false);
            Assert.That(b.Share, Is.EqualTo(0.5).Within(0.02), "half the refill time, half full");
            Run(b, P.RefillSeconds, under: false);
            Assert.That(b.Left, Is.EqualTo(P.Seconds), "never past full");
            Assert.That(b.Full, Is.True);
        }

        [Test]
        public void OutOfBreathItDrownsInHitsEveryFewSeconds()
        {
            var b = new Breath(P, Dice(7));
            Assert.That(Run(b, P.Seconds - 0.1, under: true), Is.Zero, "no damage while there's breath");
            int first = Run(b, 0.2, under: true);
            Assert.That(b.Drowning, Is.True);
            Assert.That(first, Is.InRange(1, 6), "the first hit comes as the breath runs out: 1d6");
            Assert.That(Run(b, P.DrownEvery - 0.2, under: true), Is.Zero, "then nothing until the next");
            for (int i = 0; i < 4; i++)
                Assert.That(Run(b, P.DrownEvery, under: true), Is.InRange(1, 6), $"one 1d6 hit every {P.DrownEvery} s (hit {i + 2})");
        }

        [Test]
        public void DrowningStopsAsSoonAsTheHeadIsOut()
        {
            var b = new Breath(P, Dice(1));
            Run(b, P.Seconds + 1, under: true);
            Assert.That(b.Drowning, Is.True);
            Assert.That(b.Step(0.02, false), Is.Zero);
            Assert.That(b.Drowning, Is.False);
            Assert.That(Run(b, P.DrownEvery * 3, under: false), Is.Zero);
        }

        [Test]
        public void OneLongStepCountsEveryHit()
        {
            // A long frame (a hitch) still deals each hit it covers: out at 30 s, then at 33, 36 and 39.
            var b = new Breath(P, Dice(3));
            int damage = b.Step(P.Seconds + 3 * P.DrownEvery + 0.5, true);
            Assert.That(damage, Is.InRange(4, 24));
        }

        [Test]
        public void RefillMakesItFull()
        {
            var b = new Breath(P, Dice(0));
            Run(b, P.Seconds + 5, under: true);
            b.Refill();
            Assert.That(b.Left, Is.EqualTo(P.Seconds));
            Assert.That(b.Drowning || b.Under, Is.False);
        }

        [Test]
        public void ItsDataNotAConstant()
        {
            // A mod or a tweak to sim.json changes it (Rules.Mods deep-merges over sim).
            var p = Parse("{ \"seconds\": 10, \"refillSeconds\": 1, \"drownEvery\": 2, \"drownDice\": 2, \"drownSides\": 4 }");
            Assert.That((p.Seconds, p.RefillSeconds, p.DrownEvery, p.DrownDice, p.DrownSides), Is.EqualTo((10.0, 1.0, 2.0, 2, 4)));
            var b = new Breath(p, Dice(5));
            Assert.That(Run(b, 9.9, under: true), Is.Zero);
            Assert.That(Run(b, 0.2, under: true), Is.InRange(2, 8), "2d4");
            var defaults = Parse("null");
            Assert.That(defaults.Seconds, Is.EqualTo(30));
        }

        static BreathParams Parse(string json) =>
            BreathParams.Parse(Newtonsoft.Json.Linq.JToken.Parse(json));
    }
}
