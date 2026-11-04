using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

// A transcriber's adapter (TranscribedSpeech) with the stub transcriber and WAV files: the work runs
// on another thread, the text comes out through Take on this one, a transcriber slower than the
// timeout ends the hold cleanly and the next one works, and putting it away cancels the work. Also
// the switch: Windows dictation stays the default, and only an Editor or development build gets the stub.
public class TranscribedSpeechTests
{
    const string Said = "make the deer three times bigger";
    static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(50), Slow = TimeSpan.FromSeconds(3);
    static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300), LongTimeout = TimeSpan.FromSeconds(5);

    string folder;

    [SetUp]
    public void MakeFolder()
    {
        folder = Path.Combine(Application.temporaryCachePath, "transcribed-speech-tests");
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void RemoveFolder() => Directory.Delete(folder, true);

    string Wav(string name, float[] samples)
    {
        string path = Path.Combine(folder, name + ".wav");
        File.WriteAllBytes(path, SpeechSamples.ToWav(samples, SpeechSamples.Rate));
        return path;
    }

    static float[] Tone(float seconds, float loudness = 0.3f)
    {
        var samples = new float[(int)(seconds * SpeechSamples.Rate)];
        for (int i = 0; i < samples.Length; i++) samples[i] = loudness * Mathf.Sin(2f * Mathf.PI * 220f * i / SpeechSamples.Rate);
        return samples;
    }

    // Take, as GameVoice calls it each frame, until Done; what was heard, and how long it took.
    static (string text, bool done, double ms) Hold(TranscribedSpeech speech, double mostMs = 5000)
    {
        string text = "";
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < mostMs)
        {
            while (speech.Take(out var line))
            {
                if (line.heard == SpeechHeard.Final) text += line.text;
                if (line.heard == SpeechHeard.Done) return (text, true, clock.Elapsed.TotalMilliseconds);
            }
            Thread.Sleep(5);
        }
        return (text, false, clock.Elapsed.TotalMilliseconds);
    }

    static bool Until(Func<bool> done, int mostMs = 3000)
    {
        for (var clock = Stopwatch.StartNew(); clock.ElapsedMilliseconds < mostMs; Thread.Sleep(5))
            if (done()) return true;
        return done();
    }

    [Test]
    public void AFastTranscriberDeliversTextOnTheMainThread()
    {
        var stub = new StubTranscriber(TimeSpan.FromMilliseconds(300), Said);
        using var speech = new TranscribedSpeech(new WavAudio(Wav("ask", Tone(1f))), stub, LongTimeout);
        speech.Start();
        Assert.AreEqual(SpeechState.Listening, speech.State);
        var stop = Stopwatch.StartNew();
        speech.Stop();
        Assert.Less(stop.ElapsedMilliseconds, 100, "Stop waited for the transcriber");
        Assert.IsTrue(speech.Transcribing);
        var (text, done, _) = Hold(speech);
        Assert.IsTrue(done);
        Assert.AreEqual(Said, text);
        Assert.AreEqual(SpeechState.Idle, speech.State);
        Assert.IsNull(speech.Why);
        Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, stub.Thread, "the transcriber ran on the main thread");
        Assert.AreEqual(1f, speech.Heard, 0.01f);
    }

    [Test]
    public void ASlowTranscriberTimesOutCleanlyAndTheNextOneWorks()
    {
        var stub = new StubTranscriber(Slow, Said);
        using var speech = new TranscribedSpeech(new WavAudio(Wav("ask", Tone(1f))), stub, ShortTimeout);
        speech.Start();
        speech.Stop();
        var (text, done, ms) = Hold(speech);
        Assert.IsTrue(done, "the hold never ended");
        Assert.Less(ms, 1500, "the timeout didn't end the hold in time");
        Assert.AreEqual("", text);
        Assert.AreEqual(SpeechState.Unavailable, speech.State);
        StringAssert.Contains("longer than", speech.Why);
        Assert.IsTrue(Until(() => stub.SawCancel), "the work wasn't cancelled");
        Assert.IsTrue(Until(() => speech.Abandoned == 0), "the cancelled work never ended");

        stub.Delay = Fast;
        speech.Start();
        speech.Stop();
        (text, done, _) = Hold(speech);
        Assert.IsTrue(done);
        Assert.AreEqual(Said, text);
        Assert.AreEqual(SpeechState.Idle, speech.State);
        Assert.IsNull(speech.Why);
    }

    [Test]
    public void ANewHoldGivesUpOnWorkStillGoing()
    {
        var stub = new StubTranscriber(Slow, Said);
        using var speech = new TranscribedSpeech(new WavAudio(Wav("ask", Tone(1f))), stub, LongTimeout);
        speech.Start();
        speech.Stop();
        Assert.IsTrue(Until(() => stub.Calls == 1), "the work never started");
        speech.Start();
        Assert.IsTrue(Until(() => stub.SawCancel), "the old work wasn't given up on");
        stub.Delay = Fast;
        speech.Stop();
        var (text, done, _) = Hold(speech);
        Assert.IsTrue(done);
        Assert.AreEqual(Said, text);
    }

    [Test]
    public void PuttingItAwayCancelsTheWork()
    {
        var stub = new StubTranscriber(Slow, Said);
        var speech = new TranscribedSpeech(new WavAudio(Wav("ask", Tone(1f))), stub, LongTimeout);
        speech.Start();
        speech.Stop();
        Assert.IsTrue(Until(() => stub.Calls == 1), "the work never started");
        var clock = Stopwatch.StartNew();
        speech.Dispose();
        Assert.Less(clock.ElapsedMilliseconds, 100, "Dispose waited for the work");
        Assert.IsTrue(Until(() => stub.SawCancel, 1000), "the work wasn't cancelled");
        Assert.IsTrue(Until(() => speech.Abandoned == 0, 1000));
    }

    [TestCase(1f)]
    [TestCase(0f)]
    public void ASilentOrEmptyRecordingSaysNothingWithinTheTimeout(float seconds)
    {
        var stub = new StubTranscriber(Fast, Said);
        using var speech = new TranscribedSpeech(new WavAudio(Wav("silence", new float[(int)(seconds * SpeechSamples.Rate)])), stub, ShortTimeout);
        speech.Start();
        speech.Stop();
        var (text, done, ms) = Hold(speech);
        Assert.IsTrue(done);
        Assert.Less(ms, ShortTimeout.TotalMilliseconds);
        Assert.AreEqual("", text);
        Assert.AreEqual(SpeechState.Idle, speech.State);
    }

    [Test]
    public void AFileItCantReadSaysWhyAndEndsTheHold()
    {
        string path = Path.Combine(folder, "not-sound.wav");
        File.WriteAllText(path, "not a wav");
        using var speech = new TranscribedSpeech(new WavAudio(path), new StubTranscriber(Fast), LongTimeout);
        speech.Start();
        Assert.AreEqual(SpeechState.Unavailable, speech.State);
        StringAssert.Contains("not-sound.wav", speech.Why);
        Assert.IsTrue(speech.Take(out var line));
        Assert.AreEqual(SpeechHeard.Done, line.heard);
    }

    [Test]
    public void TheStubSaysTheFilesName()
    {
        Assert.AreEqual("make the deer bigger", StubTranscriber.TextFor("/x/make-the-deer-bigger.wav"));
    }

    [Test]
    public void WhisperIsTheDefault()
    {
        Assert.AreEqual(SpeechChoice.Whisper, SpeechChoice.Chosen(new string[0], ""));
        Assert.AreEqual(SpeechChoice.Whisper, SpeechChoice.Chosen(new[] { "Game.exe", "-speech" }, null));
        Assert.AreEqual(SpeechChoice.StandIn, SpeechChoice.Chosen(new string[0], SpeechChoice.StandIn));
        Assert.AreEqual(SpeechChoice.Stub, SpeechChoice.Chosen(new[] { "Game.exe", "-speech", SpeechChoice.Stub }, SpeechChoice.Windows));
    }

    [TestCase(" Make the deer bigger.", "Make the deer bigger.")]
    [TestCase("[BLANK_AUDIO]", "")]
    [TestCase(" (wind blowing)  freeze the river *sighs* ", "freeze the river")]
    [TestCase(null, "")]
    public void WhisperKeepsOnlyTheWords(string written, string words) =>
        Assert.AreEqual(words, WhisperTranscriber.Words(written));

    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(6, 3)]
    [TestCase(32, 4)]
    public void WhisperLeavesCoresForTheGame(int cores, int threads) =>
        Assert.AreEqual(threads, WhisperTranscriber.Threads(cores));

    [Test]
    public void SilenceNeverReachesWhisper()
    {
        using var whisper = new WhisperTranscriber("no-such-model.bin");
        Assert.AreEqual("", whisper.Transcribe(new float[SpeechSamples.Rate], System.Threading.CancellationToken.None));
    }

    [Test]
    public void OnlyAnEditorOrDevelopmentBuildGetsTheStub()
    {
        Assert.IsFalse(SpeechChoice.Allowed(SpeechChoice.Stub, editor: false, developmentBuild: false));
        Assert.IsTrue(SpeechChoice.Allowed(SpeechChoice.Stub, editor: true, developmentBuild: false));
        Assert.IsTrue(SpeechChoice.Allowed(SpeechChoice.Stub, editor: false, developmentBuild: true));
        Assert.IsTrue(SpeechChoice.Allowed(SpeechChoice.Windows, editor: false, developmentBuild: false));
    }

    [Test]
    public void TheFactoryMakesTheStubHere()
    {
        using var speech = SpeechChoice.Make(SpeechChoice.Stub);
        Assert.IsInstanceOf<TranscribedSpeech>(speech);
        Assert.IsInstanceOf<StubTranscriber>(((TranscribedSpeech)speech).Transcriber);
    }
}
