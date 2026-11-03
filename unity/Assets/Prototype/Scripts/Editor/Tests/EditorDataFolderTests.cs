using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// The Editor's data folder (#78): Unity gives the Editor the shipped game's persistentDataPath, where a
// player's saved mods, play logs and chat records are, so the game's own folders send it to the project's
// Library instead, and a test run's TestResults.xml goes there too (TestResultsHome). A build's folder is
// GameFoldersTests' (ABuildKeepsTheDataFolderItIsGiven).
public class EditorDataFolderTests
{
    static bool Under(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('/', '\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [Test]
    public void TheEditorsIsNotTheShippedGames()
    {
        var folders = ChemGrid.Folders;
        Assert.That(Path.GetFullPath(folders.PlayerData), Is.Not.EqualTo(Path.GetFullPath(Application.persistentDataPath)));
        Assert.That(Under(folders.PlayerData, Application.persistentDataPath), Is.False, folders.PlayerData);
        Assert.That(Under(folders.Claude.Path, Application.persistentDataPath), Is.False, "the chat's folder and saved mods");
    }

    [Test]
    public void ItIsInTheProjectsLibrary() =>
        Assert.That(Under(ChemGrid.Folders.PlayerData, Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library")), ChemGrid.Folders.PlayerData);

    [Test]
    public void ATestRunsResultsGoToTheLibrary()
    {
        Assert.That(Under(TestResultsHome.File, Application.persistentDataPath), Is.False, TestResultsHome.File);
        Assert.That(Under(TestResultsHome.File, Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library")), TestResultsHome.File);
    }

    [Test]
    public void ThisRunHasNoSaverWritingToTheShippedFolder()
    {
        var names = TestResultsHome.Registered().Select(c => c.GetType().FullName).ToList();
        Assert.That(names, Has.Some.Contains(nameof(TestResultsHome)), "the runner's callbacks were found: " + string.Join(", ", names));
        Assert.That(names, Has.None.EqualTo("Unity.PerformanceTesting.Editor.PerformanceTestRunSaver"), "taken off when the run started");
    }
}
