using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>world/places.json (Places.cs): the named landmarks, generated from the world data where it can be.</summary>
    public class PlacesTests
    {
        static WorldPlaces Valley => WorldPlaces.Load(ChemistryTests.WorldDir());

        [Test]
        public void PlacesFileIsUpToDate()
        {
            bool write = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLACES_OUT"));
            foreach (string map in Directory.GetDirectories(ChemistryTests.Folders.Maps))
            {
                string path = Path.Combine(map, WorldPlaces.FileName);
                string made = WorldPlaces.Generate(map);
                if (write) File.WriteAllText(path, made);
                Assert.That(File.ReadAllText(path).Replace("\r\n", "\n"), Is.EqualTo(made),
                    $"{path} is behind the map's data; regenerate it: PLACES_OUT=1 dotnet test core --filter PlacesFileIsUpToDate");
            }
        }

        [Test]
        public void TheVillagesTheWaterAndTheCoursesAreThere()
        {
            var places = Valley;
            foreach (var spec in VillageLayout.Load(ChemistryTests.WorldDir()))
            {
                var village = places.Find(spec.Name);
                Assert.That(village, Is.Not.Null, spec.Name);
                Assert.That(village.Kind, Is.EqualTo("village"));
                Assert.That(village.Generated, Is.True);
                Assert.That(village.Contains(spec.X, spec.Y), Is.True, $"{spec.Name}'s square is in it");
            }
            var lake = places.Find("the lake");
            Assert.That(lake, Is.Not.Null);
            Assert.That(lake.Kind, Is.EqualTo("lake"));
            Assert.That(lake.Count, Is.GreaterThan(1000), "the valley's lake is thousands of cells");
            Assert.That(lake.Contains(lake.Middle.x, lake.Middle.y), Is.True);
            var gauntlet = CourseLayout.Load(ChemistryTests.WorldDir());
            Assert.That(lake.Bounds.Bottom, Is.LessThanOrEqualTo(gauntlet.Area.Y), "the Gauntlet is on the lake's far shore");
            var sea = places.All.Single(p => p.Kind == "sea");
            var key = places.Find("Sunward Key");
            Assert.That(sea.Bounds.Contains(key.Middle.x, key.Middle.y) && !sea.Contains(key.Middle.x, key.Middle.y), Is.True, "Sunward Key is an island in the sea");
            var river = places.Find("river");
            Assert.That(river.Generated, Is.False, "the river's course is written by hand");
            Assert.That(river.DistanceTo(lake.Middle.x, lake.Middle.y), Is.GreaterThan(0));
            Assert.That(lake.DistanceTo(river.Cells().Last().x, river.Cells().Last().y), Is.LessThan(12), "the river runs into the lake");
            Assert.That(places.Find("golf"), Is.Not.Null);
            Assert.That(places.Find("The Meadow Coil"), Is.Not.Null, "found by name in any case");
            Assert.That(places.All.Count(p => p.Name == "The Gauntlet"), Is.EqualTo(1), "a puzzle with a course's name is the course");
        }

        [Test]
        public void ShapesGiveTheirCells()
        {
            var places = WorldPlaces.FromJson(@"{ ""places"": [
                { ""id"": ""box"", ""area"": [2, 3, 4, 5] },
                { ""id"": ""road"", ""line"": [[0, 0], [10, 0]], ""width"": 3 },
                { ""id"": ""ring"", ""circle"": { ""at"": [20, 20], ""radius"": 2 } },
                { ""id"": ""odd"", ""rows"": [[7, 1, 2, 5, 6], [9, 3, 3]] } ] }");
            var box = places.Find("box");
            Assert.That(box.Count, Is.EqualTo(9));
            Assert.That(box.Contains(4, 5) && !box.Contains(5, 5), Is.True);
            Assert.That(box.DistanceTo(7, 5), Is.EqualTo(3));
            Assert.That(places.Find("road").Count, Is.EqualTo(39), "11 cells long and 3 across, with round ends");
            Assert.That(places.Find("ring").Count, Is.EqualTo(13));
            var odd = places.Find("odd");
            Assert.That(odd.Cells().ToList(), Is.EqualTo(new[] { (1, 7), (2, 7), (5, 7), (6, 7), (3, 9) }));
            Assert.That(odd.Contains(3, 7) || odd.Contains(3, 8), Is.False);
            Assert.That(places.Find("nowhere"), Is.Null);
        }

        [Test]
        public void ABadFileSaysWhy()
        {
            Assert.Throws<FormatException>(() => WorldPlaces.FromJson(@"{ ""places"": [ { ""id"": ""a"" } ] }"), "no cells");
            Assert.Throws<FormatException>(() => WorldPlaces.FromJson(@"{ ""places"": [ { ""id"": ""a"", ""area"": [0, 0, 1, 1] }, { ""id"": ""a"", ""area"": [0, 0, 1, 1] } ] }"), "twice");
            Assert.That(WorldPlaces.Load(Path.Combine(ChemistryTests.WorldDir(), "no-such-folder")).Count, Is.EqualTo(0), "no file: no places");
        }
    }
}
