using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// A release's voice trace keeps the timing and drops the words (#53). This Editor keeps them, so
// these take the release path by hand; the recognisers are read as text, as Windows' is compiled
// only on Windows.
public class VoiceTraceTests
{
    const string Said = "make all fire blue";

    // What a trace line may not hold outside Words: anything GameVoice or a recogniser has heard.
    static readonly Regex BareWords = new(@"\{[^{}]*\b(text|message|said|guess|heard\.text|line\.text|Typed)\b[^{}]*\}");
    static readonly Regex InWords = new(@"\{VoiceTrace\.Words\((?:[^(){}]|\([^(){}]*\))*\)\}");

    [Test]
    public void AReleaseKeepsNoWords()
    {
        Assert.IsFalse(VoiceTrace.KeepsWords(editor: false, developmentBuild: false));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AnEditorOrDevelopmentBuildKeepsThem(bool editor, bool developmentBuild)
    {
        Assert.IsTrue(VoiceTrace.KeepsWords(editor, developmentBuild));
    }

    [Test]
    public void AReleaseLogsHowMuchWasSaidNotWhat()
    {
        string shown = VoiceTrace.Words(Said, keep: false);
        Assert.AreEqual($"({Said.Length})", shown);
        foreach (string word in Said.Split(' ')) StringAssert.DoesNotContain(word, shown);
    }

    [Test]
    public void AReleaseLineKeepsItsTiming()
    {
        LogAssert.Expect(LogType.Log, new Regex($@"^VOICE \+\d+ ms final \({Said.Length}\)$"));
        VoiceTrace.Begin();
        VoiceTrace.Line($"final {VoiceTrace.Words(Said, keep: false)}");
    }

    [Test]
    public void AnEditorLogsTheWords()
    {
        Assert.AreEqual($"\"{Said}\" ({Said.Length})", VoiceTrace.Words(Said));
    }

    [TestCase("GameVoice.cs")]
    [TestCase("WindowsDictation.cs")]
    [TestCase("VoiceCheck.cs")]
    [TestCase("TranscribedSpeech.cs")]
    [TestCase("SpeechCheck.cs")]
    public void EveryWordInATraceGoesThroughWords(string file)
    {
        string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Prototype/Scripts/Net", file));
        int traces = 0;
        foreach (string line in lines)
        {
            if (!line.Contains("VoiceTrace.Line(") && !line.Contains("Debug.Log")) continue;
            traces++;
            string outside = InWords.Replace(line, "");
            Assert.IsFalse(BareWords.IsMatch(outside), $"{file} logs words past VoiceTrace.Words: {line.Trim()}");
        }
        Assert.Greater(traces, 3, $"{file} should have trace lines to read");
    }

    [Test]
    public void TheCheckAboveSeesBareWords()
    {
        Assert.IsTrue(BareWords.IsMatch("VoiceTrace.Line($\"partial {text}\");"));
        Assert.IsTrue(BareWords.IsMatch("VoiceTrace.Line($\"done, {message}\");"));
        Assert.IsFalse(BareWords.IsMatch(InWords.Replace("VoiceTrace.Line($\"partial {VoiceTrace.Words(Joined(said, guess))}\");", "")));
    }
}
