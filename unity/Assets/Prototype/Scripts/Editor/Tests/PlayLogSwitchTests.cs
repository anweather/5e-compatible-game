using NUnit.Framework;
using UnityEngine;

// The play log's switch (PlayLog.Wanted, PlayLog.StartsWriting): what's stored under the key old
// games wrote, read as they read it, decides what a player's start does. The Editor never starts
// the log, so these ask as a player would.
public class PlayLogSwitchTests
{
    const string Key = "playLog.on"; // spelt out: a new key would turn old opt-outs back on
    static readonly string[] NoArgs = { };

    bool hadChoice;
    int choice;

    [SetUp]
    public void KeepTheChoice()
    {
        hadChoice = PlayerPrefs.HasKey(Key);
        choice = PlayerPrefs.GetInt(Key, 1);
    }

    [TearDown]
    public void PutTheChoiceBack()
    {
        if (hadChoice) PlayerPrefs.SetInt(Key, choice);
        else PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    static bool PlayerStarts(params string[] args) => PlayLog.StartsWriting(false, PlayLog.Wanted, args);

    [Test]
    public void AGameThatNeverChoseWrites()
    {
        PlayerPrefs.DeleteKey(Key);
        Assert.That(PlayLog.Wanted, Is.True);
        Assert.That(PlayerStarts(NoArgs), Is.True);
    }

    [Test]
    public void AnOldOffStaysOff()
    {
        PlayerPrefs.SetInt(Key, 0);
        Assert.That(PlayLog.Wanted, Is.False);
        Assert.That(PlayerStarts(NoArgs), Is.False);
    }

    [Test]
    public void AnOldOnStaysOn()
    {
        PlayerPrefs.SetInt(Key, 1);
        Assert.That(PlayerStarts(NoArgs), Is.True);
    }

    [Test]
    public void TheSwitchWritesTheOldKey()
    {
        PlayLog.Wanted = false;
        Assert.That(PlayerPrefs.GetInt(Key, 1), Is.EqualTo(0));
        PlayLog.Wanted = true;
        Assert.That(PlayerPrefs.GetInt(Key, 0), Is.EqualTo(1));
    }

    [Test]
    public void TheFlagStopsItWhateverIsStored()
    {
        PlayerPrefs.SetInt(Key, 1);
        Assert.That(PlayerStarts("game", "-no-play-log"), Is.False);
    }

    [Test]
    public void TheEditorNeverStartsIt()
    {
        Assert.That(PlayLog.StartsWriting(true, true, NoArgs), Is.False);
    }
}
