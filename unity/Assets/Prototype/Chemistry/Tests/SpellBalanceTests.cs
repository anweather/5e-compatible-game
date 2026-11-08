// Spell balance from the 2026-11-06 playtest (docs/playtests/2026-11-06-human-results.md, part two): Fireball came into line
// with the other level-3 spells, Meteor stays the big one but rests the hands for it, and Dust Storm and Flood do something to
// what they land on. Each spell lands on a field packed with raiders too tough to die, so what it deals is all there to count.
using System.Linq;
using NUnit.Framework;
using static Chem.HandsDef;

namespace Chem.Tests
{
    public class SpellBalanceTests
    {
        static Rules R => ChemistryTests.R;

        // A raider on every cell of a walled room but the caster's (a spell from self starts in front of it), each with 900 HP.
        const int Size = 31, Middle = 15, Hp = 900;

        // What one cast at the room's middle, eastward, does to each raider over 90 ticks (15 s), its burning included.
        static int[] Dealt(string spell, string kind = "giant-spider", int charge = 1)
        {
            var w = ChemistryTests.Room(Size, Size, 7, R);
            bool self = R.Spell(spell).Origin == "self";
            for (int x = 1; x < Size - 1; x++)
                for (int y = 1; y < Size - 1; y++)
                    if (!self || x != Middle || y != Middle) w.SpawnRaider(kind, x, y, Hp);
            var all = w.Raiders.ToList();
            w.Cast(spell, Middle, Middle, 1, charge);
            for (int t = 0; t < 90; t++) w.Step();
            return all.Select(r => Hp - r.Hp).Where(d => d > 0).ToArray();
        }

        [Test]
        public void TheNumbersArePinned()
        {
            var fireball = R.Spell("fireball");
            Assert.That((fireball.Damage, fireball.Radius), Is.EqualTo((new[] { 3, 6 }, 2)), "Fireball: 3d6, 25 ft across");
            var meteor = R.Spell("meteor");
            Assert.That((meteor.Damage, meteor.Radius), Is.EqualTo((new[] { 10, 6 }, 4)), "Meteor: as it was");
            var dust = R.Spell("dust-storm");
            Assert.That((dust.Damage, dust.ForceThrow), Is.EqualTo((new[] { 1, 6 }, 18)), "Dust Storm stings and staggers");
            var flood = R.Spell("flood");
            Assert.That((flood.Damage, flood.ForcePush), Is.EqualTo((new[] { 2, 8 }, 1)), "Flood slams and knocks back");

            var hands = R.Sim.Hold.Hands;
            Assert.That(hands.Both("fire", "fire", Thrust).CastTicks, Is.EqualTo(15));
            var meteorRow = hands.Both("fire", "fire", Throw);
            Assert.That((meteorRow.CastTicks, meteorRow.Cooldown), Is.EqualTo((24, 90)));
            Assert.That(hands.Both("fire", "fire", Thrust).Cooldown, Is.Zero, "Fireball rests the hands as any merge does");
        }

        [Test]
        public void FireballHitsNoHarderThanTheOtherLevelThreeSpells()
        {
            var fireball = Dealt("fireball");
            Assert.That(fireball.Average(), Is.LessThan(Dealt("lightning-bolt", "bone-hound").Average()), "Lightning hits harder, along its line");
            Assert.That(fireball.Average(), Is.LessThan(Dealt("falling-star").Average() * 1.2), "about Falling Star's, the other level-3 fire");
            Assert.That(fireball.Length, Is.AtLeast(25), "but over its whole 25 ft, and its embers past it");
        }

        [Test]
        public void MeteorIsClearlyTheBiggerSpell()
        {
            int[] fireball = Dealt("fireball"), meteor = Dealt("meteor");
            Assert.That(meteor.Average(), Is.GreaterThan(fireball.Average() * 2.5), "a hit");
            Assert.That(meteor.Length, Is.GreaterThan(fireball.Length * 5 / 2), "the raiders it reaches");
        }

        [Test]
        public void DustStormStingsAndStaggersEverythingInIt()
        {
            var w = ChemistryTests.Room(Size, Size, 7, R);
            var caught = w.SpawnRaider("giant-spider", Middle + 1, Middle, Hp);
            var outside = w.SpawnRaider("giant-spider", Middle + 4, Middle, Hp);
            w.Cast("dust-storm", Middle, Middle, 1);
            Assert.That(caught.Hp, Is.LessThan(Hp));
            Assert.That(caught.StatusOf(World.ThrownStatus), Is.EqualTo(18), "blind and stumbling for 3 s");
            Assert.That((outside.Hp, outside.StatusOf(World.ThrownStatus)), Is.EqualTo((Hp, 0)), "past its 25 ft, nothing");
            Assert.That(Dealt("dust-storm").Length, Is.EqualTo(25));
        }

        [Test]
        public void FloodSlamsAndKnocksBackWhatStandsInIt()
        {
            var w = ChemistryTests.Room(Size, Size, 7, R);
            var r = w.SpawnRaider("giant-spider", Middle + 3, Middle, Hp);
            w.Cast("flood", Middle, Middle, 1);
            Assert.That(r.Hp, Is.LessThan(Hp));
            Assert.That(r.X, Is.EqualTo(Middle + 3 + 2), "a tap knocks it two cells on (force.push 1, for the charge and one more)");
        }
    }
}
