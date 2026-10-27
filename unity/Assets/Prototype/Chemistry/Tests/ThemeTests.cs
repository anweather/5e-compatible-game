using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// World themes (D11): the DSL's checks and mix-ins (ThemeSchema, ThemeRegistry), the blueprint
    /// solver (ThemeBlueprint) and what it says about each cell (ThemeField), on the checked-in themes.
    /// </summary>
    public class ThemeTests
    {
        static string WorldDir() => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world");
        static ThemeRegistry Registry => ThemeRegistry.Load(WorldDir());
        static Theme Home => Registry.Themes["home-valley"];

        /// <summary>Region size for the tests: a 3×3 core 576 cells across, the valley's size.</summary>
        const double RegionCells = 192;

        static IReadOnlyCollection<string> Looks => Registry.Looks.Keys.ToList();

        static List<string> FileErrors(string json) => ThemeSchema.ValidateFile(JToken.Parse(json), Looks);

        [Test]
        public void TheCheckedInThemesLoadAndMixinsAreNotThemes()
        {
            var r = Registry;
            Assert.That(r.Themes.Keys.OrderBy(k => k), Is.EqualTo(new[] { "ember-crown", "home-valley", "river-valley", "sunscorched-frontier" }));
            Assert.That(r.Looks.ContainsKey("meadow") && r.Looks.ContainsKey("snowpeaks"));
        }

        [Test]
        public void AMixinsFeaturesMergeByIdAndAnOverrideChangesOnlyItsKeys()
        {
            var ember = Registry.Themes["ember-crown"];
            var river = ember.Feature("lava-river");
            Assert.That((string)river["to"], Is.EqualTo("southeast"), "ember-crown moves the mixin's lava river");
            Assert.That((string)river["material"], Is.EqualTo("lava"), "and keeps the rest of it");
            Assert.That(ember.Feature("lava-lake"), Is.Not.Null, "the mixin's other features come along");
        }

        [Test]
        public void TheValidatorNamesEveryProblem()
        {
            var errors = FileErrors(@"{ 'id': 'Bad Id', 'name': 'x', 'revision': 0, 'colour': 1, 'features': [
                { 'id': 'z', 'kind': 'zone', 'biome': 'jungle' },
                { 'id': 'z', 'kind': 'lake', 'at': 'up', 'material': 'milk' },
                { 'id': 'i', 'kind': 'influence', 'look': 'meadow', 'area': 'continent', 'around': 'z', 'reach': 3 } ] }");
            Assert.That(errors, Does.Contain("unknown key \"colour\""));
            Assert.That(errors, Does.Contain("id must be kebab-case"));
            Assert.That(errors, Does.Contain("revision must be an integer >= 1"));
            Assert.That(errors.Any(e => e.StartsWith("features[0] (z).biome must be one of", System.StringComparison.Ordinal)));
            Assert.That(errors, Does.Contain("features[0] (z).at is required"));
            Assert.That(errors, Does.Contain("features[1]: duplicate id \"z\""));
            Assert.That(errors, Does.Contain("features[1] (z).material must be water/lava/ice"));
            Assert.That(errors.Any(e => e.StartsWith("features[1] (z).at must be a compass word", System.StringComparison.Ordinal)));
            Assert.That(errors, Does.Contain("features[2] (i): an influence needs exactly one area: area \"continent\", at, around or along (got area, around)"));
            Assert.That(errors, Does.Contain("features[2] (i).reach must be a number in [0.02, 1]"));
            Assert.That(FileErrors(File.ReadAllText(Path.Combine(WorldDir(), "themes", "home-valley.json"))), Is.Empty);
        }

        [Test]
        public void MergedThemesCheckTheirReferencesAndMixinCyclesAreCaught()
        {
            var file = JObject.Parse(@"{ 'id': 'a', 'name': 'A', 'revision': 1, 'features': [
                { 'id': 'r', 'kind': 'river', 'from': 'north', 'to': 'south', 'through': ['nowhere'] } ] }");
            var errors = ThemeSchema.Validate(ThemeSchema.Merge(file, _ => null), Looks);
            Assert.That(errors, Is.EqualTo(new[] { "feature r.through: unknown feature \"nowhere\"" }));

            var a = JObject.Parse("{ 'id': 'a', 'name': 'A', 'revision': 1, 'mixins': ['b'] }");
            var b = JObject.Parse("{ 'id': 'b', 'name': 'B', 'revision': 1, 'mixin': true, 'mixins': ['a'] }");
            var e = Assert.Throws<System.InvalidOperationException>(() => ThemeSchema.Merge(a, id => id == "a" ? a : b));
            Assert.That(e.Message, Is.EqualTo("world theme mixin cycle: a -> b -> a"));
        }

        [Test]
        public void TheHomeValleyLaysOutWhereItsGoalsSay()
        {
            var bp = ThemeBlueprint.Solve(1, Home, RegionCells);
            Assert.That(bp.Continent.CoreHalf, Is.EqualTo(288));
            var range = bp.Centres["north-range"];
            Assert.That(range.Y, Is.LessThan(-0.5), "the range is far north (y grows south)");
            var tarn = bp.Centres["tarn"];
            Assert.That(tarn.X, Is.LessThan(0), "the tarn is to the north-west");
            Assert.That(tarn.Y, Is.LessThan(0));
            Assert.That(bp.Zones.Select(z => z.Id), Is.SupersetOf(new[] { "north-range:foothills", "north-range", "north-range:peaks", "west-woods", "east-woods", "south-fields" }));
            Assert.That(bp.Rivers.Single().Anchors.Count, Is.EqualTo(3), "the brook runs from the rim, through the tarn, to the rim");

            var field = new ThemeField(bp, new GeoBox(-400, -400, 400, 400));
            var c = bp.Zones.Single(z => z.Id == "north-range").Blob;
            Assert.That(field.Look((int)c.Cx, (int)c.Cy), Is.EqualTo("snowpeaks").Or.EqualTo("mountains"), "the range's heart is mountain");
            var lake = bp.Lakes.Single().Blob;
            Assert.That(field.Material((int)lake.Cx, (int)lake.Cy), Is.EqualTo(ThemeField.MatWater), "the tarn is water");
            Assert.That(field.Material(0, 0), Is.EqualTo(ThemeField.MatNone), "the start is dry");
        }

        [Test]
        public void AnyWindowSaysTheSameAboutACellAndTheBlueprintIsAFunctionOfTheSeed()
        {
            var a = ThemeBlueprint.Solve(5, Home, RegionCells);
            var b = ThemeBlueprint.Solve(5, Home, RegionCells);
            var big = new ThemeField(a, new GeoBox(-400, -400, 400, 400));
            for (int y = -300; y < 300; y += 37)
                for (int x = -300; x < 300; x += 41)
                {
                    var small = new ThemeField(b, new GeoBox(x - 1, y - 1, x + 1, y + 1));
                    Assert.That(small.Look(x, y), Is.EqualTo(big.Look(x, y)), $"look at ({x}, {y})");
                    Assert.That(small.Material(x, y), Is.EqualTo(big.Material(x, y)), $"material at ({x}, {y})");
                }
            Assert.That(Digest(ThemeBlueprint.Solve(6, Home, RegionCells)), Is.Not.EqualTo(Digest(a)), "another seed, another layout");
        }

        [Test]
        public void GoldenHomeValley()
        {
            // Pins the port's output: a change that moves it reshapes worlds made with this theme
            // (bump the theme's revision, D11).
            Assert.That(Digest(ThemeBlueprint.Solve(1, Home, RegionCells)), Is.EqualTo("c3c8448e"));
        }

        // A hash of what the field says over the core, every 6 cells.
        static string Digest(Blueprint bp)
        {
            var field = new ThemeField(bp, new GeoBox(-400, -400, 400, 400));
            unchecked
            {
                uint h = 0x811c9dc5u;
                for (int y = -288; y < 288; y += 6)
                    for (int x = -288; x < 288; x += 6)
                    {
                        string look = field.Look(x, y) ?? "-";
                        foreach (char ch in look) h = (h ^ ch) * 0x01000193u;
                        h = (h ^ (uint)field.Material(x, y)) * 0x01000193u;
                    }
                return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
