using System.IO;
using NUnit.Framework;
using UnityEngine;

// The seam between the game and its recognisers (J-061): the port and what uses it name no
// recogniser, and the factory picks by name, so Whisper can arrive as an adapter and a line.
public class VoiceSeamTests
{
    static readonly string[] GameSide = { "ISpeech.cs", "GameVoice.cs", "GameChat.cs" };

    [Test]
    public void TheGameSideNamesNoRecogniser()
    {
        foreach (string file in GameSide)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Prototype/Scripts/Net", file));
            foreach (string word in new[] { "Windows", "Dictation", "Whisper", "Microphone" })
                Assert.IsFalse(text.Contains(word), $"{file} names {word}");
        }
    }

    [Test]
    public void TheFactoryPicksByName()
    {
        using var standIn = SpeechChoice.Make(SpeechChoice.StandIn);
        Assert.IsInstanceOf<StandInSpeech>(standIn);
    }

    [Test]
    public void AnUnknownAdapterSaysWhyAndEndsTheHold()
    {
        using var none = SpeechChoice.Make("no-such-adapter");
        Assert.AreEqual(SpeechState.Unavailable, none.State);
        Assert.IsNotEmpty(none.Why);
        Assert.IsFalse(none.Take(out _));
        none.Start();
        Assert.IsTrue(none.Take(out var line));
        Assert.AreEqual(SpeechHeard.Done, line.heard);
        Assert.IsFalse(none.Take(out _));
    }

    [Test]
    public void AHoldAlwaysEndsWithDone()
    {
        using var speech = new StandInSpeech { FailWith = "no" };
        speech.Start();
        Assert.AreEqual(SpeechState.Unavailable, speech.State);
        Assert.AreEqual("no", speech.Why);
        Assert.IsTrue(speech.Take(out var line));
        Assert.AreEqual(SpeechHeard.Done, line.heard);
    }
}
