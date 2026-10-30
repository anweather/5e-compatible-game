// Element combos (docs/SPELL_COMBOS.md). The goldens are shared with
// sandbox/test/combos.test.ts.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class ComboTests
    {
        static readonly string[] Elements = { "fire", "water", "earth", "air" };
        static Rules rules;

        static Rules Base => rules ??= Rules.Load(ChemistryTests.RulesDir());

        static bool Load(World w, string player, string element) => w.ApplyCombo(ComboCommand.Load(player, element));
        static bool Cancel(World w, string player) => w.ApplyCombo(ComboCommand.Cancel(player));
        static void Cycle(World w, string player, int by) => w.ApplyCombo(ComboCommand.Cycle(player, by));
        static bool CastSelected(World w, string player) => w.ApplyCombo(ComboCommand.CastSelected(player));

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

        static IReadOnlyList<string> Patched(string combos)
        {
            var e = Assert.Throws<ModException>(() => Base.WithMods(new[] { ModPatch.Parse("{ 'combos': " + combos + " }") }));
            return e.Errors;
        }

        static World Small() => new World(Base, 8, 8, 1);

        [Test]
        public void TheTableHasFourSinglesAndSixteenOrderedPairsOnRealSpells()
        {
            Assert.DoesNotThrow(() => Base.WithMods(System.Array.Empty<ModPatch>()));
            var c = Base.Combos;
            Assert.That(c.Elements, Is.EqualTo(Elements));
            foreach (var first in Elements)
            {
                Assert.That(c.Combo(first), Is.Not.Null, first);
                foreach (var second in Elements) Assert.That(c.Combo(CombosDef.IdOf(first, second)), Is.Not.Null, first + "+" + second);
            }
            Assert.That(c.MoveFactor, Is.EqualTo(0.6));
            Assert.That(c.Combo("fire+air").Name, Is.EqualTo("Flame Wave"));
            Assert.That(c.Combo("air+fire").Name, Is.EqualTo("Lightning"));
            Assert.That(c.Combo("fire+fire").Held, Is.EqualTo("meteor"));
        }

        [Test]
        public void EveryComboCastsARealSpellInThatSpellsShape()
        {
            var all = Base.Combos.All;
            Assert.That(all, Has.Count.EqualTo(20));
            foreach (var d in all) Assert.That(d.Shape, Is.EqualTo(Base.Spell(d.Spell).Shape), d.Id);
            Assert.That(all.Count(d => d.Todo != null), Is.EqualTo(11));
            Assert.That(Base.Combos.Combo("fire+water").Spell, Is.EqualTo("fog-cloud"));
            Assert.That(Base.Combos.Combo("fire+water").Todo, Is.Not.Null);
        }

        [Test]
        public void TheDpadListsTheSpellsWithNoElement()
        {
            Assert.That(Base.Combos.Dpad.Select(d => d.Name), Is.EqualTo(new[] { "Hasten Time", "Shapeshift", "Light", "Control Weather" }));
            Assert.That(Base.Combos.Dpad.Select(d => d.Spell), Is.EqualTo(new[] { "hasten-time", "shapeshift", null, "control-weather" }));
        }

        [Test]
        public void OneElementGoesOffWhenItsMeterDrainsAndThePlayerWalksSlowerMeanwhile()
        {
            var w = Small();
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(1));
            Assert.That(Load(w, "ann", "fire"));
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(0.6));
            Run(w, 5);
            Assert.That(w.CastOf("ann").Meter, Is.EqualTo(1));
            Assert.That(w.CombosFired, Is.Empty);
            w.Step();
            var f = w.CombosFired.Single();
            Assert.That((f.Player, f.Combo, f.Name, f.Spell, f.Shape, f.Discovered), Is.EqualTo(("ann", "fire", "Fire Bolt", "fire-bolt", "point", true)));
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(1));
            Assert.That(w.CastOf("ann").First, Is.Null);
            w.Step();
            Assert.That(w.CombosFired, Is.Empty, "only for the tick it went off");
        }

        [Test]
        public void ASecondElementInsideTheWindowMakesItTheOrderedPairOnThePairsMeter()
        {
            var w = Small();
            Load(w, "ann", "air");
            Run(w, 3);
            Assert.That(Load(w, "ann", "fire"));
            Assert.That(w.CastOf("ann").Second, Is.EqualTo("fire"));
            Assert.That(w.CastOf("ann").Meter, Is.EqualTo(9));
            Assert.That(w.CastOf("ann").Total, Is.EqualTo(9));
            Assert.That(UntilFired(w), Is.EqualTo(9));
            Assert.That(w.CombosFired[0].Combo, Is.EqualTo("air+fire"));
            Assert.That(w.CombosFired[0].Spell, Is.EqualTo("shocking-grasp"));
        }

        [Test]
        public void TheWindowShutsAThirdElementIsRefusedAndSoIsAnUnknownOne()
        {
            var w = Small();
            Assert.That(Load(w, "ann", "cold"), Is.False);
            Load(w, "ann", "fire");
            Run(w, 4);
            Assert.That(Load(w, "ann", "air"), Is.False, "the window was 4 ticks");
            Assert.That(UntilFired(w), Is.EqualTo(2));
            Assert.That(w.CombosFired[0].Combo, Is.EqualTo("fire"));
            Load(w, "ann", "fire");
            Assert.That(Load(w, "ann", "air"));
            Assert.That(Load(w, "ann", "water"), Is.False);
            Assert.That(w.CastOf("ann").Second, Is.EqualTo("air"));
        }

        [Test]
        public void CancelDropsTheElementsAndNothingCasts()
        {
            var w = Small();
            Assert.That(Cancel(w, "ann"), Is.False, "nothing to drop");
            Load(w, "ann", "earth");
            Load(w, "ann", "earth");
            Run(w, 3);
            Assert.That(Cancel(w, "ann"));
            Assert.That(w.CastOf("ann").First, Is.Null);
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(1));
            for (int t = 0; t < 20; t++)
            {
                w.Step();
                Assert.That(w.CombosFired, Is.Empty);
            }
            Assert.That(w.KnowsCombo("ann", "earth+earth"), Is.False);
        }

        [Test]
        public void AComboIsUnknownUntilCastPerPlayerAndOnlyTheFirstCastDiscoversIt()
        {
            var w = Small();
            Assert.That(w.KnowsCombo("ann", "fire"), Is.False);
            Load(w, "ann", "fire");
            Load(w, "bob", "water");
            Run(w, 6);
            Assert.That(w.CombosFired.Select(f => (f.Player, f.Combo, f.Discovered)), Is.EqualTo(new[] { ("ann", "fire", true), ("bob", "water", true) }));
            Assert.That(w.KnowsCombo("ann", "fire"));
            Assert.That(w.KnowsCombo("bob", "fire"), Is.False);
            Load(w, "ann", "fire");
            Run(w, 6);
            Assert.That(w.CombosFired[0].Discovered, Is.False);
            Assert.That(w.CastOf("ann").Known, Is.EqualTo(new[] { "fire" }));
        }

        [Test]
        public void TheDpadCyclesBothWaysCastsThroughTheMeterAndRefusesASlotWithNoSpell()
        {
            var w = Small();
            Assert.That(w.SelectedDpad("ann").Name, Is.EqualTo("Hasten Time"));
            Cycle(w, "ann", -1);
            Assert.That(w.SelectedDpad("ann").Name, Is.EqualTo("Control Weather"));
            Cycle(w, "ann", 1);
            Cycle(w, "ann", 1);
            Assert.That(w.CastOf("ann").Slot, Is.EqualTo(1));
            Cycle(w, "ann", 1);
            Assert.That(w.SelectedDpad("ann").Name, Is.EqualTo("Light"));
            Assert.That(CastSelected(w, "ann"), Is.False, "Light has no spell yet");
            Cycle(w, "ann", -1);
            Assert.That(CastSelected(w, "ann"));
            Assert.That(w.MoveFactorOf("ann"), Is.EqualTo(0.6));
            Assert.That(Load(w, "ann", "fire"), Is.False, "no element during a d-pad cast");
            Assert.That(CastSelected(w, "ann"), Is.False);
            Assert.That(UntilFired(w), Is.EqualTo(12));
            var f = w.CombosFired.Single();
            Assert.That((f.Player, f.Combo, f.Name, f.Spell, f.Shape, f.Discovered), Is.EqualTo(("ann", (string)null, "Shapeshift", "shapeshift", "target", false)));
            Assert.That(w.CastOf("ann").Known, Is.Empty);
        }

        sealed class Blank : IChunkSource
        {
            public string Version => "blank";
            public void Make(int x, int y, Cell cell) { }
        }

        [Test]
        public void ACastInProgressAndTheGrimoireComeThroughASave()
        {
            var w = Small();
            Load(w, "ann", "fire");
            Run(w, 6);
            Load(w, "ann", "air");
            Load(w, "ann", "fire");
            Cycle(w, "bob", 1);
            w.Step();
            var back = World.Load(w.Save(new Blank()), Base, new Blank());
            Assert.That(back.CastState(), Is.EqualTo(w.CastState()));
            Assert.That(back.CastState(), Is.EqualTo("ann:air:fire:8:9:0:0:-1:fire/bob:-:-:0:0:0:1:-1:"));
            Assert.That(UntilFired(back), Is.EqualTo(8));
            Assert.That(back.KnowsCombo("ann", "air+fire"));
        }

        [Test]
        public void ASaveWithNoCastsHasNoCastsKey()
        {
            Assert.That(JObject.Parse(Small().Save(new Blank()))["casts"], Is.Null);
        }

        [Test]
        public void ATimelineReplaysTheCombosALiveWorldRan()
        {
            var commands = new List<(int tick, ComboCommand command)>
            {
                (3, ComboCommand.Load("ann", "water")),
                (4, ComboCommand.Load("ann", "air")),
                (5, ComboCommand.Load("bob", "earth")),
                (7, ComboCommand.Cancel("bob")),
                (9, ComboCommand.Cycle("bob", -1)),
            };
            var live = Small();
            var set = RuleSet.Plain(Base);
            for (int t = 1; t <= 30; t++)
            {
                var b = new TickBatch(t);
                b.Combos.AddRange(commands.Where(c => c.tick == t).Select(c => c.command));
                b.RunOn(live, set, out _);
            }
            var replay = Small();
            var timeline = TickBatch.Timeline(null, null, 30);
            TickBatch.AddCombos(timeline, commands);
            for (int t = 1; t <= 30; t++) timeline[t].RunOn(replay, set, out _);
            Assert.That(replay.CastHash(), Is.EqualTo(live.CastHash()));
            Assert.That(live.CastState(), Is.EqualTo("ann:-:-:0:0:0:0:-1:water+air/bob:-:-:0:0:0:3:-1:"));
        }

        [Test]
        public void ATickInSlicesDrainsTheMeterTheSame()
        {
            var w = Small();
            Load(w, "ann", "fire");
            int ticks = 0;
            while (ticks < 6)
                if (w.StepSome(() => true)) ticks++;
            Assert.That(w.KnowsCombo("ann", "fire"));
        }

        [Test]
        public void TheTablesChecksUseTheSandboxWording()
        {
            Assert.That(Patched("{ 'window': 0, 'moveFactor': 2 }"), Is.EqualTo(new[] { "combos: window must be a whole number from 1", "combos: moveFactor must be above 0 and at most 1" }));
            Assert.That(Patched("{ 'window': 7 }"), Is.EqualTo(Elements.Select(e => $"combo {e}: castTicks must be at least the window")));
            Assert.That(Patched("{ 'singles': { 'fire': { 'name': '', 'spell': 'nope', 'shape': 'blob', 'castTicks': 0, 'held': 'nada' } } }"), Is.EqualTo(new[]
            {
                "combo fire: needs a name",
                "combo fire: unknown spell nope",
                "combo fire: unknown shape blob",
                "combo fire: castTicks must be a whole number from 1",
                "combo fire: held spell nada unknown",
                "combo fire: castTicks must be at least the window",
            }));
            Assert.That(Patched("{ 'elements': ['fire', 'void'], 'pairs': { 'fire': { 'void': { 'name': 'V', 'spell': 'fireball', 'shape': 'burst', 'castTicks': 3 } } } }"), Is.EqualTo(new[]
            {
                "combos: unknown element void",
                "combo void: missing",
                "combo void+fire: missing",
                "combo void+void: missing",
            }));
            Assert.That(Patched("{ 'dpad': [ { 'spell': 'nope', 'castTicks': 0 } ] }"), Is.EqualTo(new[] { "dpad 0: needs a name", "dpad 0: unknown spell nope", "dpad 0: castTicks must be a whole number from 1" }));
        }

        [Test]
        public void AModCanChangeAComboAndACastItsModTookAwayDoesntGoOff()
        {
            var w = Small();
            Load(w, "ann", "fire");
            var r = Base.WithMods(new[] { ModPatch.Parse("{ 'combos': { 'singles': { 'fire': { 'name': 'Spark', 'spell': 'shocking-grasp', 'shape': 'point', 'castTicks': 6 } } } }") });
            Assert.That(r.Fingerprint, Is.Not.EqualTo(Base.Fingerprint), "combos are in the fingerprint");
            Assert.That(Base.Combos.Combo("fire").Name, Is.EqualTo("Fire Bolt"), "the base rules are untouched");
            w.ApplyRules(r);
            Assert.That(UntilFired(w), Is.EqualTo(6));
            Assert.That(w.CombosFired[0].Name, Is.EqualTo("Spark"));
            CastSelected(w, "bob");
            w.ApplyRules(Base.WithMods(new[] { ModPatch.Parse("{ 'combos': { 'dpad': [] } }") }));
            for (int t = 0; t < 12; t++)
            {
                w.Step();
                Assert.That(w.CombosFired, Is.Empty);
            }
            Assert.That(w.CastOf("bob").Meter, Is.EqualTo(0));
        }

        // The goldens: the same steps as sandbox/test/combos.test.ts give the same text and hashes.

        [Test]
        public void GoldenComboCast()
        {
            var w = new World(Base, 16, 12, 7);
            for (int x = 0; x < 16; x++) w.At(x, 6).T = "grass";
            Load(w, "ann", "fire");
            Run(w, 2);
            Load(w, "ann", "fire");
            Assert.That(w.CastState(), Is.EqualTo("ann:fire:fire:12:12:0:0:-1:"));
            Assert.That(UntilFired(w), Is.EqualTo(12));
            Assert.That(w.CastState(), Is.EqualTo("ann:-:-:0:0:0:0:-1:fire+fire"));
            w.Cast(w.CombosFired[0].Spell, 8, 6, 1);
            Run(w, 6);
            Assert.That($"{w.CastHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("55751533 6184569 368494589"));
        }

        [Test]
        public void GoldenCancel()
        {
            var w = new World(Base, 16, 12, 7);
            Load(w, "ann", "water");
            w.Step();
            Load(w, "ann", "earth");
            Run(w, 3);
            Load(w, "bob", "air");
            Assert.That(w.CastState(), Is.EqualTo("ann:water:earth:6:9:0:0:-1:/bob:air:-:6:6:4:0:-1:"));
            Cancel(w, "ann");
            Run(w, 20);
            Assert.That(w.CastState(), Is.EqualTo("ann:-:-:0:0:0:0:-1:/bob:-:-:0:0:0:0:-1:air"));
            Assert.That($"{w.CastHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("2f3926dc 1c93d7c5 7"));
        }

        [Test]
        public void GoldenDiscovery()
        {
            var w = new World(Base, 16, 12, 7);
            Load(w, "ann", "fire");
            Load(w, "bob", "air");
            Run(w, 6);
            Load(w, "ann", "air");
            Load(w, "ann", "fire");
            Cycle(w, "bob", -1);
            CastSelected(w, "bob");
            Run(w, 9);
            Load(w, "ann", "fire");
            Run(w, 9);
            Assert.That(w.CastState(), Is.EqualTo("ann:-:-:0:0:0:0:-1:air+fire,fire/bob:-:-:0:0:0:3:-1:air"));
            Assert.That($"{w.CastHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("f2b02f12 1c93d7c5 7"));
        }
    }
}
