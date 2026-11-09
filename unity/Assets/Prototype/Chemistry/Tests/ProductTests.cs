using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Products by data (docs/design/primitives.md P3): what fire and water leave comes from the rules
    /// (sim.json fire.ash and smoke, water.boils and freezes; a material's burn.ash and burn.smoke), so a
    /// mod's material leaves a product of its own choosing with no code naming it.
    /// </summary>
    [Category("Simulation")]
    public class ProductTests
    {
        static Rules baseRules;

        static Rules Base => baseRules ??= Rules.Load(ChemistryTests.RulesDir());

        static Rules With(string mod) => Base.WithMods(new List<ModPatch> { ModPatch.Parse(mod.Replace('\'', '"')) });

        static IReadOnlyList<string> ErrorsOf(string mod) => Assert.Throws<ModException>(() => With(mod)).Errors;

        // Peat burns down to bog, leaving cinders and giving off reek: three names only the mod knows.
        const string Peat = @"{
            'terrain': { 'peat': { 'name': 'Peat', 'tags': ['ground'], 'burn': { 'catch': 1, 'fuel': 6, 'leaves': 'bog', 'ash': 'cinders', 'smoke': 'reek' } },
                         'bog': { 'name': 'Bog', 'tags': ['ground'] } },
            'covers': { 'cinders': { 'name': 'Cinders', 'tags': [] } },
            'gases': { 'reek': { 'name': 'Reek', 'decay': 0.01 } } }";

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        [Test]
        public void AModsMaterialBurnsToItsOwnAshAndSmoke()
        {
            var w = new World(With(Peat), 12, 5, 4, "peat");
            for (int y = 0; y < w.H; y++) w.Apply(0, y, "fire");
            bool reeked = false;
            for (int t = 0; t < 300; t++)
            {
                w.Step();
                reeked |= w.Cells.Any(c => c.Gas == "reek");
                Assert.That(w.Cells.Any(c => c.Gas == "smoke"), Is.False, "no smoke from peat");
            }
            var burnt = w.Cells.Where(c => c.T == "bog").ToList();
            Assert.That(burnt.Count, Is.GreaterThan(10), "the peat burned away");
            Assert.That(burnt.All(c => c.Cover == "cinders"), "each burnt cell is left with the mod's cinders");
            Assert.That(reeked, "the mod's reek rose from it");
        }

        [Test]
        public void AModsSimSaysWhatWaterBoilsInto()
        {
            var rules = With(@"{ 'gases': { 'vapour': { 'name': 'Vapour', 'decay': 0.01 } },
                'terrain': { 'griddle': { 'name': 'Griddle', 'tags': ['ground'], 'boil': {} } },
                'sim': { 'water': { 'boils': 'vapour' } } }");
            var w = new World(rules, 5, 5, 2, "griddle");
            w.At(2, 2).Water = 3;
            Run(w, 20);
            Assert.That(w.At(2, 2).Water, Is.LessThan(3), "the griddle boils it off");
            Assert.That(w.Cells.Any(c => c.Gas == "vapour"), "as the mod's vapour");
            Assert.That(w.Cells.Any(c => c.Gas == "steam"), Is.False);
        }

        [Test]
        public void TheBaseRulesNameTodaysProducts()
        {
            var s = Base.Sim;
            Assert.That((s.Fire.Ash, s.Fire.Smoke, s.WaterBoils, s.WaterFreezes), Is.EqualTo(("ash", "smoke", "steam", "ice")));
            Assert.That((s.Weather.SnowCover, s.Weather.BankCover, s.Weather.MeteorSpell), Is.EqualTo(("snow", "snowbank", "meteor")));
            Assert.That((World.SnowCover, World.BankCover), Is.EqualTo((s.Weather.SnowCover, s.Weather.BankCover)), "the view's copies match the rules");
        }

        [Test]
        public void AProductMustBeAMaterialTheRulesKnow()
        {
            Assert.That(ErrorsOf("{ 'sim': { 'water': { 'boils': 'fizz' } } }"), Has.Member("sim water.boils: unknown gas fizz"));
            Assert.That(ErrorsOf("{ 'sim': { 'weather': { 'snow': { 'cover': 'steam' } } } }"), Has.Member("sim weather.snow.cover: unknown cover steam"));
            Assert.That(ErrorsOf("{ 'sim': { 'weather': { 'meteors': { 'spell': 'comet' } } } }"), Has.Member("sim weather.meteors.spell: unknown spell comet"));
            Assert.That(ErrorsOf("{ 'covers': { 'thatch-mat': { 'name': 'Mat', 'tags': [], 'burn': { 'catch': 1, 'ash': 'smoke' } } } }"),
                Has.Member("cover thatch-mat: burn.ash smoke is not a cover"));
            Assert.That(ErrorsOf("{ 'covers': { 'thatch-mat': { 'name': 'Mat', 'tags': [], 'burn': { 'catch': 1, 'smoke': 'ash' } } } }"),
                Has.Member("cover thatch-mat: burn.smoke ash is not a gas"));
        }
    }
}
