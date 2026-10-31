using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The look and effect families (rules/looks.json, docs/RULES_MCP.md "Looks"): the data parses,
    /// the terrain table that used to live in CellDressing came across unchanged, and looks stay
    /// the view's: they reach neither the rules fingerprint nor a world's state.
    /// </summary>
    public class LookTests
    {
        static Rules baseRules;
        static Rules Base => baseRules ??= Rules.Load(ChemistryTests.RulesDir());
        static LookFamilies Families => LookFamilies.Load(ChemistryTests.RulesDir());

        static ModPatch Example(string name) =>
            ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", name + ".json")));

        static ModPatch Mod(string json) => ModPatch.Parse(json.Replace('\'', '"'));

        // CellDressing.TerrainLook as it stood before the table moved to materials.json (colour, height).
        static readonly Dictionary<string, (float r, float g, float b, float height)> OldTable = new Dictionary<string, (float, float, float, float)>
        {
            ["bedrock"] = (0.24f, 0.23f, 0.25f, 1.5f), ["stone"] = (0.50f, 0.50f, 0.52f, 1.2f),
            ["pillar"] = (0.55f, 0.45f, 0.35f, 1.2f), ["rubble"] = (0.45f, 0.43f, 0.40f, 0.05f),
            ["dirt"] = (0.50f, 0.38f, 0.25f, 0f), ["grass"] = (0.33f, 0.55f, 0.22f, 0f),
            ["brush"] = (0.20f, 0.36f, 0.14f, 0.6f), ["tree"] = (0.16f, 0.38f, 0.15f, 2.2f),
            ["stump"] = (0.13f, 0.10f, 0.08f, 0.15f), ["wood"] = (0.58f, 0.40f, 0.22f, 0f),
            ["deadfall"] = (0.32f, 0.23f, 0.15f, 0.4f),
            ["sprouts"] = (0.42f, 0.55f, 0.28f, 0f), ["sapling"] = (0.22f, 0.45f, 0.18f, 0.8f),
            ["pit"] = (0.30f, 0.24f, 0.18f, 0f), ["metal"] = (0.30f, 0.29f, 0.28f, 0.06f),
            ["rock"] = (0.47f, 0.45f, 0.42f, 0f), ["brick"] = (0.55f, 0.27f, 0.19f, 0f),
            ["rusted-metal"] = (0.42f, 0.22f, 0.12f, 0.06f), ["mud"] = (0.24f, 0.17f, 0.10f, 0f),
            ["sand"] = (0.86f, 0.77f, 0.56f, 0f), ["glass"] = (0.73f, 0.87f, 0.89f, 0f),
            ["pane"] = (0.66f, 0.82f, 0.88f, 0f), ["gate"] = (0.3f, 0.29f, 0.28f, 0f),
            ["gate_open"] = (0.3f, 0.29f, 0.28f, 0f), ["coil"] = (0.45f, 0.32f, 0.18f, 0f),
            ["logwall"] = (0.4f, 0.27f, 0.16f, 0f), ["worn-logwall"] = (0.52f, 0.42f, 0.3f, 0f),
        };

        // ---------- the data ----------

        [Test]
        public void TheFamiliesParse()
        {
            var blast = Families.Find("spells", "blast");
            Assert.That(blast, Is.Not.Null);
            Assert.That(blast.ScaleBy, Is.EqualTo("radius"));
            Assert.That(blast.Base, Is.EqualTo(2f));
            Assert.That(blast.Impact, Is.EqualTo("Fire/BigExplosion"));
            Assert.That(Families.Find("terrain", "plant").Forms, Is.EqualTo(new[] { "bush", "tree", "sapling", "stump" }));
            Assert.That(Families.Find("gases", "bank").Drawn, Is.False);
            Assert.That(Families.Find("terrain", "nonsense"), Is.Null);
        }

        [Test]
        public void EveryFamilySaysWhatItIsOrWhatItNeeds()
        {
            foreach (var table in new[] { "terrain", "covers", "gases", "spells", "fire" })
            {
                Assert.That(Families.In(table), Is.Not.Empty, table);
                foreach (var f in Families.In(table))
                    Assert.That(f.Drawn ? f.About : f.Needs, Is.Not.Null.And.Not.Empty, $"{table} {f.Name}");
            }
        }

        [Test]
        public void EveryTerrainAndSpellNamesAFamilyTheViewDraws()
        {
            foreach (var t in Base.Terrain.Values)
            {
                Assert.That(t.Look, Is.Not.Null, t.Id);
                var family = Families.Find("terrain", t.Look.Family);
                Assert.That(family, Is.Not.Null, t.Id);
                Assert.That(family.Drawn, Is.True, t.Id);
                if (family.Forms.Count > 0) Assert.That(family.Forms, Does.Contain(t.Look.Form), t.Id);
            }
            foreach (var s in Base.Spells)
            {
                Assert.That(s.Look, Is.Not.Null, s.Id);
                Assert.That(Families.Find("spells", s.Look.Family), Is.Not.Null, s.Id);
            }
        }

        [Test]
        public void TheOldTerrainTableCameAcrossUnchanged()
        {
            foreach (var pair in OldTable)
            {
                var look = Base.Terrain[pair.Key].Look;
                Assert.That(look.R, Is.EqualTo(pair.Value.r), pair.Key + " red");
                Assert.That(look.G, Is.EqualTo(pair.Value.g), pair.Key + " green");
                Assert.That(look.B, Is.EqualTo(pair.Value.b), pair.Key + " blue");
                Assert.That(look.Height, Is.EqualTo(pair.Value.height), pair.Key + " height");
            }
        }

        // ---------- scaling by the rules in force ----------

        [Test]
        public void TheBlastScalesWithTheRadiusInForceAndTheCharge()
        {
            var blast = Families.Find("spells", "blast");
            var modded = Base.WithMods(new[] { Example("big-fireball") });
            Assert.That(EffectScale.Of(blast.ScaleBy, blast.Base, Base.Spell("fireball"), 1), Is.EqualTo(1f), "a plain Fireball draws as it always did");
            Assert.That(EffectScale.Of(blast.ScaleBy, blast.Base, modded.Spell("fireball"), 1), Is.EqualTo(2f), "radius 4 over 2");
            Assert.That(EffectScale.Of(blast.ScaleBy, blast.Base, Base.Spell("fireball"), 3), Is.EqualTo(2f), "held: two cells wider");
            Assert.That(EffectScale.Of(null, 1f, Base.Spell("fireball"), 1), Is.EqualTo(1f), "a family that doesn't scale");
        }

        [Test]
        public void EverySpellThatScalesDrawsAtItsOldSizeOnTheBaseRules()
        {
            foreach (var s in Base.Spells.Where(s => Families.Find("spells", s.Look.Family).ScaleBy != null))
            {
                var f = Families.Find("spells", s.Look.Family);
                Assert.That(EffectScale.Of(f.ScaleBy, f.Base, s, 1), Is.EqualTo(1f), s.Id);
            }
        }

        // ---------- looks stay the view's ----------

        [Test]
        public void TheFingerprintIgnoresLooks()
        {
            var relook = Base.WithMods(new[] { Mod("{ 'terrain': { 'grass': { 'name': 'Grass', 'tags': ['ground', 'turf'], 'look': { 'family': 'block', 'color': [1, 0, 0], 'height': 3 } } } }") });
            var plain = Base.WithMods(new[] { Mod("{ 'terrain': { 'grass': { 'name': 'Grass', 'tags': ['ground', 'turf'] } } }") });
            Assert.That(relook.Fingerprint, Is.EqualTo(plain.Fingerprint));
            Assert.That(relook.Terrain["grass"].Look.Family, Is.EqualTo("block"));
        }

        [Test]
        public void ALookCannotChangeTheWorld()
        {
            // The same world under the same casts, on the base rules and with every look changed.
            var doc = JObject.Parse(Base.ToJson());
            var terrain = new JObject();
            foreach (var p in ((JObject)doc["terrain"]).Properties())
            {
                var entry = (JObject)p.Value.DeepClone();
                entry["look"] = new JObject { ["family"] = "block", ["height"] = 9, ["color"] = new JArray(1, 0, 1) };
                terrain[p.Name] = entry;
            }
            var relooked = Base.WithMods(new[] { ModPatch.Parse(new JObject { ["terrain"] = terrain }.ToString()) });
            Assert.That(relooked.Fingerprint, Is.EqualTo(Base.Fingerprint));
            var a = new World(Base, 32, 32, 7, "grass");
            var b = new World(relooked, 32, 32, 7, "grass");
            foreach (var w in new[] { a, b })
            {
                w.Cast("fireball", 16, 16, 0);
                for (int i = 0; i < 120; i++) w.Step();
            }
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
        }

        [Test]
        public void AModThatSaysNothingOfTheLookKeepsIt()
        {
            var modded = Base.WithMods(new[] { Example("big-fireball") });
            Assert.That(modded.Spell("fireball").Look.Family, Is.EqualTo("blast"));
            var regrass = Base.WithMods(new[] { Mod("{ 'terrain': { 'grass': { 'name': 'Grass', 'tags': ['ground', 'turf'] } } }") });
            Assert.That(regrass.Terrain["grass"].Look.Family, Is.EqualTo("ground"));
        }

        [Test]
        public void TheModsMaterialIsDrawnByItsFamily()
        {
            var rules = Base.WithMods(new[] { Example("moonstone") });
            var look = rules.Terrain["moonstone"].Look;
            Assert.That(look.Family, Is.EqualTo("block"));
            Assert.That(look.Glow, Is.GreaterThan(0f));
            Assert.That(look.Height, Is.GreaterThan(0f));
            var w = new World(rules, 16, 16, 3, "grass");
            w.Cast("moon-frost", 8, 8, 0);
            for (int i = 0; i < 4; i++) w.Step();
            Assert.That(w.At(8, 8).T, Is.EqualTo("moonstone"));
        }

        [Test]
        public void ABadLookGroupIsRefused()
        {
            var errors = Assert.Throws<ModException>(() => Base.WithMods(new[] { Mod("{ 'terrain': { 'grass': { 'name': 'Grass', 'tags': ['ground'], 'look': { 'family': 'block', 'color': [2, 0, 0] } } } }") })).Errors;
            Assert.That(errors, Has.Some.EqualTo("terrain grass: look.color must be three numbers from 0 to 1"));
        }
    }
}
