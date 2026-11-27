// The first slice of the world squares (J-080 phase 1, docs/design/world-squares.md): one landmark intent, "a village
// with a landmark", made three ways by biome. Its rows read back through the map files' own readers, the building rows
// pass the rules' checks, the same arguments give the same rows, and each form's rows have a golden for two seeds.
// LANDMARK_PNG_OUT=<folder> dotnet test core --filter "FullyQualifiedName~LandmarkIntentTests" writes a top-down picture
// of each form on a flat patch (LandmarkPicture).
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class LandmarkIntentTests
    {

        internal static string GameWorldDir() => LandFolders.GameWorldDir();

        static LandmarkIntent intent;
        internal static LandmarkIntent Village => intent ??= LandmarkIntent.Load(GameWorldDir(), "village-with-landmark");

        internal const int SiteX = 300, SiteY = 300;

        [Test]
        public void ItHasThreeFormsAndEveryBiomeFindsOne()
        {
            Assert.That(Village.Forms, Is.EqualTo(new[] { "temperate", "mountains", "foggy-marsh" }));
            Assert.That(Village.FormFor("temperate"), Is.EqualTo("temperate"));
            Assert.That(Village.FormFor("grassland"), Is.EqualTo("temperate"));
            Assert.That(Village.FormFor("ashlands"), Is.EqualTo("mountains"));
            Assert.That(Village.FormFor("poison-swamp"), Is.EqualTo("foggy-marsh"));
            Assert.That(Village.FormFor("salt-flat"), Is.EqualTo("temperate"), "a biome it doesn't name takes *");
            Assert.That(Village.FormFor(null), Is.EqualTo("temperate"));
            var biomes = BiomeTable.Load(Path.Combine(GameWorldDir(), "biomes.json"));
            foreach (var row in biomes.Rows) Assert.DoesNotThrow(() => Village.Make(row.Id, SiteX, SiteY, 1), row.Id);
        }

        [Test]
        public void AFormThatUsesAMissingOneOrGoesRoundIsRefused()
        {
            Assert.Throws<FormatException>(() => LandmarkIntent.Parse("{ \"id\": \"x\", \"by\": { \"a\": { \"use\": \"b\" } } }"), "no *");
            Assert.Throws<FormatException>(() => LandmarkIntent.Parse("{ \"id\": \"x\", \"by\": { \"*\": { \"use\": \"nope\" } } }"));
            Assert.Throws<FormatException>(() => LandmarkIntent.Parse("{ \"id\": \"x\", \"by\": { \"*\": { \"use\": \"a\" }, \"a\": { \"use\": \"*\" } } }"));
        }

        [Test]
        public void TheSameArgumentsMakeTheSameRowsAndASeedChangesThem()
        {
            foreach (string form in Village.Forms)
            {
                var a = Village.Make(form, SiteX, SiteY, 5);
                var b = Village.Make(form, SiteX, SiteY, 5);
                Assert.That(b.Hash(), Is.EqualTo(a.Hash()), form);
                Assert.That(b.BuildingsJson, Is.EqualTo(a.BuildingsJson));
                Assert.That(Village.Make(form, SiteX, SiteY, 6).Hash(), Is.Not.EqualTo(a.Hash()), $"{form}: another seed");
                Assert.That(Village.Make(form, SiteX + 64, SiteY, 5).Hash(), Is.Not.EqualTo(a.Hash()), $"{form}: another site");
            }
        }

        [Test]
        public void AVillageLaidAsRowsSkipsPlotsTheCallerRefuses()
        {
            foreach (string form in new[] { "mountains", "foggy-marsh" })
            {
                var all = Village.Make(form, SiteX, SiteY, 1);
                Assert.That(Village.Make(form, SiteX, SiteY, 1, (x, y) => true).Hash(), Is.EqualTo(all.Hash()), $"{form}: taking every cell is the same as no predicate");
                var west = Village.Make(form, SiteX, SiteY, 1, (x, y) => x < SiteX);
                var houses = west.Buildings.OfType<JObject>().Where(b => (string)b["style"] == "village").ToList();
                Assert.That(houses.Count, Is.LessThan(all.Buildings.Count(b => (string)b["style"] == "village")), $"{form}: some refused");
                Assert.That(houses.All(b => (int)b["at"][0] + (int)b["joins"]["ne"][0] / 2 <= SiteX), $"{form}: none on a refused cell");
                Assert.That(Village.Make(form, SiteX, SiteY, 1, (x, y) => false).Buildings.Count(b => (string)b["style"] == "village"), Is.Zero);
            }
        }

        [TestCase("temperate", 1, 0x2f446555u), TestCase("temperate", 2, 0xf6215265u)]
        [TestCase("mountains", 1, 0x3634a466u), TestCase("mountains", 2, 0xcca4426eu)]
        [TestCase("foggy-marsh", 1, 0xdfb98014u), TestCase("foggy-marsh", 2, 0x2dc492ddu)]
        public void Golden(string biome, int seed, uint expected)
        {
            uint hash = Village.Make(biome, SiteX, SiteY, seed).Hash();
            TestContext.Out.WriteLine($"LANDMARK GOLDEN {biome} seed {seed} 0x{hash:x8}");
            Assert.That(hash, Is.EqualTo(expected));
        }
    }
}
