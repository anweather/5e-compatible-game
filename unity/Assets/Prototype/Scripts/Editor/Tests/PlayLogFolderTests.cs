using System;
using System.IO;
using Chem;
using NUnit.Framework;
using UnityEngine;

// Where the play log goes (PlayLog.Folder): beside the player log, else the player's data folder, and
// Unity's own data folder when no rules can be found, since the play log is what tells of a broken start.
public class PlayLogFolderTests
{
    string root;

    [SetUp]
    public void MakeFolders() => root = Path.Combine(Path.GetTempPath(), "play-log-folder-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void RemoveFolders()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public void BesideTheLogWhenThereIsOne() =>
        Assert.That(PlayLog.Folder(Path.Combine(root, "Player.log"), () => throw new InvalidOperationException("not asked")), Is.EqualTo(root));

    [Test]
    public void ThePlayersDataFolderWithNoLog() =>
        Assert.That(PlayLog.Folder(null, () => new GameFolders(Path.Combine(root, "rules"), Path.Combine(root, "data"))), Is.EqualTo(Path.Combine(root, "data")));

    [Test]
    public void UnitysDataFolderWhenNoRulesCanBeFound()
    {
        string streaming = Directory.CreateDirectory(Path.Combine(root, "build", "StreamingAssets")).FullName;
        string project = Directory.CreateDirectory(Path.Combine(root, "project", "Assets")).FullName;
        GameFolders NoRules() => GameFolders.ForGame(streaming, project, false, Path.Combine(root, "data"));
        Assert.That(() => NoRules().Rules, Throws.TypeOf<DirectoryNotFoundException>(), "no rules/ above the temp folders");
        Assert.That(PlayLog.Folder(null, NoRules), Is.EqualTo(Application.persistentDataPath));
    }
}
