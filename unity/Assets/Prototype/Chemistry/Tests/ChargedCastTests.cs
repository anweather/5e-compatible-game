// Holding the last element to charge a cast by elements (docs/SPELL_COMBOS.md).
// The goldens are shared with sandbox/test/charged.test.ts.
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class ChargedCastTests
    {
        static Rules rules;

        static Rules Base => rules ??= Rules.Load(ChemistryTests.RulesDir());

        static bool Hold(World w, string element) => w.ApplyCombo(ComboCommand.Load("ann", element, true));
        static bool Release(World w, int charge) => w.ApplyCombo(ComboCommand.Release("ann", charge));

        static void Run(World w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        // Steps until a cast goes off, and says how many ticks that took.
        static int UntilFired(World w)
        {
            for (int t = 1; t <= 60; t++)
            {
                w.Step();
                if (w.CombosFired.Count > 0) return t;
            }
            Assert.Fail("nothing went off");
            return 0;
        }

        // Holds a combo's elements, then lets go at this charge once the meter has run out.
        static ComboFired Charged(World w, string combo, int charge)
        {
            foreach (var element in combo.Split('+')) Hold(w, element);
            Run(w, 20);
            Release(w, charge);
            UntilFired(w);
            return w.CombosFired[0];
        }

        [TestCase(0.0, 3, 1)]
        [TestCase(0.44, 3, 1)]
        [TestCase(0.45, 3, 2)]
        [TestCase(0.89, 3, 2)]
        [TestCase(0.9, 3, 3)]
        [TestCase(30.0, 3, 3)]
        [TestCase(30.0, 2, 2)]
        [TestCase(30.0, 1, 1)]
        public void TheChargeComesFromHowLongTheButtonIsHeld(double seconds, int max, int charge) =>
            Assert.That(ElementHold.ChargeOf(seconds, max), Is.EqualTo(charge));

        [Test]
        public void TheClockRunsFromTheLastElementPressed()
        {
            var hold = new ElementHold();
            Assert.That((hold.Holding, hold.ChargeAt(5, 3)), Is.EqualTo((false, 1)));
            hold.Press(0, 10);
            Assert.That(hold.ChargeAt(10.5, 3), Is.EqualTo(2));
            hold.Press(2, 10.5);
            Assert.That((hold.Button, hold.ChargeAt(10.6, 3)), Is.EqualTo((2, 1)), "a second element starts the hold again");
            hold.Drop();
            Assert.That((hold.Holding, hold.ChargeAt(20, 3)), Is.EqualTo((false, 1)));
        }

        [Test]
        public void TheHeldVariantIsOnlyAtFullCharge()
        {
            var wall = Base.Combos.Combo("earth+earth");
            Assert.That(wall.SpellAt(1, 3), Is.EqualTo("wall-of-stone"));
            Assert.That(wall.SpellAt(2, 3), Is.EqualTo("wall-of-stone"));
            Assert.That(wall.SpellAt(3, 3), Is.EqualTo("stone-ramp"));
            Assert.That(wall.SpellAt(1, 1), Is.EqualTo("wall-of-stone"), "a spell that can't charge has no held form");
            var fireball = Base.Combos.Combo("fire+fire");
            Assert.That(fireball.SpellAt(3, 3), Is.EqualTo("fireball"), "and most combos have none");
        }

        [Test]
        public void EveryComboOnARealSpellChargesToTheMax()
        {
            var w = new World(Base, 8, 8, 7);
            Assert.That(Base.Combos.All.Select(w.MaxChargeOf), Is.All.EqualTo(World.MaxCharge));
            Assert.That(World.Chargeable(null), Is.False);
            Assert.That(w.MaxChargeOf(null), Is.EqualTo(1));
        }

        [Test]
        public void ATapGoesOffWhenATapAlwaysDid()
        {
            var plain = new World(Base, 8, 8, 7);
            plain.ApplyCombo(ComboCommand.Load("ann", "fire"));
            int expected = UntilFired(plain);

            var tapped = new World(Base, 8, 8, 7);
            Hold(tapped, "fire");
            tapped.Step();
            Assert.That(Release(tapped, 1));
            Assert.That(UntilFired(tapped), Is.EqualTo(expected - 1));
            Assert.That(tapped.CombosFired[0].Charge, Is.EqualTo(1));
            Assert.That(tapped.CombosFired[0].Spell, Is.EqualTo("fire-bolt"));
            Assert.That(tapped.CastState(), Is.EqualTo(plain.CastState()), "a tap leaves nothing behind");
        }

        [Test]
        public void AHeldCastWaitsAtTheEndOfItsMeterForTheRelease()
        {
            var w = new World(Base, 8, 8, 7);
            Hold(w, "fire");
            Run(w, 40);
            Assert.That(w.CombosFired, Is.Empty);
            var c = w.CastOf("ann");
            Assert.That((c.Casting, c.Meter, c.Holding), Is.EqualTo((true, 1, true)));
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(Base.Combos.MoveFactor), "still slowed while held");
            Release(w, 2);
            w.Step();
            Assert.That(w.CombosFired.Single().Charge, Is.EqualTo(2));
            Assert.That(c.Charge, Is.EqualTo(1), "and it's spent");
        }

        [Test]
        public void ReleasedEarlyTheChargeWaitsForTheMeter()
        {
            var w = new World(Base, 8, 8, 7);
            Hold(w, "fire");
            w.Step();
            Release(w, 3);
            Assert.That(UntilFired(w), Is.EqualTo(Base.Combos.Combo("fire").CastTicks - 1));
            Assert.That(w.CombosFired[0].Charge, Is.EqualTo(3));
        }

        [Test]
        public void AReleaseIsOnlyTakenForAHoldAndKeptInRange()
        {
            var w = new World(Base, 8, 8, 7);
            Assert.That(Release(w, 2), Is.False, "nothing held");
            Hold(w, "fire");
            Assert.That(Release(w, 99));
            Assert.That(w.CastOf("ann").Charge, Is.EqualTo(World.MaxCharge));
            Assert.That(Release(w, 1), Is.False, "let go once");
            w.ApplyCombo(ComboCommand.Cancel("ann"));
            Hold(w, "fire");
            Assert.That(Release(w, -4));
            Assert.That(w.CastOf("ann").Charge, Is.EqualTo(1));
        }

        [Test]
        public void TheSecondElementTakesTheHoldOverAndCancelDropsIt()
        {
            var w = new World(Base, 8, 8, 7);
            Hold(w, "earth");
            Hold(w, "water");
            Run(w, 20);
            Assert.That(w.CombosFired, Is.Empty, "still held");
            Assert.That(w.CastOf("ann").Second, Is.EqualTo("water"));
            Assert.That(w.ApplyCombo(ComboCommand.Cancel("ann")));
            Assert.That((w.CastOf("ann").Casting, w.CastOf("ann").Holding), Is.EqualTo((false, false)));
            Assert.That(Release(w, 3), Is.False, "a cancelled cast is gone");
            Run(w, 20);
            Assert.That(w.CombosFired, Is.Empty);
        }

        [Test]
        public void AnElementTheCoreRefusesTakesNoHold()
        {
            var w = new World(Base, 8, 8, 7);
            w.ApplyCombo(ComboCommand.Load("ann", "fire"));
            Run(w, Base.Combos.Window);
            Assert.That(w.CastOf("ann").TakesElement, Is.False, "the window has shut");
            Assert.That(Hold(w, "water"), Is.False);
            Assert.That(w.CastOf("ann").Holding, Is.False);
        }

        [Test]
        public void AHeldCastGoesOffAsATapAfterASave()
        {
            var w = new World(Base, 8, 8, 7);
            Hold(w, "fire");
            w.Step();
            var back = World.Load(w.Save(new ComboTests.Blank()), Base, new ComboTests.Blank());
            Assert.That(back.CastOf("ann").Holding, Is.False, "no button is down on a loaded game");
            Assert.That(UntilFired(back), Is.EqualTo(Base.Combos.Combo("fire").CastTicks - 1));

            Release(w, 3);
            var charged = World.Load(w.Save(new ComboTests.Blank()), Base, new ComboTests.Blank());
            Assert.That(charged.CastState(), Is.EqualTo(w.CastState()), "a charge let go is kept");
            Assert.That(charged.CastHash(), Is.EqualTo(w.CastHash()));
        }

        [Test]
        public void TheChargeIsInTheChecksumAndTheReplay()
        {
            var a = new World(Base, 8, 8, 7);
            var b = new World(Base, 8, 8, 7);
            string plain = a.CastHash();
            Hold(a, "fire");
            Hold(b, "fire");
            Assert.That(a.CastHash(), Is.Not.EqualTo(plain));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            Release(a, 2);
            Release(b, 3);
            Assert.That(a.CastHash(), Is.Not.EqualTo(b.CastHash()), "two peers on different charges would show as a desync");

            var commands = new (int tick, ComboCommand command)[]
            {
                (1, ComboCommand.Load("ann", "earth", true)),
                (2, ComboCommand.Load("ann", "earth", true)),
                (16, ComboCommand.Release("ann", 3)),
            };
            var live = new World(Base, 8, 8, 7);
            var set = RuleSet.Plain(Base);
            string fired = null;
            for (int t = 1; t <= 20; t++)
            {
                var batch = new TickBatch(t);
                batch.Combos.AddRange(commands.Where(c => c.tick == t).Select(c => c.command));
                batch.RunOn(live, set, out _);
                if (live.CombosFired.Count > 0) fired = live.CombosFired[0].Spell;
            }
            Assert.That(fired, Is.EqualTo("stone-ramp"));
            var replay = new World(Base, 8, 8, 7);
            var timeline = TickBatch.Timeline(null, null, 20);
            TickBatch.AddCombos(timeline, commands);
            for (int t = 1; t <= 20; t++) timeline[t].RunOn(replay, set, out _);
            Assert.That(replay.CastHash(), Is.EqualTo(live.CastHash()));
        }

        [Test]
        public void HeldToTheMaxWallOfStoneIsTheRamp()
        {
            var w = new World(Base, 24, 12, 7, "dirt");
            var fired = Charged(w, "earth+earth", World.MaxCharge);
            Assert.That((fired.Spell, fired.Shape, fired.Name, fired.Charge), Is.EqualTo(("stone-ramp", "line", "Wall of Stone", 3)));
            Assert.That(w.KnowsCombo("ann", "earth+earth"), "the grimoire keeps the combo");
            w.Cast(fired.Spell, 4, 6, 1, fired.Charge);
            var heights = Enumerable.Range(4, 12).Select(x => w.At(x, 6).Height).ToList();
            Assert.That(heights, Is.Ordered, "it climbs away from the caster");
            Assert.That(heights.Max(), Is.GreaterThan(new World(Base, 24, 12, 7, "dirt").At(4, 6).Height + 1));

            var short1 = new World(Base, 24, 12, 7, "dirt");
            Assert.That(Charged(short1, "earth+earth", World.MaxCharge - 1).Spell, Is.EqualTo("wall-of-stone"));
        }

        [Test]
        public void HeldToTheMaxRaiseEarthDigs()
        {
            var w = new World(Base, 12, 12, 7, "dirt");
            var fired = Charged(w, "earth", World.MaxCharge);
            Assert.That(fired.Spell, Is.EqualTo("mold-earth"));
            w.Cast(fired.Spell, 5, 5, 1, fired.Charge);
            Assert.That(w.At(5, 5).Height, Is.LessThan(0));
            Assert.That(Charged(new World(Base, 12, 12, 7, "dirt"), "earth", 2).Spell, Is.EqualTo("mold-earth-heap"));
        }

        // Plant Growth by elements on burnt ground and meadow: what a tap can't reach.
        static int Saplings(int charge)
        {
            var w = ChemistryTests.Room(19, 19, 7, Base);
            for (int x = 1; x < 18; x++)
                for (int y = 1; y < 18; y++)
                {
                    w.At(x, y).T = y < 9 ? "dirt" : "grass";
                    w.At(x, y).Cover = y < 9 ? "ash" : null;
                }
            var fired = Charged(w, "earth+water", charge);
            w.Cast(fired.Spell, 9, 9, 1, fired.Charge);
            return w.Cells.Count(c => c.T == "sapling");
        }

        [Test]
        public void PlantGrowthAtFullChargeSeedsSaplingsThatATapDoesNot()
        {
            Assert.That(Saplings(1), Is.EqualTo(0));
            Assert.That(Saplings(World.MaxCharge), Is.GreaterThan(5));
        }

        // The goldens: the same steps as sandbox/test/charged.test.ts give the same text and hashes.

        [Test]
        public void GoldenChargedPlantGrowth()
        {
            var w = new World(Base, 16, 12, 7);
            for (int x = 0; x < 16; x++) w.At(x, 6).T = "grass";
            Hold(w, "earth");
            Run(w, 2);
            Hold(w, "water");
            Run(w, 12);
            Assert.That(w.CastState(), Is.EqualTo("ann:earth:water:1:9:0:0:-1::hold:c1"));
            Release(w, 3);
            Assert.That(w.CastState(), Is.EqualTo("ann:earth:water:1:9:0:0:-1::c3"));
            Assert.That(UntilFired(w), Is.EqualTo(1));
            var fired = w.CombosFired[0];
            Assert.That((fired.Spell, fired.Charge), Is.EqualTo(("plant-growth", 3)));
            Assert.That(w.CastState(), Is.EqualTo("ann:-:-:0:0:0:0:-1:earth+water"));
            w.Cast(fired.Spell, 8, 6, 1, fired.Charge);
            Run(w, 6);
            Assert.That($"{w.CastHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("ff240b66 f5d5e3aa 4167084909"));
        }

        [Test]
        public void GoldenHeldStoneRamp()
        {
            var w = new World(Base, 24, 12, 7);
            var fired = Charged(w, "earth+earth", 3);
            Assert.That(fired.Spell, Is.EqualTo("stone-ramp"));
            w.Cast(fired.Spell, 4, 6, 1, fired.Charge);
            Run(w, 2);
            Assert.That($"{w.CastHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("cdeed8e1 9aa186c6 7"));
        }
    }
}
