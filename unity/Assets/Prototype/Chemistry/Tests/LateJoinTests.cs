// A late joiner's replay against the host's past (D4, docs/TESTING_LAYERS.md layer 1; #173, #164, #55, #83), on the
// harness in LockstepKit.cs: a host run on scripted commands, its joiners and snapshots replayed beside it and compared
// part by part of the checksum at every tick.
using System;
using System.Linq;
using NUnit.Framework;
using static Chem.Tests.LockstepKit;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class LateJoinTests
    {
        static Rules R => ChemistryTests.R;

        [Test]
        public void TheHarnessCatchesAMissingCommand()
        {
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
        public void ADownedWizardStaysDownForATickTrigger()
        {
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
            Func<World> build = () => Meadow(R);
            var host = Host(build, 60, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 12));
                if (t == 11) s.Strikes.Add(StrikeCommand.Stand("p0", 10, 10, 0));
                if (t == 15) s.Combos.Add(ComboCommand.Attune("p0", HandsDef.Left, "fire"));
                if (t == 23) s.Strikes.Add(StrikeCommand.Stand("p0", 12, 10, 12));
                if (t == 26) s.Strikes.Add(StrikeCommand.Stand("p0", 13, 10, 12));
                if (t == 31) s.Strikes.Add(StrikeCommand.Stand("p0", 13, 10, 0));
                if (t == 40) s.Combos.Add(ComboCommand.Attune("p0", HandsDef.Right, "fire"));
                if (t == 40) s.Strikes.Add(StrikeCommand.Stand("p0", 14, 10, 12));
                if (t == 44) s.Strikes.Add(StrikeCommand.Stand("p0", 15, 10, 12));
            });
            AssertJoinersMatch(host, build, new[] { 27, 41, 60 });
        }

        [Test]
        public void ARuleSwitchWhileAChunkIsInactiveReplaysTheSame()
        {
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
        public void PeersAndJoinersAgreeWithCharactersCrossingTheSeam()
        {
            const int ticks = 300, size = Chunks.Size;
            Func<World> build = () => new World(R, 12 * size, 2 * size, 5, "grass", wrapX: true);
            var planner = new ChunkPlanner();
            bool bothSides = false;
            int switches = 0;
            var host = Host(build, ticks, (t, w, s) =>
            {
                if (t == 1) s.Mods = Mod("steps", BoltsAndMeteors);
                int k = t % 180, off = k < 90 ? 2 * k : 360 - 2 * k;
                var stands = new[] { (w.W - 90 + off, 40), (90 - off, 90) };
                s.Strikes.Add(StrikeCommand.Stand("p0", stands[0].Item1, stands[0].Item2, 12));
                s.Strikes.Add(StrikeCommand.Stand("p1", stands[1].Item1, stands[1].Item2, 12));
                if (t == 50) s.Casts.Add(new CastCommand("fireball", 0, 64, 1, by: "p0"));
                var plan = t == 1 ? ChunkStreaming.StartPlan(w, stands) : planner.Plan(w, stands);
                s.Chunks.AddRange(plan);
                switches += plan.Count;
                bothSides |= w.IsActive(11, 0) && w.IsActive(0, 0) && !w.IsActive(5, 0);
            });
            Assert.That(host.World.TriggeredCasts, Is.GreaterThan(10), "the walks fire triggers");
            Assert.That(switches, Is.GreaterThan(10), "and switch chunks as they go");
            Assert.That(bothSides, Is.True, "the chunks either side of the seam active together, the far side of the world not");
            Assert.That(host.World.StrikeText(), Does.Not.Contain(":-").And.Not.Contain($"|wp0:{12 * size + 1}"), "the stands folded");
            AssertJoinersMatch(host, build, new[] { 1, 45, 50, 51, 90, 91, 150, 181, 240, ticks });
        }

        [Test]
        public void PeersAndJoinersAgreeWithCharactersCrossingBothSeamsOfATorus()
        {
            const int ticks = 300, size = Chunks.Size;
            Func<World> build = () => new World(R, 8 * size, 6 * size, 5, "grass", wrapX: true, wrapY: true);
            var planner = new ChunkPlanner();
            bool corners = false;
            int switches = 0;
            var host = Host(build, ticks, (t, w, s) =>
            {
                if (t == 1) s.Mods = Mod("steps", BoltsAndMeteors);
                int k = t % 180, off = k < 90 ? 2 * k : 360 - 2 * k;
                var stands = new[] { (w.W - 90 + off, w.H - 90 + off), (200, 90 - off) };
                s.Strikes.Add(StrikeCommand.Stand("p0", stands[0].Item1, stands[0].Item2, 12));
                s.Strikes.Add(StrikeCommand.Stand("p1", stands[1].Item1, stands[1].Item2, 12));
                if (t == 50) s.Casts.Add(new CastCommand("fireball", 0, 0, 1, by: "p0"));
                var plan = t == 1 ? ChunkStreaming.StartPlan(w, stands) : planner.Plan(w, stands);
                s.Chunks.AddRange(plan);
                switches += plan.Count;
                corners |= w.IsActive(7, 5) && w.IsActive(0, 0) && w.IsActive(7, 0) && w.IsActive(0, 5);
            });
            Assert.That(host.World.TriggeredCasts, Is.GreaterThan(10), "the walks fire triggers");
            Assert.That(switches, Is.GreaterThan(10), "and switch chunks as they go");
            Assert.That(corners, Is.True, "the four chunks round the corner active together");
            Assert.That(host.World.StrikeText(), Does.Not.Contain(":-"), "the stands folded");
            AssertJoinersMatch(host, build, new[] { 1, 45, 50, 51, 90, 91, 150, 181, 240, ticks });
        }

        [Test]
        public void AJoinerFromASnapshotRunsAsAPeerThereFromTheStart()
        {
            const int ticks = 420, size = Chunks.Size;
            Func<World> build = () =>
            {
                var w = new World(R, 12 * size, 2 * size, 5, "grass", wrapX: true);
                for (int y = 0; y < w.H; y += 6)
                    for (int x = (y * 5) % 13; x < w.W; x += 13) w.Paint(x, y, "tree");
                for (int x = 100; x < 110; x++)
                    for (int y = 20; y < 26; y++)
                    {
                        w.Paint(x, y, "pit");
                        w.AddWater(x, y, 4);
                    }
                w.KeepStart();
                return w;
            };
            var high = Mod("high", "{ 'sim': { 'height': { 'max': 12, 'blockAt': 12 } } }");
            var steps = Mod("steps", BoltsAndMeteors);
            var planner = new ChunkPlanner();
            var at = new[] { 1, 49, 50, 60, 120, 121, 150, 200, 201, 260, 300, 380 };
            bool dragonCame = false;
            var host = Host(build, ticks, (t, w, s) =>
            {
                if (t == 1) s.Mods = steps;
                if (t == 120) s.Mods = steps.Concat(high).ToArray();
                if (t == 200) s.Mods = steps;
                int k = t % 180, off = k < 90 ? 2 * k : 360 - 2 * k;
                var stands = new[] { (w.W - 90 + off, 40), (90 - off, 90) };
                s.Strikes.Add(StrikeCommand.Stand("p0", stands[0].Item1, stands[0].Item2, 12));
                s.Strikes.Add(StrikeCommand.Stand("p1", stands[1].Item1, stands[1].Item2, 12));
                if (t == 50) s.Casts.Add(new CastCommand("fireball", 0, 64, 1, by: "p0"));
                if (t == 60) s.Casts.Add(new CastCommand("create-water", 20, 30, 1, by: "p1"));
                if (t == 150) s.Casts.Add(new CastCommand("fire-bolt", w.W - 4, 100, 1, by: "p1"));
                if (t == 260) s.Dragon = true;
                s.Chunks.AddRange(t == 1 ? ChunkStreaming.StartPlan(w, stands) : planner.Plan(w, stands));
                dragonCame |= w.Dragon != null;
            }, at);
            Assert.That(dragonCame, Is.True, "the dragon came");
            Assert.That(host.World.TriggeredCasts, Is.GreaterThan(10), "the walks fire triggers");
            var joinAts = new[] { 1, 200, ticks };
            var replays = at.Select(t => (Func<string>)(() => SnapshotDifference(host, host.Snapshots[t], new FillSource("grass"))))
                .Concat(joinAts.Select(j => (Func<string>)(() => FirstDifference(host, build, j)))).ToArray();
            var found = Replayed(replays, replay => replay());
            for (int i = 0; i < at.Length; i++) Assert.That(found[i], Is.Null, $"snapshot at {at[i]}");
            for (int i = at.Length; i < found.Length; i++) Assert.That(found[i], Is.Null);
        }

        [Test]
        public void AStartLaidBeforeKeepStartComesBackFromASnapshot()
        {
            Func<World> build = () =>
            {
                var w = new World(R, 2 * Chunks.Size, Chunks.Size, 5, "grass");
                for (int y = 0; y < w.H; y += 6)
                    for (int x = (y * 5) % 13; x < w.W; x += 13) w.Paint(x, y, "tree");
                w.KeepStart();
                return w;
            };
            var host = Host(build, 30, (t, w, s) => { }, new[] { 1 });
            Assert.That(SnapshotDifference(host, host.Snapshots[1], new FillSource("grass")), Is.Null);
        }

        [Test]
        public void ASnapshotThatDoesntFitIsRefusedSayingWhy()
        {
            const int size = Chunks.Size;
            Func<World> build = () => new World(R, 3 * size, size, 5, "grass");
            var high = Mod("high", "{ 'sim': { 'height': { 'max': 12, 'blockAt': 12 } } }");
            var host = Host(build, 20, (t, w, s) =>
            {
                if (t == 2) s.Mods = high;
                if (t == 5) s.Casts.Add(new CastCommand("fireball", 40, 30, 1, by: "p0"));
            }, new[] { 20 });
            var snapshot = host.Snapshots[20];
            Assert.That(snapshot.Join(R, new FillSource("grass")).world.CellHash(), Is.EqualTo(host.World.CellHash()), "it fits as it is");

            var map = Assert.Throws<SaveException>(() => snapshot.Join(R, new FillSource("sand")));
            StringAssert.Contains("made by", map.Message);
            var otherRules = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"strike\": { \"reach\": 3 } } }") });
            var rules = Assert.Throws<SaveException>(() => snapshot.Join(otherRules, new FillSource("grass")));
            StringAssert.Contains("rules", rules.Message);
            var tampered = JoinSnapshot.Parse(snapshot.Text().Replace("\"checksum\":[\"" + snapshot.Checksum[0], "\"checksum\":[\"00000000"));
            var drift = Assert.Throws<SaveException>(() => tampered.Join(R, new FillSource("grass")));
            StringAssert.Contains("doesn't come back to the host's world", drift.Message);
            Assert.Throws<SaveException>(() => JoinSnapshot.Parse("{ not json"));
            Assert.Throws<SaveException>(() => JoinSnapshot.Parse("{ \"format\": 9 }"));
        }

        [Test]
        public void ASnapshotOverOtherLandIsRefused()
        {
            const int size = Chunks.Size;
            int[] Land() => Enumerable.Range(0, 2 * size * size).Select(i => (i % 7) - 3).ToArray();
            Func<World> build = () =>
            {
                var w = new World(R, 2 * size, size, 5, "grass");
                w.SetLand(Land());
                return w;
            };
            var host = Host(build, 10, (t, w, s) =>
            {
                if (t == 3) s.Casts.Add(new CastCommand("create-water", 30, 30, 1, by: "p0"));
            }, new[] { 10 });
            var snapshot = host.Snapshots[10];
            Assert.That(SnapshotDifference(host, snapshot, new FillSource("grass"), Land()), Is.Null, "given the host's land");
            var none = Assert.Throws<SaveException>(() => snapshot.Join(R, new FillSource("grass")));
            StringAssert.Contains("map's land", none.Message);
            var other = Land();
            other[5] += 1;
            var wrong = Assert.Throws<SaveException>(() => snapshot.Join(R, new FillSource("grass"), other));
            StringAssert.Contains("other land", wrong.Message);
        }

        [Test]
        public void AWalkUnderAStepTriggerKeepsAStandAChecksumOrAFiring()
        {
            const int ticks = 600;
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
