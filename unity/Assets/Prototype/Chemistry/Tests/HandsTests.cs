// The hands (D16, docs/design/unified-casting.md; the Hold's side in the-hold.md, phase 3), on the map's own rows: where the
// attuning room is comes from them and hold.json.
using System.Linq;
using NUnit.Framework;
using static Chem.HandsDef;
using static Chem.Tests.HoldTests;

namespace Chem.Tests
{
    public class HandsTests
    {
        static Rules R => ChemistryTests.R;
        static HandsDef Table => R.Sim.Hold.Hands;

        static (int x, int y) Room(World w) => ((w.HoldSite.AttuningFrom.x + w.HoldSite.AttuningTo.x) / 2, (w.HoldSite.AttuningFrom.y + w.HoldSite.AttuningTo.y) / 2);

        // A hand's trigger, or its bumper.
        static void Trigger(World w, int hand, bool taken = true) =>
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, hand)), Is.EqualTo(taken), $"trigger {hand}");

        static void Bumper(World w, int hand, bool taken = true) =>
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, hand, bumper: true)), Is.EqualTo(taken), $"bumper {hand}");

        // Attuned in the room (null: nothing), then out of it, with a run going.
        static World Holding(string left, string right, int seed = 7)
        {
            var w = Field(seed);
            Stand(w, Me, Room(w));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, left)));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, right)));
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            Away(w);
            return w;
        }

        // What goes off, spells and acts, stepping until something does, and the ticks it took.
        static (string[] out_, int ticks) Out(World w)
        {
            for (int i = 1; i < 60; i++)
            {
                w.Step();
                var fired = w.CombosFired.Where(f => f.Player == Me).Select(f => f.Spell ?? f.Act).ToArray();
                if (fired.Length > 0) return (fired, i);
            }
            return (System.Array.Empty<string>(), 0);
        }

        static string Fired(World w) => Out(w).out_.SingleOrDefault();

        static HandsDef T => Table;

        [Test]
        public void TheTableIsAndrewsAndEverySpellIsUnique()
        {
            var spells = T.All.Where(r => r.Spell != null).Select(r => r.Spell).ToList();
            Assert.That(spells.Count, Is.EqualTo(12 + 18), "the twelve singles and the pairs, Dust Storm and Sand Blast cut");
            Assert.That(spells.Distinct().Count(), Is.EqualTo(spells.Count), "never a re-skin");
            Assert.That(spells.All(s => R.Spell(s) != null));
            string[][] rows =
            {
                new[] { "fire", "burning-hands", "fire-bolt", "flame-wave" },
                new[] { "water", "create-water", "ray-of-frost", "sleet-storm" },
                new[] { "earth", "mold-earth-heap", "earthen-trench", "spike-growth" },
                new[] { "air", "shocking-grasp", "gust-of-wind", "thunderwave" },
            };
            foreach (var row in rows)
                for (int g = 0; g < 3; g++) Assert.That(T.One(row[0], g).Spell, Is.EqualTo(row[1 + g]));
        }

        // unified-casting.md's pair table: thrust, then throw; null for a cut row.
        [TestCase("fire", "fire", "fireball", "meteor")]
        [TestCase("water", "water", "wave", "flood")]
        [TestCase("earth", "earth", "wall-of-stone", "stone-ramp")]
        [TestCase("air", "air", "whirlwind", null)]
        [TestCase("fire", "water", "scalding-steam", "geyser")]
        [TestCase("fire", "earth", "forge", "lava-pool")]
        [TestCase("fire", "air", "falling-star", "lightning-bolt")]
        [TestCase("water", "earth", "mud", "plant-growth")]
        [TestCase("water", "air", "frost", "rain-cloud")]
        [TestCase("earth", "air", null, "rolling-boulder")]
        public void ThePairsAreTheDocs(string a, string b, string thrust, string thrown)
        {
            Assert.That(T.Both(a, b, Thrust)?.Spell, Is.EqualTo(thrust));
            Assert.That(T.Both(b, a, Throw)?.Spell, Is.EqualTo(thrown), "in either order");
            Assert.That(T.Both(a, b, Sweep), Is.Null, "sweep and sweep is empty for now");
        }

        [Test]
        public void NoRowIsADustSpell()
        {
            Assert.That(T.Find("dust-storm").slot, Is.Null);
            Assert.That(T.Find("sand-blast").slot, Is.Null);
            Assert.That(R.Spell("dust-storm"), Is.Not.Null, "the spells stay, for mods and the chemistry");
        }

        // A hand's button held down from now, as the view sends it.
        static void Down(World w, int hand, bool bumper = false, bool taken = true) =>
            Assert.That(w.ApplyCombo(ComboCommand.HandDown(Me, hand, bumper)), Is.EqualTo(taken), $"down {hand}");

        static void Up(World w, int hand, bool taken = true) =>
            Assert.That(w.ApplyCombo(ComboCommand.HandUp(Me, hand)), Is.EqualTo(taken), $"up {hand}");

        static int PerLevel => R.Combos.ChargeTicks;

        [Test]
        public void TheTriggerThrustsAndTheBumperThrows()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
            Trigger(w, Left);
            Assert.That(Fired(w), Is.EqualTo("create-water"));
            Ticks(w, T.HandCooldown);
            Bumper(w, Right);
            Assert.That(Fired(w), Is.EqualTo("fire-bolt"));
            Bumper(w, Left);
            Assert.That(Fired(w), Is.EqualTo("ray-of-frost"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TriggerAndBumperOnOneHandSweep(bool bumperFirst)
        {
            var w = Holding("water", "fire");
            if (bumperFirst) Bumper(w, Right);
            else Trigger(w, Right);
            Ticks(w, T.Window - 1);
            if (bumperFirst) Trigger(w, Right);
            else Bumper(w, Right);
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { "flame-wave" }));
            Assert.That(ticks, Is.EqualTo(T.One("fire", Sweep).CastTicks));
        }

        [Test]
        public void TheOtherButtonIsTooLateAfterTheWindow()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Ticks(w, T.Window);
            Bumper(w, Right, taken: false);
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
        }

        [Test]
        public void TheSameButtonTwiceIsNothingMore()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Trigger(w, Right, taken: false);
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
        }

        [Test]
        public void ASweptHandTakesNoMore()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Bumper(w, Right);
            Trigger(w, Right, taken: false);
            Bumper(w, Right, taken: false);
        }

        [Test]
        public void ALoneTriggerIsNotHeldBack()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Assert.That(Out(w).ticks, Is.EqualTo(T.One("fire", Thrust).CastTicks));
        }

        [TestCase(false, "falling-star")]
        [TestCase(true, "lightning-bolt")]
        public void TheSameGestureOnBothIsTheTables(bool bumpers, string spell)
        {
            var w = Holding("air", "fire");
            if (bumpers)
            {
                Bumper(w, Right);
                Bumper(w, Left);
            }
            else
            {
                Trigger(w, Right);
                Trigger(w, Left);
            }
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { spell }));
            Assert.That(ticks, Is.EqualTo(T.Both("fire", "air", bumpers ? Throw : Thrust).CastTicks));
        }

        [TestCase("fire", Thrust, "fireball")]
        [TestCase("fire", Throw, "meteor")]
        [TestCase("earth", Throw, "stone-ramp")]
        public void TheSameElementHasItsOwn(string element, int gesture, string spell)
        {
            var w = Holding(element, element);
            w.ApplyCombo(ComboCommand.Hand(Me, Left, gesture == Throw));
            w.ApplyCombo(ComboCommand.Hand(Me, Right, gesture == Throw));
            Assert.That(Fired(w), Is.EqualTo(spell));
        }

        [Test]
        public void DifferentGesturesAreBothAtOnce()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Bumper(w, Left);
            Assert.That(w.HandSpellsUnderWay(Me), Is.EquivalentTo(new[] { "burning-hands", "ray-of-frost" }), "the meter names both");
            var (fired, _) = Out(w);
            Assert.That(fired, Is.EquivalentTo(new[] { "burning-hands", "ray-of-frost" }));
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(T.HandCooldown), "each cools on its own");
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.HandCooldown));
        }

        [Test]
        public void ASweepAndAThrustGoOffEachOnItsOwn()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Bumper(w, Right);
            Trigger(w, Left);
            var (first, ticks) = Out(w);
            // Apart: Unity's NUnit compares a tuple's array by reference.
            Assert.That(first, Is.EqualTo(new[] { "create-water" }), "the thrust doesn't wait for the sweep");
            Assert.That(ticks, Is.EqualTo(T.One("water", Thrust).CastTicks), "the thrust doesn't wait for the sweep");
            var (second, more) = Out(w);
            Assert.That(second, Is.EqualTo(new[] { "flame-wave" }));
            Assert.That(ticks + more, Is.EqualTo(T.One("fire", Sweep).CastTicks));
        }

        [Test]
        public void BothSweepsWithNoRowGoOffAsTheyAre()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Bumper(w, Right);
            Trigger(w, Left);
            Bumper(w, Left);
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EquivalentTo(new[] { "flame-wave", "sleet-storm" }));
            Assert.That(ticks, Is.EqualTo(9));
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(T.HandCooldown), "no row, no lock on both");
        }

        [TestCase(Thrust, "punch")]
        [TestCase(Throw, "throw")]
        [TestCase(Sweep, "shove")]
        public void EmptyHandsAct(int gesture, string act)
        {
            var w = Holding(null, null);
            if (gesture != Throw) Trigger(w, Left);
            if (gesture != Thrust) Bumper(w, Left);
            var (fired, _) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { act }));
            Assert.That(w.HandCooling(Me, Left), Is.Zero, "an act has its own limit");
        }

        [Test]
        public void OneElementIsOnBothHands()
        {
            var w = Holding(null, "fire");
            Assert.That(w.HandHolds(Me, Left), Is.EqualTo("fire"));
            Assert.That(w.HandsOf(Me).Holds[Left], Is.Null, "what's attuned stays as it is");
            Trigger(w, Left);
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
            Ticks(w, T.HandCooldown);
            Trigger(w, Left);
            Trigger(w, Right);
            Assert.That(Fired(w), Is.EqualTo("fireball"), "fire alone makes both triggers a fire pair");
        }

        [Test]
        public void AGesturesRangeComesWithItsSpell()
        {
            var w = Holding("water", "fire");
            Bumper(w, Right);
            w.Step();
            ComboFired? fired = null;
            for (int i = 0; i < 20 && fired == null; i++)
            {
                w.Step();
                if (w.CombosFired.Count > 0) fired = w.CombosFired[0];
            }
            Assert.That(fired?.Range, Is.EqualTo(T.RangeOf(Throw)));
            Assert.That(T.RangeOf(Thrust), Is.LessThan(T.RangeOf(Throw)));
            Assert.That(T.RangeOf(Sweep), Is.EqualTo(T.RangeOf(Thrust)));
        }

        [TestCase(10, 10, 12, 10, 8, 12, 10)]
        [TestCase(10, 10, 110, 10, 8, 18, 10)]
        [TestCase(10, 10, 10, 2, 8, 10, 2)]
        [TestCase(0, 0, 30, 40, 10, 6, 8)]
        [TestCase(0, 0, 500, 0, 0, 500, 0)]
        public void ATargetComesBackAlongItsLine(int fx, int fy, int tx, int ty, int range, int x, int y) =>
            Assert.That(Within(fx, fy, tx, ty, range), Is.EqualTo((x, y)));

        [Test]
        public void AHandCoolsDownAlone()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Out(w);
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.HandCooldown));
            Trigger(w, Right, taken: false);
            Trigger(w, Left);
            Out(w);
            Ticks(w, w.HandCooling(Me, Right) - 1);
            Trigger(w, Right, taken: false);
            Ticks(w, 1);
            Trigger(w, Right);
        }

        [Test]
        public void ACombinedSpellLocksBoth()
        {
            var w = Holding("air", "fire");
            Trigger(w, Right);
            Trigger(w, Left);
            Out(w);
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(T.BothCooldown));
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.BothCooldown));
            Trigger(w, Left, taken: false);
            Bumper(w, Right, taken: false);
            Ticks(w, T.BothCooldown);
            Trigger(w, Left);
        }

        [Test]
        public void ACoolingHandCantJoin()
        {
            var w = Holding("air", "fire");
            Trigger(w, Left);
            Out(w);
            Trigger(w, Right);
            Trigger(w, Left, taken: false);
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
        }

        [Test]
        public void ADroppedCastCostsNoCooldownAndLeavesNothing()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Assert.That(w.ApplyCombo(ComboCommand.Cancel(Me)));
            Assert.That(w.HandCooling(Me, Right), Is.Zero);
            Assert.That(w.ApplyCombo(ComboCommand.CastSelected(Me)));
            Assert.That(Fired(w), Is.EqualTo(T.Kit[0].Spell));
        }

        [Test]
        public void AttuningInTheRoomIsAtOnce()
        {
            var w = Field();
            var room = Room(w);
            Stand(w, Me, (room.x, room.y));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "earth")));
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo("earth"));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "no-such")), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, 2, "air")), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, null)), "a hand can hold nothing");
            Assert.That(w.HandsOf(Me).Holds[Left], Is.Null);
            Stand(w, Me, room, 0);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False, "down in it");
        }

        [Test]
        public void OutsideTheHoldAttuningTakesAMoment()
        {
            var w = Field();
            Away(w);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), "anywhere, while the Hold isn't running");
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo(T.Start[Right]), "not yet");
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "earth")), Is.False, "one at a time");
            Trigger(w, Left, taken: false);
            Ticks(w, T.AttuneTicks - 1);
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo(T.Start[Right]));
            Ticks(w, 1);
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo("air"));
            Assert.That(w.ApplyCombo(ComboCommand.Attune("nobody", Right, "air")), "a wizard with no stand is in the world too");
            Stand(w, Me, (1, 1), 0);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")), Is.False, "not while down");
        }

        [Test]
        public void ARunKeepsTheRoomRule()
        {
            var w = Holding("water", "fire");
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False, "out of the room in a run");
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo("fire"));
        }

        [Test]
        public void GoldenAttuningIsRefusedMidCast()
        {
            var w = Field();
            Away(w);
            w.ApplyCombo(ComboCommand.HandDown(Me, Right));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")), Is.False, "a hand charging");
            Ticks(w, 2);
            w.ApplyCombo(ComboCommand.HandUp(Me, Right));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")), Is.False, "a hand let go but not gone off");
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
            Assert.That(w.ApplyCombo(ComboCommand.Load(Me, "earth")));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")), Is.False, "an element cast");
            Out(w);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")), "once nothing's under way");
            Assert.That(w.CastOf(Me).Casting, "the moment shows as a meter");
            Assert.That(w.ApplyCombo(ComboCommand.Cancel(Me)), "and can be dropped");
            Assert.That(w.HandsOf(Me).Busy, Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Left, "air")));
            Ticks(w, T.AttuneTicks);
            Assert.That(w.HandsOf(Me).Holds[Left], Is.EqualTo("air"));
            Assert.That(w.CastHash(), Is.EqualTo("f8c9fb76"));
        }

        [Test]
        public void TheRoomComesFromTheRow()
        {
            var s = WorldHold.Load(ChemistryTests.WorldDir());
            var at = s.Row["at"];
            var from = s.Row["joins"]["k1"];
            Assert.That(s.AttuningFrom, Is.EqualTo(((int)at[0] + (int)from[0] / 2, (int)at[1] + (int)from[1] / 2)));
            Assert.That(s.InAttuning(s.Plate.x, s.Plate.y), "the plate is in it");
            Assert.That(s.InAttuning(s.Heart.x, s.Heart.y), Is.False, "the heart isn't");
            Assert.That(s.InAttuning(s.Rise.x, s.Rise.y), Is.False);
        }

        [Test]
        public void OutsideARunTheHandsAreOnAndTheElementPadStays()
        {
            var w = Field();
            Away(w);
            Assert.That(w.HandsOn(Me));
            Assert.That(w.DpadOf(Me), Is.SameAs(R.Combos.Dpad), "the kit is the Hold's");
            Assert.That(w.ApplyCombo(ComboCommand.Load(Me, "fire")), "the element pad stays until the view's on hands");
            Trigger(w, Right, taken: false);
            Out(w);
            Trigger(w, Right);
            Assert.That(w.ApplyCombo(ComboCommand.Load(Me, "fire")), Is.False, "nor the pad over a hand");
        }

        [Test]
        public void AWorldWithNoHoldHasHandsToo()
        {
            var w = new World(R, 64, 64, 7);
            Assert.That(w.HandsText(), Is.Empty, "hands nobody's used hash as they did");
            Assert.That(w.HandsOn(Me));
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right)));
            Assert.That(Fired(w), Is.EqualTo("burning-hands"));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")));
            Assert.That(w.HoldReadsStand(1, 1), Is.False);
        }

        [Test]
        public void TheKitIsTheRowDuringARun()
        {
            var w = Holding("water", "fire");
            Assert.That(w.DpadOf(Me), Is.SameAs(Table.Kit));
            Assert.That(w.ApplyCombo(ComboCommand.CastSelected(Me)));
            Assert.That(Fired(w), Is.EqualTo(Table.Kit[0].Spell));
            Assert.That(w.HandCooling(Me, Right), Is.Zero, "a kit spell costs no hand");
        }

        [Test]
        public void AStandInTheRoomIsOneTheHoldReads()
        {
            var w = Field();
            var room = Room(w);
            Assert.That(w.HoldReadsStand(room.x, room.y));
            Assert.That(w.HoldReadsStand(w.HoldSite.Plate.x, w.HoldSite.Plate.y));
            Assert.That(w.HoldReadsStand(w.HoldSite.Rise.x, w.HoldSite.Rise.y), Is.False);
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(9, 3)]
        public void GoldenAChargedHandCast(int levels, int charge)
        {
            var w = Holding("water", "fire");
            Down(w, Right);
            int cast = T.One("fire", Thrust).CastTicks;
            Ticks(w, cast - 1 + levels * PerLevel);
            Assert.That(w.HandCharge(Me, Right), Is.EqualTo(charge));
            Assert.That(w.CastOf(Me).Holding);
            Assert.That(w.CombosFired, Is.Empty, "held, it waits for the release");
            Up(w, Right);
            Up(w, Right, taken: false);
            w.Step();
            var fired = w.CombosFired.Single();
            Assert.That((fired.Spell, fired.Charge), Is.EqualTo(("burning-hands", charge)));
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.HandCooldown), "the cooldown starts as it goes off");
            Assert.That(w.CastHash(), Is.EqualTo(new[] { "fc6f40e0", "8e71669b", "ecc39b7d", "1a85cd66" }[System.Math.Min(levels, 3)]));
        }

        [Test]
        public void ATapIsChargeOneAndAnEarlyReleaseStillTakesATapsTime()
        {
            var w = Holding("water", "fire");
            Down(w, Right);
            Ticks(w, 2);
            Up(w, Right);
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { "burning-hands" }));
            Assert.That(ticks + 2, Is.EqualTo(T.One("fire", Thrust).CastTicks));
            Assert.That(w.CombosFired.Single().Charge, Is.EqualTo(1));
        }

        [Test]
        public void GoldenAMergeGoesOffAtTheLowerCharge()
        {
            var w = Holding("fire", "fire");
            Down(w, Left);
            Ticks(w, 2 * PerLevel);
            Down(w, Right);
            Assert.That(w.CastOf(Me).Merge, Is.EqualTo("fireball"), "the view shows the merge while the hands are held");
            Assert.That(w.HandSpellsUnderWay(Me), Is.EqualTo(new[] { "fireball" }), "one cast, not two");
            Ticks(w, T.Both("fire", "fire", Thrust).CastTicks - 1 + PerLevel);
            Assert.That(w.HandCharge(Me, Left), Is.EqualTo(3));
            Assert.That(w.HandCharge(Me, Right), Is.EqualTo(2));
            Up(w, Left);
            Assert.That(w.HandsOf(Me).Down, Is.EqualTo(new[] { false, false }), "a release of either lets go of both");
            var (fired, _) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { "fireball" }));
            Assert.That(w.CombosFired.Single().Charge, Is.EqualTo(2), "the lower of the two");
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(T.BothCooldown));
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.BothCooldown));
            Assert.That(w.CastOf(Me).Merge, Is.Null);
            Assert.That(w.CastHash(), Is.EqualTo("28c76308"));
        }

        [Test]
        public void ATapJoiningAChargingHandFiresTheMergeAtOne()
        {
            var w = Holding("air", "fire");
            Down(w, Right, bumper: true);
            Ticks(w, 12);
            Bumper(w, Left);
            Assert.That(w.HandsOf(Me).Down, Is.EqualTo(new[] { false, false }), "a tap is a release");
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { "lightning-bolt" }));
            Assert.That(ticks, Is.EqualTo(T.Both("fire", "air", Throw).CastTicks));
            Assert.That(w.CombosFired.Single().Charge, Is.EqualTo(1));
        }

        [Test]
        public void GoldenOneHandFiresWhileTheOtherCharges()
        {
            var w = Holding("water", "fire");
            Down(w, Left, bumper: true);
            Trigger(w, Right);
            Assert.That(w.CastOf(Me).Merge, Is.Null, "different gestures don't merge");
            var (first, _) = Out(w);
            Assert.That(first, Is.EqualTo(new[] { "burning-hands" }));
            Assert.That(w.CombosFired.Single().Charge, Is.EqualTo(1));
            Assert.That(w.HandsOf(Me).Gesture[Left], Is.EqualTo(Throw), "the left hand charges on");
            Ticks(w, T.HandCooldown);
            Trigger(w, Right);
            Assert.That(Out(w).out_, Is.EqualTo(new[] { "burning-hands" }), "and the right rapid-fires");
            Ticks(w, 3 * PerLevel);
            Assert.That(w.HandCharge(Me, Left), Is.EqualTo(World.MaxCharge));
            Up(w, Left);
            w.Step();
            Assert.That((w.CombosFired.Single().Spell, w.CombosFired.Single().Charge), Is.EqualTo(("ray-of-frost", World.MaxCharge)));
            Assert.That(w.CastHash(), Is.EqualTo("3798243e"));
        }

        [Test]
        public void GoldenTwoPeersWithDifferentHandsStayInLockstep()
        {
            // Two peers' copies of one world, each given the same stamped commands: Ana's hands and Bo's differ.
            const string bo = "Bo";
            var commands = new (int tick, ComboCommand command)[]
            {
                (1, ComboCommand.Attune(Me, Left, "earth")),
                (1, ComboCommand.Attune(bo, Right, "air")),
                (8, ComboCommand.HandDown(Me, Left)),
                (8, ComboCommand.HandDown(Me, Right)),
                (9, ComboCommand.HandDown(bo, Right, true)),
                (10, ComboCommand.Hand(bo, Left)),
                (20, ComboCommand.HandUp(bo, Right)),
                (27, ComboCommand.HandUp(Me, Right)),
                (40, ComboCommand.HandDown(Me, Left, true)), // refused: the merge cooled both hands
                (60, ComboCommand.HandDown(Me, Left, true)),
                (64, ComboCommand.HandUp(Me, Left)),
            };
            string Run(World w)
            {
                var set = RuleSet.Plain(R);
                var timeline = TickBatch.Timeline(null, null, 80);
                TickBatch.AddCombos(timeline, commands);
                var said = new System.Collections.Generic.List<string>();
                for (int t = 1; t <= 80; t++)
                {
                    timeline[t].RunOn(w, set, out _);
                    foreach (var f in w.CombosFired)
                    {
                        said.Add($"{t}:{f.Player}:{f.Spell}:{f.Charge}");
                        if (f.Spell != null) w.Cast(f.Spell, f.Player == Me ? 20 : 40, 30, 1, f.Charge);
                    }
                }
                return string.Join(" ", said) + " " + w.CastHash() + " " + w.CellHash();
            }
            string one = Run(new World(R, 64, 64, 7, "dirt")), other = Run(new World(R, 64, 64, 7, "dirt"));
            Assert.That(other, Is.EqualTo(one));
            Assert.That(one, Is.EqualTo("15:Bo:create-water:1 20:Bo:gust-of-wind:3 27:Ana:forge:3 65:Ana:earthen-trench:1 cb1fffb9 3d94a395"));
        }

        [Test]
        public void ASaveKeepsAHandCharging()
        {
            var source = new Grass();
            var w = new World(R, 64, 64, 7);
            w.MakeFrom(source);
            w.ApplyCombo(ComboCommand.HandDown(Me, Left));
            Ticks(w, 3);
            Trigger(w, Right);
            Ticks(w, 1);
            var loaded = World.Load(w.Save(source), R, source);
            Assert.That(loaded.HandsText(), Is.EqualTo(w.HandsText()));
            Assert.That(loaded.CastState(), Is.EqualTo(w.CastState()));
            foreach (var x in new[] { w, loaded })
            {
                Ticks(x, 12);
                x.ApplyCombo(ComboCommand.HandUp(Me, Left));
                x.Step();
            }
            Assert.That(loaded.CastHash(), Is.EqualTo(w.CastHash()));
        }

        [Test]
        public void TheHandsAreInTheChecksum()
        {
            var one = Holding("water", "fire");
            var other = Holding("air", "fire");
            Assert.That(other.HandsText(), Is.Not.EqualTo(one.HandsText()));
            Assert.That(other.CastHash(), Is.Not.EqualTo(one.CastHash()));
        }

        [Test]
        public void TwoWorldsMatchThroughTheSameInputs()
        {
            var one = Holding("air", "fire", 11);
            var other = Holding("air", "fire", 11);
            foreach (var w in new[] { one, other })
            {
                Trigger(w, Right);
                Bumper(w, Left);
                Out(w);
                Ticks(w, T.BothCooldown);
                Trigger(w, Left);
                Trigger(w, Right);
                Ticks(w, 2);
            }
            Assert.That(other.CastHash(), Is.EqualTo(one.CastHash()));
            Assert.That(other.CellHash(), Is.EqualTo(one.CellHash()));
        }

        [Test]
        public void ASaveKeepsTheHands()
        {
            var source = new Grass();
            var w = new World(R, 576, 576, 7);
            w.MakeFrom(source);
            WorldBuildings.Load(ChemistryTests.WorldDir()).LayInto(w);
            w.SetHold(WorldHold.Load(ChemistryTests.WorldDir()));
            Stand(w, Me, Room(w));
            w.ApplyCombo(ComboCommand.Attune(Me, Left, "air"));
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            Away(w);
            Bumper(w, Right);
            Ticks(w, 2);
            var loaded = World.Load(w.Save(source), R, source);
            loaded.SetHold(w.HoldSite);
            Assert.That(loaded.HandsText(), Is.EqualTo(w.HandsText()));
            Assert.That(loaded.CastState(), Is.EqualTo(w.CastState()));
            Assert.That(Fired(loaded), Is.EqualTo("fire-bolt"));
        }

        // The rules with hold.json's hands edited, as the rule check finds them.
        static System.Collections.Generic.IReadOnlyList<string> Errors(System.Action<Newtonsoft.Json.Linq.JObject> edit)
        {
            var files = System.IO.Directory.GetFiles(ChemistryTests.RulesDir(), "*.json").ToDictionary(System.IO.Path.GetFileName, System.IO.File.ReadAllText);
            var hold = Newtonsoft.Json.Linq.JObject.Parse(files["hold.json"]);
            edit((Newtonsoft.Json.Linq.JObject)hold["hands"]);
            files["hold.json"] = hold.ToString();
            var r = Rules.FromJson(files);
            return Assert.Throws<ModException>(() => r.WithMods(new[] { ModPatch.Parse("{ 'id': 'check' }") })).Errors;
        }

        [Test]
        public void TheRulesSayABadRow()
        {
            Assert.That(() => R.WithMods(new[] { ModPatch.Parse("{ 'id': 'check' }") }), Throws.Nothing, "the table as it stands is good");
            Assert.That(Errors(h => h["window"] = 7).All(e => e.EndsWith("castTicks must be at least the window", System.StringComparison.Ordinal)));
            Assert.That(Errors(h => h["spells"]["fire"]["throw"]["spell"] = "nope"), Does.Contain("hands: fire throw: unknown spell nope"));
            Assert.That(Errors(h => ((Newtonsoft.Json.Linq.JObject)h["spells"]["fire"]).Remove("sweep")), Does.Contain("hands: fire sweep: missing"));
            Assert.That(Errors(h => ((Newtonsoft.Json.Linq.JObject)h["range"]).Remove("long")), Does.Contain("hands: throw has no range"));
            Assert.That(Errors(h => h["kit"][0]["spell"] = "nope"), Does.Contain("hands: kit: unknown spell nope"));
            Assert.That(Errors(h => h["start"]["left"] = "nope"), Does.Contain("hands: start left must be in the table"));
            Assert.That(Errors(h => h["attuneTicks"] = 0), Does.Contain("hands: attuneTicks must be a whole number from 1"));
        }
    }
}
