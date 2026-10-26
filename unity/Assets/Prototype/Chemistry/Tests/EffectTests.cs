using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Physics props' effects on cells as lockstep inputs (Effects.cs): stamped into a tick batch
    /// next to the casts, applied at that tick, and replayed by a late joiner from the history.
    /// </summary>
    public class EffectTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A grass field with dirt paths, an oil patch at (10..12, 4..6) and a puddle.</summary>
        static World Field()
        {
            var w = new World(R, 30, 12, 7, "grass");
            for (int x = 0; x < w.W; x += 6)
                for (int y = 0; y < w.H; y++) w.Paint(x, y, "dirt");
            for (int y = 4; y <= 6; y++)
                for (int x = 10; x <= 12; x++) w.SetCover(x, y, "oil");
            w.AddWater(20, 3, 3);
            return w;
        }

        // A host's session: a cast at tick 2, a barrel blowing up in the oil at 6, a burning crate
        // at 9 and its embers at 15, a lantern smashing at 20.
        static List<TickBatch> HostSession(int ticks)
        {
            var stamped = new List<TickBatch>();
            for (int t = 1; t <= ticks; t++)
            {
                var b = new TickBatch(t);
                if (t == 2) b.Casts.Add(new CastCommand("ray-of-frost", 20, 3, 1));
                if (t == 6) b.Effects.Add(new EffectCommand(EffectKind.Explode, 11, 5, "barrel"));
                if (t == 9) b.Effects.Add(new EffectCommand(EffectKind.Burn, 3, 9, "crate"));
                if (t == 15) b.Effects.Add(new EffectCommand(EffectKind.Embers, 3, 9, "crate"));
                if (t == 20) b.Effects.Add(new EffectCommand(EffectKind.Spill, 25, 2, "bullseye-lantern"));
                stamped.Add(b);
            }
            return stamped;
        }

        static World Run(IEnumerable<TickBatch> batches)
        {
            var world = Field();
            var rules = RuleSet.Plain(R);
            foreach (var b in batches) rules = b.RunOn(world, rules, out _);
            return world;
        }

        [Test]
        public void ALateJoinerReplayingTheHistoryWithPropEffectsMatchesTheLiveWorld()
        {
            const int ticks = 45;
            var live = HostSession(ticks);
            var host = Run(live);

            // What the host sends a late joiner: every cast and every effect with its tick.
            var casts = live.SelectMany(b => b.Casts.Select(c => (b.Tick, c))).ToList();
            var effects = live.SelectMany(b => b.Effects.Select(e => (b.Tick, e))).ToList();
            Assert.That(effects, Has.Count.EqualTo(4));
            var timeline = TickBatch.Timeline(casts, null, ticks, effects);
            var joiner = Run(Enumerable.Range(1, ticks).Select(t => timeline[t]));

            Assert.That(joiner.Tick, Is.EqualTo(host.Tick));
            Assert.That(joiner.CellHash(), Is.EqualTo(host.CellHash()));

            var castsOnly = Run(TickBatch.Timeline(casts, null, ticks).OrderBy(p => p.Key).Select(p => p.Value));
            Assert.That(castsOnly.CellHash(), Is.Not.EqualTo(host.CellHash()), "the effects made a difference");
        }

        [Test]
        public void AnEffectTakesHoldAtItsTickAndNotBefore()
        {
            var session = HostSession(10);
            var plain = session.Select(b => new TickBatch(b.Tick, b.Casts)).ToList();
            Assert.That(Run(session.Take(5)).CellHash(), Is.EqualTo(Run(plain.Take(5)).CellHash()), "identical before the blast");

            var after = Run(session.Take(6));
            // The barrel's blast: radius 1 of burning oil around (11, 5), fire at least 3 on the batch's tick.
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    Assert.That(after.At(11 + dx, 5 + dy).Fire, Is.GreaterThan(0), $"({11 + dx}, {5 + dy}) burns");
            Assert.That(after.Log.Any(l => l.Text.StartsWith("KABOOM!", System.StringComparison.Ordinal)), "logged on every machine alike");
        }

        [Test]
        public void TheSameEffectsInTheSameOrderGiveTheSameWorld()
        {
            // Two batches with the same effects stamped in a different order differ only if the effects
            // interact; the host's order is the one sent, so everyone applies them in that order.
            var a = new TickBatch(1, null, null, new[]
            {
                new EffectCommand(EffectKind.Explode, 11, 5, "barrel"),
                new EffectCommand(EffectKind.Spill, 12, 5, "bullseye-lantern"),
            });
            var b = new TickBatch(1, null, null, a.Effects);
            Assert.That(Run(new[] { a }).CellHash(), Is.EqualTo(Run(new[] { b }).CellHash()));
        }

        [Test]
        public void AnEffectOffTheMapOrForAnUnknownThingChangesNothing()
        {
            var before = Field();
            var world = Field();
            world.ApplyEffect(new EffectCommand(EffectKind.Explode, -1, 3, "barrel"));
            world.ApplyEffect(new EffectCommand(EffectKind.Burn, 99, 3, "crate"));
            world.ApplyEffect(new EffectCommand(EffectKind.Explode, 4, 4, "no-such-entity"));
            world.ApplyEffect(new EffectCommand(EffectKind.Spill, 4, 4, "no-such-item"));
            world.ApplyEffect(new EffectCommand(EffectKind.Embers, 4, 4, "no-such-entity"));
            Assert.That(world.CellHash(), Is.EqualTo(before.CellHash()));
        }

        [Test]
        public void ApplyingAnEffectIsWhatTheEntityCodeDoes()
        {
            var direct = Field();
            direct.Explode(11, 5, R.Entity("barrel"));
            direct.Spill(25, 2, R.Item("bullseye-lantern"));
            direct.LeaveEmbers(3, 9, R.Entity("crate"));
            var viaEffects = Field();
            viaEffects.ApplyEffect(new EffectCommand(EffectKind.Explode, 11, 5, "barrel"));
            viaEffects.ApplyEffect(new EffectCommand(EffectKind.Spill, 25, 2, "bullseye-lantern"));
            viaEffects.ApplyEffect(new EffectCommand(EffectKind.Embers, 3, 9, "crate"));
            Assert.That(viaEffects.CellHash(), Is.EqualTo(direct.CellHash()));
        }
    }
}
