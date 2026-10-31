// A wizard at 0 health (#14): how long it stays down, from sim.json.
using NUnit.Framework;

namespace Chem.Tests
{
    public class DownedTests
    {
        static DownedParams P => Rules.Load(ChemistryTests.RulesDir()).Sim.Downed;

        [Test]
        public void TheTimeDownComesFromSimJson()
        {
            Assert.That(P.Seconds, Is.EqualTo(3));
            Assert.That(DownedParams.Parse(null).Seconds, Is.EqualTo(3));
            Assert.That(DownedParams.Parse(Newtonsoft.Json.Linq.JObject.Parse("{\"seconds\":5}")).Seconds, Is.EqualTo(5));
        }

        [Test]
        public void NothingHappensUntilItFalls()
        {
            var d = new Downed(P);
            Assert.That(d.IsDown, Is.False);
            Assert.That(d.Step(100), Is.False);
            Assert.That(d.Clear(), Is.False);
        }

        [Test]
        public void ItsDownForTheSecondsThenDue()
        {
            var d = new Downed(P);
            Assert.That(d.Fall());
            Assert.That(d.IsDown);
            Assert.That(d.Step(2.9), Is.False);
            Assert.That(d.Step(0.2), Is.True);
            Assert.That(d.IsDown, "until it's cleared");
            Assert.That(d.Clear());
            Assert.That(d.IsDown, Is.False);
        }

        [Test]
        public void FallingAgainWhileDownDoesntStartTheClockAgain()
        {
            var d = new Downed(P);
            d.Fall();
            d.Step(2.9);
            Assert.That(d.Fall(), Is.False);
            Assert.That(d.Step(0.2), Is.True);
        }

        [Test]
        public void ItCanFallAgainOnceItsUp()
        {
            var d = new Downed(P);
            d.Fall();
            d.Step(3.1);
            d.Clear();
            Assert.That(d.Fall());
            Assert.That(d.Step(1), Is.False);
        }
    }
}
