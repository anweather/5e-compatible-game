using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

// The adapter for a transcriber (Whisper's kind): it records speech audio through the hold, and on
// Stop hands the samples to the transcriber on a worker thread. Take, every frame on the main
// thread, looks whether the work has ended; the main thread never waits on it. The words come out as
// one Final, then Done, the way any recogniser's do, so GameVoice treats them as it treats Windows'.
// The timeout is ours: past it, the work's token is cancelled, the work is left to end by itself and
// the hold ends with a line saying so, so the next press works at once. Dispose cancels the same way.
// Nothing leaves the machine, so there's no notice.
public sealed class TranscribedSpeech : ISpeech
{
    /// <summary>How long transcribing may take before the hold gives up on it.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    readonly ISpeechAudio audio;
    readonly ITranscriber transcriber;
    readonly Queue<(SpeechHeard heard, string text)> heard = new();
    readonly Stopwatch recording = new(), transcribing = new();
    CancellationTokenSource cancel;
    Task<string> work;
    Task leftBehind = Task.CompletedTask;
    int abandoned;

    public TranscribedSpeech(ISpeechAudio audio, ITranscriber transcriber, TimeSpan timeout)
    {
        this.audio = audio;
        this.transcriber = transcriber;
        Timeout = timeout;
    }

    /// <summary>How long transcribing may take.</summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>The recording's length the last hold, in ms of the clock.</summary>
    public double RecordMs { get; private set; }

    /// <summary>How long the last transcribing took, or ran before it was given up on.</summary>
    public double TranscribeMs { get; private set; }

    /// <summary>Seconds of sound in the last recording.</summary>
    public float Heard { get; private set; }

    /// <summary>Whether the transcriber is still at the last recording.</summary>
    public bool Transcribing => work != null;

    /// <summary>Cancelled work that hasn't ended yet.</summary>
    public int Abandoned => Volatile.Read(ref abandoned);

    /// <summary>What turns the sound into text.</summary>
    public ITranscriber Transcriber => transcriber;

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Source => audio.Name;
    public string ChangeSource => audio.ChangeSource;
    public float Level => audio.Level;
    public string Notice => null;

    public void Start()
    {
        Abandon("a new hold started");
        Why = null;
        RecordMs = TranscribeMs = 0;
        Heard = 0f;
        string why = audio.Open();
        if (why != null)
        {
            VoiceTrace.Line($"{audio.Name} didn't open");
            Fail(why);
            return;
        }
        recording.Restart();
        State = SpeechState.Listening;
        VoiceTrace.Line($"recording from {audio.Name}");
    }

    public void Stop()
    {
        if (State != SpeechState.Listening || work != null) return;
        float[] samples = audio.Close();
        RecordMs = recording.Elapsed.TotalMilliseconds;
        Heard = SpeechSamples.Seconds(samples);
        VoiceTrace.Line($"recorded {Heard:0.0} s in {RecordMs:0} ms, peak {SpeechSamples.Peak(samples):0.000}; {transcriber.Name} transcribing");
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        var engine = transcriber;
        transcribing.Restart();
        work = Task.Run(() => engine.Transcribe(samples, token), token);
    }

    public bool Take(out (SpeechHeard heard, string text) line)
    {
        if (work != null) Collect();
        else if (State == SpeechState.Listening) audio.Pump();
        return heard.TryDequeue(out line);
    }

    // The work's end, read on the main thread; or the timeout, which ends the hold without it.
    void Collect()
    {
        TranscribeMs = transcribing.Elapsed.TotalMilliseconds;
        if (!work.IsCompleted)
        {
            if (transcribing.Elapsed < Timeout) return;
            Abandon($"no text in {Timeout.TotalSeconds:0.#} s");
            Fail($"Making out the words took longer than {Timeout.TotalSeconds:0.#} s, so nothing came through. Try again, or type.");
            return;
        }
        var done = work;
        work = null;
        cancel.Dispose();
        cancel = null;
        if (done.Status == TaskStatus.RanToCompletion)
        {
            string text = (done.Result ?? "").Trim();
            VoiceTrace.Line($"transcribed in {TranscribeMs:0} ms: {VoiceTrace.Words(text)}");
            if (text.Length > 0) heard.Enqueue((SpeechHeard.Final, text));
            End();
            return;
        }
        string error = done.Exception?.GetBaseException().Message ?? "cancelled";
        VoiceTrace.Line($"transcribing failed after {TranscribeMs:0} ms: {error}");
        Debug.LogWarning("Voice: transcribing failed: " + error);
        Fail("Making out the words failed. Try again, or type.");
    }

    // Cancels work still going and leaves it to end on its own thread, which nothing waits for.
    void Abandon(string why)
    {
        if (work == null) return;
        var left = work;
        var source = cancel;
        work = null;
        cancel = null;
        source.Cancel();
        Interlocked.Increment(ref abandoned);
        VoiceTrace.Line($"gave up transcribing: {why}; {Abandoned} still out");
        leftBehind = Task.WhenAll(leftBehind, left.ContinueWith(t =>
        {
            _ = t.Exception; // observed, so an abandoned failure is never rethrown
            source.Dispose();
            Interlocked.Decrement(ref abandoned);
        }, TaskScheduler.Default));
    }

    void Fail(string why)
    {
        Why = why;
        End();
        State = SpeechState.Unavailable;
    }

    void End()
    {
        State = SpeechState.Idle;
        heard.Enqueue((SpeechHeard.Done, ""));
    }

    public void Dispose()
    {
        Abandon("put away");
        if (audio.Recording) audio.Close();
        audio.Dispose();
        heard.Clear();
        // A model must outlive the work still using it, so it goes once all of that has ended.
        var engine = transcriber;
        leftBehind.ContinueWith(_ => engine.Dispose(), TaskScheduler.Default);
    }
}
