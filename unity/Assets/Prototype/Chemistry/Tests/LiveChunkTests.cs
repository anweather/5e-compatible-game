using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Live cells and settled chunks (docs/WORLD_STREAMING.md, "Part 2, second slice"): a chunk
    /// remembers the last tick anything in it was live (fire, gas, charge, a timer counting down,
    /// water moving), and is settled once nothing has been for sim.json's streaming.settleTicks.
    /// What lingers after a character walks away is decided from this.
    /// </summary>
    public class LiveChunkTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        static World Meadow() => new World(R, 3 * S, 3 * S, 3, "grass");

        static (int x, int y) Middle => (S + S / 2, S + S / 2);

        // Steps until the middle chunk settles, at most `most` ticks; the ticks it took.
        static int StepUntilSettled(World w, int most)
        {
            for (int t = 1; t <= most; t++)
            {
                w.Step();
                if (w.IsSettled(1, 1)) return t;
            }
            Assert.Fail($"still live after {most} ticks");
            return most;
        }

        [Test]
        public void TheSettleTicksComeFromTheRulesAndAreChecked()
        {
            Assert.That(R.Sim.Streaming.SettleTicks, Is.EqualTo(12), "sim.json streaming.settleTicks");
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"settleTicks\": 0 } } }") })).Errors;
            Assert.That(errors, Does.Contain("sim streaming.settleTicks must be at least 1"));
        }

        [Test]
        public void AMeadowWhereNothingHappensIsSettled()
        {
            var w = Meadow();
            for (int i = 0; i < 20; i++) w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(World.NeverLive));
            Assert.That(w.IsSettled(1, 1), Is.True);
        }

        [Test]
        public void AFireIsLiveUntilItBurnsOutThenTheChunkSettles()
        {
            var w = Meadow();
            w.Cast("fire-bolt", Middle.x, Middle.y, 0);
            w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(w.Tick), "burning this tick");
            Assert.That(w.IsSettled(1, 1), Is.False);
            StepUntilSettled(w, 5000);
            for (int y = S; y < 2 * S; y++)
                for (int x = S; x < 2 * S; x++) Assert.That(w.At(x, y).Fire, Is.Zero, $"no fire left at {x},{y}");
            Assert.That(w.Tick - w.LastLive(1, 1), Is.EqualTo(R.Sim.Streaming.SettleTicks), "settled the first tick it could be");
        }

        [Test]
        public void ChargeIsLiveUntilItDecays()
        {
            var w = Meadow();
            w.At(Middle.x, Middle.y).Charge = 3;
            w.Step();
            Assert.That(w.IsSettled(1, 1), Is.False);
            Assert.That(StepUntilSettled(w, 200), Is.LessThan(40), "a little charge decays in a few ticks, then the chunk settles");
        }

        [Test]
        public void GasIsLive()
        {
            var w = Meadow();
            w.Cast("fog-cloud", Middle.x, Middle.y, 0);
            w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(w.Tick));
        }

        [Test]
        public void ATimerCountingDownIsLive()
        {
            var w = Meadow();
            w.At(Middle.x, Middle.y).Life = 5;
            w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(w.Tick));
        }

        [Test]
        public void MovingWaterIsLiveAndSettlesOnceItStops()
        {
            var w = Meadow();
            w.Cast("create-water", Middle.x, Middle.y, 0);
            w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(w.Tick), "the water spreads");
            StepUntilSettled(w, 5000);
            int water = 0;
            for (int y = S; y < 2 * S; y++)
                for (int x = S; x < 2 * S; x++) water += w.At(x, y).Water;
            Assert.That(water, Is.GreaterThan(0), "standing water left behind doesn't keep the chunk live");
        }

        [Test]
        public void WaterMovingInTheRainIsNotLive()
        {
            var w = Meadow();
            w.WeatherSet = (WeatherKind.Rain, 0, 10000);
            for (int i = 0; i < 1000 && w.Weather.Rain == 0; i++) w.Step();
            Assert.That(w.Weather.Rain, Is.GreaterThan(0), "raining");
            int before = w.LastLive(1, 1);
            w.AddWater(Middle.x, Middle.y, 12);
            for (int i = 0; i < 5; i++) w.Step();
            Assert.That(w.At(Middle.x + 1, Middle.y).Water, Is.GreaterThan(0), "the water spread");
            Assert.That(w.LastLive(1, 1), Is.EqualTo(before), "rain touches every wet chunk; it alone mustn't keep them running");
        }

        [Test]
        public void LiveCellsOnAnEdgeAreSeenFromThatSide()
        {
            var w = Meadow();
            w.At(2 * S - 1, Middle.y).Charge = 3;
            w.Step();
            Assert.That(w.IsLiveAtEdge(1, 1, 1), Is.True, "east");
            Assert.That(w.IsLiveAtEdge(1, 1, 3), Is.False, "west");
            Assert.That(Chunks.EdgesOf(0), Is.EqualTo(1 | 8), "the north-west corner is on two edges");
        }

        [Test]
        public void AnInactiveChunkIsNeverMarkedLive()
        {
            var w = Meadow();
            w.Cast("fire-bolt", Middle.x, Middle.y, 0);
            w.SetActive(1, 1, false);
            for (int i = 0; i < 10; i++) w.Step();
            Assert.That(w.LastLive(1, 1), Is.EqualTo(World.NeverLive), "an inactive chunk doesn't tick, so nothing in it moves");
        }
    }
}
