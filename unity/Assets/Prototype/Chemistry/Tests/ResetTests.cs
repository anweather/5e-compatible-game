using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Resetting the world online (J-014, NetChem's part A): at the reset's tick every peer rebuilds
    /// the world from its seed and runs that tick's batch on it, the mods in force re-stamped in it.
    /// The network's tick keeps counting while the rebuilt world starts again at tick 0, so the
    /// clock and the weather start again too. A peer that ran through the reset, and a late joiner
    /// whose history starts at the reset, both end exactly where a fresh world with the same mods
    /// does.
    /// </summary>
    public class ResetTests
    {
        static Rules R => ChemistryTests.R;

        static ActiveMod Wildfire => new ActiveMod("wildfire", File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "wildfire.json")));

        /// <summary>A peer as NetChem runs one: its world, and the network tick its world's tick 0 sits at.</summary>
        sealed class Peer
        {
            /// <summary>The world as it starts: a meadow with a pond, the same every time.</summary>
            static World Fresh()
            {
                var w = new World(R, 128, 128, 11, "grass");
                for (int y = 40; y < 50; y++)
                    for (int x = 60; x < 75; x++) { w.Paint(x, y, "pit"); w.AddWater(x, y, 8); }
                return w;
            }

            public World World = Fresh();
            public int Base;
            RuleSet rules = RuleSet.Plain(R);

            public void Run(TickBatch b)
            {
                if (b.Reset)
                {
                    World = Fresh();
                    Base = b.Tick - 1;
                    rules = RuleSet.Plain(R);
                }
                rules = b.RunOn(World, rules, out _);
                Assert.That(Base + World.Tick, Is.EqualTo(b.Tick), "the network tick keeps counting");
            }

            public string Hash => World.CellHash() + "/" + World.SkyHash() + "/" + World.Rules.Fingerprint;
        }

        const int ResetAt = 41, Until = 100;

        static readonly (int tick, CastCommand cast)[] Casts =
        {
            (3, new CastCommand("fire-bolt", 20, 20, 0)),
            (12, new CastCommand("earthen-trench", 58, 45, 3)),
            (30, new CastCommand("hasten-time", 64, 64, 0)),   // moves the clock: a reset must put it back
            (45, new CastCommand("fire-bolt", 90, 90, 0)),
            (60, new CastCommand("earthen-trench", 76, 45, 1)),
        };

        // The host's batches: wildfire on at tick 5, the reset at ResetAt (re-stamping wildfire), casts before and after.
        static List<TickBatch> HostBatches()
        {
            var batches = new List<TickBatch>();
            for (int t = 1; t <= Until; t++)
            {
                var b = new TickBatch(t, Casts.Where(c => c.tick == t).Select(c => c.cast));
                if (t == 5) b.Mods = new[] { Wildfire };
                if (t == ResetAt)
                {
                    b.Reset = true;
                    b.Mods = new[] { Wildfire };
                }
                batches.Add(b);
            }
            return batches;
        }

        [Test]
        public void APeerThatRanThroughTheResetMatchesAFreshWorldWithTheSameMods()
        {
            var live = new Peer();
            foreach (var b in HostBatches()) live.Run(b);

            // The same history as a fresh world would have it: the mods at its first tick, the later casts shifted.
            var fresh = new Peer();
            for (int t = 1; t <= Until - ResetAt + 1; t++)
            {
                var b = new TickBatch(t, Casts.Where(c => c.tick == t + ResetAt - 1).Select(c => c.cast));
                if (t == 1) b.Mods = new[] { Wildfire };
                fresh.Run(b);
            }
            Assert.That(live.Hash, Is.EqualTo(fresh.Hash));
            Assert.That(live.World.Minutes, Is.EqualTo(fresh.World.Minutes), "the clock started again (Hasten Time's lead is gone)");
            Assert.That(live.World.Rules.Fingerprint, Is.Not.EqualTo(R.Fingerprint), "the mod came through the reset");
        }

        [Test]
        public void ALateJoinerReplaysFromTheResetAndAgrees()
        {
            var live = new Peer();
            foreach (var b in HostBatches()) live.Run(b);

            // What the host keeps after the reset: the history from ResetAt on, the mods at its head.
            var timeline = TickBatch.Timeline(Casts.Where(c => c.tick >= ResetAt), new[] { new RuleChange(ResetAt, new[] { Wildfire }) }, Until, from: ResetAt);
            Assert.That(timeline.Keys.Min(), Is.EqualTo(ResetAt), "nothing from before the reset");
            Assert.That(timeline[ResetAt].Reset, Is.True, "the history starts with the reset");
            var joiner = new Peer();
            for (int t = ResetAt; t <= Until; t++) joiner.Run(timeline[t]);
            Assert.That(joiner.Hash, Is.EqualTo(live.Hash));
        }

        [Test]
        public void AHistoryFromTheStartHasNoReset()
        {
            var timeline = TickBatch.Timeline(System.Array.Empty<(int, CastCommand)>(), null, 10);
            Assert.That(timeline.Values.Any(b => b.Reset), Is.False);
            Assert.That(timeline.Keys.Min(), Is.EqualTo(1));
        }
    }
}
