// Golf in lockstep (D4, D22): a golf turn and the late joiners of a host playing golf replay the host's past, on the
// lockstep kit (LockstepKit.cs). Were LateJoinTests' until golf was a mode of its own; each world a run builds plays
// golf (GolfMode.Install), the joiners' too.
using System;
using System.Linq;
using NUnit.Framework;
using static Chem.Tests.LockstepKit;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class GolfJoinTests
    {
        static Rules R => ChemistryTests.R;

        [Test]
        public void AGolfTurnReplaysTheSame()
        {
            Func<World> build = () =>
            {
                var w = GolfMode.Install(Meadow(R));
                w.ApplyGolf(new GolfCommand(10, 10, 30, 10));
                return w;
            };
            var host = Host(build, 120, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 8, 10, 12));
                if (t == 3) s.Strikes.Add(StrikeCommand.Stand("p1", 60, 60, 12));
                if (t == 4 || t == 20 || t == 70) s.Casts.Add(new CastCommand("gust-of-wind", w.Golf().X - 2, w.Golf().Y, 1, by: "p0"));
                if (t >= 10 && t <= 26 && t % 4 == 2) s.Strikes.Add(StrikeCommand.Stand("p1", 20 + t / 4, 14, 12));
                if (t == 29) s.Strikes.Add(StrikeCommand.Stand("p1", 60, 60, 12));
            });
            Assert.That(host.World.Golf().Strokes, Is.EqualTo(2), "the second stroke waits for p1's shot; the third goes");
            AssertJoinersMatch(host, build, new[] { 30, 90, 120 });
        }

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
                if (t % 37 == 0 && w.Golf().InPlay) s.Casts.Add(new CastCommand("gust-of-wind", w.Golf().X - 2, w.Golf().Y, 1, by: "p" + (t / 37 % 3 == 0 ? 1 : 0)));
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
            var w = GolfMode.Install(Meadow(R));
            w.ApplyGolf(new GolfCommand(16, 24, 50, 24));
            return w;
        }

        [Test]
        public void JoinersAtAnyTickReplayTheHostsPast()
        {
            const int ticks = 360;
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var host = Host(Course, ticks, Walks(seed));
                Assert.That(host.World.TriggeredCasts, Is.GreaterThan(10), $"seed {seed}: the walks fire triggers");
                Assert.That(host.World.Golf().Strokes, Is.GreaterThan(2), $"seed {seed}: and play golf");
                AssertJoinersMatch(host, Course, new[] { 1, 29, 30, 31, 61, 100, 149, 150, 151, 200, 221, 245, 299, 300, 330, ticks });
                int stands = host.AllStrikes.Count(s => s.Kind == StrikeKind.Stand), kept = host.KeptAt[ticks].Count(s => s.Kind == StrikeKind.Stand);
                Assert.That(kept, Is.LessThan(stands * 3 / 4), $"seed {seed}: kept {kept} of {stands} stands");
            }
        }

        [Test]
        public void AJoinerFromASnapshotOfARoundTakesItsModesBeforeTheCheck()
        {
            Func<World> build = () =>
            {
                var w = GolfMode.Install(Meadow(R));
                w.ApplyGolf(new GolfCommand(10, 10, 30, 10));
                w.KeepStart();
                return w;
            };
            var at = new[] { 30, 61, 100 };
            var host = Host(build, 140, (t, w, s) =>
            {
                if (t == 2) s.Strikes.Add(StrikeCommand.Stand("p0", 8, 10, 12));
                if (t == 4 || t == 50) s.Casts.Add(new CastCommand("gust-of-wind", w.Golf().X - 2, w.Golf().Y, 1));
                if (t == 60) s.Dragon = true;
            }, at);
            Assert.That(host.World.Golf().Strokes, Is.GreaterThan(0));
            var golfing = GolfMode.Install(Meadow(R));
            foreach (var found in Replayed(at, t => SnapshotDifference(host, host.Snapshots[t], new FillSource("grass"), null, golfing))) Assert.That(found, Is.Null);
            var wrong = Assert.Throws<SaveException>(() => host.Snapshots[30].Join(R, new FillSource("grass")));
            StringAssert.Contains("casts", wrong.Message, "golf's slot is in the checksum, so a joiner without golf is refused");
        }
    }
}
