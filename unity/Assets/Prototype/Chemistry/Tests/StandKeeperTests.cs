// Which stands a late joiner's strike history keeps (StandKeeper), stand by stand; LateJoinTests replays them on real worlds.
// Ported from the Editor's CommandChannelTests, which had them with NetChem's StrikeIntent before the keeper came to the core.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class StandKeeperTests
    {
        /// <summary>A host's history as NetChem keeps it: each tick's stand stamped, then the tick run (Ran) with the world's reads so far.</summary>
        sealed class History
        {
            public readonly List<StampedStrike> Kept = new List<StampedStrike>();
            public readonly StandKeeper<StampedStrike> Keeper;

            public History(System.Func<bool> triggers = null, System.Func<int, int, bool> keeps = null) =>
                Keeper = new StandKeeper<StampedStrike>(triggers ?? (() => false), (x, y) => x >= 0 && y >= 0, keeps);

            public void Stamp(int tick, StrikeCommand command) => Keeper.Record(Kept, new StampedStrike(tick, command));

            public int[] Ticks => Kept.Select(s => s.Tick).ToArray();
        }

        static StrikeCommand Stand(int x, int y, int hp = 12) => StrikeCommand.Stand("p0", x, y, hp);

        [Test]
        public void AStandStandingAtAChecksumOrARuleReadingStaysTheRestGo()
        {
            // Without triggers: 28 the first; 29 and 30 stand at the checksum at 30; 31 goes; 33 the newest, 32 until 33 has run.
            var h = new History();
            for (int t = 28; t <= 33; t++)
            {
                h.Stamp(t, Stand(t, 10));
                h.Keeper.Ran(t, 0);
            }
            Assert.That(h.Ticks, Is.EqualTo(new[] { 28, 29, 30, 32, 33 }));

            // A rule read the stands at 45 (a golf turn, a hand attuning): the stands round it stay as a checksum's.
            h = new History();
            for (int t = 41; t <= 51; t += 2)
            {
                h.Stamp(t, Stand(t, 10));
                h.Keeper.Ran(t, t >= 45 ? 1 : 0);
                h.Keeper.Ran(t + 1, t >= 45 ? 1 : 0);
            }
            Assert.That(h.Ticks, Is.EqualTo(new[] { 41, 43, 45, 49, 51 }), "43 stood when 45 read; 47 goes");
        }

        [Test]
        public void ABigWorldsWindowIsAChecksumToo()
        {
            var h = new History();
            for (int t = 40; t <= 48; t += 4)
            {
                h.Stamp(t, Stand(t, 10));
                h.Keeper.Ran(t, 0, 45);
                h.Keeper.Ran(t + 3, 0, 45);
            }
            Assert.That(h.Ticks, Is.EqualTo(new[] { 40, 44, 48 }), "44 stands at the window's end at 45");
        }

        [Test]
        public void AStrikeOrALeavingKeepsEveryStandUpToIt()
        {
            var h = new History();
            for (int t = 1; t <= 6; t++)
            {
                h.Stamp(t, Stand(t, 10));
                if (t == 3) h.Stamp(t, StrikeCommand.Strike("p0", t, 10, 1, 0));
                h.Keeper.Ran(t, 0);
            }
            Assert.That(h.Kept.Select(s => $"{s.Kind}@{s.Tick}"), Is.EqualTo(new[] { "Stand@1", "Stand@2", "Stand@3", "Strike@3", "Stand@5", "Stand@6" }), "4 goes");
        }

        [Test]
        public void AStandOffTheWorldIsNeverKept()
        {
            var h = new History();
            h.Stamp(1, Stand(4, 4));
            h.Stamp(2, Stand(-3, 4));
            Assert.That(h.Ticks, Is.EqualTo(new[] { 1 }), "the core ignores it");
        }

        [Test]
        public void WithATriggerReadingStandsTheStandsKeptAreThoseAReplayNeeds()
        {
            const int fireAt = 7;
            var h = new History(() => true);
            // East along a row, a turn south for two cells, then east again with a step that fired a trigger at tick 7.
            var walk = new[] { (10, 10), (11, 10), (12, 10), (13, 10), (13, 11), (13, 12), (14, 12), (15, 12) };
            for (int t = 1; t <= walk.Length; t++)
            {
                h.Stamp(t, Stand(walk[t - 1].Item1, walk[t - 1].Item2));
                if (t < walk.Length) h.Keeper.Ran(t, t >= fireAt ? 1 : 0);
            }
            // 1: the first (no stand before it to step from); 4 and 6: the stand after each steps another way from the
            // stand before them (from 1 to 6 is diagonal, 4 to 6 south; from 4 to 7 diagonal, 6 to 7 east); 7: it fired;
            // 8: the newest, not run yet. 2, 3 and 5 fired nothing and leave the step after them as it was.
            Assert.That(h.Ticks, Is.EqualTo(new[] { 1, 4, 6, 7, 8 }));

            // Hurt to the ground and up again: a stand that's no step can't be shown to fire nothing, so it stays.
            h = new History(() => true);
            var hurt = new[] { (1, 12), (2, 12), (2, 0), (3, 0), (4, 12), (5, 12), (6, 12), (7, 12) };
            for (int t = 1; t <= hurt.Length; t++)
            {
                h.Stamp(t, Stand(hurt[t - 1].Item1, 10, hurt[t - 1].Item2));
                h.Keeper.Ran(t, 0);
            }
            Assert.That(h.Ticks, Is.EqualTo(new[] { 1, 3, 4, 5, 7, 8 }),
                "2 goes (the fall after it is no step from 1 either); 3 and 4 are down and 5 gets up, none a step, so they stay; 6 goes, 7 until 8 has run");

            // Without triggers the same stands go but those a checksum or a rule saw: down or not, nothing reads them.
            h = new History();
            for (int t = 1; t <= hurt.Length; t++)
            {
                h.Stamp(t, Stand(hurt[t - 1].Item1, 10, hurt[t - 1].Item2));
                h.Keeper.Ran(t, 0);
            }
            Assert.That(h.Ticks, Is.EqualTo(new[] { 1, 7, 8 }));
        }

        [Test]
        public void StandsKeptWhileATriggerWasOnStayOnceItsOff()
        {
            bool triggers = true;
            var h = new History(() => triggers);
            for (int t = 1; t <= 6; t++)
            {
                h.Stamp(t, Stand(10 + t, 10));
                h.Keeper.Ran(t, t >= 4 ? t - 3 : 0);
            }
            var kept = h.Ticks;
            Assert.That(kept, Is.EqualTo(new[] { 1, 3, 4, 5, 6 }), "3 stood when 4 fired, 4 and 5 fired; 6 hasn't run");
            triggers = false;
            h.Stamp(7, Stand(20, 10));
            h.Keeper.Ran(7, 3);
            h.Stamp(8, Stand(21, 10));
            Assert.That(h.Ticks, Is.EqualTo(kept.Append(7).Append(8)), "off: only the stands since the trigger went off may go");
        }

        [Test]
        public void AStandTheHoldReadsAndTheOneThatLeavesItStay()
        {
            const int plate = 5;
            var h = new History(keeps: (x, y) => x == plate);
            var walk = new[] { 1, 2, plate, 8, 9, 10, 11 };
            for (int t = 1; t <= walk.Length; t++)
            {
                h.Stamp(t, Stand(walk[t - 1], 1));
                h.Keeper.Ran(t, 0);
            }
            var kept = h.Kept.Select(s => s.X).ToList();
            Assert.That(kept, Is.EqualTo(new[] { 1, 2, plate, 8, 10, 11 }), "the stand off it says when it was left; 9 goes, 10 until 11 has run");
        }
    }
}
