using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The spell radial (docs/SPELL_RADIAL.md): the groups in the rules, the wedge and arc
    /// geometry, and the state machine for both pointer and stick input.
    /// </summary>
    public class RadialTests
    {
        static Rules baseRules;

        static Rules Base => baseRules ??= Rules.Load(ChemistryTests.RulesDir());

        static Rules With(string mod) => Base.WithMods(new[] { ModPatch.Parse(mod.Replace('\'', '"')) });

        static readonly RadialTuning Tuning = new RadialTuning();

        // The arc layout below (FiveEarth), clockwise from the top, 72° a wedge: fire 0°, water 72°, storm 144°, earth 216°, arcane 288°.
        // The game's own layout has a sixth, Time & Weather, after Arcane.
        const int Fire = 0, Water = 1, Storm = 2, Earth = 3, Arcane = 4;

        // The arc mechanics are tested on the layout they were written for: Fire Bolt alone under
        // Fire, an Earth wedge of five spells (Earthen Trench and Move Earth back on the radial), and
        // no Time & Weather wedge (its spells off the radial, so the five wedges stay 72° apart).
        // TheLayoutFollowsTheGroupsAndSpellsJsonOrder checks the game's own layout.
        static Rules fiveEarth;
        static Rules FiveEarth => fiveEarth ??= With(@"{ 'spells': [
            { 'id': 'burning-hands', 'group': 'fire', 'name': 'Burning Hands', 'level': 1, 'shape': 'cone', 'origin': 'self', 'length': 3, 'element': 'fire', 'radial': false, 'blurb': '' },
            { 'id': 'fireball', 'group': 'fire', 'name': 'Fireball', 'level': 3, 'shape': 'burst', 'radius': 2, 'element': 'fire', 'radial': false, 'blurb': '' },
            { 'id': 'meteor', 'group': 'fire', 'name': 'Meteor', 'level': 7, 'shape': 'burst', 'radius': 4, 'element': 'fire', 'radial': false, 'blurb': '' },
            { 'id': 'earthen-trench', 'group': 'earth', 'name': 'Earthen Trench', 'level': 2, 'shape': 'line', 'origin': 'self', 'length': 8, 'effect': 'terraform', 'dig': 2, 'blurb': '' },
            { 'id': 'move-earth', 'group': 'earth', 'name': 'Move Earth', 'level': 6, 'shape': 'burst', 'radius': 3, 'effect': 'terraform', 'raise': 2, 'taper': true, 'blurb': '' },
            { 'id': 'hasten-time', 'group': 'time', 'name': 'Hasten Time', 'level': 3, 'shape': 'burst', 'origin': 'self', 'radius': 0, 'effect': 'haste', 'rate': 20, 'duration': 180, 'radial': false, 'blurb': '' },
            { 'id': 'control-weather', 'group': 'time', 'name': 'Control Weather', 'level': 8, 'shape': 'burst', 'origin': 'self', 'radius': 0, 'effect': 'weather', 'duration': 720, 'radial': false, 'blurb': '' },
            { 'id': 'fog-cloud', 'group': 'time', 'name': 'Fog Cloud', 'level': 1, 'shape': 'burst', 'radius': 4, 'element': 'fog', 'radial': false, 'blurb': '' } ] }");

        static SpellRadial NewRadial() => new SpellRadial(RadialLayout.From(FiveEarth));

        /// <summary>A vector at a compass angle (degrees clockwise from up), y up.</summary>
        static double X(double angle, double length) => length * Math.Sin(angle * Math.PI / 180);
        static double Y(double angle, double length) => length * Math.Cos(angle * Math.PI / 180);

        static void Stick(SpellRadial r, double angle, double length, double time) =>
            r.Feed(RadialInput.Stick(X(angle, length), Y(angle, length), true, time));

        static RadialResult Centre(SpellRadial r, double time, bool held = true, bool cancel = false) =>
            r.Feed(RadialInput.Stick(0, 0, held, time, cancel));

        static RadialResult Move(SpellRadial r, double dx, double dy, double time, bool held = true) =>
            r.Feed(RadialInput.Mouse(dx, dy, held, time));

        static void IsSelected(RadialResult result, string id)
        {
            Assert.That(result.Outcome, Is.EqualTo(RadialOutcome.Selected));
            Assert.That(result.SpellId, Is.EqualTo(id));
        }

        // ---------- data ----------

        [Test]
        public void EveryBaseSpellIsInAKnownGroup()
        {
            Assert.That(Base.Groups.Select(g => g.Id), Is.EqualTo(new[] { "fire", "water", "storm", "earth", "arcane", "time" }));
            foreach (var s in Base.Spells) Assert.That(Base.Group(s.Group), Is.Not.Null, s.Id);
            Assert.That(Base.Group("water").Name, Is.EqualTo("Water & Ice"));
            Assert.That(Base.Groups.All(g => g.Color.Length == 7 && g.Color[0] == '#'));
            Assert.That(Base.Spell("grease").Group, Is.EqualTo("arcane"));
        }

        [Test]
        public void TheLayoutFollowsTheGroupsAndSpellsJsonOrder()
        {
            var layout = RadialLayout.From(Base);
            Assert.That(layout.Groups.Select(g => g.Id), Is.EqualTo(new[] { "fire", "water", "storm", "earth", "arcane", "time" }));
            Assert.That(layout.Groups[5].SpellIds, Is.EqualTo(new[] { "hasten-time", "control-weather", "fog-cloud" }));
            Assert.That(layout.Groups[Fire].SpellIds, Is.EqualTo(new[] { "fire-bolt", "fireball", "meteor" }), "Burning Hands stays off the radial");
            Assert.That(RadialLayout.From(FiveEarth).Groups[Fire].SpellIds, Is.EqualTo(new[] { "fire-bolt" }), "a group with one spell");
            Assert.That(layout.Groups[Earth].SpellIds, Is.EqualTo(new[] { "mold-earth", "mold-earth-heap", "wall-of-stone" }), "Earthen Trench and Move Earth stay off the radial");
            Assert.That(RadialLayout.From(FiveEarth).Groups[Earth].SpellIds, Is.EqualTo(new[] { "mold-earth", "mold-earth-heap", "earthen-trench", "wall-of-stone", "move-earth" }), "a group with five");
            Assert.That(layout.Groups[Arcane].SpellIds, Is.EqualTo(new[] { "misty-step", "levitate", "mage-hand" }), "Grease stays off the radial");
            Assert.That(layout.GroupOfSpell("thunderwave"), Is.EqualTo(Storm));
            Assert.That(layout.IndexOf("earth"), Is.EqualTo(Earth));
            Assert.That(layout.GroupOfSpell("nope"), Is.EqualTo(-1));
        }

        [Test]
        public void NoElementHasMoreThanThreeSpells()
        {
            // Andrew's rule for the bar (2026-10-27): at most three spells to an element. A spell
            // past three stays in the rules with "radial": false (for mods, tests and demos).
            foreach (var g in RadialLayout.From(Base).Groups)
                Assert.That(g.SpellIds.Count, Is.LessThanOrEqualTo(3), g.Id + ": " + string.Join(", ", g.SpellIds));
        }

        [Test]
        public void AModCanAddAGroupAndRegroupASpell()
        {
            var r = With(@"{ 'groups': { 'frost': { 'name': 'Frost', 'order': 2.5, 'color': '#bfe6ff' }, 'empty': { 'name': 'Empty', 'order': 9 } },
                'spells': [ { 'id': 'ray-of-frost', 'group': 'frost', 'name': 'Ray of Frost', 'level': 0, 'shape': 'line', 'element': 'cold' } ] }");
            Assert.That(r.Groups.Select(g => g.Id), Is.EqualTo(new[] { "fire", "water", "frost", "storm", "earth", "arcane", "time", "empty" }));
            Assert.That(r.Group("empty").Color, Is.EqualTo(Rules.DefaultGroupColor));
            var layout = RadialLayout.From(r);
            Assert.That(layout.Groups.Select(g => g.Id), Is.EqualTo(new[] { "fire", "water", "frost", "storm", "earth", "arcane", "time" }), "an empty group gets no wedge");
            Assert.That(layout.Groups[1].SpellIds, Is.EqualTo(new[] { "create-water", "sleet-storm" }));
            Assert.That(layout.Groups[2].SpellIds, Is.EqualTo(new[] { "ray-of-frost" }));
            Assert.That(r.Fingerprint, Is.Not.EqualTo(Base.Fingerprint), "groups are in the fingerprint");
        }

        [Test]
        public void ASpellWithNoGroupGoesUnderTheLastElement()
        {
            var r = With("{ 'spells': [ { 'id': 'meteor-swarm', 'name': 'Meteor Swarm', 'level': 9, 'shape': 'burst', 'element': 'fire' } ] }");
            var groups = RadialLayout.From(r).Groups;
            var last = groups[groups.Count - 1];
            Assert.That(last.Id, Is.EqualTo("time"));
            Assert.That(last.SpellIds[last.SpellIds.Count - 1], Is.EqualTo("meteor-swarm"));
        }

        [Test]
        public void RulesWithoutGroupsGetOneWedge()
        {
            var r = Rules.FromJson(new System.Collections.Generic.Dictionary<string, string>
            {
                ["materials.json"] = "{}",
                ["reactions.json"] = "{}",
                ["spells.json"] = "[{ 'id': 'a', 'name': 'A', 'shape': 'point' }, { 'id': 'b', 'name': 'B', 'shape': 'point' }]".Replace('\'', '"'),
            });
            var layout = RadialLayout.From(r);
            Assert.That(layout.Count, Is.EqualTo(1));
            Assert.That(layout.Groups[0].SpellIds, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void UnknownGroupsAndBadGroupsAreRefused()
        {
            var errors = Assert.Throws<ModException>(() => With("{ 'spells': [ { 'id': 'x', 'group': 'nope', 'name': 'X', 'shape': 'point' } ] }")).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "spell x: unknown group nope" }));
            errors = Assert.Throws<ModException>(() => With("{ 'groups': { 'bad': { 'order': 'first', 'color': 'red' } } }")).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "group bad: needs a name", "group bad: order must be a number", "group bad: color must be #rrggbb" }));
            errors = Assert.Throws<ModException>(() => ModPatch.Parse("{ \"groups\": { \"x\": 3 } }")).Errors;
            Assert.That(errors, Is.EqualTo(new[] { "\"groups\" must be an object of entries by id" }));
        }

        // ---------- geometry ----------

        [TestCase(0, Fire)]
        [TestCase(35.999, Fire)]
        [TestCase(36, Water)]
        [TestCase(107.999, Water)]
        [TestCase(108, Storm)]
        [TestCase(179.999, Storm)]
        [TestCase(180, Earth)]
        [TestCase(251.999, Earth)]
        [TestCase(252, Arcane)]
        [TestCase(323.999, Arcane)]
        [TestCase(324, Fire)]
        [TestCase(359.999, Fire)]
        [TestCase(360, Fire)]
        [TestCase(-36, Fire)]
        public void EveryWedgeBoundaryBelongsToTheClockwiseWedge(double angle, int wedge)
        {
            Assert.That(RadialGeometry.WedgeAt(angle, 5), Is.EqualTo(wedge));
        }

        [Test]
        public void AnglesAreClockwiseFromUp()
        {
            Assert.That(RadialGeometry.AngleOf(0, 1), Is.EqualTo(0));
            Assert.That(RadialGeometry.AngleOf(1, 0), Is.EqualTo(90).Within(1e-9));
            Assert.That(RadialGeometry.AngleOf(0, -1), Is.EqualTo(180).Within(1e-9));
            Assert.That(RadialGeometry.AngleOf(-1, 0), Is.EqualTo(270).Within(1e-9));
            Assert.That(RadialGeometry.DeltaAngle(350, 10), Is.EqualTo(20));
            Assert.That(RadialGeometry.DeltaAngle(10, 350), Is.EqualTo(-20));
            Assert.That(RadialGeometry.WedgeAt(0, 0), Is.EqualTo(-1));
            Assert.That(RadialGeometry.WedgeAt(123, 1), Is.EqualTo(0));
        }

        [TestCase(140.9, -1)]
        [TestCase(141, 0)]
        [TestCase(170.9, 0)]
        [TestCase(171, 1)]
        [TestCase(201, 2)]
        [TestCase(216, 2)]
        [TestCase(231, 3)]
        [TestCase(261, 4)]
        [TestCase(291, 4)]
        [TestCase(291.1, -1)]
        public void FiveSpellsSpreadOverAnArcAroundTheirWedge(double angle, int slot)
        {
            // Earth, centred on 216°: five 30° slots, 150° in all.
            Assert.That(RadialGeometry.SlotAt(angle, Earth, 5, 5, Tuning), Is.EqualTo(slot));
        }

        [Test]
        public void ASmallArcIsNeverNarrowerThanItsWedge()
        {
            // Fire Bolt alone fills the whole fire wedge, both edges included.
            Assert.That(RadialGeometry.ArcSpan(1, 5, Tuning), Is.EqualTo(72));
            Assert.That(RadialGeometry.SlotAt(324, Fire, 5, 1, Tuning), Is.EqualTo(0));
            Assert.That(RadialGeometry.SlotAt(36, Fire, 5, 1, Tuning), Is.EqualTo(0));
            Assert.That(RadialGeometry.SlotAt(36.1, Fire, 5, 1, Tuning), Is.EqualTo(-1));
            Assert.That(RadialGeometry.SlotCentre(Fire, 5, 0, 1, Tuning), Is.EqualTo(0));
            Assert.That(RadialGeometry.SlotCentre(Earth, 5, 0, 5, Tuning), Is.EqualTo(156));
            Assert.That(RadialGeometry.SlotCentre(Earth, 5, 4, 5, Tuning), Is.EqualTo(276));
            Assert.That(RadialGeometry.ArcSpan(20, 5, Tuning), Is.EqualTo(300), "a huge group is capped");
        }

        // ---------- stick ----------

        [Test]
        public void StickDwellOpensTheArc()
        {
            var r = NewRadial();
            Centre(r, 0);
            Assert.That(r.State, Is.EqualTo(RadialState.Elements));
            Stick(r, 216, 0.6, 0.05);
            Assert.That((r.State, r.Element, r.Spell), Is.EqualTo((RadialState.Elements, Earth, -1)));
            Stick(r, 216, 0.6, 0.3);
            Assert.That(r.State, Is.EqualTo(RadialState.Elements), "not yet");
            Stick(r, 216, 0.6, 0.4);
            Assert.That((r.State, r.Spell), Is.EqualTo((RadialState.Spells, 2)), "after resting 0.35 s");
            Assert.That(r.HighlightedSpell, Is.EqualTo("earthen-trench"));
        }

        [Test]
        public void StickPushOutOpensTheArcAtTheThreshold()
        {
            var r = NewRadial();
            Stick(r, 216, 0.89, 0);
            Assert.That(r.State, Is.EqualTo(RadialState.Elements));
            Stick(r, 216, 0.9, 0.01);
            Assert.That((r.State, r.Element, r.Spell), Is.EqualTo((RadialState.Spells, Earth, 2)));
            Stick(r, 160, 0.6, 0.02);
            Assert.That(r.HighlightedSpell, Is.EqualTo("mold-earth"), "the stick then picks within the arc");
        }

        [Test]
        public void StickSpringBackKeepsTheHighlightAndReleaseSelectsIt()
        {
            var r = NewRadial();
            Stick(r, 216, 1, 0);
            Stick(r, 160, 1, 0.05);
            Stick(r, 160, 0.2, 0.08);
            Centre(r, 0.1);
            Assert.That((r.State, r.Element, r.HighlightedSpell), Is.EqualTo((RadialState.Spells, Earth, "mold-earth")));
            IsSelected(Centre(r, 0.3, held: false), "mold-earth");
            Assert.That(r.State, Is.EqualTo(RadialState.Closed));
            Assert.That(r.Current, Is.EqualTo("mold-earth"));
        }

        [Test]
        public void StickElementAloneSelectsItsRememberedSpell()
        {
            var r = NewRadial();
            Stick(r, 72, 0.5, 0);
            Centre(r, 0.1);
            IsSelected(Centre(r, 0.2, held: false), "ray-of-frost");
            r.Chose("create-water");
            Stick(r, 72, 0.5, 1);
            IsSelected(Centre(r, 1.1, held: false), "create-water");
        }

        [Test]
        public void StickOutsideTheArcMovesToThatElement()
        {
            var r = NewRadial();
            Stick(r, 216, 1, 0);
            Stick(r, 0, 1, 0.05);
            Assert.That((r.State, r.Element, r.HighlightedSpell), Is.EqualTo((RadialState.Spells, Fire, "fire-bolt")));
            Stick(r, 72, 0.5, 0.1);
            Assert.That((r.State, r.Element), Is.EqualTo((RadialState.Elements, Water)), "back in ring 1, the arc closes");
        }

        [Test]
        public void BCancels()
        {
            var r = NewRadial();
            r.Chose("grease");
            Stick(r, 216, 1, 0);
            var result = Centre(r, 0.2, cancel: true);
            Assert.That(result.Outcome, Is.EqualTo(RadialOutcome.Cancelled));
            Assert.That(r.State, Is.EqualTo(RadialState.Closed));
            Assert.That(r.Current, Is.EqualTo("grease"));
            Assert.That(Centre(r, 0.3, held: false).Outcome, Is.EqualTo(RadialOutcome.None), "the release after it does nothing");
        }

        // ---------- pointer ----------

        [Test]
        public void MouseDeadZoneHighlightsNothingAndReleaseThereCancels()
        {
            var r = NewRadial();
            Move(r, 0.2, 0, 0);
            Assert.That(r.Element, Is.EqualTo(-1));
            Assert.That(Move(r, 0, 0, 0.5, held: false).Outcome, Is.EqualTo(RadialOutcome.Cancelled));
        }

        [Test]
        public void PointerReleaseInTheCentreCancels()
        {
            var r = NewRadial();
            Move(r, 0.6, 0, 0);
            Assert.That(r.Element, Is.EqualTo(Water));
            Move(r, -0.6, 0, 0.1);
            Assert.That(r.Element, Is.EqualTo(-1), "unlike a stick, a pointer back in the centre points at nothing");
            Assert.That(Move(r, 0, 0, 0.2, held: false).Outcome, Is.EqualTo(RadialOutcome.Cancelled));
        }

        [Test]
        public void PointerDwellShowsTheArcAndPushingOutPicks()
        {
            var r = NewRadial();
            Move(r, 0.6, 0, 0);
            Move(r, 0, 0, 0.2);
            Assert.That(r.State, Is.EqualTo(RadialState.Elements));
            Move(r, 0, 0, 0.4);
            Assert.That((r.State, r.Element, r.Spell), Is.EqualTo((RadialState.Spells, Water, -1)), "the arc is out; ring 1 picks no spell");
            Move(r, 0.6, 0, 0.5);
            // The arc's middle spell, whichever of the water spells that is.
            string middle = r.HighlightedSpell;
            Assert.That(Base.Spell(middle)?.Group, Is.EqualTo("water"));
            IsSelected(Move(r, 0, 0, 0.6, held: false), middle);
        }

        [Test]
        public void MousePushOutOpensTheArcAndBackInCloses()
        {
            var r = NewRadial();
            Move(r, 0, 1.2, 0);
            Assert.That((r.State, r.HighlightedSpell), Is.EqualTo((RadialState.Spells, "fire-bolt")));
            Move(r, 0, 5, 0.05);
            Assert.That(r.CursorY, Is.EqualTo(Tuning.MouseMaxRadius).Within(1e-9), "the cursor is held to the ring");
            Move(r, X(90, 0.6), Y(90, 0.6) - 1.6, 0.1);
            Assert.That((r.State, r.Element), Is.EqualTo((RadialState.Elements, Water)));
        }

        [Test]
        public void PointerElementAloneSelectsItsRememberedSpell()
        {
            var r = NewRadial();
            Move(r, X(216, 0.5), Y(216, 0.5), 0);
            IsSelected(Move(r, 0, 0, 0.1, held: false), "mold-earth");
        }

        // ---------- tap and steps ----------

        [Test]
        public void AQuickTapSwapsToThePreviousSpell()
        {
            var r = NewRadial();
            r.Chose("fire-bolt");
            r.Chose("grease");
            Centre(r, 10);
            var result = Centre(r, 10.15, held: false);
            Assert.That((result.Outcome, result.SpellId), Is.EqualTo((RadialOutcome.SwapPrevious, "fire-bolt")));
            Assert.That((r.Current, r.Previous), Is.EqualTo(("fire-bolt", "grease")));
            Move(r, 0, 0, 20);
            result = Move(r, 0, 0, 20.2, held: false);
            Assert.That((result.Outcome, result.SpellId), Is.EqualTo((RadialOutcome.SwapPrevious, "grease")), "a mouse tap too, and back again");
        }

        [Test]
        public void ALongHoldOrAnyMovementIsNotATap()
        {
            var r = NewRadial();
            r.Chose("fire-bolt");
            r.Chose("grease");
            Centre(r, 0);
            Assert.That(Centre(r, 0.25, held: false).Outcome, Is.EqualTo(RadialOutcome.Cancelled), "held too long, nothing highlighted");
            Stick(r, 216, 0.5, 1);
            IsSelected(Centre(r, 1.1, held: false), "mold-earth");
            Assert.That(r.Previous, Is.EqualTo("grease"));
        }

        [Test]
        public void ATapWithNoPreviousSpellSwapsToNothing()
        {
            var r = NewRadial();
            Centre(r, 0);
            var result = Centre(r, 0.1, held: false);
            Assert.That((result.Outcome, result.SpellId), Is.EqualTo((RadialOutcome.SwapPrevious, (string)null)));
        }

        [Test]
        public void StepsMoveWithinAnElementAndBetweenElements()
        {
            var r = NewRadial();
            Assert.That(r.StepInElement(1), Is.EqualTo("fire-bolt"), "nothing chosen yet: the first spell");
            Assert.That(r.StepInElement(1), Is.EqualTo("fire-bolt"), "one spell wraps to itself");
            r.Chose("misty-step");
            Assert.That(r.StepInElement(1), Is.EqualTo("levitate"));
            Assert.That(r.StepInElement(-1), Is.EqualTo("misty-step"));
            Assert.That(r.StepInElement(-1), Is.EqualTo("mage-hand"), "wraps");
            Assert.That(r.StepElement(1), Is.EqualTo("fire-bolt"), "arcane wraps to fire");
            Assert.That(r.StepElement(-1), Is.EqualTo("mage-hand"), "back to arcane's last choice");
            r.Chose("create-water");
            r.StepElement(1);
            Assert.That(r.StepElement(-1), Is.EqualTo("create-water"));
            Assert.That(r.Previous, Is.EqualTo("shocking-grasp"));
            Assert.That(r.State, Is.EqualTo(RadialState.Closed), "steps never open the radial");
        }

        [Test]
        public void NumberKeysOpenStraightOntoAnArc()
        {
            var r = NewRadial();
            r.Chose("wall-of-stone");
            r.Chose("fire-bolt");
            r.OpenAt(Earth, 0);
            Assert.That((r.State, r.HighlightedSpell), Is.EqualTo((RadialState.Spells, "wall-of-stone")));
            IsSelected(Centre(r, 0.05, held: false), "wall-of-stone");
        }
    }
}
