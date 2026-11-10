// A late joiner's replay against the host's past (D4, docs/TESTING_LAYERS.md layer 1; #173, #164, #55, #83). The host
// runs a world tick by tick on scripted commands, stamping the stands into a strike history through StandKeeper as
// NetChem does (stamped before the tick runs, Ran after it). A joiner arriving at a tick gets that history, keeps
// every command after it as the live batches bring them, and replays all of it on a fresh world. Both are compared
// at every tick, part by part of the checksum (NetChem.Checksum), with the stands apart from the rest of the casts
// part: the stands may differ only at a tick no rule and no checksum reads them, and everything else never.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class LateJoinTests
    {
        static Rules R => ChemistryTests.R;

        static World Meadow(Rules rules) => new World(rules, 64, 64, 5, "grass");

        /// <summary>What one tick stamps: casts, a rule switch, chunk switches, combo input and stands, strikes or leavings.</summary>
        sealed class Stamp
        {
            public readonly List<CastCommand> Casts = new List<CastCommand>();
            public readonly List<ComboCommand> Combos = new List<ComboCommand>();
            public readonly List<StrikeCommand> Strikes = new List<StrikeCommand>();
            public readonly List<ChunkSwitch> Chunks = new List<ChunkSwitch>();
            public IReadOnlyList<ActiveMod> Mods;
        }

        /// <summary>A world's checksum at a tick, part by part (NetChem.Checksum's order), the stands apart from the rest of the casts.</summary>
        static string[] Parts(World w) => new[]
        {
            w.CellHash(), w.Rules.Fingerprint, w.ThingsNow.Fingerprint, w.SkyHash(), w.DefenseHash(), w.CastText(""), w.DragonHash(), w.LandHash(),
            w.StrikeText(),
        };

        static readonly string[] PartNames = { "cells", "rules", "things", "sky", "defense", "casts but the stands", "dragon", "land", "stands" };
        const int StandsPart = 8;

        /// <summary>A tick whose checksum peers compare: the window's end (RollingHash), or every Every'th (the probes' TickApplied).</summary>
        static bool ChecksumTick(World w, int tick) => tick % RollingHash.Every == 0 || tick % RollingHash.WindowFor(w) == 0;

        /// <summary>The host's run: what it stamped each tick, the strike history it kept, and its checksum's parts each tick.</summary>
        sealed class HostRun
        {
            public readonly Dictionary<int, Stamp> Stamped = new Dictionary<int, Stamp>();
            public readonly Dictionary<int, StampedStrike[]> KeptAt = new Dictionary<int, StampedStrike[]>();
            public readonly Dictionary<int, string[]> Seen = new Dictionary<int, string[]>();
            public readonly List<StampedStrike> AllStrikes = new List<StampedStrike>();
            public World World;
            public int Ticks;
        }

        /// <summary>
        /// Runs the host for <paramref name="ticks"/>: each tick <paramref name="script"/> says what's asked (it may look at the
        /// world as it stands), the stands go through a StandKeeper into the history, the batch runs, then the keeper hears
        /// the tick ran. The history is kept as it stood at each tick, for joiners arriving then.
        /// </summary>
        static HostRun Host(Func<World> build, int ticks, Action<int, World, Stamp> script)
        {
            var run = new HostRun { World = build(), Ticks = ticks };
            var w = run.World;
            var set = RuleSet.Plain(w.Rules);
            var history = new List<StampedStrike>();
            Stamp now = null;
            // A rule switch stamped for a tick that hasn't run may switch on a trigger that reads stands (NetChem.StandsReadByTriggers).
            var keeper = new StandKeeper<StampedStrike>(() => w.TriggersReadStands || now?.Mods != null, w.InBounds, w.HoldReadsStand);
            for (int t = 1; t <= ticks; t++)
            {
                now = new Stamp();
                script(t, w, now);
                run.Stamped[t] = now;
                foreach (var s in now.Strikes)
                {
                    var stamped = new StampedStrike(t, s);
                    run.AllStrikes.Add(stamped);
                    keeper.Record(history, stamped);
                }
                set = Batch(t, now, now.Strikes).RunOn(w, set, out _);
                keeper.Ran(t, w.StandReads, RollingHash.WindowFor(w));
                run.KeptAt[t] = history.ToArray();
                run.Seen[t] = Parts(w);
            }
            return run;
        }

        static TickBatch Batch(int t, Stamp stamp, IEnumerable<StrikeCommand> strikes)
        {
            var batch = new TickBatch(t, stamp.Casts, stamp.Mods);
            batch.Combos.AddRange(stamp.Combos);
            batch.Chunks.AddRange(stamp.Chunks);
            batch.Strikes.AddRange(strikes);
            return batch;
        }

        /// <summary>
        /// A joiner arriving at <paramref name="joinAt"/>: the strike history kept then, every strike stamped after it, and every
        /// other command (those are all kept), replayed on a fresh world. The first tick and part where it differs from the
        /// host's, or null: the stands may differ only at a tick whose checksum no peer compares.
        /// </summary>
        static string FirstDifference(HostRun host, Func<World> build, int joinAt)
        {
            var strikes = host.KeptAt[joinAt].Concat(host.AllStrikes.Where(s => s.Tick > joinAt)).ToLookup(s => s.Tick, s => s.Command);
            var w = build();
            var set = RuleSet.Plain(w.Rules);
            for (int t = 1; t <= host.Ticks; t++)
            {
                set = Batch(t, host.Stamped[t], strikes[t]).RunOn(w, set, out _);
                var seen = Parts(w);
                for (int p = 0; p < seen.Length; p++)
                {
                    if (seen[p] == host.Seen[t][p] || (p == StandsPart && !ChecksumTick(w, t))) continue;
                    return $"joined at {joinAt}: tick {t}, {PartNames[p]}: host {host.Seen[t][p]}, joiner {seen[p]}";
                }
            }
            return null;
        }

        static void AssertJoinersMatch(HostRun host, Func<World> build, IEnumerable<int> joinAts)
        {
            foreach (int at in joinAts) Assert.That(FirstDifference(host, build, at), Is.Null);
        }

        [Test]
        public void TheHarnessCatchesAMissingCommand()
        {
            // The harness itself: a joiner whose history lost a stand differs, and says where.
            Func<World> build = () => Meadow(R);
            var host = Host(build, 40, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 12));
                if (t == 12) s.Casts.Add(new CastCommand("fire-bolt", 20, 20, 1, by: "p0"));
            });
            Assert.That(FirstDifference(host, build, 40), Is.Null, "the whole history: the same");
            host.KeptAt[40] = Array.Empty<StampedStrike>();
            Assert.That(FirstDifference(host, build, 40), Is.EqualTo("joined at 40: tick 30, stands: host |wp0:10:10:12:0, joiner "));
        }

        [Test]
        public void AStandAChecksumSawIsReplayed()
        {
            // #55 and #164: p1 stands once early and next only after three checksums; p0 walks and casts a Fireball. The
            // old rule (the newest stand since the last strike) dropped p1's first stand, so a joiner's checksums at 30, 60
            // and 90 had no p1 in their stands, in the casts part only.
            Func<World> build = () => Meadow(R);
            var host = Host(build, 150, (t, w, s) =>
            {
                if (t % 3 == 0) s.Strikes.Add(StrikeCommand.Stand("p0", 10 + t / 6, 20, 12));
                if (t == 5) s.Strikes.Add(StrikeCommand.Stand("p1", 30, 30, 12));
                if (t == 25) s.Casts.Add(new CastCommand("fireball", 40, 40, 1, by: "p0"));
                if (t == 100) s.Strikes.Add(StrikeCommand.Stand("p1", 31, 30, 12));
            });
            AssertJoinersMatch(host, build, new[] { 101, 120, 150 });
        }

        [Test]
        public void AGolfTurnReplaysTheSame()
        {
            // #173: p0 strokes, p1 comes onto the course and walks about it, p0 tries again while p1 is there (someone else's shot), p1 goes,
            // and p0 strokes again. Golf reads every wizard's stand to say whose turn it is (World.GolfWaiting). All before
            // the first checksum, so it's the turn that differs, not only the stands a checksum sees.
            Func<World> build = () =>
            {
                var w = Meadow(R);
                w.ApplyGolf(new GolfCommand(10, 10, 30, 10));
                return w;
            };
            var host = Host(build, 120, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 8, 10, 12));
                if (t == 3) s.Strikes.Add(StrikeCommand.Stand("p1", 60, 60, 12));
                if (t == 4 || t == 20 || t == 70) s.Casts.Add(new CastCommand("gust-of-wind", w.Golf.X - 2, w.Golf.Y, 1, by: "p0"));
                if (t >= 10 && t <= 26 && t % 4 == 2) s.Strikes.Add(StrikeCommand.Stand("p1", 20 + t / 4, 14, 12));
                if (t == 29) s.Strikes.Add(StrikeCommand.Stand("p1", 60, 60, 12));
            });
            Assert.That(host.World.Golf.Strokes, Is.EqualTo(2), "the second stroke waits for p1's shot; the third goes");
            AssertJoinersMatch(host, build, new[] { 30, 90, 120 });
        }

        [Test]
        public void ADownedWizardStaysDownForATickTrigger()
        {
            // A tick trigger reads every standing wizard each tick (World.Ticked): p0 is downed, crawls a cell still down and
            // gets up. The first down stand fires nothing and no checksum sees it, but leaving it out has p0 standing, so
            // the joiner's trigger would fire round it at 14, when the host's didn't.
            const string shower = "{ 'triggers': { 'shower': { 'on': 'tick', 'who': 'each', 'every': 12, 'do': [ { 'cast': 'fire-bolt', 'at': 'here' } ] } } }";
            var mods = new[] { new ActiveMod("shower", shower.Replace('\'', '"')) };
            Func<World> build = () => Meadow(R);
            var host = Host(build, 90, (t, w, s) =>
            {
                if (t == 1) s.Mods = mods;
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 12));
                if (t == 4) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 0));
                if (t == 20) s.Strikes.Add(StrikeCommand.Stand("p0", 11, 10, 0));
                if (t == 26) s.Strikes.Add(StrikeCommand.Stand("p0", 12, 10, 12));
                if (t == 28) s.Strikes.Add(StrikeCommand.Stand("p0", 14, 10, 12));
            });
            Assert.That(host.World.TriggerFirings, Is.GreaterThan(3));
            AssertJoinersMatch(host, build, new[] { 21, 27, 90 });
        }

        [Test]
        public void ADownedWizardCantAttuneOnTheReplayEither()
        {
            // Attuning a hand reads whether the wizard is down (World.Attune), anywhere.
            Func<World> build = () => Meadow(R);
            var host = Host(build, 60, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 12));
                if (t == 11) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 0));
                if (t == 15) s.Combos.Add(ComboCommand.Attune("p0", HandsDef.Left, "fire"));
                if (t == 23) s.Strikes.Add(StrikeCommand.Stand("p0", 12, 10, 12));
                if (t == 26) s.Strikes.Add(StrikeCommand.Stand("p0", 13, 10, 12));
                // Down again, and up in the tick it tries: the hand is read before the stands land (TickBatch.Prepare).
                if (t == 31) s.Strikes.Add(StrikeCommand.Stand("p0", 13, 10, 0));
                if (t == 40) s.Combos.Add(ComboCommand.Attune("p0", HandsDef.Right, "fire"));
                if (t == 40) s.Strikes.Add(StrikeCommand.Stand("p0", 14, 10, 12));
                if (t == 44) s.Strikes.Add(StrikeCommand.Stand("p0", 15, 10, 12));
            });
            AssertJoinersMatch(host, build, new[] { 27, 41, 60 });
        }

        // Step triggers that fire casts behind and ahead of a walker (CommandChannelTests had them), and a tick trigger's shower.
        const string BoltsAndMeteors = "{ 'triggers': { " +
            "'bolt-steps': { 'on': 'step', 'who': 'each', 'every': 9, 'do': [ { 'cast': 'fire-bolt', 'at': 'behind', 'by': 2 } ] }, " +
            "'meteor-steps': { 'on': 'step', 'who': 'each', 'every': 15, 'do': [ { 'cast': 'meteor', 'at': 'ahead', 'by': 2, 'delay': 6 } ] } } }";

        const string Showers = "{ 'triggers': { 'shower': { 'on': 'tick', 'who': 'each', 'every': 12, 'chance': 0.5, 'do': [ { 'cast': 'fire-bolt', 'at': 'around', 'within': 3 } ] } } }";

        static ActiveMod[] Mod(string id, string json) => new[] { new ActiveMod(id, json.Replace('\'', '"')) };

        /// <summary>
        /// Two players walking about a golf course: runs, turns, stops, hurts, a fall and getting up, a stand off the world,
        /// punches, golf strokes by either (so turns wait), Fireballs; the step triggers on from tick 1 to 150, the tick
        /// trigger from 220 to 300, nothing in between.
        /// </summary>
        static Action<int, World, Stamp> Walks(int seed)
        {
            var random = new Random(seed);
            var at = new[] { (20, 20), (40, 30) };
            var way = new[] { (1, 0), (0, 1) };
            var hp = new[] { 12, 12 };
            return (t, w, s) =>
            {
                if (t == 1) s.Mods = Mod("steps", BoltsAndMeteors);
                if (t == 150 || t == 300) s.Mods = Array.Empty<ActiveMod>();
                if (t == 220) s.Mods = Mod("showers", Showers);
                if (t % 37 == 0 && w.Golf.InPlay) s.Casts.Add(new CastCommand("gust-of-wind", w.Golf.X - 2, w.Golf.Y, 1, by: "p" + (t / 37 % 3 == 0 ? 1 : 0)));
                if (t % 90 == 45) s.Casts.Add(new CastCommand("fireball", at[1].Item1 + 6, at[1].Item2, 1, by: "p1"));
                for (int p = 0; p < 2; p++)
                {
                    string player = "p" + p;
                    int roll = random.Next(100);
                    if (roll < 40) continue;
                    if (roll < 50) way[p] = (random.Next(3) - 1, random.Next(3) - 1);
                    else if (roll < 54) hp[p] = hp[p] > 0 ? random.Next(0, 12) : 12;
                    else if (roll < 55)
                    {
                        s.Strikes.Add(StrikeCommand.Strike(player, at[p].Item1, at[p].Item2, 1, 0));
                        continue;
                    }
                    else if (roll == 56)
                    {
                        s.Strikes.Add(StrikeCommand.Stand(player, -3, at[p].Item2, hp[p]));
                        continue;
                    }
                    int x = at[p].Item1 + way[p].Item1, y = at[p].Item2 + way[p].Item2;
                    if (x < 4 || x > 59 || y < 4 || y > 59) way[p] = (-way[p].Item1, -way[p].Item2);
                    else at[p] = (x, y);
                    s.Strikes.Add(StrikeCommand.Stand(player, at[p].Item1, at[p].Item2, hp[p]));
                }
            };
        }

        static World Course()
        {
            var w = Meadow(R);
            w.ApplyGolf(new GolfCommand(16, 24, 50, 24));
            return w;
        }

        [Test]
        public void ARuleSwitchWhileAChunkIsInactiveReplaysTheSame()
        {
            // #190: Moonstone on, moonstone laid in two chunks, the far one deactivated, Moonstone off (the near chunk is made
            // to fit then, the far one waits), the far one activated (made to fit then). Joiners before, between and after match.
            var moonstone = new ActiveMod("moonstone", System.IO.File.ReadAllText(System.IO.Path.Combine(ChemistryTests.RulesDir(), "mods", "moonstone.json")));
            Func<World> build = () => new World(R, 3 * Chunks.Size, Chunks.Size, 5, "grass");
            string waiting = null, after = null;
            var host = Host(build, 45, (t, w, s) =>
            {
                if (t == 1) s.Mods = new[] { moonstone };
                if (t == 3)
                {
                    s.Casts.Add(new CastCommand("moon-frost", 30, 30, 1, by: "p0"));
                    s.Casts.Add(new CastCommand("moon-frost", 150, 30, 1, by: "p0"));
                }
                if (t == 8) s.Chunks.Add(new ChunkSwitch(2, 0, false));
                if (t == 12) s.Mods = Array.Empty<ActiveMod>();
                if (t == 20) s.Casts.Add(new CastCommand("fire-bolt", 40, 40, 1, by: "p0"));
                if (t == 29) waiting = w.At(150, 30).T + " " + w.At(30, 30).T;
                if (t == 30) s.Chunks.Add(new ChunkSwitch(2, 0, true));
                if (t == 31) after = w.At(150, 30).T;
            });
            Assert.That(waiting, Is.EqualTo("moonstone dirt"), "the switch made the active chunk fit and left the inactive one");
            Assert.That(after, Is.EqualTo("dirt"), "made to fit when it activated");
            AssertJoinersMatch(host, build, new[] { 1, 7, 8, 11, 12, 13, 29, 30, 31, 45 });
        }

        [Test]
        public void JoinersAtAnyTickReplayTheHostsPast()
        {
            const int ticks = 360;
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var host = Host(Course, ticks, Walks(seed));
                Assert.That(host.World.TriggeredCasts, Is.GreaterThan(10), $"seed {seed}: the walks fire triggers");
                Assert.That(host.World.Golf.Strokes, Is.GreaterThan(2), $"seed {seed}: and play golf");
                AssertJoinersMatch(host, Course, new[] { 1, 29, 30, 31, 61, 100, 149, 150, 151, 200, 221, 245, 299, 300, 330, ticks });
                int stands = host.AllStrikes.Count(s => s.Kind == StrikeKind.Stand), kept = host.KeptAt[ticks].Count(s => s.Kind == StrikeKind.Stand);
                Assert.That(kept, Is.LessThan(stands * 3 / 4), $"seed {seed}: kept {kept} of {stands} stands");
            }
        }

        [Test]
        public void AWalkUnderAStepTriggerKeepsAStandAChecksumOrAFiring()
        {
            // #83: one player walks a long loop, a stand a tick, with a step trigger on and nobody striking. The history
            // keeps the first stand, a stand a checksum, the stands round each firing and the newest: not one a step.
            const int ticks = 600;
            // A square, 40 cells a side, east, south, west and north.
            var loop = Enumerable.Range(0, 40).Select(i => (10 + i, 10)).Concat(Enumerable.Range(0, 40).Select(i => (50, 10 + i)))
                .Concat(Enumerable.Range(0, 40).Select(i => (50 - i, 50))).Concat(Enumerable.Range(0, 40).Select(i => (10, 50 - i))).ToArray();
            Func<World> build = () => Meadow(R);
            var host = Host(build, ticks, (t, w, s) =>
            {
                if (t == 1) s.Mods = Mod("steps", BoltsAndMeteors);
                s.Strikes.Add(StrikeCommand.Stand("p0", loop[t % loop.Length].Item1, loop[t % loop.Length].Item2, 12));
            });
            int kept = host.KeptAt[ticks].Length, firings = host.World.TriggerFirings;
            Assert.That(firings, Is.GreaterThan(20));
            Assert.That(kept, Is.LessThanOrEqualTo(2 + ticks / RollingHash.Every + 3 * firings), $"kept {kept} of {ticks}, with {firings} firings");
            Assert.That(kept, Is.LessThan(ticks / 2));
            AssertJoinersMatch(host, build, new[] { 150, 451, ticks });
        }
    }
}
