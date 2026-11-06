// The hands (docs/design/the-hold.md, phase 3), on the map's own rows: where the attuning room is comes from them and hold.json.
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
            Assert.That(spells.Count, Is.EqualTo(12 + 16 + 4));
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

        [Test]
        public void TheSixteenPairsAreThere()
        {
            var combos = R.Combos.All.Where(c => c.Id.Contains('+')).Select(c => c.Spell).ToList();
            var table = R.Combos.Elements.SelectMany(a => R.Combos.Elements.SelectMany(b => new[] { T.Both(a, b, Thrust).Spell, T.Both(a, b, Throw).Spell })).Distinct().ToList();
            Assert.That(combos.Count, Is.EqualTo(16));
            Assert.That(combos.All(table.Contains), string.Join(",", combos.Where(c => !table.Contains(c))));
            Assert.That(table.Count, Is.EqualTo(20), "and four same-element extras");
            Assert.That(R.Combos.Elements.All(a => R.Combos.Elements.All(b => T.Both(a, b, Sweep) == null)), "sweep and sweep is empty for now");
        }

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

        [TestCase(false, "lightning-bolt")]
        [TestCase(true, "falling-star")]
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
        [TestCase("air", Throw, "dust-storm")]
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
            var (fired, _) = Out(w);
            Assert.That(fired, Is.EquivalentTo(new[] { "burning-hands", "ray-of-frost" }));
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(T.HandCooldown), "each cools on its own");
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(T.HandCooldown));
        }

        [Test]
        public void ASweepAndAThrustAreBoth()
        {
            var w = Holding("water", "fire");
            Trigger(w, Right);
            Bumper(w, Right);
            Trigger(w, Left);
            var (fired, ticks) = Out(w);
            Assert.That(fired, Is.EquivalentTo(new[] { "flame-wave", "create-water" }));
            Assert.That(ticks, Is.EqualTo(T.One("fire", Sweep).CastTicks), "together, when the longer is done");
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
        public void AnEmptyHandActs(int gesture, string act)
        {
            var w = Holding(null, "fire");
            if (gesture != Throw) Trigger(w, Left);
            if (gesture != Thrust) Bumper(w, Left);
            var (fired, _) = Out(w);
            Assert.That(fired, Is.EqualTo(new[] { act }));
            Assert.That(w.HandCooling(Me, Left), Is.Zero, "an act has its own limit");
        }

        [Test]
        public void AnEmptyHandAndASpellByTheSameGestureAreBoth()
        {
            var w = Holding(null, "fire");
            Trigger(w, Left);
            Trigger(w, Right);
            Assert.That(Out(w).out_, Is.EquivalentTo(new[] { "punch", "burning-hands" }));
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
        public void AttuningWorksOnlyInTheRoom()
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
            Away(w);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False, "out of the room");
            Assert.That(w.HandsOf(Me).Holds[Right], Is.EqualTo("earth"));
            Stand(w, Me, room, 0);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False, "down in it");
            Assert.That(w.ApplyCombo(ComboCommand.Attune("nobody", Right, "air")), Is.False, "no stand at all");
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
        public void OutsideARunTheHandsAreOff()
        {
            var w = Field();
            Away(w);
            Assert.That(w.HandsOn(Me), Is.False);
            Trigger(w, Right, taken: false);
            Assert.That(w.ApplyCombo(ComboCommand.Load(Me, "fire")), "the element pad stays");
            Assert.That(w.DpadOf(Me), Is.SameAs(R.Combos.Dpad));
        }

        [Test]
        public void AWorldWithNoHoldHasNoHands()
        {
            var w = new World(R, 64, 64, 7);
            Assert.That(w.HandsOn(Me), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, Right)), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False);
            Assert.That(w.HoldReadsStand(1, 1), Is.False);
            Assert.That(w.HandsText(), Is.Empty);
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
        }
    }
}
