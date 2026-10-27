using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Lingering (ChunkPlanner, docs/WORLD_STREAMING.md "Part 2, second slice"): a chunk that leaves
    /// every keep distance while live stays active until it settles, what is live on its edge
    /// activates the inactive chunk across it, and the caps (sim.json streaming.lingerChunks,
    /// lingerTicks) bound both. The worlds are bare dirt with grass where a fire should burn, so a
    /// fire burns out where it's put.
    /// </summary>
    public class LingerTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        static World Dirt(Rules rules = null) => new World(rules ?? R, 9 * S, 9 * S, 3, "dirt");

        static (int x, int y) Middle(int cx, int cy) => (cx * S + S / 2, cy * S + S / 2);

        static void Grass(World w, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) w.Paint(x, y, "grass");
        }

        /// <summary>A 10×10 patch of grass round the middle of a chunk, set alight.</summary>
        static void Fire(World w, int cx, int cy)
        {
            var (x, y) = Middle(cx, cy);
            Grass(w, x - 5, y - 5, x + 4, y + 4);
            w.Cast("fire-bolt", x, y, 0);
        }

        /// <summary>Plans and steps, as the grid does solo: the plan applies before each tick.</summary>
        static void Run(World w, ChunkPlanner p, (int x, int y) at, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                ChunkStreaming.Apply(w, p.Plan(w, new[] { at }));
                w.Step();
            }
        }

        static bool Burning(World w, int cx, int cy)
        {
            for (int y = cy * S; y < (cy + 1) * S; y++)
                for (int x = cx * S; x < (cx + 1) * S; x++)
                    if (w.At(x, y).Fire > 0) return true;
            return false;
        }

        static Rules With(string streaming) =>
            R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": " + streaming + " } }") });

        [Test]
        public void TheLingerCapsComeFromTheRulesAndAreChecked()
        {
            Assert.That(R.Sim.Streaming.LingerChunks, Is.EqualTo(16), "sim.json streaming.lingerChunks");
            Assert.That(R.Sim.Streaming.LingerTicks, Is.EqualTo(1800), "sim.json streaming.lingerTicks, 5 minutes");
            var errors = Assert.Throws<ModException>(() => With("{ \"lingerChunks\": 0 }")).Errors;
            Assert.That(errors, Does.Contain("sim streaming.lingerChunks must be at least 1"));
        }

        [Test]
        public void AFireWalkedAwayFromBurnsOnThenItsChunkDeactivates()
        {
            var w = Dirt();
            var p = new ChunkPlanner();
            Fire(w, 4, 4);
            Run(w, p, Middle(4, 4), 3);
            Assert.That(Burning(w, 4, 4), Is.True);

            // Four chunks west is past the keep distance (3).
            var away = Middle(0, 4);
            Run(w, p, away, 3);
            Assert.That(w.IsActive(4, 4), Is.True, "still burning, so it lingers");
            Assert.That(p.Lingering, Does.Contain((4, 4)));
            Assert.That(w.IsActive(4, 3), Is.False, "its quiet neighbour, as far away, deactivates");

            int t = 0;
            while (w.IsActive(4, 4) && t++ < 2000) Run(w, p, away, 1);
            Assert.That(w.IsActive(4, 4), Is.False, "deactivates once it settles");
            Assert.That(Burning(w, 4, 4), Is.False, "the fire burned out first");
            Assert.That(w.Tick - w.LastLive(4, 4), Is.EqualTo(R.Sim.Streaming.SettleTicks + 1), "let go the tick after it settled");
            Assert.That(p.Lingering, Is.Empty);
        }

        [Test]
        public void FireCrossingIntoAnInactiveChunkActivatesIt()
        {
            var w = Dirt();
            var p = new ChunkPlanner();
            // A strip of grass from chunk (4, 4) into (5, 4); the wizard three chunks west of (4, 4).
            var (_, y) = Middle(4, 4);
            Grass(w, 4 * S + 40, y - 2, 5 * S + 20, y + 2);
            var at = Middle(1, 4);
            Run(w, p, at, 1);
            Assert.That(w.IsActive(4, 4), Is.True, "within keep");
            Assert.That(w.IsActive(5, 4), Is.False, "past keep and quiet");

            w.Cast("fire-bolt", 4 * S + 41, y, 0);
            bool crossed = false;
            for (int t = 0; t < 600 && !crossed; t++)
            {
                Run(w, p, at, 1);
                crossed = Burning(w, 5, 4);
            }
            Assert.That(crossed, Is.True, "the fire burned on into the next chunk");
            Assert.That(p.Lingering, Does.Contain((5, 4)));

            for (int t = 0; t < 2000 && w.IsActive(5, 4); t++) Run(w, p, at, 1);
            Assert.That(w.IsActive(5, 4), Is.False, "and it deactivates once burned out");
            Assert.That(Burning(w, 5, 4), Is.False, "burned out, not cut off");
        }

        [Test]
        public void AtTheChunkCapTheNearestLingerAndTheRestAreCutOff()
        {
            var w = Dirt(With("{ \"lingerChunks\": 1 }"));
            var p = new ChunkPlanner();
            Fire(w, 4, 4);
            Fire(w, 6, 4);
            Run(w, p, Middle(5, 4), 3);
            Run(w, p, Middle(0, 4), 1);
            Assert.That(p.Lingering, Is.EquivalentTo(new[] { (4, 4) }), "the nearer fire lingers");
            Assert.That(w.IsActive(6, 4), Is.False, "the farther one is cut off");
            Assert.That(Burning(w, 6, 4), Is.True, "and waits, still burning");
        }

        [Test]
        public void NothingLingersLongerThanLingerTicks()
        {
            var w = Dirt(With("{ \"lingerTicks\": 6 }"));
            var p = new ChunkPlanner();
            Fire(w, 4, 4);
            Run(w, p, Middle(4, 4), 3);
            var away = Middle(0, 4);
            Run(w, p, away, 1);
            Assert.That(w.IsActive(4, 4), Is.True);
            Run(w, p, away, 7);
            Assert.That(w.IsActive(4, 4), Is.False, "let go after 6 ticks");
            Assert.That(Burning(w, 4, 4), Is.True, "while still burning");
            Run(w, p, away, 30);
            Assert.That(w.IsActive(4, 4), Is.False, "cut off, it stays inactive");
            Run(w, p, Middle(4, 4), 1);
            Assert.That(w.IsActive(4, 4), Is.True, "until a wizard comes back");
        }

        [Test]
        public void LingeringSwitchesInTickBatchesReplayTheSame()
        {
            // Live: the host plans with lingering while a wizard lights fires and walks away east.
            var live = Dirt();
            var p = new ChunkPlanner();
            var rules = RuleSet.Plain(R);
            var casts = new List<(int, CastCommand)>();
            var switches = new List<(int, ChunkSwitch)>();
            bool lingered = false;
            Grass(live, 0, 4 * S, 9 * S - 1, 4 * S + 3);
            for (int t = 1; t <= 240; t++)
            {
                var batch = new TickBatch(t);
                var at = (t * 2, 4 * S + 1);
                if (t % 40 == 1)
                {
                    var cast = new CastCommand("fire-bolt", at.Item1 + 3, at.Item2, 1);
                    batch.Casts.Add(cast);
                    casts.Add((t, cast));
                }
                foreach (var s in p.Plan(live, new[] { at }))
                {
                    batch.Chunks.Add(s);
                    switches.Add((t, s));
                }
                lingered |= p.Lingering.Count > 0;
                rules = batch.RunOn(live, rules, out _);
            }

            // Replay: what a late joiner rebuilds from the host's history, with no planner of its own.
            var replay = Dirt();
            Grass(replay, 0, 4 * S, 9 * S - 1, 4 * S + 3);
            var replayRules = RuleSet.Plain(R);
            var timeline = TickBatch.Timeline(casts, null, 240, null, switches);
            for (int t = 1; t <= 240; t++) replayRules = timeline[t].RunOn(replay, replayRules, out _);

            Assert.That(replay.CellHash(), Is.EqualTo(live.CellHash()));
            Assert.That(switches.Any(s => !s.Item2.Active), Is.True, "chunks deactivated behind the wizard");
            Assert.That(lingered, Is.True, "fires left behind lingered");
        }
    }
}
