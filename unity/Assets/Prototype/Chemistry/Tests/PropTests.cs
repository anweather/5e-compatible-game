using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The rules side of the Unity physics props: entities.json, a barrel's blast
    /// (World.Explode, a port of explode's cell part) and PropState's ticks.
    /// </summary>
    public class PropTests
    {
        static Rules R => ChemistryTests.R;

        static EntityDef Crate => R.Entity("crate");
        static EntityDef Barrel => R.Entity("barrel");
        static EntityDef Boulder => R.Entity("boulder");

        /// <summary>A 7x7 dirt room ringed by bedrock.</summary>
        static World Room() => ChemistryTests.Room(7, 7);

        static int Burning(World w) => w.Cells.Count(c => c.Fire > 0);

        [Test]
        public void EntitiesLoadFromRules()
        {
            Assert.That(Crate.Hp, Is.EqualTo(6));
            Assert.That(Crate.Flammable);
            Assert.That(Crate.Explodes, Is.Null);
            Assert.That(Boulder.Mass, Is.EqualTo(3));
            Assert.That(R.Entity("wizard").Player);
            Assert.That(R.Entity("nope"), Is.Null);
            var blast = Barrel.Explodes;
            Assert.That(blast.Cover, Is.EqualTo("oil"));
            Assert.That((blast.Radius, blast.Fire, blast.Push), Is.EqualTo((1, 3, 2)));
            Assert.That(blast.Damage, Is.EqualTo(new[] { 2, 6 }));
            Assert.That(R.Sim.BurnDamageEvery, Is.EqualTo(3));
        }

        [Test]
        public void BarrelBlastStampsBurningOilInItsRadius()
        {
            var w = Room();
            int stamped = w.Explode(3, 3, Barrel);
            Assert.That(stamped, Is.EqualTo(9));
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var c = w.At(3 + dx, 3 + dy);
                    Assert.That(c.Cover, Is.EqualTo("oil"));
                    Assert.That(c.Fire, Is.GreaterThanOrEqualTo(3));
                    Assert.That(c.Fuel, Is.GreaterThan(0));
                }
            Assert.That(Burning(w), Is.EqualTo(9));
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("KABOOM! The oil barrel explodes"));
        }

        [Test]
        public void BlastSkipsWallsAndHarmlessThings()
        {
            var w = Room();
            Assert.That(w.Explode(1, 1, Barrel), Is.EqualTo(4), "a corner: five of its nine cells are bedrock");
            Assert.That(w.At(0, 0).Cover, Is.Null);
            Assert.That(Room().Explode(3, 3, Crate), Is.EqualTo(0), "crates don't explode");
        }

        [Test]
        public void CratesAndBarrelsFloatBouldersSink()
        {
            Assert.That(new PropState(Crate).Floats);
            Assert.That(new PropState(Barrel).Floats);
            Assert.That(new PropState(Boulder).Floats, Is.False);
            Assert.That(new PropState(Boulder).Lift, Is.GreaterThan(0), "water still slows a sinking boulder");
        }

        [Test]
        public void SubmergedIsTheShareBelowTheSurface()
        {
            Assert.That(PropState.Submerged(-0.5, 1, 0), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(PropState.Submerged(0.2, 1, 0), Is.EqualTo(0));
            Assert.That(PropState.Submerged(-2, 1, 0), Is.EqualTo(1));
            Assert.That(PropState.Submerged(0, 0, 1), Is.EqualTo(0));
        }

        [Test]
        public void LevitateLastsItsDurationThenLandsOnce()
        {
            var crate = new PropState(Crate);
            var ground = new Cell("dirt");
            crate.Levitate(10, 48);
            Assert.That(crate.Levitating(57));
            Assert.That(crate.Levitating(58), Is.False);
            Assert.That(crate.Step(ground, 57, 3), Is.EqualTo(PropEvents.None));
            Assert.That(crate.Step(ground, 58, 3), Is.EqualTo(PropEvents.Landed));
            Assert.That(crate.Step(ground, 59, 3), Is.EqualTo(PropEvents.None));
        }

        [Test]
        public void ATimberCrateCatchesSlowlyThenBurnsDown()
        {
            // A crate is timber (entities.json material): it catches after 1/flammability ticks in fire
            // (0.15: seven) and burns as long as timber's fuel.
            var crate = new PropState(Crate);
            var fire = new Cell("dirt") { Fire = 2 };
            for (int t = 1; t < 7; t++) Assert.That(crate.Step(fire, t, 3), Is.EqualTo(PropEvents.None), $"not yet at tick {t}");
            Assert.That(crate.Step(fire, 7, 3) & PropEvents.Ignited, Is.EqualTo(PropEvents.Ignited));
            Assert.That(crate.BurnTime, Is.EqualTo(Crate.MaterialDef.Fuel));
            int burns = 0, tick = 7;
            while (!crate.Destroyed && tick < 100)
            {
                var events = crate.Step(fire, ++tick, 3);
                if ((events & PropEvents.Burned) != 0) burns++;
            }
            Assert.That(tick, Is.EqualTo(24), "6 HP, 1 damage every 3rd tick from the 9th");
            Assert.That(burns, Is.EqualTo(6));
        }

        [Test]
        public void ABurningCrateKeepsBurningAwayFromTheFireUntilWaterDousesIt()
        {
            var crate = new PropState(Crate);
            for (int t = 1; t <= 7; t++) crate.Step(new Cell("dirt") { Fire = 1 }, t, 3);
            crate.Step(new Cell("dirt"), 8, 3);
            Assert.That(crate.Burning, "burning lasts its material's fuel after the fire");
            var pond = new Cell("pit") { Water = 4 };
            Assert.That(crate.Step(pond, 10, 3) & PropEvents.Doused, Is.EqualTo(PropEvents.Doused));
            Assert.That(crate.Burning, Is.False);
            Assert.That(crate.Hp, Is.EqualTo(6));
        }

        [Test]
        public void BouldersNeitherBurnNorBreak()
        {
            var boulder = new PropState(Boulder);
            Assert.That(boulder.Step(new Cell("dirt") { Fire = 3 }, 1, 3), Is.EqualTo(PropEvents.None));
            Assert.That(boulder.Damage(500), Is.False);
            Assert.That(boulder.Hp, Is.EqualTo(999));
        }

        [Test]
        public void ALevitatingPropFloatsAboveTheFire()
        {
            var barrel = new PropState(Barrel);
            barrel.Levitate(0, 10);
            Assert.That(barrel.Step(new Cell("dirt") { Fire = 3 }, 1, 3), Is.EqualTo(PropEvents.None));
            Assert.That(barrel.Burning, Is.False);
        }
    }
}
