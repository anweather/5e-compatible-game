using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Catching up (World.CatchUp, docs/WORLD_STREAMING.md "Part 2, second slice"): a chunk that
    /// deactivated while live runs the ticks it missed when it next activates, up to sim.json
    /// streaming.catchUpTicks, so a fire left behind has burned on when a wizard comes back. With
    /// quiet neighbours it ends exactly as if it had never stopped.
    /// </summary>
    public class CatchUpTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        static World Dirt(Rules rules = null) => new World(rules ?? R, 9 * S, 9 * S, 3, "dirt");

        /// <summary>A 10×10 patch of grass round the middle of chunk (4, 4), set alight, burning a few ticks.</summary>
        static World Burning(Rules rules = null)
        {
            var w = Dirt(rules);
            int x = 4 * S + S / 2, y = 4 * S + S / 2;
            for (int j = y - 5; j < y + 5; j++)
                for (int i = x - 5; i < x + 5; i++) w.Paint(i, j, "grass");
            w.Cast("fire-bolt", x, y, 0);
            for (int t = 0; t < 3; t++) w.Step();
            return w;
        }

        [Test]
        public void TheCatchUpTicksComeFromTheRules()
        {
            Assert.That(R.Sim.Streaming.CatchUpTicks, Is.EqualTo(720), "sim.json streaming.catchUpTicks, 2 minutes");
        }

        [Test]
        public void AChunkCaughtUpMatchesOneThatNeverStopped()
        {
            var ran = Burning();
            var stopped = Burning();
            stopped.SetActive(4, 4, false);
            for (int t = 0; t < 100; t++)
            {
                ran.Step();
                stopped.Step();
            }
            Assert.That(stopped.ChunkHash(4, 4), Is.Not.EqualTo(ran.ChunkHash(4, 4)), "inactive, it waited");
            stopped.SetActive(4, 4, true);
            Assert.That(stopped.CaughtUp(4, 4), Is.EqualTo(100));
            Assert.That(stopped.ChunkHash(4, 4), Is.EqualTo(ran.ChunkHash(4, 4)), "caught up, it is where it would have been");
            Assert.That(stopped.Tick, Is.EqualTo(ran.Tick), "the world's clock didn't move");
        }

        [Test]
        public void TheCatchUpIsCapped()
        {
            var w = Burning(R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"catchUpTicks\": 5 } } }") }));
            w.SetActive(4, 4, false);
            for (int t = 0; t < 100; t++) w.Step();
            w.SetActive(4, 4, true);
            Assert.That(w.CaughtUp(4, 4), Is.EqualTo(5));
        }

        [Test]
        public void AChunkThatSettledBeforeItStoppedDoesntCatchUp()
        {
            var w = Dirt();
            for (int t = 0; t < 20; t++) w.Step();
            w.SetActive(4, 4, false);
            for (int t = 0; t < 50; t++) w.Step();
            w.SetActive(4, 4, true);
            Assert.That(w.CaughtUp(4, 4), Is.Zero);
        }

        [Test]
        public void ACutOffFireHasBurnedOnWhenTheWizardComesBack()
        {
            var w = Burning(R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"lingerTicks\": 6 } } }") }));
            var p = new ChunkPlanner();
            void Run((int x, int y) at, int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    ChunkStreaming.Apply(w, p.Plan(w, new[] { at }));
                    w.Step();
                }
            }
            var home = (4 * S + S / 2, 4 * S + S / 2);
            var away = (S / 2, 4 * S + S / 2);
            Run(away, 20);
            Assert.That(w.IsActive(4, 4), Is.False, "cut off while burning");
            string left = w.ChunkHash(4, 4);
            Run(away, 60);
            Assert.That(w.ChunkHash(4, 4), Is.EqualTo(left), "waiting while no one is near");
            Run(home, 1);
            Assert.That(w.IsActive(4, 4), Is.True);
            Assert.That(w.CaughtUp(4, 4), Is.GreaterThan(50), "the ticks missed since it was cut off");
        }

        [Test]
        public void CatchUpsInTickBatchesReplayTheSame()
        {
            // Live: the host stops chunk (4, 4) while it burns and starts it again later.
            var live = Burning();
            var rules = RuleSet.Plain(R);
            var switches = new List<(int, ChunkSwitch)> { (10, new ChunkSwitch(4, 4, false)), (80, new ChunkSwitch(4, 4, true)) };
            var timeline = TickBatch.Timeline(new List<(int, CastCommand)>(), null, 120, null, switches);
            int caught = 0;
            for (int t = 1; t <= 120; t++)
            {
                rules = timeline[t].RunOn(live, rules, out _);
                if (t == 80) caught = live.CaughtUp(4, 4);
            }

            // Replay: a late joiner from the same history.
            var replay = Burning();
            var replayRules = RuleSet.Plain(R);
            for (int t = 1; t <= 120; t++) replayRules = timeline[t].RunOn(replay, replayRules, out _);

            Assert.That(caught, Is.GreaterThan(0), "it caught up");
            Assert.That(replay.CellHash(), Is.EqualTo(live.CellHash()));
        }
    }
}
