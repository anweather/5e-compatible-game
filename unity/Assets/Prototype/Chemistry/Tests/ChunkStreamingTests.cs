using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Chunks waking and sleeping around the players (ChunkStreaming), and the switches travelling
    /// in tick batches so a replay (a late joiner) pauses the same chunks at the same ticks.
    /// </summary>
    public class ChunkStreamingTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        /// <summary>A meadow of 7×7 chunks, all simulated, as a new world starts.</summary>
        static World Meadow(uint seed = 3) => new World(R, 7 * S, 7 * S, seed, "grass");

        static (int x, int y) Middle(int cx, int cy) => (cx * S + S / 2, cy * S + S / 2);

        static HashSet<(int, int)> Active(World w) => new HashSet<(int, int)>(w.ChunkCoords.Where(c => w.IsActive(c.cx, c.cy)));

        [Test]
        public void APlayerKeepsTheChunksAroundThemAwakeAndTheRestSleep()
        {
            var w = Meadow();
            var plan = ChunkStreaming.Plan(w, new[] { Middle(1, 1) });
            ChunkStreaming.Apply(w, plan);
            // Within 2 chunks of (1, 1) stay awake: columns and rows 0..3.
            var expected = new HashSet<(int, int)>();
            for (int cy = 0; cy <= 3; cy++)
                for (int cx = 0; cx <= 3; cx++) expected.Add((cx, cy));
            Assert.That(Active(w), Is.EquivalentTo(expected));
            Assert.That(plan.All(s => !s.Active), "everything was awake already, so the plan only puts chunks to sleep");
        }

        [Test]
        public void ChunksWakeAheadOfAPlayerAndSleepOnlyWellBehindThem()
        {
            var w = Meadow();
            ChunkStreaming.Apply(w, ChunkStreaming.Plan(w, new[] { Middle(1, 1) }));
            // One chunk east: all of the ring around (2, 1) is awake already, and nothing is more than 2 behind.
            Assert.That(ChunkStreaming.Plan(w, new[] { Middle(2, 1) }), Is.Empty);
            // One more: column 4 wakes ahead, column 0 (now 3 behind) sleeps.
            var east = ChunkStreaming.Plan(w, new[] { Middle(3, 1) });
            Assert.That(east.Where(s => s.Active).Select(s => (s.CX, s.CY)), Is.EqualTo(new[] { (4, 0), (4, 1), (4, 2) }));
            Assert.That(east.Where(s => !s.Active).Select(s => (s.CX, s.CY)), Is.EqualTo(new[] { (0, 0), (0, 1), (0, 2), (0, 3) }));
            ChunkStreaming.Apply(w, east);
            // Three more east: the old corner falls more than 2 behind and sleeps.
            var far = ChunkStreaming.Plan(w, new[] { Middle(5, 1) });
            ChunkStreaming.Apply(w, far);
            Assert.That(w.IsActive(0, 0), Is.False);
            Assert.That(w.IsActive(6, 2), Is.True, "the ring around the player is awake");
        }

        [Test]
        public void ThePlanIsTheSameWhateverOrderThePlayersComeIn()
        {
            var a = Meadow();
            var b = Meadow();
            var players = new[] { Middle(1, 1), Middle(5, 5), Middle(5, 1) };
            var planA = ChunkStreaming.Plan(a, players);
            var planB = ChunkStreaming.Plan(b, players.Reverse().ToArray());
            Assert.That(planA, Is.EqualTo(planB));
            Assert.That(ChunkStreaming.Plan(a, System.Array.Empty<(int, int)>()), Is.Empty, "no players: nothing changes");
        }

        [Test]
        public void ASleepingChunkKeepsItsFireUntilItWakes()
        {
            var w = Meadow();
            var (x, y) = Middle(6, 6);
            w.Cast("fire-bolt", x, y, 0);
            ChunkStreaming.Apply(w, new[] { new ChunkSwitch(6, 6, false) });
            string before = w.ChunkHash(6, 6);
            for (int i = 0; i < 30; i++) w.Step();
            Assert.That(w.ChunkHash(6, 6), Is.EqualTo(before), "paused: the fire neither spreads nor burns out");
            ChunkStreaming.Apply(w, new[] { new ChunkSwitch(6, 6, true) });
            for (int i = 0; i < 30; i++) w.Step();
            Assert.That(w.ChunkHash(6, 6), Is.Not.EqualTo(before), "awake again, it burns");
        }

        [Test]
        public void SwitchesInTickBatchesReplayTheSameAsTheyRanLive()
        {
            // Live: a player walks east across the meadow while fires burn behind and ahead.
            var live = Meadow(9);
            var rules = RuleSet.Plain(R);
            var casts = new List<(int, CastCommand)>();
            var switches = new List<(int, ChunkSwitch)>();
            for (int t = 1; t <= 120; t++)
            {
                var batch = new TickBatch(t);
                if (t % 20 == 1)
                {
                    var (x, y) = Middle(t / 20, 3);
                    var cast = new CastCommand("fire-bolt", x + 3, y, 1);
                    batch.Casts.Add(cast);
                    casts.Add((t, cast));
                    foreach (var s in ChunkStreaming.Plan(live, new[] { (x, y) }))
                    {
                        batch.Chunks.Add(s);
                        switches.Add((t, s));
                    }
                }
                rules = batch.RunOn(live, rules, out _);
            }

            // Replay: what a late joiner rebuilds from the host's history.
            var replay = Meadow(9);
            var replayRules = RuleSet.Plain(R);
            var timeline = TickBatch.Timeline(casts, null, 120, null, switches);
            for (int t = 1; t <= 120; t++) replayRules = timeline[t].RunOn(replay, replayRules, out _);

            Assert.That(replay.CellHash(), Is.EqualTo(live.CellHash()));
            Assert.That(Active(replay), Is.EquivalentTo(Active(live)));
            Assert.That(Active(live).Count, Is.LessThan(49), "some chunks went to sleep");
        }
    }
}
