// GroundPackTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in GroundPackTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class GroundPackWorldTests
    {
        [Test]
        public void EveryBakedMapWasBakedFromItsInputsAsTheyAre()
        {
            var baked = Directory.GetDirectories(LandFolders.Folders.Maps).Where(GroundPack.In).ToArray();
            Assert.That(baked, Is.Not.Empty, "the valley has a ground/");
            foreach (string dir in baked)
            {
                var head = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, GroundPack.FolderName, GroundPack.FileName)));
                Assert.That((string)head["inputs"], Is.EqualTo(MapInputs.Hash(dir)),
                    $"{Path.GetFileName(dir)}'s ground/ was baked from other inputs than its folder has now: run Prototype > Bake Map Data and commit what it writes");
            }
        }

        [Test]
        public void TheInputsHashMovesWithAStampAndNotWithLineEndings()
        {
            string dir = Path.Combine(Path.GetTempPath(), "inputs-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, MapInputs.ThemesFolder));
                File.WriteAllText(Path.Combine(dir, MapStamps.FileName), "{\n  \"stamps\": []\n}\n");
                File.WriteAllText(Path.Combine(dir, MapInputs.ThemesFolder, "a.json"), "{}\n");
                string before = MapInputs.Hash(dir);
                File.WriteAllText(Path.Combine(dir, MapStamps.FileName), "{\r\n  \"stamps\": []\r\n}\r\n");
                Assert.That(MapInputs.Hash(dir), Is.EqualTo(before), "CRLF reads as LF");
                File.WriteAllText(Path.Combine(dir, MapStamps.FileName), "{\n  \"stamps\": [1]\n}\n");
                Assert.That(MapInputs.Hash(dir), Is.Not.EqualTo(before), "a stamp edit");
                File.WriteAllText(Path.Combine(dir, MapStamps.FileName), "{\n  \"stamps\": []\n}\n");
                File.WriteAllText(Path.Combine(dir, MapInputs.ThemesFolder, "a.json"), "{ }\n");
                Assert.That(MapInputs.Hash(dir), Is.Not.EqualTo(before), "a theme edit");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void AMomentUnderTheGroundIsNoFall()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 9; k++) watch.Feed(3f, 0.1f);
            watch.Feed(-1f, 0.1f);
            for (int k = 0; k < 9; k++) watch.Feed(3f, 0.1f);
            Assert.That(watch.Fell, Is.False);
            Assert.That(watch.Deepest, Is.EqualTo(3f));
        }

        [Test]
        public void ASecondWellUnderIsAFall()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 11; k++) watch.Feed(4400f, 0.1f);
            Assert.That(watch.Fell);
        }

        [Test]
        public void AFewCentimetresUnderIsNothing()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 100; k++) watch.Feed(0.5f, 0.1f);
            Assert.That(watch.Fell, Is.False);
        }
    }
}
