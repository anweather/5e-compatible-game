using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The air spells on raiders (D16, docs/design/unified-casting.md; the 2026-11-06 playtest: Gust "should push raiders back,
    /// and could knock them off the cliff, with diminishing returns", and Whirlwind must do something): World.CastShove and
    /// World.LiftInTheEye, in the core, so every peer moves the raider alike.
    /// </summary>
    [Category("Simulation")]
    public class AirTests
    {
        static Rules R => ChemistryTests.R;

        [Test]
        public void AGustShovesFurtherHeldStaggersAndWearsOff()
        {
            var w = new World(R, 40, 8, 5, "dirt");
            var hound = w.SpawnRaider("bone-hound", 5, 3, 6);
            w.Cast("gust-of-wind", 2, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((9, 3)), "a tap: force.push 2 a charge level and one more, so 4");
            Assert.That(hound.StatusOf(World.ThrownStatus), Is.EqualTo(R.Sim.TicksPerSecond), "it staggers a second");
            Assert.That(hound.StatusOf(World.ShovedStatus), Is.EqualTo(1));

            var spider = w.SpawnRaider("giant-spider", 5, 5, 12);
            w.Cast("gust-of-wind", 2, 5, 1, 3);
            Assert.That((spider.X, spider.Y), Is.EqualTo((13, 5)), "held to the most: 8");

            w.Cast("gust-of-wind", 8, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((11, 3)), "the second shove goes half as far");
            w.Cast("gust-of-wind", 10, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((11, 3)), "from the third it braces");
            for (int i = 0; i < 60; i++) w.Step();
            Assert.That(hound.StatusOf(World.ShovedStatus), Is.EqualTo(2), "the shoves it felt never wear off");
            Assert.That($"{w.CellHash()} {w.RngState} {hound.X},{spider.X}", Is.EqualTo("a93553c5 5 11,13"), "the golden");
        }

        [Test]
        public void AGustThrowsARaiderOverTheEdge()
        {
            // A ledge: the land drops 6 steps (3 m) past x 8, so a shove from the top takes the hound over and the fall hurts it.
            var w = new World(R, 30, 8, 5, "dirt");
            var land = new int[30 * 8];
            int step = R.Sim.WaterUnitsPerStep;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 30; x++) land[y * 30 + x] = x <= 8 ? 6 * step : 0;
            w.SetLand(land);
            var hound = w.SpawnRaider("bone-hound", 7, 3, 20);
            w.Cast("gust-of-wind", 4, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((9, 3)), "over the edge, where the shove ends");
            Assert.That(hound.Hp, Is.EqualTo(20 - R.Sim.FallDamage * 3), "FallDamage for every 2 steps it fell");
        }

        [Test]
        public void AWhirlwindLiftsARaiderAndFlingsItOut()
        {
            var w = new World(R, 40, 12, 5, "dirt");
            var hound = w.SpawnRaider("bone-hound", 11, 5, 30);
            w.Cast("whirlwind", 10, 5, 1);
            bool lifted = false;
            int most = 0;
            for (int i = 0; i < 30; i++)
            {
                w.Step();
                lifted |= hound.StatusOf(World.LiftedStatus) > 0;
                most = System.Math.Max(most, hound.X);
            }
            Assert.That(lifted, "pulled into the eye and lifted off its feet");
            Assert.That(most, Is.GreaterThanOrEqualTo(15), "flung out east, the way it was cast");
            Assert.That(hound.Hp, Is.EqualTo(30 - R.Sim.FallDamage * 2), "hurt by the landing");
            Assert.That($"{w.CellHash()} {w.RngState} {hound.X},{hound.Y}", Is.EqualTo("7f7b6ec5 5 17,5"), "the golden");
        }

        [Test]
        public void TheShoveAndTheLiftAreTheRules()
        {
            var mod = ModPatch.Parse("{ \"sim\": { \"shove\": { \"most\": 1, \"staggerTicks\": 2 } }, \"spells\": [{ \"id\": \"whirlwind\", \"lift\": { \"hurt\": 3 } }] }");
            var rules = R.WithMods(new[] { mod });

            var w = new World(rules, 40, 8, 5, "dirt");
            var hound = w.SpawnRaider("bone-hound", 5, 3, 6);
            w.Cast("gust-of-wind", 2, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((9, 3)));
            Assert.That(hound.StatusOf(World.ThrownStatus), Is.EqualTo(2), "shove.staggerTicks");
            w.Cast("gust-of-wind", 8, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((9, 3)), "shove.most 1: it braces from the second");

            var lifted = new World(rules, 40, 12, 5, "dirt");
            var other = lifted.SpawnRaider("bone-hound", 11, 5, 30);
            lifted.Cast("whirlwind", 10, 5, 1);
            for (int i = 0; i < 30; i++) lifted.Step();
            Assert.That(other.Hp, Is.EqualTo(30 - R.Sim.FallDamage * 3), "lift.hurt");
        }

        [Test]
        public void FromUpOnAWallALineGoesOverItAndFromTheGroundTheWallStopsIt()
        {
            // A stone wall three cells thick (x 4 to 6). Cast from its top (the caster's cell, x 4, is the wall's), a Gust goes
            // over the rest of it and knocks the hound beyond back; cast from the ground before it, the wall stops the line.
            var w = new World(R, 30, 8, 5, "dirt");
            for (int x = 4; x <= 6; x++) w.At(x, 3).T = "stone";
            Assert.That(w.IsBlocking(w.At(5, 3)), "stone blocks");
            var hound = w.SpawnRaider("bone-hound", 9, 3, 30);
            Assert.That(w.SpellCells(R.Spell("gust-of-wind"), 3, 3, 1), Has.Count.EqualTo(2), "from the ground: up to the wall's first cell");
            w.Cast("gust-of-wind", 3, 3, 1);
            Assert.That(hound.X, Is.EqualTo(9), "the wall stopped it");
            var over = w.SpellCells(R.Spell("gust-of-wind"), 5, 3, 1);
            Assert.That(over[0], Is.EqualTo((7, 3)), "from the top: it starts past the wall and doesn't touch it");
            w.Cast("gust-of-wind", 5, 3, 1);
            Assert.That(hound.X, Is.EqualTo(13), "knocked back from the wall top");
            var slant = w.SpellCells(R.Spell("lightning-bolt"), 5, 3, 1, 1, 16, 4);
            Assert.That(slant.TrueForAll(c => !w.IsBlocking(w.At(c.x, c.y))) && slant.Count > 10, "a slanted bolt goes over it too");
        }

        [Test]
        public void ALineReachesAFlyerOverWhatStopsIt()
        {
            // A tree across the bolt's way: on the ground it stops the line, but the ash wing flies over it, and the bolt reaches it.
            var w = HoldTests.Field();
            var at = (x: w.HoldSite.Heart.x - 30, y: w.HoldSite.Heart.y - 30);
            w.Paint(at.x + 4, at.y, "tree");
            Assert.That(w.IsBlocking(w.At(at.x + 4, at.y)));
            var flyer = w.SpawnRaider("ash-wing", at.x + 8, at.y, 200);
            var walker = w.SpawnRaider("bone-hound", at.x + 9, at.y, 200);
            Assert.That(w.Flying(flyer) && !w.Flying(walker));
            w.Cast("lightning-bolt", at.x + 1, at.y, 1);
            Assert.That(flyer.Hp, Is.LessThan(200), "struck over the tree");
            Assert.That(walker.Hp, Is.EqualTo(200), "the one on the ground is behind it");
            var beside = w.SpawnRaider("ash-wing", at.x + 10, at.y + 1, 200);
            w.Cast("lightning-bolt", at.x + 1, at.y, 1);
            Assert.That(beside.Hp, Is.LessThan(200), "it arcs to a flyer a cell beside its way");
        }
    }
}
