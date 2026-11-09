// A map's folder and MapCheck (docs/design/maps-as-data.md): every shipped map passes, and each way a folder can quietly
// be the wrong world is said by name.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class MapTests
    {
        // The FNV-1a of the cell map taken from the baked scene when it moved to cells.txt.
        // Moved for hold-east-path: the west brook is dry, its cells grass (and the trees its draws moved).
        const uint ValleyCells = 307482015u;

        string map;

        [SetUp]
        public void CopyTheValley()
        {
            map = Path.Combine(Path.GetTempPath(), "map-tests-" + Guid.NewGuid().ToString("N"), "valley");
            Directory.CreateDirectory(map);
            foreach (string file in Directory.GetFiles(ChemistryTests.WorldDir())) File.Copy(file, Path.Combine(map, Path.GetFileName(file)));
        }

        [TearDown]
        public void RemoveTheCopy() => Directory.Delete(Path.GetDirectoryName(map), true);

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

        // The copied map's side, as its map.json says.
        int Side => MapFile.Load(map).W;

        string Errors => string.Join("\n", MapCheck.Errors(map));

        [Test]
        public void ASizeOffTheChunksIsSaid()
        {
            Change(o => o["size"] = new JArray(600, 576));
            Assert.That(Errors, Does.Contain("whole number of 64-cell chunks"));
        }

        [Test]
        public void EveryMapPassesItsChecks()
        {
            var maps = Directory.GetDirectories(ChemistryTests.Folders.Maps);
            Assert.That(maps.Select(Path.GetFileName), Does.Contain(GameFolders.DefaultMap));
            foreach (string folder in maps) Assert.That(MapCheck.Errors(folder), Is.Empty, folder);
        }

        [Test]
        public void TheValleysCellsAreTheShapeTheyAlwaysWere()
        {
            var rows = MapFile.ReadCells(ChemistryTests.WorldDir()).Split('\n');
            Assert.That(rows.Length, Is.EqualTo(576));
            Assert.That(rows.All(r => r.Length == 576));
            uint hash = 2166136261;
            foreach (char c in string.Join("\n", rows)) hash = (hash ^ c) * 16777619;
            Assert.That(hash, Is.EqualTo(ValleyCells), "the cells the scene held, byte for byte");
        }

        [Test]
        public void AWorldDroppedBelowTheSeaIsSaid()
        {
            LandBecomes(u => u - 304); // the 38 m the yard's corner once took off the second map
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

        void RowMovesTo(int x, int y)
        {
            string path = Path.Combine(map, WorldBuildings.FileName);
            var o = JObject.Parse(File.ReadAllText(path));
            ((JArray)o["buildings"]).OfType<JObject>().Single(r => (string)r["id"] == "hold#1")["at"] = new JArray(x, y);
            File.WriteAllText(path, o.ToString());
        }

        [Test]
        public void ALaidRowInTheLakeIsSaid()
        {
            // The brook behind the castle that this used is dry since hold-east-path: the lake east of the castle.
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
        public void ALaidRowOnAThingIsSaid()
        {
            var thing = WorldThings.Load(map).All.First(t => t.CellX > 40 && t.CellY > 40 && t.CellX < 530 && t.CellY < 530);
            RowMovesTo(thing.CellX - 16, thing.CellY - 16);
            Assert.That(Errors, Does.Contain("stands on the thing " + thing.Id));
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
            Assert.That(MapFile.Load(map).Dresses, Is.EquivalentTo(new[] { "lake", "rocks", "river-rocks", "court", "landmarks", "falls", "bridge", "tower", "defense-field", "coast", "skyline" }));
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
            // Any whole number of chunks is a size (map-size); the valley's cells and land then don't fit it, and say so.
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

        // The cells stand where the map says (map.json's yard and fullDetail): the valley's are where they always stood.
        [Test]
        public void TheValleysLayoutIsTheOneItAlwaysHad()
        {
            var file = MapFile.Load(ChemistryTests.WorldDir());
            TerrainGrid a = file.Layout(), b = TerrainGrid.Valley();
            Assert.That((a.CellSize, a.OriginX, a.OriginZ, a.ChunksX, a.ChunksZ, a.U0, a.V0, a.GridW, a.GridH),
                Is.EqualTo((b.CellSize, b.OriginX, b.OriginZ, b.ChunksX, b.ChunksZ, b.U0, b.V0, b.GridW, b.GridH)));
            for (int j = 0; j < b.ChunksZ; j++)
                for (int i = 0; i < b.ChunksX; i++) Assert.That(a.IsHighRes(i, j), Is.EqualTo(b.IsHighRes(i, j)), $"chunk {i},{j}");
            Assert.That(file.Origin, Is.EqualTo((279, 330)));
            Assert.That(file.Spawn, Is.EqualTo((292, 328)), "the yard's @");
        }

        [Test]
        public void AMapWithNoYardStandsOnItsMiddle()
        {
            var file = MapFile.Load(Path.Combine(ChemistryTests.Folders.Maps, "flat-1728"));
            var layout = file.Layout();
            Assert.That((layout.ChunksX, layout.ChunksZ, layout.GridW), Is.EqualTo((27, 27, 1728)));
            var (x, z) = layout.CellCenter(864, 864);
            Assert.That((x, z), Is.EqualTo((0f, 0f)), "the middle cell at world (0, 0)");
            Assert.That(layout.IsHighRes(13, 13), Is.False, "no full detail when the map names none");
        }
    }
}
