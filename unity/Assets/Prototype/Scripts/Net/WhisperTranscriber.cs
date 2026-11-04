using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Whisper;
using Whisper.Utils;

// Whisper, on this machine (J-061): whisper.cpp through the whisper.unity package, behind ITranscriber.
// Everything here runs on TranscribedSpeech's worker thread, the model's load too, so a native library
// that won't load ends up as that hold's failure line, never a crash at start or a stall in a frame.
// One model can't run two at once, so calls wait for each other on the gate. A cancel can't stop
// whisper once it's going: the package hides its abort callback, and set by hand it only took effect
// after the encoder, nearly all of the work (a call cancelled at 30 ms still ran 359 of 384 ms). So a
// cancelled call runs to its end and its words are dropped; the next one waits for it on this worker
// thread, never the main one.
// Nothing is logged of what was said: the package's own logging, which prints it, is turned down.
public sealed class WhisperTranscriber : ITranscriber
{
    /// <summary>Whisper drops less than a second of sound, so shorter holds get silence added.</summary>
    const float LeastSeconds = 1.2f;
    const int MostThreads = 4;

    // Whisper's way of writing what it heard that isn't words: [BLANK_AUDIO], (wind blowing), *sighs*.
    static readonly Regex NotWords = new(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", RegexOptions.Compiled);

    readonly object gate = new();
    readonly string model;
    readonly bool gpu;
    WhisperWrapper whisper;
    WhisperParams settings;

    public WhisperTranscriber(string model, bool gpu = false)
    {
        this.model = model;
        this.gpu = gpu;
        LogUtils.Level = LogLevel.Error;
    }

    public string Name => $"Whisper ({Path.GetFileNameWithoutExtension(model)}, {(gpu ? "GPU" : "CPU")})";

    /// <summary>How long loading the model took, in ms; 0 until it has.</summary>
    public double LoadMs { get; private set; }

    public string Transcribe(float[] samples, CancellationToken cancel)
    {
        if (samples.Length == 0 || SpeechSamples.Peak(samples) < StubTranscriber.Quiet) return "";
        lock (gate)
        {
            cancel.ThrowIfCancellationRequested();
            if (whisper == null) Load();
            var result = whisper.GetText(Padded(samples), SpeechSamples.Rate, 1, settings);
            cancel.ThrowIfCancellationRequested();
            if (result == null) throw new InvalidOperationException("whisper gave no result");
            return Words(result.Result);
        }
    }

    /// <summary>What whisper wrote, without what it marks as not being words.</summary>
    public static string Words(string text) =>
        Regex.Replace(NotWords.Replace(text ?? "", " "), @"\s+", " ").Trim();

    /// <summary>The threads to use on a machine of this many cores: half, for the game keeps running.</summary>
    public static int Threads(int cores) => Math.Clamp(cores / 2, 1, MostThreads);

    void Load()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var context = WhisperContextParams.GetDefaultParams();
        context.UseGpu = gpu;
        whisper = WhisperWrapper.InitFromFile(model, context) ?? throw new InvalidOperationException("whisper couldn't load its model");
        settings = WhisperParams.GetDefaultParams();
        settings.Language = "en";
        settings.NoContext = true;
        settings.ThreadsCount = Threads(Environment.ProcessorCount);
        LoadMs = clock.Elapsed.TotalMilliseconds;
    }

    static float[] Padded(float[] samples)
    {
        int least = (int)(LeastSeconds * SpeechSamples.Rate);
        if (samples.Length >= least) return samples;
        var padded = new float[least];
        Array.Copy(samples, padded, samples.Length);
        return padded;
    }

    public void Dispose()
    {
        // WhisperWrapper frees the model when it's collected.
        lock (gate) whisper = null;
    }
}
