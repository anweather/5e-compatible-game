// LandmarkIntentTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in LandmarkIntentTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class LandmarkIntentWorldTests
    {
        static Rules R => ChemistryTests.R;

        [TestCase("temperate"), TestCase("mountains"), TestCase("foggy-marsh")]
        public void ItsRowsReadBackThroughTheMapFilesReaders(string biome)
        {
            foreach (int seed in new[] { 1, 2, 77 })
            {
                var rows = LandmarkIntentTests.Village.Make(biome, LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, seed);
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
        public void EachFormIsWhatItSays()
        {
            var tree = LandmarkIntentTests.Village.Make("temperate", LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, 1);
            var row = (JObject)tree.Buildings[0];
            var tables = R.Buildings;
            var cells = BuildingRaster.Cells(BuildingRow.Read((string)row["id"], row, tables), tables);
            Assert.That(row["parts"].Single()["kind"].ToString(), Is.EqualTo("great-tree"), "the view's own tree, scaled up");
            Assert.That((int)row["parts"][0]["scale"], Is.InRange(3, 4));
            Assert.That((string)row["parts"][0]["tree"], Is.EqualTo("Cypress"), "a broad-crowned kind");
            Assert.That(cells.Count(c => c.Terrain == "logwall"), Is.GreaterThan(12), "a trunk");
            Assert.That(cells.Count(c => c.Terrain == "deadfall"), Is.GreaterThan(40), "roots");
            Assert.That(cells.Any(c => c.X == LandmarkIntentTests.SiteX && c.Y == LandmarkIntentTests.SiteY && c.Terrain == "logwall"), "the trunk stands on the site");

            var volcano = LandmarkIntentTests.Village.Make("mountains", LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, 1);
            Assert.That(volcano.Stamps.Select(s => (string)s["profile"]), Is.EqualTo(new[] { "cone", "dome" }));
            Assert.That((int)volcano.Stamps[0]["rise"], Is.GreaterThan(400), "a high cone");
            Assert.That((int)volcano.Stamps[1]["rise"], Is.LessThan(0), "a crater in its top");
            Assert.That(volcano.Stamps[1]["at"], Is.EqualTo(volcano.Stamps[0]["at"]));
            Assert.That(volcano.Stamps.Select(t => (string)t["bare"]), Is.EqualTo(new[] { "rock", "lava-crust" }), "a bare cone and a crusted crater");
            Assert.That(MapStamps.FromJson(volcano.StampsJson).All.Select(t => t.Bare), Is.EqualTo(new[] { "rock", "lava-crust" }), "as stamps.json reads it");
            var cone = ((int)volcano.Stamps[0]["at"][0], (int)volcano.Stamps[0]["at"][1]);
            int coneRadius = (int)volcano.Stamps[0]["radius"], crater = (int)volcano.Stamps[1]["radius"];
            Assert.That(volcano.Village, Is.Null, "laid as rows on its shelf, not a square");
            var rowsBuilt = volcano.Buildings.OfType<JObject>().ToList();
            var lookout = rowsBuilt.Single(b => ((string)b["note"]).Contains("lookout"));
            var houses = rowsBuilt.Where(b => b != lookout).ToList();
            Assert.That(houses.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(houses.All(b => Dist(Middle(b), volcano.VillageAt) <= 16), "clustered on the shelf");
            Assert.That(houses.All(b => Dist(Middle(b), cone) > coneRadius * 0.75), "none up the steep cone");
            var shelf = houses.Single(b => b["pad"]?["more"] != null);
            Assert.That(((string)shelf["note"]).Contains("well house") && Dist(Middle(shelf), volcano.VillageAt) < 3, "the shelf's height is its middle's, where its well house stands");
            Assert.That((bool)shelf["level"] && (bool)shelf["pad"]["more"][0]["clear"], "one pad levels the whole shelf");
            var trail = WayCells(lookout);
            Assert.That((bool)lookout["roads"]["way"]["graded"], "the trail is cut into the slope");
            Assert.That(Dist(trail[0], volcano.VillageAt), Is.LessThan(20), "it leaves the village");
            Assert.That(Dist(trail[trail.Count - 1], cone), Is.LessThan(crater + 10), "and ends by the crater");
            Assert.That(Dist(Middle(lookout), cone), Is.LessThan(crater + 16), "at a lookout on the rim");
            int switchbacks = 0;
            for (int k = 2; k < trail.Count; k++)
            {
                double a = Math.Atan2(trail[k - 1].y - cone.Item2, trail[k - 1].x - cone.Item1) - Math.Atan2(trail[k - 2].y - cone.Item2, trail[k - 2].x - cone.Item1);
                double b = Math.Atan2(trail[k].y - cone.Item2, trail[k].x - cone.Item1) - Math.Atan2(trail[k - 1].y - cone.Item2, trail[k - 1].x - cone.Item1);
                if (Math.Sign(Math.IEEERemainder(a, 2 * Math.PI)) != Math.Sign(Math.IEEERemainder(b, 2 * Math.PI))) switchbacks++;
            }
            Assert.That(switchbacks, Is.GreaterThanOrEqualTo(3), "it switches back as it climbs");

            var fishing = LandmarkIntentTests.Village.Make("foggy-marsh", LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, 1);
            Assert.That((int)fishing.Stamps[0]["rise"], Is.LessThan(0), "a lake's hollow");
            var boathouses = fishing.Buildings.OfType<JObject>().Where(b => b["parts"]?.Any(p => (string)p["kind"] == "dock") == true).ToList();
            Assert.That(boathouses.Count, Is.GreaterThanOrEqualTo(2), "boathouses with docks");
            var lake = ((int)fishing.Stamps[0]["at"][0], (int)fishing.Stamps[0]["at"][1]);
            var shoreHouses = fishing.Buildings.OfType<JObject>().Where(b => (string)b["style"] == "village").ToList();
            Assert.That(shoreHouses.Count, Is.GreaterThanOrEqualTo(6));
            var far = shoreHouses.Select(b => Distance(b, lake)).ToList();
            Assert.That(far.Max() - far.Min(), Is.LessThan(10), "on one arc");
            int across = Math.Max(shoreHouses.Max(b => (int)b["at"][0]) - shoreHouses.Min(b => (int)b["at"][0]), shoreHouses.Max(b => (int)b["at"][1]) - shoreHouses.Min(b => (int)b["at"][1]));
            Assert.That(across, Is.GreaterThan(60), "spread round the shore, not bunched");
            var village = WorldPlaces.FromJson(fishing.PlacesJson).All.Single(p => p.Kind == "village");
            Assert.That(village.Contains(lake.Item1, lake.Item2), Is.False, "the lake isn't the village");
            Assert.That(shoreHouses.All(b => village.Contains((int)b["at"][0], (int)b["at"][1])));
            var land = LandmarkPicture.Land(fishing, LandmarkIntentTests.SiteX - 128, LandmarkIntentTests.SiteY - 128, 256);
            foreach (var b in boathouses)
            {
                var (wx, wy) = LandmarkPicture.DockEnd(b);
                Assert.That(land[(wy - (LandmarkIntentTests.SiteY - 128)) * 256 + wx - (LandmarkIntentTests.SiteX - 128)], Is.LessThan(LandmarkPicture.WaterBelow), $"{b["id"]}'s dock ends over water");
            }
        }

        [Test]
        public void ThingsKeepOffEveryRow()
        {
            foreach (string form in LandmarkIntentTests.Village.Forms)
                foreach (int seed in new[] { 1, 2, 3, 4, 5 })
                {
                    var rows = LandmarkIntentTests.Village.Make(form, LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, seed);
                    var taken = new System.Collections.Generic.HashSet<(int, int)>();
                    foreach (var b in rows.Buildings.OfType<JObject>())
                        foreach (var c in BuildingRaster.Cells(BuildingRow.Read((string)b["id"], b, R.Buildings), R.Buildings)) taken.Add((c.X, c.Y));
                    var on = WorldThings.FromJson(rows.ThingsJson).All.Where(t => taken.Contains((t.CellX, t.CellY))).ToList();
                    Assert.That(on, Is.Empty, $"{form} seed {seed}: {string.Join(", ", on.Select(t => $"{t.Kind} at {t.CellX},{t.CellY}"))}");
                }
        }

        [TestCase("temperate"), TestCase("mountains"), TestCase("foggy-marsh")]
        public void ItsPictureLaysTheVillageOnAFlatPatch(string biome)
        {
            var rows = LandmarkIntentTests.Village.Make(biome, LandmarkIntentTests.SiteX, LandmarkIntentTests.SiteY, 1);
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

        static (double x, double y) Middle(JObject b) =>
            ((int)b["at"][0] + (int)b["joins"]["ne"][0] / 4.0, (int)b["at"][1] + (int)b["joins"]["se"][1] / 4.0);

        static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        static List<(double x, double y)> WayCells(JObject b)
        {
            int ax = (int)b["at"][0], ay = (int)b["at"][1];
            return b["roads"]["way"]["through"].Select(j => b["joins"][(string)j]).Select(p => (ax + (int)p[0] / 2.0, ay + (int)p[1] / 2.0)).ToList();
        }

        static double Distance(JObject b, (int x, int y) to)
        {
            double mx = (int)b["at"][0] + (int)b["joins"]["ne"][0] / 4.0, my = (int)b["at"][1] + (int)b["joins"]["se"][1] / 4.0;
            return Math.Sqrt((mx - to.x) * (mx - to.x) + (my - to.y) * (my - to.y));
        }
    }
}
