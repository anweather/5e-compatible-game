using System;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Where the game's folders are (GameFolders): a build's packed rules or the repo's, world/ beside them, and the
    /// player's mods in the chat's folder (#63), a probe's while one is set, and the Editor's own data folder (#78). Every folder here is a temp one.
    /// </summary>
    public class GameFoldersTests
    {
        string root, repo, project, packed, data;

        [SetUp]
        public void MakeFolders()
        {
            root = Path.Combine(Path.GetTempPath(), "game-folders-" + Guid.NewGuid().ToString("N"));
            repo = Path.Combine(root, "repo");
            project = Directory.CreateDirectory(Path.Combine(repo, "unity", "Library", "VP", "1", "Assets")).FullName;
            packed = Directory.CreateDirectory(Path.Combine(root, "build", "StreamingAssets")).FullName;
            data = Path.Combine(root, "data");
            Rules(Path.Combine(repo, "rules"));
        }

        [TearDown]
        public void RemoveFolders() => Directory.Delete(root, true);

        static void Rules(string dir) => File.WriteAllText(Path.Combine(Directory.CreateDirectory(dir).FullName, "materials.json"), "{}");

        [Test]
        public void TheEditorWalksUpToTheReposRulesFromADeepProject()
        {
            Rules(Path.Combine(packed, "rules"));
            var folders = GameFolders.ForGame(packed, project, editor: true, data);
            Assert.That(folders.Rules, Is.EqualTo(Path.Combine(repo, "rules")), "the Editor never reads a pack");
            Assert.That(folders.World, Is.EqualTo(Path.Combine(repo, "world", "maps", "valley")));
            Assert.That(folders.ShippedMods, Is.EqualTo(Path.Combine(repo, "rules", "mods")));
        }

        [Test]
        public void ABuildReadsItsPackedRulesWhenThereAreAny()
        {
            Assert.That(GameFolders.ForGame(packed, project, editor: false, data).Rules, Is.EqualTo(Path.Combine(repo, "rules")), "no pack: the repo's");
            Rules(Path.Combine(packed, "rules"));
            var folders = GameFolders.ForGame(packed, project, editor: false, data);
            Assert.That(folders.Rules, Is.EqualTo(Path.Combine(packed, "rules")));
            Assert.That(folders.World, Is.EqualTo(Path.Combine(packed, "world", "maps", "valley")));
        }

        [Test]
        public void ABuildKeepsTheDataFolderItIsGiven()
        {
            Assert.That(GameFolders.ForGame(packed, project, editor: false, data).PlayerData, Is.EqualTo(data), "no pack");
            Rules(Path.Combine(packed, "rules"));
            Assert.That(GameFolders.ForGame(packed, project, editor: false, data).PlayerData, Is.EqualTo(data), "packed");
        }

        [Test]
        public void TheEditorKeepsItsOwnDataFolderInTheProjectsLibrary()
        {
            var folders = GameFolders.ForGame(packed, project, editor: true, data);
            string own = Path.Combine(repo, "unity", "Library", "VP", "1", "Library", "EditorPlayerData");
            Assert.That(folders.PlayerData, Is.EqualTo(own), "never the shipped game's (#78)");
            Assert.That(folders.Claude.Path, Is.EqualTo(Path.Combine(own, ClaudeFolder.Name)));
            Assert.That(GameFolders.EditorPlayerData(project + Path.DirectorySeparatorChar), Is.EqualTo(own), "a trailing slash");
        }

        [Test]
        public void APlayersModsAreInTheChatsFolderOrAProbes()
        {
            var folders = GameFolders.ForGame(packed, project, editor: false, data);
            Assert.That(folders.PlayerData, Is.EqualTo(data));
            Assert.That(folders.Claude.Path, Is.EqualTo(Path.Combine(data, ClaudeFolder.Name)));
            Assert.That(folders.Mods, Is.EqualTo(Path.Combine(data, "claude-terminal", "mods")), "beside notes.md, as #63 put them");
            folders.Claude.Test = Path.Combine(root, "probe");
            Assert.That(folders.Mods, Is.EqualTo(Path.Combine(root, "probe", "mods")));
            folders.Claude.Test = null;
            Assert.That(folders.Mods, Is.EqualTo(Path.Combine(data, "claude-terminal", "mods")));
        }

        [Test]
        public void WithNoPlayerAModGoesToRulesMods()
        {
            var folders = GameFolders.InRepo(project);
            Assert.That(folders.PlayerData, Is.Null);
            Assert.That(folders.Claude, Is.Null);
            Assert.That(folders.Mods, Is.EqualTo(folders.ShippedMods), "the MCP server ships a mod by saving it there");
        }

        [Test]
        public void TheReposFoldersAreFoundAndNothingAboveIsAnError()
        {
            Assert.That(File.Exists(Path.Combine(ChemistryTests.RulesDir(), "materials.json")));
            Assert.That(File.Exists(Path.Combine(ChemistryTests.WorldDir(), WorldThings.FileName)));
            Assert.That(File.Exists(Path.Combine(ChemistryTests.Folders.Repo, "docs", "GLOSSARY.md")), "the repo's root");
            Assert.That(GameFolders.InRepo(project).Repo, Is.EqualTo(repo));
            Assert.That(new GameFolders(Path.Combine(repo, "rules") + Path.DirectorySeparatorChar).World, Is.EqualTo(Path.Combine(repo, "world", "maps", "valley")), "a trailing slash");
            Assert.Throws<DirectoryNotFoundException>(() => GameFolders.InRepo(packed));
        }
    }
}
