using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The player save (PlayerSave.cs; docs/design/player-saves.md, step 3): it comes back as it was written, the Hold's
    /// old progress file still loads, a write cut short leaves the last save, and the same progress is the same text.
    /// </summary>
    [Category("Unit")]
    public class PlayerSaveTests
    {
        static string Temp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "player-save-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string FileIn(string folder) => Path.Combine(folder, PlayerSave.FileName);

        static PlayerSave Read(string folder, string id = null)
        {
            var s = new PlayerSave(folder, id);
            s.Load();
            return s;
        }

        [Test]
        public void ASaveComesBackAsItWasWritten()
        {
            string folder = Temp();
            var s = new PlayerSave(folder, "ann");
            s.Raise(17);
            s.Known.UnionWith(new[] { "steam-burst", "mud-wall" });
            s.Save();
            var back = Read(folder);
            Assert.That(back.Id, Is.EqualTo("ann"), "the save's own id when none is given");
            Assert.That(back.Points, Is.EqualTo(17));
            Assert.That(back.Known, Is.EqualTo(new[] { "mud-wall", "steam-burst" }));
            Assert.That(back.Trouble, Is.Null);
            Assert.That(back.Text(), Is.EqualTo(File.ReadAllText(FileIn(folder))), "a save of a load is the save again");
        }

        [Test]
        public void TheSameProgressIsTheSameText()
        {
            var a = new PlayerSave(Temp(), "ann");
            var b = new PlayerSave(Temp(), "ann");
            a.Known.Add("b-combo");
            a.Known.Add("a-combo");
            b.Known.Add("a-combo");
            b.Known.Add("b-combo");
            a.Raise(5);
            b.Raise(3);
            b.Raise(5);
            Assert.That(a.Text(), Is.EqualTo(b.Text()));
            // The format: a change here means old player saves need a new version.
            Assert.That(a.Text(), Is.EqualTo("{\n  \"version\": 2,\n  \"id\": \"ann\",\n  \"points\": 5,\n  \"known\": [\n    \"a-combo\",\n    \"b-combo\"\n  ]\n}\n"));
            Assert.That(new PlayerSave(Temp()).Text(), Is.EqualTo("{\n  \"version\": 2,\n  \"points\": 0,\n  \"known\": []\n}\n"), "no id, none written");
        }

        // The same progress is the same text on a Mac and a PC: "\n" whatever Environment.NewLine is, and a save written
        // with "\r\n" (by hand, or by a build before this) still reads.
        [Test]
        public void TheTextIsTheSameOnEveryMachine()
        {
            var s = new PlayerSave(Temp(), "ann");
            s.Raise(5);
            s.Known.Add("mud-wall");
            Assert.That(s.Text(), Does.Not.Contain("\r"));
            string folder = Temp();
            File.WriteAllText(FileIn(folder), s.Text().Replace("\n", "\r\n"));
            var back = Read(folder);
            Assert.That((back.Id, back.Points, back.Trouble), Is.EqualTo(("ann", 5, (string)null)));
            Assert.That(back.Known, Is.EqualTo(new[] { "mud-wall" }));
            Assert.That(back.Text(), Is.EqualTo(s.Text()), "and writes it back as \"\\n\"");
        }

        [Test]
        public void TheHoldsOldProgressFileLoads()
        {
            string folder = Temp();
            File.WriteAllText(FileIn(folder), "{\n  \"version\": 1,\n  \"points\": 23\n}");
            var s = Read(folder, "ann");
            Assert.That((s.Points, s.Known.Count, s.Trouble), Is.EqualTo((23, 0, (string)null)));
            var old = new ProgressFile(folder);
            old.Load();
            Assert.That(old.Points, Is.EqualTo(23), "as the Hold reads it");
            s.Known.Add("mud-wall");
            s.Save();
            Assert.That(Read(folder).Points, Is.EqualTo(23));
            Assert.That(File.ReadAllText(FileIn(folder) + ".bak"), Does.Contain("\"version\": 1"), "the old file kept as the copy");
        }

        [Test]
        public void AWriteCutShortLeavesTheLastSave()
        {
            string folder = Temp();
            var s = new PlayerSave(folder, "ann");
            s.Raise(10);
            s.Save();
            // A crash after the new save was half written, before its rename: the file beside it, torn.
            File.WriteAllText(FileIn(folder) + ".new", "{\n  \"version\": 2,\n  \"id\": \"an");
            var back = Read(folder);
            Assert.That(back.Points, Is.EqualTo(10));
            Assert.That(back.Trouble, Is.Null, "nothing went wrong with the save itself");
            back.Raise(20);
            back.Save();
            Assert.That(Read(folder).Points, Is.EqualTo(20));
            Assert.That(File.Exists(FileIn(folder) + ".new"), Is.False, "the torn file is gone with the next save");
        }

        [Test]
        public void TheHoldsPointsKeepWhatElseTheSaveHolds()
        {
            string folder = Temp();
            var s = new PlayerSave(folder, "ann");
            s.Known.Add("mud-wall");
            s.Raise(4);
            s.Save();
            var hold = new ProgressFile(folder);
            hold.Load();
            hold.Save(30);
            var back = Read(folder);
            Assert.That((back.Id, back.Points), Is.EqualTo(("ann", 30)));
            Assert.That(back.Known, Is.EqualTo(new[] { "mud-wall" }));
        }

        [TestCase("{ \"version\": 2, \"points\": 4, \"known\": [1] }")]
        [TestCase("{ \"version\": 2, \"points\": 4 }")]
        [TestCase("{ \"version\": 2, \"id\": 7, \"points\": 4, \"known\": [] }")]
        [TestCase("{ \"version\": 2, \"points\": -1, \"known\": [] }")]
        public void ABadSaveFallsBackToTheLastGoodCopy(string bad)
        {
            string folder = Temp();
            var s = new PlayerSave(folder, "ann");
            s.Raise(10);
            s.Save();
            s.Raise(20);
            s.Save();
            File.WriteAllText(FileIn(folder), bad);
            var back = Read(folder);
            Assert.That(back.Points, Is.EqualTo(10));
            Assert.That(back.Trouble, Is.Not.Null);
            Assert.That(File.ReadAllText(FileIn(folder) + ".broken"), Is.EqualTo(bad));
        }
    }
}
