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
        const uint ValleyCells = 3225049811u;

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
            var land = LandFile.Read(File.ReadAllText(path), MapCheck.Size, MapCheck.Size).Select(move).ToArray();
            File.WriteAllText(path, LandFile.Write(land, MapCheck.Size, MapCheck.Size, 0.125));
        }

        string Errors => string.Join("\n", MapCheck.Errors(map));

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
            var land = LandFile.Read(File.ReadAllText(Path.Combine(map, MapFile.LandName)), MapCheck.Size, MapCheck.Size);
            int sea = Array.FindIndex(land, u => u < -4);
            Change(o => o["spawn"] = new JArray(sea % MapCheck.Size, sea / MapCheck.Size));
            Assert.That(Errors, Does.Contain("@ of cells.txt"));
            Assert.That(Errors, Does.Contain("under the sea"));
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
        public void AnotherSizeIsRefusedByName()
        {
            Change(o => o["size"] = new JArray(640, 640));
            Assert.That(Errors, Does.Contain("only 576 x 576"));
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
    }
}
