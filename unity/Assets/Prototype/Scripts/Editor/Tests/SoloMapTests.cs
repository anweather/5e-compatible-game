using System.IO;
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
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("MAP: test-fixtures/maps/nowhere-at-all isn't a map"));
        string folder = ChemGrid.PlayFolder("test-fixtures/maps/nowhere-at-all");
        Assert.That(folder, Is.EqualTo(ChemGrid.PlayFolder("")));
    }
}
