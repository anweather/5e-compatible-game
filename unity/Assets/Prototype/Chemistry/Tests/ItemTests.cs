using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// items.json: parsing, the checks (the same wording as validateRules in
    /// sandbox/src/rules.ts; the expected list is the one in sandbox/test/items.test.ts),
    /// when a lantern breaks, and what it spills (World.Spill).
    /// </summary>
    public class ItemTests
    {
        static Rules R => ChemistryTests.R;

        static ItemDef Lantern => R.Item("bullseye-lantern");

        static ModPatch Mod(string json) => ModPatch.Parse(json.Replace('\'', '"'));

        [Test]
        public void ItemsLoadFromRulesInSrdUnits()
        {
            Assert.That(R.Items.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(R.Item("nope"), Is.Null);

            var ball = R.Item("glow-ball-amber").Light;
            Assert.That((ball.Bright, ball.Dim, ball.Cone), Is.EqualTo((20.0, 20.0, 0.0)), "Light: 20 ft bright, 20 ft dim, all round");
            Assert.That(ball.BrightMetres, Is.EqualTo(6.096).Within(1e-9));
            Assert.That(ball.RangeMetres, Is.EqualTo(12.192).Within(1e-9));

            var beam = Lantern.Light;
            Assert.That(beam.RangeMetres, Is.EqualTo(36.576).Within(1e-9), "60 ft bright + 60 ft dim");
            Assert.That(beam.Cone, Is.EqualTo(2 * System.Math.Atan(0.5) * 180 / System.Math.PI).Within(0.01), "a 5e cone is as wide as it is long");
            Assert.That(Lantern.Use, Is.EqualTo(ItemUses.Light));

            Assert.That(R.Item("spyglass").Zoom, Is.EqualTo(2));
            Assert.That(R.Item("sending-stone").Message, Is.Not.Empty);
            Assert.That(R.Item("dancing-lights").Reach, Is.EqualTo(60));
        }

        [Test]
        public void TheLanternBreaksOnlyWhenItLandsHard()
        {
            Assert.That(Lantern.Breaks.Speed, Is.EqualTo(8));
            Assert.That(Lantern.BreaksAt(4.4), Is.False, "dropped from hand height (1 m): about 4.4 m/s");
            Assert.That(Lantern.BreaksAt(7.99), Is.False);
            Assert.That(Lantern.BreaksAt(8), Is.True);
            Assert.That(Lantern.BreaksAt(12), Is.True, "thrown hard");
            Assert.That(R.Item("spyglass").BreaksAt(100), Is.False, "no breaks: never breaks");
            foreach (var ball in new[] { "glow-ball-amber", "glow-ball-azure", "glow-ball-rose" })
            {
                Assert.That(R.Item(ball).BreaksAt(4.4), Is.False, $"{ball}: a drop from the hand");
                Assert.That(R.Item(ball).BreaksAt(12), Is.True, $"{ball}: glass, thrown hard");
                Assert.That(R.Item(ball).Breaks.Spill, Is.Null, $"{ball}: nothing to spill, it just shatters");
                Assert.That(R.ItemHas(ball, "brittle"), Is.True, $"{ball}: brittle from its glass, so it shatters into shards");
            }
        }

        [Test]
        public void ABrokenLanternSpillsBurningOilOnItsSquare()
        {
            var w = ChemistryTests.Room(7, 7);
            Assert.That(w.Spill(3, 3, Lantern), Is.EqualTo(1), "radius 0: the one 5 ft square");
            var c = w.At(3, 3);
            Assert.That(c.Cover, Is.EqualTo("oil"));
            Assert.That(c.Fire, Is.GreaterThanOrEqualTo(3));
            Assert.That(w.Log[w.Log.Count - 1].Text, Does.Contain("bullseye lantern smashes"));
            Assert.That(w.Spill(3, 3, R.Item("spyglass")), Is.EqualTo(0), "nothing to spill");
            Assert.That(w.Spill(-1, 3, Lantern), Is.EqualTo(0), "off the board");
        }

        [Test]
        public void AnItemRunsAsALightUnbreakableProp()
        {
            var e = R.Item("spyglass").AsEntity();
            var prop = new PropState(e);
            Assert.That(prop.Floats, "mass 1 floats");
            Assert.That(e.Flammable, Is.False);
            Assert.That(prop.Damage(50), Is.False, "breaks by landing hard, not by damage");
        }

        [Test]
        public void AModCanReplaceAnItem()
        {
            var mod = ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "bright-lantern.json")));
            var r = R.WithMods(new[] { mod });
            Assert.That(r.Item("bullseye-lantern").Light.Bright, Is.EqualTo(120));
            Assert.That(r.Item("bullseye-lantern").Breaks.Spill.Radius, Is.EqualTo(1));
            Assert.That(R.Item("bullseye-lantern").Light.Bright, Is.EqualTo(60), "the base rules are untouched");
            Assert.That(r.Fingerprint, Is.Not.EqualTo(R.Fingerprint));
        }

        [Test]
        public void ABrokenItemIsRefusedWithTheSandboxWording()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { Mod(@"{ 'items': {
                'crate': { 'name': 'Crate', 'mass': 0 },
                'torch': { 'name': '', 'use': 'light', 'light': { 'bright': -1, 'color': 'orange', 'cone': 200 } },
                'flask': { 'name': 'Flask', 'breaks': { 'spill': { 'cover': 'lava', 'radius': -2 } } },
                'stone': { 'name': 'Stone', 'use': 'send' },
                'glass': { 'name': 'Glass', 'use': 'zoom', 'zoom': 1 },
                'mote': { 'name': 'Mote', 'use': 'point', 'reach': 'far' },
                'wand': { 'name': 'Wand', 'use': 'wave', 'light': 3 } } }") })).Errors;
            Assert.That(errors, Is.EqualTo(new[]
            {
                "item crate: id is taken by an entity",
                "item crate: mass must be a number above 0",
                "item torch: needs a name",
                "item torch: light.bright must be a number from 0",
                "item torch: light.cone must be a number from 0 to 179",
                "item torch: light.color must be #rrggbb",
                "item flask: breaks.speed must be a number above 0",
                "item flask: breaks.spill.cover lava unknown",
                "item flask: breaks.spill.radius must be a number from 0",
                "item stone: use send needs a message",
                "item glass: use zoom needs a zoom above 1",
                "item mote: reach must be a number from 0",
                "item mote: use point needs a light",
                "item mote: use point needs a reach above 0",
                "item wand: light must be an object",
                "item wand: unknown use wave",
            }));
        }

        [Test]
        public void AnItemsSectionOfTheWrongTypeIsRefused()
        {
            var e = Assert.Throws<ModException>(() => Mod("{ 'items': { 'ball': 3 } }"));
            Assert.That(e.Errors, Is.EqualTo(new[] { "\"items\" must be an object of entries by id" }));
        }
    }
}
