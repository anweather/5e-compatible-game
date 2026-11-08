// The first slice of the world squares (J-080 phase 1, docs/design/world-squares.md): one landmark intent, "a village
// with a landmark", made three ways by biome. Its rows read back through the map files' own readers, the building rows
// pass the rules' checks, the same arguments give the same rows, and each form's rows have a golden for two seeds.
// LANDMARK_PNG_OUT=<folder> dotnet test core --filter "FullyQualifiedName~LandmarkIntentTests" writes a top-down picture
// of each form on a flat patch (LandmarkPicture).
using System;
using System.Collections.Generic;
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
            Assert.That(row["parts"].Single()["kind"].ToString(), Is.EqualTo("great-tree"), "the view's own tree, scaled up");
            Assert.That((int)row["parts"][0]["scale"], Is.InRange(3, 4));
            Assert.That((string)row["parts"][0]["tree"], Is.EqualTo("Cypress"), "a broad-crowned kind");
            Assert.That(cells.Count(c => c.Terrain == "logwall"), Is.GreaterThan(12), "a trunk");
            Assert.That(cells.Count(c => c.Terrain == "deadfall"), Is.GreaterThan(40), "roots");
            Assert.That(cells.Any(c => c.X == SiteX && c.Y == SiteY && c.Terrain == "logwall"), "the trunk stands on the site");

            var volcano = Village.Make("mountains", SiteX, SiteY, 1);
            Assert.That(volcano.Stamps.Select(s => (string)s["profile"]), Is.EqualTo(new[] { "cone", "dome" }));
            Assert.That((int)volcano.Stamps[0]["rise"], Is.GreaterThan(400), "a high cone");
            Assert.That((int)volcano.Stamps[1]["rise"], Is.LessThan(0), "a crater in its top");
            Assert.That(volcano.Stamps[1]["at"], Is.EqualTo(volcano.Stamps[0]["at"]));
            Assert.That(volcano.Stamps.Select(t => (string)t["bare"]), Is.EqualTo(new[] { "rock", "lava-crust" }), "a bare cone and a crusted crater");
            Assert.That(MapStamps.FromJson(volcano.StampsJson).All.Select(t => t.Bare), Is.EqualTo(new[] { "rock", "lava-crust" }), "as stamps.json reads it");
            // Its village winds round the cone's lower slopes (Andrew, 2026-11-08): never near its top, rising gently, houses
            // above the way and below it by turns, mostly stone, each on a flat pad.
            var cone = ((int)volcano.Stamps[0]["at"][0], (int)volcano.Stamps[0]["at"][1]);
            int coneRadius = (int)volcano.Stamps[0]["radius"];
            var houses = volcano.Buildings.OfType<JObject>().Select(b => (b, d: Distance(b, cone))).ToList();
            Assert.That(volcano.Village, Is.Null, "laid as rows, not a square");
            Assert.That(houses.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(houses.Min(h => h.d), Is.GreaterThan(coneRadius * 0.5), "none near the top");
            Assert.That(houses.Min(h => h.d), Is.LessThan(coneRadius), "up its slopes, not only at its foot");
            var way = WayCells(volcano.Buildings.OfType<JObject>().First(b => b["roads"] != null));
            Assert.That(way.Max(w => Dist(w, cone)) - way.Min(w => Dist(w, cone)), Is.InRange(15, 50), "a gentle way in from the foot");
            Assert.That(Angle(way[0], way[way.Count - 1], cone), Is.GreaterThan(90), "winding round the cone, not straight up it");
            int above = 0, below = 0;
            foreach (var (b, d) in houses)
            {
                double road = way.Min(w => Dist(w, Middle(b)));
                var nearest = way.OrderBy(w => Dist(w, Middle(b))).First();
                if (d < Dist(nearest, cone)) above++;
                else below++;
                Assert.That(road, Is.LessThan(10), $"{b["id"]} by the way");
            }
            Assert.That(above, Is.GreaterThan(1), "houses on the up slope");
            Assert.That(below, Is.GreaterThan(1), "and on the down slope");
            Assert.That(houses.Count(h => (string)h.b["wall"]?["material"] == "stone"), Is.GreaterThan(houses.Count / 2), "mostly stone");
            Assert.That(houses.All(h => h.b["pad"] != null && (bool)h.b["level"]), "each on a flat pad");

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
            // Its place is its houses, not the block they stand round: the lake's middle isn't in it.
            var village = WorldPlaces.FromJson(fishing.PlacesJson).All.Single(p => p.Kind == "village");
            Assert.That(village.Contains(lake.Item1, lake.Item2), Is.False, "the lake isn't the village");
            Assert.That(shoreHouses.All(b => village.Contains((int)b["at"][0], (int)b["at"][1])));
            // Each boathouse's front wall, where its dock goes, stands on the lake's shore: water just past it.
            var land = LandmarkPicture.Land(fishing, SiteX - 128, SiteY - 128, 256);
            foreach (var b in boathouses)
            {
                var (wx, wy) = LandmarkPicture.DockEnd(b);
                Assert.That(land[(wy - (SiteY - 128)) * 256 + wx - (SiteX - 128)], Is.LessThan(LandmarkPicture.WaterBelow), $"{b["id"]}'s dock ends over water");
            }
        }

        [Test]
        public void ThingsKeepOffEveryRow()
        {
            foreach (string form in Village.Forms)
                foreach (int seed in new[] { 1, 2, 3, 4, 5 })
                {
                    var rows = Village.Make(form, SiteX, SiteY, seed);
                    var taken = new System.Collections.Generic.HashSet<(int, int)>();
                    foreach (var b in rows.Buildings.OfType<JObject>())
                        foreach (var c in BuildingRaster.Cells(BuildingRow.Read((string)b["id"], b, R.Buildings), R.Buildings)) taken.Add((c.X, c.Y));
                    var on = WorldThings.FromJson(rows.ThingsJson).All.Where(t => taken.Contains((t.CellX, t.CellY))).ToList();
                    Assert.That(on, Is.Empty, $"{form} seed {seed}: {string.Join(", ", on.Select(t => $"{t.Kind} at {t.CellX},{t.CellY}"))}");
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

        static (double x, double y) Middle(JObject b) =>
            ((int)b["at"][0] + (int)b["joins"]["ne"][0] / 4.0, (int)b["at"][1] + (int)b["joins"]["se"][1] / 4.0);

        static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        // The way's points, in cells, from the row its road is on.
        static List<(double x, double y)> WayCells(JObject b)
        {
            int ax = (int)b["at"][0], ay = (int)b["at"][1];
            return b["roads"]["way"]["through"].Select(j => b["joins"][(string)j]).Select(p => (ax + (int)p[0] / 2.0, ay + (int)p[1] / 2.0)).ToList();
        }

        // Degrees between two points as seen from a third.
        static double Angle((double x, double y) a, (double x, double y) b, (double x, double y) from)
        {
            double t = Math.Atan2(a.y - from.y, a.x - from.x) - Math.Atan2(b.y - from.y, b.x - from.x);
            return Math.Abs(Math.IEEERemainder(t, 2 * Math.PI)) * 180 / Math.PI;
        }

        // How far a building's middle is from a cell.
        static double Distance(JObject b, (int x, int y) to)
        {
            double mx = (int)b["at"][0] + (int)b["joins"]["ne"][0] / 4.0, my = (int)b["at"][1] + (int)b["joins"]["se"][1] / 4.0;
            return Math.Sqrt((mx - to.x) * (mx - to.x) + (my - to.y) * (my - to.y));
        }

        // The goldens, to compare between machines: the five files' texts, per form and seed.
        [TestCase("temperate", 1, 0x2f446555u), TestCase("temperate", 2, 0xf6215265u)]
        [TestCase("mountains", 1, 0xcc8c716eu), TestCase("mountains", 2, 0x0c8a4196u)]
        [TestCase("foggy-marsh", 1, 0xdfb98014u), TestCase("foggy-marsh", 2, 0x2dc492ddu)]
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
