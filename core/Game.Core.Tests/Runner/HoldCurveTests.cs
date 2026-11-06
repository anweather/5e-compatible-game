// The curve's runner (core/Hold.Runner) plays: a short stretch of a moment, and the same again from the same seed.
using Chem.HoldCurve;
using NUnit.Framework;

namespace Chem.Tests
{
    public class HoldCurveTests
    {
        static HoldRun Short()
        {
            var run = new HoldRun(ChemistryTests.R, ChemistryTests.WorldDir(), 3, 1, "water", "fire", "first-wave");
            run.Play(14);
            return run;
        }

        [Test]
        public void TheRunnerPlaysAMomentAndASeedPlaysAlike()
        {
            var one = Short();
            var other = Short();
            Assert.That(one.World.Hold.Spawned, Is.GreaterThan(0));
            Assert.That(one.World.CastHash(), Is.EqualTo(other.World.CastHash()));
        }
    }
}
