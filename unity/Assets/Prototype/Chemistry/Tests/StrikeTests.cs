using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The unarmed strike (World.Strike.cs): the attack roll, the damage, the knock-back, downed, lockstep.</summary>
    public class StrikeTests
    {
        static Rules rules;

        static Rules R => rules ??= Rules.Load(ChemistryTests.RulesDir());

        // The world's generator set so the next d20 is known: 1 rolls a 13, 8 a 4, 7 a natural 1.
        const uint Rolls13 = 1, Rolls4 = 8, Rolls1 = 7;

        // Ana at (5, 5) and Bo a cell east of her, both at full health, on open ground.
        static World Yard(Rules with = null)
        {
            var w = ChemistryTests.Room(16, 12, 7, with ?? R);
            w.ApplyStrike(StrikeCommand.Stand("Bo", 6, 5, 12));
            return w;
        }

        static StrikeHit Punch(World w, uint rng, int aimX = 1, int aimY = 0)
        {
            w.RngState = rng;
            w.ApplyStrike(StrikeCommand.Strike("Ana", 5, 5, aimX, aimY));
            return w.TakeStrikeHits().Single();
        }

        static string LastLog(World w) => w.Log[w.Log.Count - 1].Text;

        [Test]
        public void TheStrikeNumbersAreData()
        {
            var p = R.Sim.Strike;
            Assert.That((p.Reach, p.KnockBack, p.CooldownTicks, p.Proficiency), Is.EqualTo((1, 1, 6, 2)));
            Assert.That((R.Entity(World.WizardEntity).Str, R.Entity(World.WizardEntity).Ac), Is.EqualTo((10, 10)));
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { Wizard(str: 40, ac: 10) })).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "entity wizard: str must be a whole number from 1 to 30" }));
        }

        [Test]
        public void ARollAtOrOverTheACHits()
        {
            var w = Yard();
            var hit = Punch(w, Rolls13);
            Assert.That((hit.Hit, hit.Roll, hit.Total, hit.TargetPlayer), Is.EqualTo((true, 13, 15, "Bo")));
            Assert.That(LastLog(w), Is.EqualTo("Ana punches Bo: 1 bludgeoning damage"));
            Assert.That(w.Stands["Bo"].Hp, Is.EqualTo(11));
        }

        [Test]
        public void ARollUnderTheACOrANaturalOneMisses()
        {
            var w = Yard();
            var miss = Punch(w, Rolls4);
            Assert.That((miss.Hit, miss.Total, miss.Damage), Is.EqualTo((false, 6, 0)));
            Assert.That(LastLog(w), Is.EqualTo("Ana's punch misses Bo"));
            Assert.That((w.Stands["Bo"].Hp, w.Stands["Bo"].X), Is.EqualTo((12, 6)), "no harm and no push");

            // A natural 1 misses whatever the bonus: against AC 1, a 1 + 2 would otherwise hit.
            var easy = Yard(R.WithMods(new[] { Wizard(str: 10, ac: 1) }));
            Assert.That(Punch(easy, Rolls1).Hit, Is.False);
        }

        [Test]
        public void DamageIsOnePlusTheStrengthModifier()
        {
            Assert.That(new[] { World.Modifier(8), World.Modifier(10), World.Modifier(11), World.Modifier(14), World.Modifier(20) }, Is.EqualTo(new[] { -1, 0, 0, 2, 5 }));
            var w = Yard(R.WithMods(new[] { Wizard(str: 14, ac: 10) }));
            var hit = Punch(w, Rolls13);
            Assert.That((hit.Total, hit.Damage), Is.EqualTo((17, 3)));
            Assert.That(LastLog(w), Is.EqualTo("Ana punches Bo: 3 bludgeoning damage"));
            Assert.That(w.Stands["Bo"].Hp, Is.EqualTo(9));
        }

        [Test]
        public void AHitKnocksTheTargetOnUnlessSomethingIsThere()
        {
            var w = Yard();
            var hit = Punch(w, Rolls13);
            Assert.That((hit.ToX, hit.ToY, w.Stands["Bo"].X), Is.EqualTo((7, 5, 7)));

            var walled = Yard();
            walled.At(7, 5).T = "bedrock";
            Punch(walled, Rolls13);
            Assert.That(walled.Stands["Bo"].X, Is.EqualTo(6), "blocking ground stops it");
        }

        [Test]
        public void TheNearestInFrontIsStruckAndNoOneBehind()
        {
            var w = Yard();
            w.ApplyStrike(StrikeCommand.Stand("Cy", 4, 5, 12));
            Assert.That(Punch(w, Rolls13, -1, 0).TargetPlayer, Is.EqualTo("Cy"));
            w.Step();
            for (int i = 0; i < R.Sim.Strike.CooldownTicks; i++) w.Step();
            w.ApplyStrike(StrikeCommand.Leave("Cy"));
            w.ApplyStrike(StrikeCommand.Strike("Ana", 5, 5, -1, 0));
            Assert.That(w.TakeStrikeHits(), Is.Empty, "Bo is behind her");
        }

        [Test]
        public void TheCooldownHoldsTheNextStrike()
        {
            var w = Yard();
            Punch(w, Rolls13);
            w.ApplyStrike(StrikeCommand.Strike("Ana", 5, 5, 1, 0));
            Assert.That(w.TakeStrikeHits(), Is.Empty);
            w.ApplyStrike(StrikeCommand.Stand("Bo", 6, 5, 12)); // back from the knock-back
            for (int i = 0; i < R.Sim.Strike.CooldownTicks - 1; i++) w.Step();
            w.ApplyStrike(StrikeCommand.Strike("Ana", 5, 5, 1, 0));
            Assert.That(w.TakeStrikeHits(), Is.Empty, "a tick short");
            w.Step();
            w.ApplyStrike(StrikeCommand.Strike("Ana", 5, 5, 1, 0));
            Assert.That(w.TakeStrikeHits(), Has.Count.EqualTo(1));
        }

        [Test]
        public void AWizardAtZeroIsDownedAndCantBeStruckAgain()
        {
            var w = Yard();
            w.ApplyStrike(StrikeCommand.Stand("Bo", 6, 5, 1));
            var hit = Punch(w, Rolls13);
            Assert.That((hit.Downed, w.Stands["Bo"].Hp, w.Stands["Bo"].Down), Is.EqualTo((true, 0, true)));
            Assert.That(LastLog(w), Is.EqualTo("Bo is downed"));
            for (int i = 0; i < R.Sim.Strike.CooldownTicks; i++) w.Step();
            w.ApplyStrike(StrikeCommand.Strike("Ana", 7, 5, -1, 0));
            Assert.That(w.TakeStrikeHits(), Is.Empty, "downed: no one to strike");
            w.ApplyStrike(StrikeCommand.Stand("Bo", 2, 2, 12));
            Assert.That(w.Stands["Bo"].Down, Is.False, "up again when its peer says so");
        }

        [Test]
        public void ARaiderIsStruckByItsACAndKnockedOn()
        {
            var w = ChemistryTests.Room(16, 12, 7, R);
            w.ApplyStrike(StrikeCommand.Stand("Ana", 5, 5, 12));
            var hound = w.SpawnRaider("bone-hound", 5, 6, 6);
            var hit = Punch(w, Rolls13, 0, 1);
            Assert.That((hit.Hit, hit.TargetRaider, hit.ToY), Is.EqualTo((true, hound.Id, 7)));
            Assert.That((hound.Hp, hound.Y), Is.EqualTo((5, 7)));
            Assert.That(w.Log.Any(l => l.Text == "Ana punches the bone hound: 1 bludgeoning damage"), Is.True);
        }

        [Test]
        public void TwoWorldsSideBySideAgree()
        {
            World Play()
            {
                var w = Yard();
                w.ApplyStrike(StrikeCommand.Stand("Cy", 5, 6, 12));
                for (int t = 0; t < 60; t++)
                {
                    var batch = new TickBatch(w.Tick + 1);
                    if (t % 7 == 0) batch.Strikes.Add(StrikeCommand.Strike("Ana", 5, 5, 1, t % 2));
                    if (t % 9 == 0) batch.Strikes.Add(StrikeCommand.Strike("Bo", w.Stands["Bo"].X, 5, -1, 0));
                    batch.RunOn(w, RuleSet.Plain(R), out _);
                }
                return w;
            }

            var a = Play();
            var b = Play();
            Assert.That(a.StrikeText(), Does.Contain("|wAna:"));
            Assert.That(a.CastHash(), Is.EqualTo(b.CastHash()));
            Assert.That(a.StrikeText(), Is.EqualTo(b.StrikeText()));
            Assert.That(a.TakeStrikeHits().Select(h => (h.Roll, h.Damage)), Is.EqualTo(b.TakeStrikeHits().Select(h => (h.Roll, h.Damage))));
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()));
        }

        [Test]
        public void AWorldWithNoWizardsHashesAsBefore()
        {
            var w = ChemistryTests.Room(16, 12, 7, R);
            string before = w.CastHash();
            w.ApplyStrike(StrikeCommand.Strike("Ana", 99, 99, 1, 0)); // off the map: nothing
            Assert.That(w.CastHash(), Is.EqualTo(before));
        }

        static ModPatch Wizard(int str, int ac) => ModPatch.Parse(
            "{ \"entities\": { \"wizard\": { \"name\": \"Wizard\", \"material\": \"flesh\", \"hp\": 12, \"str\": " + str + ", \"ac\": " + ac
            + ", \"mass\": 1, \"components\": [\"creature\"], \"player\": true } } }");
    }
}
