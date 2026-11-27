using System;
using System.Diagnostics;
using System.Threading;

// The stub transcriber: a stand-in for a Whisper model, with no model and no download, so the
// threading, the timeout and the hand-off to the chat can be tested now. It keeps its worker thread
// busy for Delay (real work, as a model would, not a sleep), watching the token, then says Text, or
// nothing for a silent recording. Only an Editor or a development build can pick it (SpeechChoice).
public sealed class StubTranscriber : ITranscriber
{
    /// <summary>A recording quieter than this is silence, and says nothing.</summary>
    public const float Quiet = 0.01f;

    int thread, calls;

    public StubTranscriber(TimeSpan delay, string text = null)
    {
        Delay = delay;
        Text = text;
    }

    /// <summary>How long each call works before it answers.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>What it says for any sound; null says how long the sound was.</summary>
    public string Text { get; set; }

    /// <summary>The thread the last call ran on.</summary>
    public int Thread => Volatile.Read(ref thread);

    /// <summary>How many calls have started.</summary>
    public int Calls => Volatile.Read(ref calls);

    /// <summary>Whether a call saw its token cancelled and gave up.</summary>
    public bool SawCancel { get; private set; }

    public string Name => "the stub transcriber (no model)";

    /// <summary>The text a stub says for a WAV file: its name, dashes as spaces.</summary>
    public static string TextFor(string wav) => System.IO.Path.GetFileNameWithoutExtension(wav).Replace('-', ' ');

    public string Transcribe(float[] samples, CancellationToken cancel)
    {
        Volatile.Write(ref thread, System.Threading.Thread.CurrentThread.ManagedThreadId);
        Interlocked.Increment(ref calls);
        var clock = Stopwatch.StartNew();
        double work = 0;
        while (clock.Elapsed < Delay)
        {
            if (cancel.IsCancellationRequested)
            {
                SawCancel = true;
                cancel.ThrowIfCancellationRequested();
            }
            for (int i = 1; i < 20000; i++) work += Math.Sqrt(i);
        }
        if (work < 0) return null; // never: keeps the work from being optimised away
        if (samples.Length == 0 || SpeechSamples.Peak(samples) < Quiet) return "";
        return Text ?? $"stub heard {SpeechSamples.Seconds(samples):0.0} seconds";
    }

    public void Dispose()
    {
    }
}
