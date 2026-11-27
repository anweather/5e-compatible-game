// MapTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in MapTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class MapWorldTests
    {

        string map;

        [SetUp]
        public void CopyTheValley()
        {
            map = Path.Combine(Path.GetTempPath(), "map-tests-" + Guid.NewGuid().ToString("N"), "valley");
            Directory.CreateDirectory(map);
            foreach (string file in Directory.GetFiles(LandFolders.WorldDir())) File.Copy(file, Path.Combine(map, Path.GetFileName(file)));
        }

        [TearDown]
        public void RemoveTheCopy() => Directory.Delete(Path.GetDirectoryName(map), true);

        string Errors => string.Join("\n", MapCheck.Errors(map));

        [Test]
        public void EveryMapPassesItsChecks()
        {
            var maps = Directory.GetDirectories(LandFolders.Folders.Maps);
            Assert.That(maps.Select(Path.GetFileName), Does.Contain(GameFolders.DefaultMap));
            foreach (string folder in maps.Where(m => !GeneratedMap.IsGenerated(m))) Assert.That(MapCheck.Errors(folder), Is.Empty, folder);
        }

        [Test]
        public void TheBigTestMapPassesItsChecks()
        {
            Assert.That(MapCheck.Errors(MapSizeBench.BigMap), Is.Empty);
            Assert.That(MapFile.Load(MapSizeBench.BigMap).W, Is.EqualTo(1728));
        }

        void RowMovesTo(int x, int y)
        {
            string path = Path.Combine(map, WorldBuildings.FileName);
            var o = JObject.Parse(File.ReadAllText(path));
            ((JArray)o["buildings"]).OfType<JObject>().Single(r => (string)r["id"] == "hold#1")["at"] = new JArray(x, y);
            File.WriteAllText(path, o.ToString());
        }

        [Test]
        public void ALaidRowOnAThingIsSaid()
        {
            var thing = WorldThings.Load(map).All.First(t => t.CellX > 40 && t.CellY > 40 && t.CellX < 530 && t.CellY < 530);
            RowMovesTo(thing.CellX - 16, thing.CellY - 16);
            Assert.That(Errors, Does.Contain("stands on the thing " + thing.Id));
        }

        [Test]
        public void AMapWithNoYardStandsOnItsMiddle()
        {
            var file = MapFile.Load(MapSizeBench.BigMap);
            var layout = file.Layout();
            Assert.That((layout.ChunksX, layout.ChunksZ, layout.GridW), Is.EqualTo((27, 27, 1728)));
            var (x, z) = layout.CellCenter(864, 864);
            Assert.That((x, z), Is.EqualTo((0f, 0f)), "the middle cell at world (0, 0)");
            Assert.That(layout.IsHighRes(13, 13), Is.False, "no full detail when the map names none");
        }

        [Test]
        public void ASizeOffTheChunksIsSaid()
        {
            Change(o => o["size"] = new JArray(600, 576));
            Assert.That(Errors, Does.Contain("whole number of 64-cell chunks"));
        }

        [Test]
        public void AWorldDroppedBelowTheSeaIsSaid()
        {
            LandBecomes(u => u - 304);
            Assert.That(Errors, Does.Contain("highest cell").And.Contain("under the sea"));
        }

        [Test]
        public void ASpawnInTheSeaOrOffItsMarkIsSaid()
        {
            var land = LandFile.Read(File.ReadAllText(Path.Combine(map, MapFile.LandName)), Side, Side);
            int sea = Array.FindIndex(land, u => u < -4);
            Change(o => o["spawn"] = new JArray(sea % Side, sea / Side));
            Assert.That(Errors, Does.Contain("@ of cells.txt"));
            Assert.That(Errors, Does.Contain("under the sea"));
        }

        [Test]
        public void ALaidRowInTheLakeIsSaid()
        {
            RowMovesTo(270, 365);
            Assert.That(Errors, Does.Contain("stands on water at"));
        }

        [Test]
        public void ALaidRowOffTheMapIsSaid()
        {
            RowMovesTo(560, 367);
            Assert.That(Errors, Does.Contain("runs off the map"));
        }

        [Test]
        public void ADrawableRangeUpsideDownIsSaid()
        {
            Change(o => o["drawable"] = new JArray(2000, -200));
            Assert.That(Errors, Does.Contain("drawable range runs from 2000 up to -200"));
            Change(o => o["drawable"] = new JArray(2000, 2000));
            Assert.That(Errors, Does.Contain("drawable range runs from 2000 up to 2000"));
        }

        [Test]
        public void LandOutsideADrawableRangeIsSaid()
        {
            Change(o => o["drawable"] = new JArray(-173, 1319));
            Assert.That(Errors, Does.Contain("lowest cell is -174, below map.json's drawable -173"));
            Assert.That(Errors, Does.Not.Contain("above map.json's drawable"));
            Change(o => o["drawable"] = new JArray(-174, 1318));
            Assert.That(Errors, Does.Contain("highest cell is 1319, above map.json's drawable 1318"));
            Assert.That(Errors, Does.Not.Contain("below map.json's drawable"));
        }

        [Test]
        public void LandRightUpToADrawableRangeIsFit()
        {
            Change(o => o["drawable"] = new JArray(-174, 1319));
            Assert.That(Errors, Is.Empty);
        }

        [Test]
        public void AFolderThatIsntTheMapItNamesIsSaid()
        {
            Change(o => o["id"] = "drowned-chain");
            Assert.That(Errors, Does.Contain("\"drowned-chain\", in the folder \"valley\""));
        }

        [Test]
        public void TheValleyDressesItsPlaces()
        {
            Assert.That(MapFile.Load(map).Dresses, Is.EquivalentTo(new[] { "lake", "rocks", "river-rocks", "court", "landmarks", "falls", "bridge", "tower", "defense-field", "coast", "skyline", "cabins", "villages", "world-edge" }));
            Change(o => o.Remove("dresses"));
            Assert.That(MapFile.Load(map).Dresses, Is.Empty, "left out, none");
            Assert.That(Errors, Is.Empty);
        }

        [Test]
        public void APlaceDressedTwiceOrMisnamedIsSaid()
        {
            Change(o => o["dresses"] = new JArray("court", "court", "Ball court"));
            Assert.That(Errors, Does.Contain("dresses court more than once").And.Contain("\"Ball court\", which isn't a place's name"));
        }

        [Test]
        public void AnotherSizeIsTakenAndItsFilesHeldToIt()
        {
            Change(o => o["size"] = new JArray(640, 640));
            Assert.That(Errors, Does.Not.Contain("chunks each way"));
            Assert.That(Errors, Does.Contain("cells.txt isn't 640 x 640").And.Contain("land.json isn't for a 640 x 640 map"));
        }

        [Test]
        public void MissingFilesAreSaid()
        {
            File.Delete(Path.Combine(map, MapFile.CellsName));
            File.Delete(Path.Combine(map, MapFile.LandName));
            Assert.That(Errors, Does.Contain("no cells.txt").And.Contain("no land.json"));
            File.Delete(Path.Combine(map, MapFile.FileName));
            Assert.That(Errors, Does.Contain("map.json can't be read"));
        }

        [Test]
        public void ALakeOnDryLandIsSaid()
        {
            LandBecomes(u => Math.Max(u, -4));
            Change(o =>
            {
                o["expects"]["lowest"] = -4;
                o["expects"]["landShare"] = 100;
            });
            Assert.That(Errors, Does.Contain("has dry land in it"));
        }

        [Test]
        public void ASeaLevelThePlacesDontShareIsSaid()
        {
            Change(o => o["seaLevel"] = -8);
            Assert.That(Errors, Does.Contain("finds water below -4"));
        }

        void Change(Action<JObject> change)
        {
            string path = Path.Combine(map, MapFile.FileName);
            var o = JObject.Parse(File.ReadAllText(path));
            change(o);
            File.WriteAllText(path, o.ToString());
        }

        void LandBecomes(Func<int, int> move)
        {
            string path = Path.Combine(map, MapFile.LandName);
            var land = LandFile.Read(File.ReadAllText(path), Side, Side).Select(move).ToArray();
            File.WriteAllText(path, LandFile.Write(land, Side, Side, 0.125));
        }

        int Side => MapFile.Load(map).W;
    }
}
