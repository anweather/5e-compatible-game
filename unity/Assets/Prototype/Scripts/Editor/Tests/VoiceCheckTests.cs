using System.Collections.Generic;
using NUnit.Framework;

// The Windows settings voice needs, said plainly when one is off (#58), and what -voice-check's
// line concludes from its three passes.
public class VoiceCheckTests
{
    static WindowsSpeechSettings.Values Settings(string pc = "Allow", string apps = "Allow", string desktop = "Allow", int? online = 1) =>
        new(pc, apps, desktop, online);

    [Test]
    public void AllOnSaysNothing()
    {
        Assert.IsNull(WindowsSpeechSettings.Problem(Settings()));
    }

    [Test]
    public void UnsetCountsAsOn()
    {
        Assert.IsNull(WindowsSpeechSettings.Problem(Settings(null, null, null, null)));
    }

    [TestCase("Deny", "Allow", "Allow", "Microphone access")]
    [TestCase("Allow", "Deny", "Allow", "Let apps access your microphone")]
    [TestCase("Allow", "Allow", "Deny", "Let desktop apps access your microphone")]
    public void AMicrophoneSwitchedOffIsNamed(string pc, string apps, string desktop, string setting)
    {
        StringAssert.Contains(setting, WindowsSpeechSettings.Problem(Settings(pc, apps, desktop)));
    }

    [Test]
    public void OnlineSpeechOffIsNamed()
    {
        Assert.AreEqual(WindowsSpeechSettings.OnlineSpeechOff, WindowsSpeechSettings.Problem(Settings(online: 0)));
    }

    static VoiceCheck.Pass Pass(string name, int finals) => new() { Name = name, Finals = finals, Done = true };

    [Test]
    public void NoneHearingWordsInSoundIsWindowsSide()
    {
        var game = Pass("game", 0);
        game.Loud = true;
        StringAssert.Contains("Windows' side", VoiceCheck.Verdict(new List<VoiceCheck.Pass> { Pass("unity", 0), Pass("unity-60s", 0), game }));
    }

    [Test]
    public void AQuietRoomProvesNothing()
    {
        StringAssert.Contains("proves nothing", VoiceCheck.Verdict(new List<VoiceCheck.Pass> { Pass("unity", 0), Pass("unity-60s", 0), Pass("game", 0) }));
    }

    [Test]
    public void OnlyTheGameMissingIsTheGamesCode()
    {
        StringAssert.Contains("the game's code", VoiceCheck.Verdict(new List<VoiceCheck.Pass> { Pass("unity", 2), Pass("unity-60s", 1), Pass("game", 0) }));
    }

    [Test]
    public void AllHearingSaysSo()
    {
        StringAssert.Contains("every recogniser heard", VoiceCheck.Verdict(new List<VoiceCheck.Pass> { Pass("unity", 1), Pass("unity-60s", 1), Pass("game", 1) }));
    }
}
