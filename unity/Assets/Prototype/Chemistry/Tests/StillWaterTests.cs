using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Still water costs nothing (World.StillWater): the water pass skips chunks where nothing has
    /// moved, and the world must come out exactly as if it hadn't. Each test runs the same world
    /// twice, skipping on and off, and compares every tick, through what disturbs still water: a
    /// trench dug beside a pond at rest, a neighbour switched off and on, a rule switch, rain,
    /// and ticks run in slices.
    /// </summary>
    public class StillWaterTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A meadow with a deep pond inside chunk (1, 1) and a shallow lake across the edge of (0, 2) and (1, 2).</summary>
        static World Ponds(bool skip)
        {
            var w = new World(R, 192, 192, 5, "grass") { SkipStillWater = skip };
            for (int y = 80; y < 110; y++)
                for (int x = 80; x < 110; x++) { w.Paint(x, y, "pit"); w.AddWater(x, y, 10); }
            for (int y = 140; y < 160; y++)
                for (int x = 50; x < 80; x++) { w.Paint(x, y, "pit"); w.AddWater(x, y, 4); }
            return w;
        }

        // What happens to both worlds on a tick, before it steps.
        static void Events(World w, int t)
        {
            if (t == 150) w.Cast("earthen-trench", 79, 95, 3);   // dug out from the still pond's west bank
            if (t == 220) w.SetActive(2, 1, false);               // the pond's east neighbour off...
            if (t == 260) w.SetActive(2, 1, true);                // ...and on again
            if (t == 300) w.ApplyRules(R);                        // a rule switch (the same rules)
            if (t == 340) w.WeatherSet = (WeatherKind.Rain, t, t + 200);
            if (t == 420) w.Cast("create-water", 100, 95, 0);     // water poured on the still pond
        }

        static List<string> Run(World w, int ticks, bool sliced, List<int> still = null)
        {
            var hashes = new List<string>();
            for (int t = 1; t <= ticks; t++)
            {
                Events(w, t);
                if (sliced)
                {
                    int n = 0;
                    while (!w.StepSome(() => ++n % 3 == 0)) { n++; }
                }
                else w.Step();
                hashes.Add(w.CellHash());
                still?.Add(w.StillChunks);
            }
            return hashes;
        }

        [Test]
        public void SkippingStillWaterChangesNothing()
        {
            var still = new List<int>();
            var skipped = Run(Ponds(true), 600, false, still);
            var full = Run(Ponds(false), 600, false);
            Assert.That(skipped, Is.EqualTo(full));
            Assert.That(still.GetRange(100, 40), Has.All.GreaterThan(0), "the pond settled, so its chunks were skipped before the trench");
            Assert.That(still[149], Is.LessThan(still[148]), "the trench (dug before tick 150) disturbed the water round it");
        }

        [Test]
        public void SkippingStillWaterChangesNothingInSlicedTicks()
        {
            Assert.That(Run(Ponds(true), 500, true), Is.EqualTo(Run(Ponds(false), 500, false)));
        }

        [Test]
        public void SkippingStillWaterChangesNothingInTheBusyValley()
        {
            var a = ChunkTests.Valley();
            var b = ChunkTests.Valley();
            b.SkipStillWater = false;
            for (int t = 0; t < 300; t++)
            {
                a.Step();
                b.Step();
                Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()), $"tick {t + 1}");
            }
        }

        [Test]
        public void ATickOfStillWaterCountsItsChunks()
        {
            var w = Ponds(true);
            for (int t = 0; t < 200; t++) w.Step();
            Assert.That(w.StillChunks, Is.EqualTo(w.ActiveChunks.Count), "every chunk is still once the water has settled");
        }
    }
}
