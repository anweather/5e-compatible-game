// A big spell for every element (J-078; docs/design/casting-pace-options.md "A Meteor for every element"): each element's
// same-element throw is its wow spell, costing what Meteor does, so Meteor is one of four rather than weaker. Tornado (air+air
// throw), Thunder Strike (fire+air thrust, in place of Falling Star), Earth Spires (earth+earth throw, in place of Stone Ramp)
// and Iceberg (water+water throw, in place of Flood). Falling Star, Stone Ramp and Flood stay in spells.json, cast by id.
using System.Linq;
using NUnit.Framework;
using static Chem.HandsDef;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class BigSpellTests
    {
        static Rules R => ChemistryTests.R;
        static HandsDef T => R.Sim.Hold.Hands;

        static void Run(World w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        [TestCase("fire", "fire", Throw, "meteor")]
        [TestCase("air", "air", Throw, "tornado")]
        [TestCase("fire", "air", Thrust, "thunder-strike")]
        [TestCase("earth", "earth", Throw, "earth-spires")]
        [TestCase("water", "water", Throw, "iceberg")]
        public void EachCostsWhatMeteorDoes(string a, string b, int gesture, string spell)
        {
            var row = T.Both(a, b, gesture);
            var meteor = T.Both("fire", "fire", Throw);
            Assert.That(row.Spell, Is.EqualTo(spell));
            Assert.That((row.CastTicks, row.Cooldown), Is.EqualTo((meteor.CastTicks, meteor.Cooldown)), "its cast and its rest");
            Assert.That(R.Spell(spell).Level, Is.EqualTo(R.Spell("meteor").Level));
            if (spell != "meteor") Assert.That(R.Spell(spell).Radial, Is.False, "a merge, off the radial");
        }

        [Test]
        public void TheSpellsTheyReplaceStayCastable()
        {
            foreach (var id in new[] { "falling-star", "stone-ramp", "flood" })
            {
                Assert.That(R.Spell(id), Is.Not.Null, id);
                Assert.That(T.Find(id).slot, Is.Null, $"{id} is off the hands");
            }
        }

        // Each one's damage per raider and reach on spell-balance's packed field, against raiders neutral to its element (a
        // giant spider is weak to lightning, a bone hound to fire).
        static int[] Dealt(string spell) =>
            SpellBalanceTests.Dealt(spell, spell == "thunder-strike" ? "bone-hound" : "giant-spider");

        [TestCase("tornado")]
        [TestCase("thunder-strike")]
        public void TheWideOnesDealAboutWhatMeteorDoes(string spell)
        {
            int[] meteor = Dealt("meteor"), big = Dealt(spell);
            Assert.That(big.Sum(), Is.InRange(meteor.Sum() / 2, meteor.Sum() * 3 / 2), "all told, within half again of Meteor's");
            Assert.That(big.Average(), Is.GreaterThan(meteor.Average() * 0.7), "a hit");
            Assert.That(big.Length, Is.GreaterThan(meteor.Length / 2), "the raiders it reaches");
        }

        [TestCase("earth-spires")]
        [TestCase("iceberg")]
        public void TheShapingOnesHitHardButNarrowerAndLeaveTheField(string spell)
        {
            int[] meteor = Dealt("meteor"), big = Dealt(spell), fireball = Dealt("fireball");
            Assert.That(big.Average(), Is.GreaterThan(meteor.Average() * 0.9), "each raider it reaches takes about a Meteor's hit");
            Assert.That(big.Length, Is.LessThan(meteor.Length), "fewer than Meteor reaches: the rest is in what it leaves");
            Assert.That(big.Sum(), Is.GreaterThan(fireball.Sum()), "still far more than a Fireball");
        }

        [Test]
        public void TheNumbersArePinned()
        {
            var lines = new[] { "meteor", "tornado", "thunder-strike", "earth-spires", "iceberg" }
                .Select(s => { var d = Dealt(s); return $"{s} {d.Length} {d.Sum()}"; });
            Assert.That(lines, Is.EqualTo(new[] { "meteor 81 3162", "tornado 104 3042", "thunder-strike 49 2138", "earth-spires 11 525", "iceberg 40 1503" }));
        }

        [Test]
        public void ATornadoIsAWhirlwindGrownHugeAndLasting()
        {
            var tornado = R.Spell("tornado");
            var whirlwind = R.Spell("whirlwind");
            Assert.That(tornado.Radius, Is.GreaterThan(whirlwind.Radius));
            Assert.That(tornado.Duration, Is.GreaterThanOrEqualTo(whirlwind.Duration * 3));
            Assert.That(tornado.Eye, Is.GreaterThan(whirlwind.Eye));
            Assert.That(tornado.Carries.Select(c => c.Id), Is.EqualTo(whirlwind.Carries.Select(c => c.Id)), "it takes on what a whirlwind does");
        }

        [Test]
        public void ATornadoLiftsARaiderAndFlingsItFar()
        {
            var w = new World(R, 50, 14, 5, "dirt");
            var hound = w.SpawnRaider("bone-hound", 12, 6, 100);
            w.Cast("tornado", 10, 6, 1);
            bool lifted = false;
            int most = 0;
            for (int i = 0; i < 40; i++)
            {
                w.Step();
                lifted |= hound.StatusOf(World.LiftedStatus) > 0;
                most = System.Math.Max(most, hound.X);
            }
            Assert.That(lifted, "lifted in its eye, two cells out");
            Assert.That(most, Is.GreaterThanOrEqualTo(20), "drawn in a cell, then flung ten east, the way it was cast");
            Assert.That(hound.Hp, Is.LessThan(100 - R.Sim.FallDamage * 4 + 1), "hurt as it lands, and by the funnel");
            Assert.That($"{w.CellHash()} {w.RngState} {hound.X},{hound.Y} {hound.Hp}", Is.EqualTo("a6cb40a5 3031295961 21,6 68"), "the golden");
        }

        [Test]
        public void OverTheLakeATornadoIsAWaterspout()
        {
            var w = new World(R, 30, 14, 5, "dirt");
            for (int y = 3; y <= 9; y++)
                for (int x = 8; x <= 14; x++) w.At(x, y).Water = 8;
            w.Cast("tornado", 11, 6, 1);
            Run(w, 3);
            Assert.That(w.Whirls.Single().Carrying, Is.EqualTo("water"));
        }

        [Test]
        public void AThunderStrikeMarksItsTargetThenStrikes()
        {
            var w = new World(R, 21, 15, 6, "dirt");
            w.At(10, 7).Gas = "cloud";
            w.At(10, 7).GasAmt = 4;
            w.At(12, 7).Gas = "smoke";
            w.At(12, 7).GasAmt = 4;
            w.At(9, 8).T = "sand";
            w.At(8, 7).Water = 2;
            w.At(8, 7).Frozen = true;
            var wet = w.SpawnRaider("goblin", 11, 8, 200);
            var dry = w.SpawnRaider("goblin", 9, 6, 200);
            wet.Status["wet"] = 60;
            w.Cast("thunder-strike", 10, 7, 1);
            Assert.That(w.At(10, 7).Star, Is.EqualTo(1), "the sky marks it");
            Assert.That(w.At(13, 7).Star, Is.EqualTo(1), "35 ft across");
            Assert.That(w.At(14, 7).Star, Is.EqualTo(0));
            Run(w, 2);
            Assert.That(dry.Hp, Is.EqualTo(200), "still coming");
            w.Step();
            Assert.That(w.Stars, Is.Empty);
            Assert.That(w.At(10, 7).Gas, Is.EqualTo("storm"), "the rain cloud charged into a storm, and the clap leaves it");
            Assert.That(w.At(12, 7).Gas, Is.Null, "the clap scatters the smoke");
            Assert.That(w.At(9, 8).T, Is.EqualTo("glass"), "sand fused");
            Assert.That(w.At(8, 7).Cracked, Is.GreaterThan(0), "the ice cracks, to shatter at the next hit");
            Assert.That(200 - wet.Hp, Is.GreaterThan(200 - dry.Hp), "a wet raider takes it twice as hard");
            Assert.That(dry.Hp, Is.LessThan(200));
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState} {wet.Hp} {dry.Hp}", Is.EqualTo("c5a93eab 2064601707 112 140"), "the golden");
        }

        [Test]
        public void AThunderclapStillScattersVapourButNotAStorm()
        {
            var w = new World(R, 9, 9, 6, "dirt");
            w.At(4, 4).Gas = "storm";
            w.At(4, 4).GasAmt = 4;
            w.At(5, 4).Gas = "steam";
            w.At(5, 4).GasAmt = 4;
            w.Apply(4, 4, "thunder");
            w.Apply(5, 4, "thunder");
            Assert.That((w.At(4, 4).Gas, w.At(5, 4).Gas), Is.EqualTo(("storm", (string)null)));
        }

        [Test]
        public void EarthSpiresZigzagTowardTheTarget()
        {
            var w = new World(R, 24, 9, 6, "dirt");
            w.Cast("earth-spires", 2, 4, 1);
            var pillars = w.Cells.Select((c, i) => (c, i)).Where(p => p.c.T == "pillar").Select(p => (p.i % 24, p.i / 24)).ToList();
            int[] side = { 0, 1, 0, -1 };
            var want = Enumerable.Range(0, 12).Select(k => (2 + k, 4 + side[k % 4])).ToList();
            Assert.That(pillars, Is.EquivalentTo(want), "a cell on, a cell off to the south, on, off to the north");
            Assert.That(w.At(13, 4).T, Is.EqualTo("dirt"), "gaps between: cover, not a wall");
        }

        [Test]
        public void ASpireThrowsTheRaiderOnItAndAnEarthSpellBreaksThem()
        {
            var w = new World(R, 24, 9, 6, "dirt");
            var r = w.SpawnRaider("goblin", 4, 4, 200);
            w.Cast("earth-spires", 2, 4, 1);
            Assert.That(w.At(4, 4).T, Is.EqualTo("dirt"), "no pillar under the raider");
            Assert.That(r.StatusOf(World.ThrownStatus), Is.EqualTo(12), "thrown up, off its feet for two seconds");
            Assert.That(r.Hp, Is.LessThan(200));
            Assert.That(w.At(6, 4).T, Is.EqualTo("pillar"));
            w.Cast("earth-spires", 6, 4, 1);
            Assert.That(w.At(6, 4).T, Is.EqualTo("rubble"), "earth breaks the first spire it meets");
            Run(w, 12);
            Assert.That($"{w.CellHash()} {w.RngState} {r.Hp}", Is.EqualTo("ed6eb77a 1135788952 161"), "the golden");
        }

        [Test]
        public void AnIcebergRisesCrashesOnAndLeavesWalkableIce()
        {
            var w = new World(R, 30, 11, 6, "dirt");
            for (int y = 2; y <= 8; y++)
                for (int x = 14; x <= 18; x++) w.At(x, y).Water = 6;
            var hound = w.SpawnRaider("bone-hound", 13, 5, 200);
            w.Cast("iceberg", 8, 5, 1);
            Assert.That(w.At(8, 5).Water > 0 && w.At(8, 5).Frozen, "it rises out of a pool it makes, frozen");
            Assert.That(w.At(8, 3).Frozen && w.At(8, 7).Frozen, "25 ft wide");
            Run(w, 16);
            Assert.That(w.Waves, Is.Empty, "rolled its 40 ft");
            Assert.That(Enumerable.Range(8, 8).All(x => w.At(x, 5).Frozen), "walkable ice all the way");
            Assert.That(w.At(16, 5).Frozen, Is.False, "past its reach the lake is open");
            Assert.That(w.At(14, 3).Frozen, "the lake it crashed into freezes over");
            Assert.That(hound.Hp, Is.LessThan(200), "crushed");
            Assert.That(hound.StatusOf("frozen"), Is.GreaterThan(0), "and frozen solid");
            Assert.That($"{w.CellHash()} {w.RngState} {hound.Hp}", Is.EqualTo("ae98df8f 1767624622 171"), "the golden");
        }

        [Test]
        public void FireMeltsAnIcebergIntoAFlood()
        {
            var w = new World(R, 30, 11, 6, "dirt");
            w.Cast("iceberg", 8, 5, 1);
            Run(w, 16);
            int ice = w.Cells.Count(c => c.Frozen);
            Assert.That(ice, Is.GreaterThanOrEqualTo(40));
            w.Cast("fireball", 11, 5, 1);
            Assert.That(w.At(11, 5).Frozen, Is.False);
            Assert.That(w.At(11, 5).Water, Is.GreaterThan(0), "its meltwater runs out");
        }
    }
}
