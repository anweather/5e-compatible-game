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

        static void Hand(World w, int hand, bool modified = false, bool taken = true) =>
            Assert.That(w.ApplyCombo(ComboCommand.Hand(Me, hand, modified)), Is.EqualTo(taken), $"hand {hand}");

        // Attuned in the room, then out of it, with a run going.
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

        // The spell that goes off, stepping until one does, and the ticks it took.
        static (string spell, int ticks) Fired(World w)
        {
            for (int i = 1; i < 60; i++)
            {
                w.Step();
                var fired = w.CombosFired.Where(f => f.Player == Me).ToList();
                if (fired.Count > 0) return (fired[0].Spell, i);
            }
            return (null, 0);
        }

        [Test]
        public void TheTableHasTheDesignsTwentyEight()
        {
            var all = Table.All.Select(s => s.Spell).ToList();
            Assert.That(all.Count, Is.EqualTo(28));
            Assert.That(all.Distinct().Count(), Is.EqualTo(28));
            Assert.That(all.All(s => R.Spell(s) != null));
            Assert.That(R.Combos.Elements.All(e => R.Combos.Elements.All(f => Table.Spell(e, f, false) != null && Table.Spell(e, f, true) != null)), "every pair, either order");
            Assert.That(Table.Kit.All(k => R.Spell(k.Spell) != null));
        }

        [Test]
        public void TheDesignsRowsAreThere()
        {
            Assert.That(Table.Spell("fire", null, false).Spell, Is.EqualTo("fire-bolt"));
            Assert.That(Table.Spell("water", null, true).Spell, Is.EqualTo("ray-of-frost"));
            Assert.That(Table.Spell("air", "fire", false).Spell, Is.EqualTo("lightning-bolt"));
            Assert.That(Table.Spell("fire", "air", true).Spell, Is.EqualTo("falling-star"));
            Assert.That(Table.Spell("earth", "earth", false).Spell, Is.EqualTo("wall-of-stone"));
            Assert.That(Table.Spell("fire", "fire", true).Spell, Is.EqualTo("meteor"));
        }

        [Test]
        public void EachHandCastsItsOwnElement()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Assert.That(Fired(w).spell, Is.EqualTo("fire-bolt"));
            Hand(w, Left);
            Assert.That(Fired(w).spell, Is.EqualTo("create-water"));
        }

        [Test]
        public void TheBumperModifiesThatHand()
        {
            var w = Holding("water", "fire");
            Hand(w, Right, modified: true);
            Assert.That(Fired(w).spell, Is.EqualTo("burning-hands"));
            Hand(w, Left, modified: true);
            Assert.That(Fired(w).spell, Is.EqualTo("ray-of-frost"));
        }

        [TestCase(false, false, "lightning-bolt")]
        [TestCase(true, false, "falling-star")]
        [TestCase(false, true, "falling-star")]
        public void BothHandsCombineEitherModified(bool first, bool second, string spell)
        {
            var w = Holding("air", "fire");
            Hand(w, Right, first);
            Hand(w, Left, second);
            Assert.That(Fired(w).spell, Is.EqualTo(spell));
        }

        [TestCase(false, "fireball")]
        [TestCase(true, "meteor")]
        public void BothHandsTheSameElement(bool modified, string spell)
        {
            var w = Holding("fire", "fire");
            Hand(w, Left, modified);
            Hand(w, Right);
            Assert.That(Fired(w).spell, Is.EqualTo(spell));
        }

        [Test]
        public void TheWindowHoldsNothingBack()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Assert.That(Fired(w).ticks, Is.EqualTo(Table.Spell("fire", null, false).CastTicks), "a one-hand spell takes its cast and no more");
        }

        [Test]
        public void TheSecondHandIsTooLateAfterTheWindow()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Ticks(w, Table.Window);
            Hand(w, Left, taken: false);
            Assert.That(Fired(w).spell, Is.EqualTo("fire-bolt"));
        }

        [Test]
        public void TheSameHandTwiceDoesNotCombine()
        {
            var w = Holding("fire", "fire");
            Hand(w, Right);
            Hand(w, Right, taken: false);
            Assert.That(Fired(w).spell, Is.EqualTo("fire-bolt"));
        }

        [Test]
        public void AHandCoolsDownAlone()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Fired(w);
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(Table.HandCooldown));
            Hand(w, Right, taken: false);
            Hand(w, Left);
            Fired(w);
            Ticks(w, w.HandCooling(Me, Right) - 1);
            Hand(w, Right, taken: false);
            Ticks(w, 1);
            Hand(w, Right);
        }

        [Test]
        public void ATwoHandSpellLocksBoth()
        {
            var w = Holding("air", "fire");
            Hand(w, Right);
            Hand(w, Left);
            Fired(w);
            Assert.That(w.HandCooling(Me, Left), Is.EqualTo(Table.BothCooldown));
            Assert.That(w.HandCooling(Me, Right), Is.EqualTo(Table.BothCooldown));
            Hand(w, Left, taken: false);
            Hand(w, Right, taken: false);
            Ticks(w, Table.BothCooldown);
            Hand(w, Left);
        }

        [Test]
        public void ACoolingHandCantJoin()
        {
            var w = Holding("air", "fire");
            Hand(w, Left);
            Fired(w);
            Hand(w, Right);
            Hand(w, Left, taken: false);
            Assert.That(Fired(w).spell, Is.EqualTo("fire-bolt"));
        }

        [Test]
        public void ADroppedCastCostsNoCooldown()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Assert.That(w.ApplyCombo(ComboCommand.Cancel(Me)));
            Assert.That(Fired(w).spell, Is.Null);
            Assert.That(w.HandCooling(Me, Right), Is.Zero);
            Hand(w, Right);
        }

        [Test]
        public void ADroppedCastLeavesNothingToGoOffLater()
        {
            var w = Holding("water", "fire");
            Hand(w, Right);
            Assert.That(w.ApplyCombo(ComboCommand.Cancel(Me)));
            Assert.That(w.ApplyCombo(ComboCommand.CastSelected(Me)));
            Assert.That(Fired(w).spell, Is.EqualTo(Table.Kit[0].Spell));
        }

        [Test]
        public void AModifiedSecondHandTakesTheModifiedSpellsCast()
        {
            var w = Holding("fire", "fire");
            Hand(w, Right);
            Hand(w, Left, modified: true);
            var (spell, ticks) = Fired(w);
            Assert.That(spell, Is.EqualTo("meteor"));
            Assert.That(ticks, Is.EqualTo(Table.Spell("fire", "fire", true).CastTicks));
        }

        [Test]
        public void AttuningWorksOnlyInTheRoom()
        {
            var w = Field();
            var room = Room(w);
            Stand(w, Me, (room.x, room.y));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "earth")));
            Assert.That(w.HandsOf(Me).Element[Right], Is.EqualTo("earth"));
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "no-such")), Is.False);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, 2, "air")), Is.False);
            Away(w);
            Assert.That(w.ApplyCombo(ComboCommand.Attune(Me, Right, "air")), Is.False, "out of the room");
            Assert.That(w.HandsOf(Me).Element[Right], Is.EqualTo("earth"));
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
            Hand(w, Right, taken: false);
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
            Assert.That(Fired(w).spell, Is.EqualTo(Table.Kit[0].Spell));
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
            Assert.That(other.CastHash(), Is.Not.EqualTo(one.CastHash()));
        }

        [Test]
        public void TwoWorldsMatchThroughTheSameInputs()
        {
            var one = Holding("air", "fire", 11);
            var other = Holding("air", "fire", 11);
            foreach (var w in new[] { one, other })
            {
                Hand(w, Right);
                Hand(w, Left, modified: true);
                Fired(w);
                Ticks(w, Table.BothCooldown);
                Hand(w, Left);
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
            Hand(w, Right);
            Ticks(w, 2);
            var loaded = World.Load(w.Save(source), R, source);
            loaded.SetHold(w.HoldSite);
            Assert.That(loaded.HandsText(), Is.EqualTo(w.HandsText()));
            Assert.That(loaded.CastState(), Is.EqualTo(w.CastState()));
            Assert.That(Fired(loaded).spell, Is.EqualTo("fire-bolt"));
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
            Assert.That(Errors(h => h["spells"]["fire"]["plain"]["spell"] = "nope"), Does.Contain("hands: fire plain: unknown spell nope"));
            Assert.That(Errors(h => ((Newtonsoft.Json.Linq.JObject)h["spells"]).Remove("fire+air")), Does.Contain("hands: fire+air plain: unknown spell undefined"));
            Assert.That(Errors(h => h["kit"][0]["spell"] = "nope"), Does.Contain("hands: kit: unknown spell nope"));
            Assert.That(Errors(h => h["start"]["left"] = "nope"), Does.Contain("hands: start left must be an element"));
        }
    }
}
