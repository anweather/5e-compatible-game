using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// A tick in slices (World.StepSome): the view may spread one tick over several frames, but the
    /// tick must be exactly the tick Step computes, whatever the slicing, or peers that slice
    /// differently would part. Commands that arrive while a tick is part done land after it.
    /// </summary>
    public class SlicedTickTests
    {
        static Rules R => ChemistryTests.R;

        // Runs ticks with a stop after every n slices (n = 0: never stop), recording each tick's hashes.
        static List<string> Run(World w, int ticks, int everySlices)
        {
            var hashes = new List<string>();
            int slices = 0;
            for (int t = 0; t < ticks; t++)
            {
                if (everySlices == 0) w.Step();
                else
                {
                    while (!w.StepSome(() => ++slices % everySlices == 0)) Assert.That(w.TickInProgress, Is.True);
                }
                Assert.That(w.TickInProgress, Is.False);
                hashes.Add(w.CellHash() + "/" + w.SkyHash());
            }
            return hashes;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(7)]
        [TestCase(1000)]
        public void ASlicedTickIsTheSameTickAsAWholeOne(int everySlices)
        {
            var whole = Run(ChunkTests.Valley(), 60, 0);
            var sliced = Run(ChunkTests.Valley(), 60, everySlices);
            Assert.That(sliced, Is.EqualTo(whole));
        }

        [Test]
        public void ATickReallyIsSpreadOverSeveralCalls()
        {
            var w = ChunkTests.Valley();
            int calls = 1;
            while (!w.StepSome(() => true)) calls++;
            Assert.That(calls, Is.GreaterThan(10), "a valley tick takes many slices");
            Assert.That(w.Tick, Is.EqualTo(1));
        }

        [Test]
        public void ACommandMidTickLandsAfterTheTick()
        {
            var reference = ChunkTests.Valley();
            reference.Step();
            reference.Cast("fire-bolt", 20, 20, 0);
            reference.SetActive(2, 2, false);
            reference.Step();

            var w = ChunkTests.Valley();
            Assert.That(w.StepSome(() => true), Is.False, "part done");
            w.Cast("fire-bolt", 20, 20, 0);
            Assert.That(w.TickInProgress, Is.False, "the cast finished the tick first");
            w.SetActive(2, 2, false);
            w.Step();
            Assert.That(w.CellHash(), Is.EqualTo(reference.CellHash()));
        }

        [Test]
        public void StepFinishesATickInProgressFirst()
        {
            var reference = ChunkTests.Valley();
            reference.Step();
            reference.Step();

            var w = ChunkTests.Valley();
            w.StepSome(() => true);
            w.Step();
            Assert.That(w.Tick, Is.EqualTo(2));
            Assert.That(w.CellHash(), Is.EqualTo(reference.CellHash()));
        }

        [Test]
        public void TickBatchesLandBetweenWholeTicks()
        {
            // A batch arriving while tick 1 is part done: tick 1 finishes, then the batch's cast and tick 2.
            var batch = new TickBatch(2, new[] { new CastCommand("fire-bolt", 30, 30, 1) });
            var reference = ChunkTests.Valley();
            reference.Step();
            batch.RunOn(reference, RuleSet.Plain(R), out _);

            var w = ChunkTests.Valley();
            Assert.That(w.StepSome(() => true), Is.False);
            batch.RunOn(w, RuleSet.Plain(R), out _);
            Assert.That(w.Tick, Is.EqualTo(2));
            Assert.That(w.CellHash(), Is.EqualTo(reference.CellHash()));
        }
    }
}
