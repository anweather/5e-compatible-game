using System.IO;
using Chem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The solo map pick (#216): the next map goes round and wraps, a saved map this copy no longer has is forgotten, and the
// choice is a player setting. And #169: a map named by a path from the repo's root resolves there, not under world/maps; a
// name that isn't a map is one clear error, then the valley.
public class SoloMapTests
{
    const string Key = "solo-map"; // spelt out: a new key would lose players' picks
    bool had;
    string kept;

    [SetUp]
    public void KeepThePick()
    {
        had = PlayerPrefs.HasKey(Key);
        kept = PlayerPrefs.GetString(Key, "");
    }

    [TearDown]
    public void PutThePickBack()
    {
        if (had) PlayerPrefs.SetString(Key, kept);
        else PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    [Test]
    public void TheNextMapGoesRoundAndWraps()
    {
        var all = new[] { ("a", "A"), ("b", "B"), ("c", "C") };
        Assert.That(SoloMap.Next(all, "a"), Is.EqualTo("b"));
        Assert.That(SoloMap.Next(all, "c"), Is.EqualTo("a"));
        Assert.That(SoloMap.Next(all, "gone"), Is.EqualTo("a"));
    }

    [Test]
    public void ASavedMapThisCopyLacksIsForgotten()
    {
        PlayerPrefs.SetString(Key, "no-such-map");
        Assert.That(SoloMap.Saved(), Is.Null);
    }

    [Test]
    public void ASavedMapThisCopyHasComesBack()
    {
        var first = ChemGrid.Maps()[0].id;
        SoloMap.Save(first);
        Assert.That(SoloMap.Saved(), Is.EqualTo(first));
    }

    [Test]
    public void NothingSavedIsNoPick()
    {
        PlayerPrefs.DeleteKey(Key);
        Assert.That(SoloMap.Saved(), Is.Null);
    }

    [Test]
    public void AMapNamedFromTheRepoRootIsFoundThere()
    {
        string folder = ChemGrid.PlayFolder("test-fixtures/maps/flat-1728");
        Assert.That(Path.GetFileName(folder), Is.EqualTo("flat-1728"));
        Assert.That(File.Exists(Path.Combine(folder, "map.json")), Is.True);
    }

    [Test]
    public void ANameThatIsNotAMapIsOneErrorThenTheValley()
    {
        // The error is said once per name (so the Editor's every look doesn't repeat it): a name of this run's own.
        string name = "test-fixtures/maps/nowhere-" + System.Guid.NewGuid().ToString("N");
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("MAP: " + name + " isn't a map"));
        string folder = ChemGrid.PlayFolder(name);
        Assert.That(folder, Is.EqualTo(ChemGrid.PlayFolder("")));
    }

    // Andrew 2026-11-25: a player with no pick loads into the hills, the sparse island (the nine before).
    [Test]
    public void NothingSavedStartsOnTheHills()
    {
        PlayerPrefs.DeleteKey(Key);
        Assert.That(SoloMap.Start(new[] { "game" }), Is.EqualTo("hills-12x10"));
    }

    [Test]
    public void TheHillsAreAMapThisCopyHas()
    {
        Assert.That(System.Array.FindIndex(ChemGrid.Maps(), m => m.id == "hills-12x10"), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ACopyWithoutTheHillsPlaysTheNineThenTheValley()
    {
        Assert.That(SoloMap.First(new[] { ("valley", "V"), ("nine", "N"), ("hills-12x10", "H") }), Is.EqualTo("hills-12x10"));
        Assert.That(SoloMap.First(new[] { ("valley", "V"), ("nine", "N") }), Is.EqualTo("nine"));
        Assert.That(SoloMap.First(new[] { ("valley", "V") }), Is.EqualTo(GameFolders.DefaultMap));
    }

    [Test]
    public void ASavedPickStillWins()
    {
        SoloMap.Save(GameFolders.DefaultMap);
        Assert.That(SoloMap.Start(new[] { "game" }), Is.EqualTo(GameFolders.DefaultMap));
    }

    [Test]
    public void AScriptedRunKeepsTheValley()
    {
        PlayerPrefs.DeleteKey(Key);
        Assert.That(SoloMap.Start(new[] { "game", "-fps-probe" }), Is.EqualTo(GameFolders.DefaultMap));
        Assert.That(SoloMap.Start(new[] { "game", "-playtest" }), Is.EqualTo(GameFolders.DefaultMap));
    }
}
