using System;
using System.Diagnostics;
using System.Threading;

// The stub transcriber: a stand-in for a Whisper model, with no model and no download, so the
// threading, the timeout and the hand-off to the chat can be tested now. It keeps its worker thread
// busy for Delay (real work, as a model would, not a sleep), watching the token, then says Text, or
// nothing for a silent recording. Only an Editor or a development build can pick it (SpeechChoice).
public sealed class StubTranscriber : ITranscriber
{
    public const float Quiet = 0.01f;

    int thread, calls;

    public StubTranscriber(TimeSpan delay, string text = null)
    {
        Delay = delay;
        Text = text;
    }

    public TimeSpan Delay { get; set; }

    public string Text { get; set; }

    public int Thread => Volatile.Read(ref thread);

    public int Calls => Volatile.Read(ref calls);

    public bool SawCancel { get; private set; }

    public string Name => "the stub transcriber (no model)";

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
        if (work < 0) return null;
        if (samples.Length == 0 || SpeechSamples.Peak(samples) < Quiet) return "";
        return Text ?? $"stub heard {SpeechSamples.Seconds(samples):0.0} seconds";
    }

    public void Dispose()
    {
    }
}
