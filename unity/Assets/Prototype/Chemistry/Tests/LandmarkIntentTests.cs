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
    public class LandmarkIntentTests
    {
        static Rules R => ChemistryTests.R;

        internal static string GameWorldDir() => Path.Combine(Directory.GetParent(ChemistryTests.RulesDir()).FullName, "world");

        static LandmarkIntent intent;
        internal static LandmarkIntent Village => intent ??= LandmarkIntent.Load(GameWorldDir(), "village-with-landmark");

        // A site well inside a valley-sized square, off the chunk lines so things fall in more than one chunk.
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

        [TestCase("temperate"), TestCase("mountains"), TestCase("foggy-marsh")]
        public void ItsRowsReadBackThroughTheMapFilesReaders(string biome)
        {
            foreach (int seed in new[] { 1, 2, 77 })
            {
                var rows = Village.Make(biome, SiteX, SiteY, seed);
                var stamps = MapStamps.FromJson(rows.StampsJson);
                Assert.That(stamps.All, Is.Not.Empty);
                Assert.That(stamps.All.All(s => s.Stamp.Known), $"{biome} {seed}: every stamp is a land stamp");
                var villages = VillageLayout.FromJson(rows.VillagesJson);
                var places = WorldPlaces.FromJson(rows.PlacesJson).All;
                if (rows.Village != null) Assert.That(villages.Single().Name, Is.EqualTo(rows.VillageName));
                else
                {
                    Assert.That(villages, Is.Empty, "a village laid as rows has no recipe");
                    Assert.That(places.Single(p => p.Kind == "village").Name, Is.EqualTo(rows.VillageName), "and names its own place");
                }
                var things = WorldThings.FromJson(rows.ThingsJson);
                Assert.That(things.Check(R), Is.Empty, $"{biome} {seed}: the things' kinds are the rules'");
                Assert.That(things.All, Is.Not.Empty);
                var place = places.Single(p => p.Kind == "landmark");
                Assert.That(place.Name, Is.EqualTo(rows.Name));
                Assert.That(place.Kind, Is.EqualTo("landmark"));
                Assert.That(rows.Buildings, Is.Not.Empty);
                Checked(rows, $"{biome} {seed}");
            }
        }

        // The building rows through the rules' own checks, as a mod's world rows are, and through the raster.
        static void Checked(LandmarkRows rows, string what)
        {
            var world = new JObject();
            foreach (var b in rows.Buildings.OfType<JObject>()) world[(string)b["id"]] = b;
            Rules with = null;
            Assert.DoesNotThrow(() => with = R.WithMods(new[] { ModPatch.Parse(new JObject { ["world"] = new JObject { ["buildings"] = world } }.ToString()) }), what);
            foreach (var b in rows.Buildings.OfType<JObject>())
            {
                var cells = BuildingRaster.Cells(BuildingRow.Read((string)b["id"], b, with.Buildings), with.Buildings);
                Assert.That(cells, Is.Not.Empty, $"{what}: {b["id"]} has cells");
            }
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
        public void EachFormIsWhatItSays()
        {
            var tree = Village.Make("temperate", SiteX, SiteY, 1);
            var row = (JObject)tree.Buildings[0];
            var tables = R.Buildings;
            var cells = BuildingRaster.Cells(BuildingRow.Read((string)row["id"], row, tables), tables);
            Assert.That(cells.Count(c => c.Terrain == "tree"), Is.GreaterThan(200), "a crown of trees");
            Assert.That(cells.Count(c => c.Terrain == "logwall"), Is.GreaterThan(60), "a trunk");
            Assert.That(cells.Count(c => c.Terrain == "deadfall"), Is.GreaterThan(20), "roots");
            Assert.That(cells.Any(c => c.X == SiteX && c.Y == SiteY && c.Terrain == "logwall"), "the trunk stands on the site");

            var volcano = Village.Make("mountains", SiteX, SiteY, 1);
            Assert.That(volcano.Stamps.Select(s => (string)s["profile"]), Is.EqualTo(new[] { "cone", "dome" }));
            Assert.That((int)volcano.Stamps[0]["rise"], Is.GreaterThan(400), "a high cone");
            Assert.That((int)volcano.Stamps[1]["rise"], Is.LessThan(0), "a crater in its top");
            Assert.That(volcano.Stamps[1]["at"], Is.EqualTo(volcano.Stamps[0]["at"]));
            // Its village climbs: houses either side of a way up towards the cone, nearer the top the further along, most of stone.
            var cone = ((int)volcano.Stamps[0]["at"][0], (int)volcano.Stamps[0]["at"][1]);
            var climb = volcano.Buildings.OfType<JObject>().Select(b => (b, d: Distance(b, cone))).ToList();
            Assert.That(volcano.Village, Is.Null, "laid as rows, not a square");
            Assert.That(climb.Count, Is.GreaterThanOrEqualTo(7), "six houses or more and the watch house");
            Assert.That(climb[climb.Count - 1].d, Is.LessThan(climb[0].d - 40), "the watch house is well up from the foot");
            Assert.That(climb.Count(c => (string)c.b["wall"]?["material"] == "stone"), Is.GreaterThan(climb.Count / 2), "mostly stone");
            Assert.That(volcano.Buildings[0]["roads"]?["way"], Is.Not.Null, "a way up between them");

            var fishing = Village.Make("foggy-marsh", SiteX, SiteY, 1);
            Assert.That((int)fishing.Stamps[0]["rise"], Is.LessThan(0), "a lake's hollow");
            var boathouses = fishing.Buildings.OfType<JObject>().Where(b => b["parts"]?.Any(p => (string)p["kind"] == "dock") == true).ToList();
            Assert.That(boathouses.Count, Is.GreaterThanOrEqualTo(2), "boathouses with docks");
            // Its village curves round the lake: every house about as far from the water's middle, spread round it.
            var lake = ((int)fishing.Stamps[0]["at"][0], (int)fishing.Stamps[0]["at"][1]);
            var shoreHouses = fishing.Buildings.OfType<JObject>().Where(b => (string)b["style"] == "village").ToList();
            Assert.That(shoreHouses.Count, Is.GreaterThanOrEqualTo(6));
            var far = shoreHouses.Select(b => Distance(b, lake)).ToList();
            Assert.That(far.Max() - far.Min(), Is.LessThan(10), "on one arc");
            int across = Math.Max(shoreHouses.Max(b => (int)b["at"][0]) - shoreHouses.Min(b => (int)b["at"][0]), shoreHouses.Max(b => (int)b["at"][1]) - shoreHouses.Min(b => (int)b["at"][1]));
            Assert.That(across, Is.GreaterThan(60), "spread round the shore, not bunched");
            // Each boathouse's front wall, where its dock goes, stands on the lake's shore: water just past it.
            var land = LandmarkPicture.Land(fishing, SiteX - 128, SiteY - 128, 256);
            foreach (var b in boathouses)
            {
                var (wx, wy) = LandmarkPicture.DockEnd(b);
                Assert.That(land[(wy - (SiteY - 128)) * 256 + wx - (SiteX - 128)], Is.LessThan(LandmarkPicture.WaterBelow), $"{b["id"]}'s dock ends over water");
            }
        }

        // How far a building's middle is from a cell.
        static double Distance(JObject b, (int x, int y) to)
        {
            double mx = (int)b["at"][0] + (int)b["joins"]["ne"][0] / 4.0, my = (int)b["at"][1] + (int)b["joins"]["se"][1] / 4.0;
            return Math.Sqrt((mx - to.x) * (mx - to.x) + (my - to.y) * (my - to.y));
        }

        // The goldens, to compare between machines: the five files' texts, per form and seed.
        [TestCase("temperate", 1, 0x8674f4f7u), TestCase("temperate", 2, 0xff75bd7eu)]
        [TestCase("mountains", 1, 0x809b1e01u), TestCase("mountains", 2, 0xec11852cu)]
        [TestCase("foggy-marsh", 1, 0xff38f27cu), TestCase("foggy-marsh", 2, 0x23425061u)]
        public void Golden(string biome, int seed, uint expected)
        {
            uint hash = Village.Make(biome, SiteX, SiteY, seed).Hash();
            TestContext.Out.WriteLine($"LANDMARK GOLDEN {biome} seed {seed} 0x{hash:x8}");
            Assert.That(hash, Is.EqualTo(expected));
        }

        [TestCase("temperate"), TestCase("mountains"), TestCase("foggy-marsh")]
        public void ItsPictureLaysTheVillageOnAFlatPatch(string biome)
        {
            var rows = Village.Make(biome, SiteX, SiteY, 1);
            var picture = LandmarkPicture.Draw(rows, R);
            Assert.That(picture.Houses, Is.GreaterThanOrEqualTo(4), $"{biome}: the village has room");
            Assert.That(picture.Pixels.Length, Is.EqualTo(LandmarkPicture.Size * LandmarkPicture.Size * 3));
            string folder = Environment.GetEnvironmentVariable("LANDMARK_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"landmark-{rows.Form}.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(picture.Pixels, LandmarkPicture.Size, LandmarkPicture.Size));
            TestContext.Out.WriteLine($"LANDMARK PICTURE {path}: {rows.Words}, {rows.Name} by {rows.VillageName}, {picture.Houses} houses");
        }
    }
}
