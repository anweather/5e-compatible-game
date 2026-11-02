using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// #63: a player's mods live in their data folder, not the install. Reading both folders, the player's copy
    /// winning an id both have, the save, and the move out of the install. Every folder here is a temp one.
    /// </summary>
    public class PlayerModsTests
    {
        const string Mod = "{ \"title\": \"Mine\", \"idea\": \"Slides go on.\", \"patch\": { \"sim\": { \"push\": { \"slideCells\": 7 } } } }";
        const string OtherMod = "{ \"title\": \"Other\", \"idea\": \"Slides go further.\", \"patch\": { \"sim\": { \"push\": { \"slideCells\": 9 } } } }";

        string root, install, player;

        [SetUp]
        public void MakeFolders()
        {
            root = Path.Combine(Path.GetTempPath(), "player-mods-" + Guid.NewGuid().ToString("N"));
            install = Directory.CreateDirectory(Path.Combine(root, "install", "mods")).FullName;
            player = Path.Combine(root, "data", "mods");
        }

        [TearDown]
        public void RemoveFolders()
        {
            SetReadOnly(install, false);
            Directory.Delete(root, true);
        }

        // ---------- helpers ----------

        // A copy of the rules with its shipped mods, so a save gone wrong lands here and not in the repo.
        string CopyOfTheRules()
        {
            string rules = Path.Combine(root, "rules"), mods = Directory.CreateDirectory(Path.Combine(rules, "mods")).FullName;
            foreach (var file in Directory.GetFiles(ChemistryTests.RulesDir(), "*.json")) File.Copy(file, Path.Combine(rules, Path.GetFileName(file)));
            foreach (var file in Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json")) File.Copy(file, Path.Combine(mods, Path.GetFileName(file)));
            return rules;
        }

        void Shipped(string id, string json = Mod) => File.WriteAllText(Path.Combine(install, id + ".json"), json);

        void Players(string id, string json = Mod)
        {
            Directory.CreateDirectory(player);
            File.WriteAllText(Path.Combine(player, id + ".json"), json);
        }

        string InInstall(string id) => Path.Combine(install, id + ".json");

        string InPlayers(string id) => Path.Combine(player, id + ".json");

        // A build's rules/mods: the ids listed as shipped, plus whatever an older copy left there.
        void BuildListed(params string[] ids)
        {
            foreach (string id in ids) Shipped(id);
            ModMove.WriteShippedList(install);
        }

        static void SetReadOnly(string dir, bool on)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var file in Directory.GetFiles(dir)) File.SetAttributes(file, on ? FileAttributes.ReadOnly : FileAttributes.Normal);
                return;
            }
            using var chmod = Process.Start(new ProcessStartInfo("chmod", (on ? "555 \"" : "755 \"") + dir + "\"") { UseShellExecute = false });
            chmod.WaitForExit();
        }

        // ---------- reading ----------

        [Test]
        public void TheCatalogListsTheShippedModsAndThePlayers()
        {
            Shipped("wild");
            Players("mine");
            var catalog = new ModCatalog(install, player);
            Assert.That(catalog.Mods.Select(m => m.Id), Is.EqualTo(new[] { "mine", "wild" }));
            Assert.That(catalog.Find("mine").Title, Is.EqualTo("Mine"));
        }

        [Test]
        public void ThePlayersFolderNeedNotExist()
        {
            Shipped("wild");
            Assert.That(new ModCatalog(install, player).Mods.Select(m => m.Id), Is.EqualTo(new[] { "wild" }));
        }

        [Test]
        public void ASavedModShowsUpOnRefresh()
        {
            var catalog = new ModCatalog(install, player);
            Players("mine");
            Assert.That(catalog.Refresh(InPlayers("mine")).Id, Is.EqualTo("mine"));
            Assert.That(catalog.Find("mine"), Is.Not.Null);
        }

        [Test]
        public void TheToolsFindAPlayersModAndAShippedOneByName()
        {
            Players("mine");
            string rules = CopyOfTheRules();
            var bench = new Workbench(rules, player);
            Assert.That(bench.PatchJson("mine"), Is.EqualTo(Mod));
            Assert.That(bench.PatchJson("wildfire"), Is.EqualTo(File.ReadAllText(Path.Combine(rules, "mods", "wildfire.json"))));
        }

        // ---------- the same id in both: the player's copy wins, for the list, the tools and a reload ----------

        [Test]
        public void ThePlayersCopyWinsAnIdBothHave()
        {
            Shipped("wild", OtherMod);
            Players("wild");
            var catalog = new ModCatalog(install, player);
            Assert.That(catalog.Mods.Count, Is.EqualTo(1), "one row, not two");
            Assert.That(catalog.Find("wild").Json, Is.EqualTo(Mod));
            Assert.That(catalog.Refresh(InInstall("wild")).Json, Is.EqualTo(Mod), "the shipped file changing doesn't bring it to the front");
        }

        [Test]
        public void ThePlayersCopyWinsInTheToolsToo()
        {
            Players("wildfire");
            Assert.That(new Workbench(CopyOfTheRules(), player).PatchJson("wildfire"), Is.EqualTo(Mod));
        }

        [Test]
        public void DeletingThePlayersCopyBringsTheShippedOneBack()
        {
            Shipped("wild", OtherMod);
            Players("wild");
            var catalog = new ModCatalog(install, player);
            File.Delete(InPlayers("wild"));
            Assert.That(catalog.Refresh(InPlayers("wild")).Json, Is.EqualTo(OtherMod));
            File.Delete(InInstall("wild"));
            Assert.That(catalog.Refresh(InInstall("wild")), Is.Null);
            Assert.That(catalog.Mods, Is.Empty);
        }

        // ---------- saving ----------

        static Draft FireDraft(Workbench bench) => bench.Tune(null, "fire.spreadChance", "0.4");

        [Test]
        public void ASaveGoesToThePlayersFolderAndTouchesNoShippedMod()
        {
            string rules = CopyOfTheRules(), shipped = Path.Combine(rules, "mods");
            var before = Directory.GetFiles(shipped).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();
            var bench = new Workbench(rules, player);
            string path = bench.Save(FireDraft(bench).Id, "Mine", "Fire spreads.");
            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(player));
            Assert.That(Directory.GetFiles(shipped).Select(f => (f, File.GetLastWriteTimeUtc(f))), Is.EqualTo(before));
        }

        [Test]
        public void ASaveTakesAShippedNameOnlyWithOverwrite()
        {
            string rules = CopyOfTheRules(), wildfire = Path.Combine(rules, "mods", "wildfire.json"), shipped = File.ReadAllText(wildfire);
            var bench = new Workbench(rules, player);
            var draft = FireDraft(bench);
            var e = Assert.Throws<RulesToolException>(() => bench.Save(draft.Id, "Mine", "Fire spreads.", "wildfire"));
            Assert.That(e.Message, Does.Contain("already a mod called wildfire"));
            Assert.That(File.Exists(InPlayers("wildfire")), Is.False);

            string path = bench.Save(draft.Id, "Mine", "Fire spreads.", "wildfire", overwrite: true);
            Assert.That(path, Is.EqualTo(InPlayers("wildfire")), "the overwrite goes in front of the shipped mod");
            Assert.That(File.ReadAllText(wildfire), Is.EqualTo(shipped));
            Assert.That(ModPatch.Parse(bench.PatchJson("wildfire")).Title, Is.EqualTo("Mine"));
        }

        // ---------- moving out of the install ----------

        [Test]
        public void TheBuildListsEveryShippedModAndNothingElse()
        {
            BuildListed("b-mod", "a-mod");
            File.WriteAllText(Path.Combine(install, "LICENSE.md"), "not a mod");
            ModMove.WriteShippedList(install);
            Assert.That(File.ReadAllText(Path.Combine(install, ModMove.ShippedList)), Is.EqualTo("a-mod\nb-mod\n"));
            Assert.That(new ModCatalog(install).Mods.Count, Is.EqualTo(2), "the list isn't read as a mod");
        }

        [Test]
        public void APlayersModMovesAndTheShippedOnesStay()
        {
            BuildListed("wild");
            Shipped("mine");
            var said = ModMove.FromInstall(install, player);
            Assert.That(File.ReadAllText(InPlayers("mine")), Is.EqualTo(Mod));
            Assert.That(File.Exists(InInstall("mine")), Is.False, "moved, not copied");
            Assert.That(File.Exists(InInstall("wild")), Is.True);
            Assert.That(File.Exists(InPlayers("wild")), Is.False);
            Assert.That(said.Count, Is.EqualTo(1));
            Assert.That(said[0], Does.Contain("moved"));
        }

        [Test]
        public void WithNoShippedListNothingMoves()
        {
            Shipped("wild");
            Assert.That(ModMove.FromInstall(install, player), Is.Empty);
            Assert.That(File.Exists(InInstall("wild")), Is.True);
            Assert.That(Directory.Exists(player), Is.False);
        }

        [Test]
        public void AnInstallWithNoPlayersModsLeavesTheDataFolderAlone()
        {
            BuildListed("wild");
            Assert.That(ModMove.FromInstall(install, player), Is.Empty);
            Assert.That(Directory.Exists(player), Is.False);
        }

        [Test]
        public void AFileInTheDataFolderIsNeverOverwritten()
        {
            BuildListed("wild");
            Shipped("mine", OtherMod);
            Players("mine");
            File.SetLastWriteTimeUtc(InInstall("mine"), DateTime.UtcNow.AddDays(1));
            var said = ModMove.FromInstall(install, player);
            Assert.That(File.ReadAllText(InPlayers("mine")), Is.EqualTo(Mod));
            Assert.That(File.ReadAllText(InInstall("mine")), Is.EqualTo(OtherMod), "the one that lost isn't thrown away");
            Assert.That(said[0], Does.Contain("differs"));
            Assert.That(ModMove.FromInstall(install, player), Is.Empty, "and it says so once");
        }

        [Test]
        public void TheSameFileInBothJustLeavesTheInstall()
        {
            BuildListed("wild");
            Shipped("mine");
            Players("mine");
            ModMove.FromInstall(install, player);
            Assert.That(File.Exists(InInstall("mine")), Is.False);
            Assert.That(File.ReadAllText(InPlayers("mine")), Is.EqualTo(Mod));
        }

        [Test]
        public void AReadOnlyInstallCopiesOnceAndDoesntFail()
        {
            BuildListed("wild");
            Shipped("mine");
            SetReadOnly(install, true);
            var said = ModMove.FromInstall(install, player);
            Assert.That(said[0], Does.Contain("its copy stays"));
            Assert.That(File.ReadAllText(InPlayers("mine")), Is.EqualTo(Mod));
            Assert.That(File.Exists(InInstall("mine")), Is.True);

            File.Delete(InPlayers("mine"));
            Assert.That(ModMove.FromInstall(install, player), Is.Empty);
            Assert.That(File.Exists(InPlayers("mine")), Is.False, "a mod the player deleted doesn't come back");
        }

        [Test]
        public void ADataFolderThatCantBeWrittenLeavesTheInstallForNextTime()
        {
            BuildListed("wild");
            Shipped("mine");
            string blocked = Path.Combine(root, "blocked");
            File.WriteAllText(blocked, "a file where the folder should go");
            var said = ModMove.FromInstall(install, Path.Combine(blocked, "mods"));
            Assert.That(said[0], Does.Contain("couldn't be moved"));
            Assert.That(File.Exists(InInstall("mine")), Is.True);
            Assert.That(new ModCatalog(install, Path.Combine(blocked, "mods")).Find("mine"), Is.Not.Null, "still in the list meanwhile");

            ModMove.FromInstall(install, player);
            Assert.That(File.Exists(InPlayers("mine")), Is.True);
            Assert.That(File.Exists(InInstall("mine")), Is.False);
        }

        // ---------- lockstep ----------

        [Test]
        public void WhereAModLivesChangesNothingInTheRoom()
        {
            Shipped("mine");
            Players("elsewhere");
            var fromInstall = new ModCatalog(install).Find("mine");
            var fromPlayer = new ModCatalog(Path.Combine(root, "none"), player).Find("elsewhere");
            Assert.That(fromPlayer.Json, Is.EqualTo(fromInstall.Json), "the room carries the text, and the text doesn't name a folder");

            var rules = Rules.Load(ChemistryTests.RulesDir());
            Assert.That(RuleSet.Compose(rules, new[] { new ActiveMod("same", fromPlayer.Json) }).Fingerprint,
                Is.EqualTo(RuleSet.Compose(rules, new[] { new ActiveMod("same", fromInstall.Json) }).Fingerprint));
        }
    }
}
