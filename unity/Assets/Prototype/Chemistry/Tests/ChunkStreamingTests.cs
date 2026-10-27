using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Chunks activating and deactivating round the characters (ChunkStreaming): each character's
    /// reach (sim.json streaming.reach, 2) activates, a chunk deactivates once no character is within
    /// streaming.keep (3), reaches that overlap are one active region, and the switches travel in
    /// tick batches so a replay (a late joiner) switches the same chunks at the same ticks.
    /// </summary>
    public class ChunkStreamingTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        /// <summary>A meadow of n×n chunks, all active, as a new world starts.</summary>
        static World Meadow(uint seed = 3, int n = 7) => new World(R, n * S, n * S, seed, "grass");

        /// <summary>The same meadow with every chunk inactive.</summary>
        static World Still(int n = 9)
        {
            var w = Meadow(3, n);
            w.SetAllActive(false);
            return w;
        }

        static (int x, int y) Middle(int cx, int cy) => (cx * S + S / 2, cy * S + S / 2);

        static HashSet<(int, int)> Active(World w) => new HashSet<(int, int)>(w.ChunkCoords.Where(c => w.IsActive(c.cx, c.cy)));

        static HashSet<(int, int)> Square(int cx0, int cy0, int cx1, int cy1)
        {
            var set = new HashSet<(int, int)>();
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++) set.Add((cx, cy));
            return set;
        }

        [Test]
        public void TheReachAndKeepComeFromTheRules()
        {
            Assert.That(R.Sim.Streaming.Reach, Is.EqualTo(2), "sim.json streaming.reach");
            Assert.That(R.Sim.Streaming.Keep, Is.EqualTo(3), "sim.json streaming.keep");
            var near = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"reach\": 1, \"keep\": 1 } } }") });
            var w = new World(near, 9 * S, 9 * S, 3, "grass");
            w.SetAllActive(false);
            ChunkStreaming.Apply(w, ChunkStreaming.Plan(w, new[] { Middle(4, 4) }));
            Assert.That(Active(w), Is.EquivalentTo(Square(3, 3, 5, 5)), "a mod's reach of 1 activates the 3×3");
        }

        [Test]
        public void StreamingDistancesAreChecked()
        {
            IReadOnlyList<string> Errors(string streaming) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(("{ 'sim': { 'streaming': " + streaming + " } }").Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'reach': 0 }"), Does.Contain("sim streaming.reach must be at least 1"));
            Assert.That(Errors("{ 'reach': 3, 'keep': 2 }"), Does.Contain("sim streaming.keep must be at least streaming.reach"));
        }

        [Test]
        public void ACharacterKeepsTheChunksWithinItsReachActive()
        {
            var w = Still();
            var plan = ChunkStreaming.Plan(w, new[] { Middle(4, 4) });
            ChunkStreaming.Apply(w, plan);
            Assert.That(Active(w), Is.EquivalentTo(Square(2, 2, 6, 6)), "the 5×5 within 2 chunks");
            Assert.That(plan.All(s => s.Active), "everything was inactive, so the plan only activates");

            // From all active, as a world starts: only those further than keep (3) deactivate.
            var fresh = Meadow(3, 9);
            ChunkStreaming.Apply(fresh, ChunkStreaming.Plan(fresh, new[] { Middle(4, 4) }));
            Assert.That(Active(fresh), Is.EquivalentTo(Square(1, 1, 7, 7)), "the 7×7 within keep stays active");
        }

        [Test]
        public void ChunksActivateAheadOfACharacterAndDeactivateOnlyWellBehind()
        {
            var w = Still();
            ChunkStreaming.Apply(w, ChunkStreaming.Plan(w, new[] { Middle(2, 4) }));
            Assert.That(Active(w), Is.EquivalentTo(Square(0, 2, 4, 6)));
            // One chunk east: column 5 activates ahead; column 0 is 3 behind, within keep, so stays.
            var east = ChunkStreaming.Plan(w, new[] { Middle(3, 4) });
            Assert.That(east.Where(s => s.Active).Select(s => (s.CX, s.CY)), Is.EqualTo(new[] { (5, 2), (5, 3), (5, 4), (5, 5), (5, 6) }));
            Assert.That(east.Where(s => !s.Active), Is.Empty);
            ChunkStreaming.Apply(w, east);
            // One more: column 6 activates, and column 0, now 4 behind, deactivates.
            var further = ChunkStreaming.Plan(w, new[] { Middle(4, 4) });
            Assert.That(further.Where(s => s.Active).Select(s => (s.CX, s.CY)), Is.EqualTo(new[] { (6, 2), (6, 3), (6, 4), (6, 5), (6, 6) }));
            Assert.That(further.Where(s => !s.Active).Select(s => (s.CX, s.CY)), Is.EqualTo(new[] { (0, 2), (0, 3), (0, 4), (0, 5), (0, 6) }));
        }

        [Test]
        public void OverlappingReachesAreOneActiveRegion()
        {
            var w = Still();
            var plan = ChunkStreaming.Plan(w, new[] { Middle(2, 4), Middle(4, 4) });
            Assert.That(plan.Select(s => (s.CX, s.CY)).Distinct().Count(), Is.EqualTo(plan.Count), "each chunk switches once, however many reaches it's in");
            ChunkStreaming.Apply(w, plan);
            Assert.That(Active(w), Is.EquivalentTo(Square(0, 2, 6, 6)), "the union of the two 5×5s");

            // They part: the chunks between stay while either is within keep, and go once neither is.
            var apart = Still();
            ChunkStreaming.Apply(apart, ChunkStreaming.Plan(apart, new[] { Middle(1, 1), Middle(7, 7) }));
            Assert.That(apart.IsActive(4, 4), Is.False, "two reaches far apart: two islands, nothing between");
            Assert.That(Active(apart), Is.EquivalentTo(Square(0, 0, 3, 3).Union(Square(5, 5, 8, 8))));
        }

        [Test]
        public void ThePlanIsTheSameWhateverOrderTheCharactersComeIn()
        {
            var a = Meadow();
            var b = Meadow();
            var characters = new[] { Middle(1, 1), Middle(5, 5), Middle(5, 1) };
            var planA = ChunkStreaming.Plan(a, characters);
            var planB = ChunkStreaming.Plan(b, characters.Reverse().ToArray());
            Assert.That(planA, Is.EqualTo(planB));
            Assert.That(ChunkStreaming.Plan(a, System.Array.Empty<(int, int)>()), Is.Empty, "no characters: nothing changes");
        }

        [Test]
        public void AnInactiveChunkKeepsItsFireUntilItActivates()
        {
            var w = Meadow();
            var (x, y) = Middle(6, 6);
            w.Cast("fire-bolt", x, y, 0);
            ChunkStreaming.Apply(w, new[] { new ChunkSwitch(6, 6, false) });
            string before = w.ChunkHash(6, 6);
            for (int i = 0; i < 30; i++) w.Step();
            Assert.That(w.ChunkHash(6, 6), Is.EqualTo(before), "inactive: the fire neither spreads nor burns out");
            ChunkStreaming.Apply(w, new[] { new ChunkSwitch(6, 6, true) });
            for (int i = 0; i < 30; i++) w.Step();
            Assert.That(w.ChunkHash(6, 6), Is.Not.EqualTo(before), "active again, it burns");
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
            Assert.That(Active(live).Count, Is.LessThan(49), "some chunks deactivated");
        }

        [Test]
        public void EverySwitchIsReportedOnceAfterItHappens()
        {
            var w = Meadow();
            var heard = new List<ChunkSwitch>();
            w.ChunkSwitched += s =>
            {
                heard.Add(s);
                Assert.That(w.IsActive(s.CX, s.CY), Is.EqualTo(s.Active), "reported once switched");
            };
            w.SetActive(2, 3, false);
            w.SetActive(2, 3, false);
            w.SetActive(2, 3, true);
            Assert.That(heard, Is.EqualTo(new[] { new ChunkSwitch(2, 3, false), new ChunkSwitch(2, 3, true) }), "no report when nothing changes");
        }

        [Test]
        public void SwitchingEveryChunkReportsEachThatChangedInRowOrder()
        {
            var w = Meadow(3, 3);
            w.SetActive(1, 0, false);
            var heard = new List<ChunkSwitch>();
            w.ChunkSwitched += heard.Add;
            w.SetAllActive(false);
            Assert.That(heard.Select(s => (s.CX, s.CY)), Is.EqualTo(Square(0, 0, 2, 2).Where(c => c != (1, 0)).OrderBy(c => c.Item2).ThenBy(c => c.Item1)));
            Assert.That(heard.All(s => !s.Active), Is.True);
        }

        [Test]
        public void AChunkIsReportedActiveOnlyOnceItHasCaughtUp()
        {
            var w = Meadow();
            var (x, y) = Middle(3, 3);
            w.Cast("fire-bolt", x, y, 0);
            w.Step();
            w.SetActive(3, 3, false);
            for (int i = 0; i < 20; i++) w.Step();
            string caughtUp = null;
            w.ChunkSwitched += s => caughtUp = s.Active ? w.ChunkHash(3, 3) : caughtUp;
            w.SetActive(3, 3, true);
            Assert.That(w.CaughtUp(3, 3), Is.EqualTo(20));
            Assert.That(caughtUp, Is.EqualTo(w.ChunkHash(3, 3)), "the cells were already caught up when it was reported");
        }

        [Test]
        public void EveryPeerHearsTheSameSwitchesAtTheSameTicks()
        {
            // The host plans as a wizard walks east; a peer (or a late joiner) runs the stamped batches.
            var host = Meadow(9);
            var peer = Meadow(9);
            var planner = new ChunkPlanner();
            var rules = RuleSet.Plain(R);
            var peerRules = RuleSet.Plain(R);
            var hostHeard = new List<(int, ChunkSwitch)>();
            var peerHeard = new List<(int, ChunkSwitch)>();
            host.ChunkSwitched += s => hostHeard.Add((host.Tick, s));
            peer.ChunkSwitched += s => peerHeard.Add((peer.Tick, s));
            for (int t = 1; t <= 90; t++)
            {
                var batch = new TickBatch(t);
                if (t % 3 == 1) batch.Chunks.AddRange(planner.Plan(host, new[] { (t * 3, 4 * S) }));
                rules = batch.RunOn(host, rules, out _);
                peerRules = batch.RunOn(peer, peerRules, out _);
            }
            Assert.That(hostHeard, Is.Not.Empty);
            Assert.That(peerHeard, Is.EqualTo(hostHeard));
        }
    }
}
