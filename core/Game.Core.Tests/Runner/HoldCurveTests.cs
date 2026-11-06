// The curve's runner (core/Hold.Runner) plays: a short stretch of a moment.
using Chem.HoldCurve;
using NUnit.Framework;

namespace Chem.Tests
{
    public class HoldCurveTests
    {
        static HoldRun Short()
        {
            var run = new HoldRun(ChemistryTests.R, ChemistryTests.WorldDir(), 3, 1, ("water", "fire"), "first-wave");
            run.Play(13);
            return run;
        }

        [Test]
        public void TheRunnerPlaysAMoment()
        {
            var run = Short();
            Assert.That(run.World.Hold.Spawned, Is.GreaterThan(0));
        }
    }
}
