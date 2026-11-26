// MapIdentityTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in MapIdentityTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class MapIdentityWorldTests
    {

        [Test]
        public void LandHashIsEmptyUntilLandIsStamped()
        {
            var w = new World(ChemistryTests.R, 64, 64, 1);
            w.SetLand(new int[64 * 64]);
            Assert.That(w.LandHash(), Is.EqualTo(""));
        }
    }

    /// <summary>
    /// island-startup: a map checked clean is taken on its record (MapCheck.Errors with a record: the folder's hash), and
    /// checked whole again once anything in its folder changes.
    /// </summary>
    [Category("Simulation")]
    public class MapCheckRecordTests
    {
        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        [Test]
        public void AMapCheckedCleanIsTakenOnItsRecordUntilItChanges()
        {
            string root = Path.Combine(Path.GetTempPath(), "map-check-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, "valley"), record = Path.Combine(root, "record", "valley.check");
            try
            {
                CopyDir(Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley"), dir);
                Assert.That(MapCheck.Errors(dir, record), Is.Empty);
                Assert.That(File.Exists(record), Is.True, "a clean check is recorded");
                string hash = MapCheck.FolderHash(dir);
                Assert.That(File.ReadAllText(record), Does.StartWith(hash));
                Assert.That(MapCheck.Errors(dir, record), Is.Empty, "taken on its record");

                // A file changed (its map.json naming another id): the hash moves and the whole check runs, finding it.
                string map = Path.Combine(dir, MapFile.FileName);
                File.WriteAllText(map, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(map), "\"id\": *\"valley\"", "\"id\": \"valley-two\""));
                Assert.That(MapCheck.FolderHash(dir), Is.Not.EqualTo(hash));
                Assert.That(MapCheck.Errors(dir, record), Is.Not.Empty, "checked whole again");
                Assert.That(File.ReadAllText(record), Does.StartWith(hash), "and a map with errors isn't recorded");
                // No record given: always the whole check.
                Assert.That(MapCheck.Errors(dir, null), Is.EqualTo(MapCheck.Errors(dir)));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
