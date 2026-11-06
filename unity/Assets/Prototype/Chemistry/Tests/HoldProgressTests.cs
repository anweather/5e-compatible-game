// Progress in the Hold (docs/design/the-hold.md, phase 5): upgrades inside a run, unlocks between runs, and the player's own save.
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Chem.HandsDef;
using static Chem.Tests.HoldTests;

namespace Chem.Tests
{
    public partial class HoldProgressTests
    {
        const string Bo = "Bo";

        static Rules R => ChemistryTests.R;
        static HoldParams P => R.Sim.Hold;

        static (int x, int y) Far(World w, int n = 0) => (w.HoldSite.Rise.x - 40 - n, w.HoldSite.Rise.y - 30);

        // Two wizards, a run, and its first wave just beaten: the offers are out.
        static World Offered(int seed = 7)
        {
            var w = Field(seed);
            Stand(w, Bo, Far(w, 2));
            Stand(w, Me, Far(w));
            w.ApplyCombo(ComboCommand.Moment(Me, "first-wave"));
            w.Hold.Moment = null;
            Until(w, () => w.Hold.ToSpawn == 0, "they come");
            Until(w, () =>
            {
                foreach (var r in w.Raiders) r.Hp = 1;
                foreach (var r in w.Raiders) r.Status["burning"] = 30;
                return w.Hold.Wave == 2;
            }, "the wave is beaten");
            return w;
        }

        static void ToTheWave(World w) => Until(w, () => w.Hold.InWave > 0, "the wave starts");

        static (World w, string left, string right) Attuned(int? points)
        {
            var w = Field();
            var room = ((w.HoldSite.AttuningFrom.x + w.HoldSite.AttuningTo.x) / 2, (w.HoldSite.AttuningFrom.y + w.HoldSite.AttuningTo.y) / 2);
            Stand(w, Me, room);
            if (points != null) Assert.That(w.ApplyCombo(ComboCommand.Bring(Me, points.Value)));
            return (w, w.HandsOf(Me).Holds[Left], w.HandsOf(Me).Holds[Right]);
        }

        static World RunningWith(int points)
        {
            var w = Attuned(points).w;
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, P.PlateTicks + 1);
            Away(w);
            return w;
        }

        static int CostTo(string id)
        {
            int total = 0;
            foreach (var u in P.Unlocks)
            {
                total += u.Cost;
                if (u.Id == id) return total;
            }
            Assert.Fail("no unlock " + id);
            return 0;
        }

        [Test]
        public void TheRowsAreThere()
        {
            Assert.That(P.Upgrades.Count, Is.GreaterThanOrEqualTo(12));
            Assert.That(P.Unlocks.Select(u => u.Id).Take(3), Is.EqualTo(new[] { "throw", "sweep", "second-hand" }));
            foreach (var element in R.Combos.Elements) Assert.That(P.Upgrades.Any(u => u.Holds == element), element);
            for (int g = 0; g < 3; g++) Assert.That(P.Upgrades.Any(u => u.Gesture == g), $"gesture {g}");
        }

        // ---------- upgrades ----------

        [Test]
        public void EveryPeerSeesTheSameOffers()
        {
            var one = Offered(11);
            var other = Offered(11);
            Assert.That(one.RunOf(Me).Offer.Count, Is.EqualTo(P.Offer));
            Assert.That(other.RunOf(Me).Offer, Is.EqualTo(one.RunOf(Me).Offer));
            Assert.That(other.RunOf(Bo).Offer, Is.EqualTo(one.RunOf(Bo).Offer));
            Assert.That(one.RunOf(Me).Offer.Distinct().Count(), Is.EqualTo(P.Offer));
        }

        [Test]
        public void EachWizardHasItsOwnOffer()
        {
            int differ = 0;
            for (int seed = 1; seed <= 5; seed++)
            {
                var w = Offered(seed);
                if (!w.RunOf(Me).Offer.SequenceEqual(w.RunOf(Bo).Offer)) differ++;
            }
            Assert.That(differ, Is.GreaterThan(0));
        }

        [Test]
        public void APickReachesOnlyThatWizard()
        {
            var w = Offered();
            string id = w.RunOf(Me).Offer[0];
            var bo = w.RunOf(Bo).Offer.ToList();
            Assert.That(w.ApplyCombo(ComboCommand.Pick(Me, id)));
            Assert.That(w.RunOf(Me).Picks, Is.EqualTo(new[] { id }));
            Assert.That(w.RunOf(Me).Offer, Is.Empty);
            Assert.That(w.RunOf(Bo).Picks, Is.Empty);
            Assert.That(w.RunOf(Bo).Offer, Is.EqualTo(bo));
        }

        [Test]
        public void OnlyWhatsOfferedCanBePicked()
        {
            var w = Offered();
            string other = P.Upgrades.First(u => !w.RunOf(Me).Offer.Contains(u.Id)).Id;
            Assert.That(w.ApplyCombo(ComboCommand.Pick(Me, other)), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Pick(Me, "no-such")), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Pick(Me, w.RunOf(Me).Offer[0])));
            Assert.That(w.ApplyCombo(ComboCommand.Pick(Me, w.RunOf(Me).Picks[0])), Is.False, "one each");
        }

        [Test]
        public void NoPickGetsTheSameSeededDefaultEverywhere()
        {
            var one = Offered(11);
            var other = Offered(11);
            var offer = one.RunOf(Me).Offer.ToList();
            ToTheWave(one);
            ToTheWave(other);
            Assert.That(one.RunOf(Me).Picks.Count, Is.EqualTo(1), "nobody holds up a run");
            Assert.That(offer, Does.Contain(one.RunOf(Me).Picks[0]));
            Assert.That(other.RunOf(Me).Picks, Is.EqualTo(one.RunOf(Me).Picks));
            Assert.That(one.RunOf(Me).Offer, Is.Empty);
        }

        [Test]
        public void AnOfferLeansIntoTheHandSet()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var w = Offered(seed);
                var holds = w.HandsOf(Me).Holds;
                Assert.That(w.RunOf(Me).Offer.Select(P.Upgrade).All(u => u.Holds == null || holds.Contains(u.Holds)), $"seed {seed}");
            }
        }

        [Test]
        public void ACooldownUpgradeShortensThatWizardsCooldown()
        {
            var w = Offered();
            w.RunOf(Me).Picks.Add("quick-hands");
            w.Hold.NextSpawn = w.Tick + 200;
            w.ApplyCombo(ComboCommand.Hand(Me, Right));
            w.ApplyCombo(ComboCommand.Hand(Bo, Right));
            Ticks(w, 8);
            Assert.That(w.HandCooling(Me, Right), Is.LessThan(w.HandCooling(Bo, Right)));
        }

        [Test]
        public void ARangeUpgradeComesWithTheSpell()
        {
            var w = Offered();
            w.RunOf(Me).Picks.Add("long-arm");
            w.Hold.NextSpawn = w.Tick + 200;
            w.ApplyCombo(ComboCommand.Hand(Me, Right, bumper: true));
            ComboFired? fired = null;
            for (int i = 0; i < 12 && fired == null; i++)
            {
                w.Step();
                if (w.CombosFired.Count > 0) fired = w.CombosFired[0];
            }
            Assert.That(fired?.Range, Is.EqualTo(P.Hands.RangeOf(Throw) * 125 / 100));
        }

        [Test]
        public void ADamageUpgradeHitsHarderForThatWizardOnly()
        {
            int Hurt(string by, string pick)
            {
                var w = Offered(11);
                w.Hold.NextSpawn = w.Tick + 500;
                w.Hold.Wizards[Me].Picks.Clear();
                if (pick != null) w.Hold.Wizards[Me].Picks.Add(pick);
                var r = w.SpawnRaider("giant-spider", 300, 300, 100000);
                w.Cast(new CastCommand("fire-bolt", r.X, r.Y, 0, by: by));
                return 100000 - r.Hp;
            }
            Assert.That(Hurt(Me, "hard-throw"), Is.GreaterThan(Hurt(Me, null)));
            Assert.That(Hurt(Bo, "hard-throw"), Is.EqualTo(Hurt(Bo, null)));
            Assert.That(Hurt(Me, "hard-thrust"), Is.EqualTo(Hurt(Me, null)), "it's a throw");
        }

        [Test]
        public void AHealthUpgradeIsThatWizards()
        {
            var w = Offered();
            w.RunOf(Me).Picks.Add("tough");
            Assert.That(w.HealthBonusOf(Me), Is.EqualTo(6));
            Assert.That(w.HealthBonusOf(Bo), Is.Zero);
        }

        [Test]
        public void UpgradesEndWithTheRun()
        {
            var w = Offered();
            w.RunOf(Me).Picks.Add("tough");
            w.Hold.Health = 1;
            w.Hold.NextSpawn = w.Tick;
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(w.HealthBonusOf(Me), Is.Zero);
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, P.PlateTicks + 1);
            Assert.That(w.HoldRunning);
            Assert.That(w.RunOf(Me), Is.Null, "a new run starts clean");
        }

        // ---------- unlocks ----------

        [Test]
        public void AFreshWizardHasOneHandOneElementAndTheThrust()
        {
            var (w, left, right) = Attuned(0);
            Assert.That(left, Is.Null);
            Assert.That(right, Is.Not.Null);
            Assert.That(w.GestureOpen(Me, Thrust));
            Assert.That(w.GestureOpen(Me, Throw), Is.False);
            Assert.That(w.GestureOpen(Me, Sweep), Is.False);
            Assert.That(w.HandOpen(Me, Right));
            Assert.That(w.HandOpen(Me, Left), Is.False);
            Assert.That(w.ElementOpen(Me, "water"), Is.False);
            Assert.That(w.UnlocksOf(Me), Is.Empty);
        }

        [Test]
        public void UnlocksComeInOrderAsPointsAddUp()
        {
            var w = Field();
            string[] all = P.Unlocks.Select(u => u.Id).ToArray();
            int total = 0;
            for (int i = 0; i < all.Length; i++)
            {
                w.ApplyCombo(ComboCommand.Bring(Me, total + P.Unlocks[i].Cost - 1));
                Assert.That(w.UnlocksOf(Me).Select(u => u.Id), Is.EqualTo(all.Take(i)), "a point short");
                total += P.Unlocks[i].Cost;
                w.ApplyCombo(ComboCommand.Bring(Me, total));
                Assert.That(w.UnlocksOf(Me).Select(u => u.Id), Is.EqualTo(all.Take(i + 1)));
            }
        }

        [Test]
        public void WhatsLockedCantBeDone()
        {
            var w = RunningWith(0);
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right, bumper: true)), Is.False, "no throw");
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Left)), Is.False, "no second hand");
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right)));
        }

        [Test]
        public void AThrowDoesntMakeASweepUntilItsUnlocked()
        {
            var w = RunningWith(CostTo("throw"));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right)));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right, bumper: true)), Is.False);
            w = RunningWith(CostTo("sweep"));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right)));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right, bumper: true)));
        }

        [Test]
        public void AttuningNeedsTheUnlock()
        {
            var w = Attuned(CostTo("second-hand")).w;
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "water")), Is.False);
            w = Attuned(CostTo("water")).w;
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "water")));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "earth")), Is.False);
            w = Attuned(0).w;
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, null)), Is.False, "no second hand to empty");
        }

        [Test]
        public void TheKitComesInSpellBySpell()
        {
            var w = RunningWith(CostTo("air"));
            Assert.That(w.DpadOf(Me), Is.Empty);
            w = RunningWith(CostTo("light"));
            Assert.That(w.DpadOf(Me).Select(k => k.Spell), Is.EqualTo(new[] { "light" }));
        }

        [Test]
        public void AWaveSurvivedEarnsAcrossRuns()
        {
            var w = RunningWith(4);
            w.Hold.Wave = 3;
            w.Hold.Health = 1;
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(w.PointsOf(Me), Is.EqualTo(4 + 2 * P.PointsPerWave));
        }

        [Test]
        public void AMomentOpensEverythingAndEarnsNothing()
        {
            var w = Attuned(0).w;
            Away(w);
            w.ApplyCombo(ComboCommand.Moment(Me, "gale-right"));
            Assert.That(w.HandsOf(Me).Holds, Is.EqualTo(new[] { "air", "air" }));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Left, bumper: true)));
            w.Hold.Wave = 9;
            w.Hold.Health = 1;
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(w.PointsOf(Me), Is.Zero);
        }

        [Test]
        public void AllPointsIsEverything()
        {
            var w = Attuned(World.AllPoints).w;
            Assert.That(w.UnlocksOf(Me).Count(), Is.EqualTo(P.Unlocks.Count));
        }

        // ---------- the player's own ----------

        [Test]
        public void AJoinersPointsMatchOnEveryPeer()
        {
            var host = Field(11);
            var joiner = Field(11);
            foreach (var w in new[] { host, joiner })
            {
                w.ApplyCombo(ComboCommand.Bring(Me, 9));
                w.ApplyCombo(ComboCommand.Bring(Bo, 30));
            }
            Assert.That(joiner.PointsOf(Bo), Is.EqualTo(host.PointsOf(Bo)));
            Assert.That(joiner.UnlocksOf(Bo).Select(u => u.Id), Is.EqualTo(host.UnlocksOf(Bo).Select(u => u.Id)));
            Assert.That(joiner.HandsText(), Is.EqualTo(host.HandsText()));
            Assert.That(joiner.CastHash(), Is.EqualTo(host.CastHash()));
            var other = Field(11);
            other.ApplyCombo(ComboCommand.Bring(Me, 9));
            other.ApplyCombo(ComboCommand.Bring(Bo, 31));
            Assert.That(other.CastHash(), Is.Not.EqualTo(host.CastHash()), "points are in the checksum");
        }

        [Test]
        public void AResetLosesNothingThatsThePlayers()
        {
            string folder = Temp();
            var save = new ProgressFile(folder);
            save.Save(21);
            var fresh = Field();
            Assert.That(fresh.PointsOf(Me), Is.Null, "a new world knows nothing");
            var again = new ProgressFile(folder);
            again.Load();
            fresh.ApplyCombo(ComboCommand.Bring(Me, again.Points));
            Assert.That(fresh.PointsOf(Me), Is.EqualTo(21));
        }

        [Test]
        public void TwoWorldsMatchThroughARunWithUpgrades()
        {
            var one = Offered(13);
            var other = Offered(13);
            foreach (var w in new[] { one, other })
            {
                w.ApplyCombo(ComboCommand.Pick(Me, w.RunOf(Me).Offer[1]));
                ToTheWave(w);
                w.ApplyCombo(ComboCommand.Hand(Me, Right));
                Ticks(w, 30);
            }
            Assert.That(other.HandsText(), Is.EqualTo(one.HandsText()));
            Assert.That(other.CastHash(), Is.EqualTo(one.CastHash()));
        }

        [Test]
        public void AWorldSaveKeepsPointsAndPicks()
        {
            var source = new Grass();
            var w = new World(R, 576, 576, 7);
            w.MakeFrom(source);
            WorldBuildings.Load(ChemistryTests.WorldDir()).LayInto(w);
            w.SetHold(WorldHold.Load(ChemistryTests.WorldDir()));
            w.ApplyCombo(ComboCommand.Bring(Me, 12));
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, P.PlateTicks + 1);
            w.Hold.Wizards[Me] = new RunWizard { Player = Me };
            w.Hold.Wizards[Me].Picks.Add("tough");
            var back = World.Load(w.Save(source), R, source);
            back.SetHold(w.HoldSite);
            Assert.That(back.PointsOf(Me), Is.EqualTo(12));
            Assert.That(back.RunOf(Me).Picks, Is.EqualTo(new[] { "tough" }));
        }

        // ---------- the save ----------

        static string Temp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "progress-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string SaveIn(string folder) => Path.Combine(folder, ProgressFile.FileName);

        static ProgressFile Read(string folder)
        {
            var f = new ProgressFile(folder);
            f.Load();
            return f;
        }

        [Test]
        public void TheSaveRoundTrips()
        {
            string folder = Temp();
            new ProgressFile(folder).Save(17);
            var f = Read(folder);
            Assert.That(f.Points, Is.EqualTo(17));
            Assert.That(f.Trouble, Is.Null);
        }

        [Test]
        public void NoSaveIsAFreshPlayerWithNoFuss()
        {
            var f = Read(Temp());
            Assert.That(f.Points, Is.Zero);
            Assert.That(f.Trouble, Is.Null);
        }

        [Test]
        public void TheSaveIsNeverWrittenLower()
        {
            string folder = Temp();
            var f = new ProgressFile(folder);
            f.Save(17);
            f.Save(5);
            Assert.That(Read(folder).Points, Is.EqualTo(17));
        }

        [TestCase("{ not json")]
        [TestCase("{ \"version\": 1, \"points\": -4 }")]
        [TestCase("{ \"version\": 99, \"points\": 4 }")]
        [TestCase("{ \"version\": 1, \"points\": \"lots\" }")]
        [TestCase("")]
        public void ABadSaveFallsBackToTheLastGoodCopyAndIsKept(string bad)
        {
            string folder = Temp();
            var f = new ProgressFile(folder);
            f.Save(10);
            f.Save(20);
            File.WriteAllText(SaveIn(folder), bad);
            var back = Read(folder);
            Assert.That(back.Points, Is.EqualTo(10));
            Assert.That(back.Trouble, Is.Not.Null);
            Assert.That(File.ReadAllText(SaveIn(folder) + ".broken"), Is.EqualTo(bad), "nothing is thrown away");
        }

        [Test]
        public void ABadSaveWithNoCopyStartsAgainButKeepsIt()
        {
            string folder = Temp();
            File.WriteAllText(SaveIn(folder), "{ not json");
            var back = Read(folder);
            Assert.That(back.Points, Is.Zero);
            Assert.That(back.Trouble, Is.Not.Null);
            Assert.That(File.Exists(SaveIn(folder) + ".broken"));
            Read(folder);
            Assert.That(File.Exists(SaveIn(folder) + ".broken1"), "a second look doesn't overwrite the first");
        }

        // ---------- what the player reads ----------

        [Test]
        public void EveryUpgradeSaysWhatItDoes()
        {
            Assert.That(P.Upgrade("fire-focus").Words(), Is.EqualTo("Fire spells do 30% more damage"));
            Assert.That(P.Upgrade("hard-thrust").Words(), Is.EqualTo("Thrusts do 20% more damage"));
            Assert.That(P.Upgrade("long-arm").Words(), Is.EqualTo("Throws reach 25% further"));
            Assert.That(P.Upgrade("quick-hands").Words(), Is.EqualTo("Your hands are ready 15% sooner"));
            Assert.That(P.Upgrade("earth-focus").Words(), Is.EqualTo("Earth spells are ready 30% sooner"));
            Assert.That(P.Upgrade("calm-hands").Words(), Is.EqualTo("Two-handed spells are ready 25% sooner"));
            Assert.That(P.Upgrade("tough").Words(), Is.EqualTo("6 more health"));
            foreach (var u in P.Upgrades) Assert.That(u.Words(), Is.Not.EqualTo("nothing yet"), u.Id);
        }

        [Test]
        public void TheNextUnlockIsSoManyPointsAway()
        {
            var w = Attuned(1).w;
            var first = P.Unlocks[0];
            Assert.That(w.NextUnlock(Me), Is.SameAs(first));
            Assert.That(w.PointsAway(Me, first), Is.EqualTo(first.Cost - 1));
            Assert.That(w.PointsAway(Me, P.Unlocks[1]), Is.EqualTo(CostTo(P.Unlocks[1].Id) - 1), "rows unlock in order");
            Assert.That(w.ApplyCombo(ComboCommand.Bring(Me, CostTo("sweep"))));
            Assert.That(w.PointsAway(Me, first), Is.Zero);
            Assert.That(w.NextUnlock(Me).Id, Is.EqualTo("second-hand"));
            Assert.That(w.UnlockGiving(u => u.Gesture == Sweep).Id, Is.EqualTo("sweep"));
            Assert.That(w.UnlockGiving(u => u.Gesture == Thrust), Is.Null, "a wizard starts with the thrust");
            Assert.That(w.ApplyCombo(ComboCommand.Bring(Me, CostTo(P.Unlocks[P.Unlocks.Count - 1].Id))));
            Assert.That(w.NextUnlock(Me), Is.Null, "everything");
            Assert.That(Attuned(null).w.NextUnlock(Me), Is.Null, "no points brought is everything");
        }
    }
}
