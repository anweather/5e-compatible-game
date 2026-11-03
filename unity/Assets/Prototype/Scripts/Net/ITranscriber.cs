using System;
using System.Threading;

// A transcriber: the engine that turns a whole recording into text after the hold, Whisper's kind.
// TranscribedSpeech calls it on a worker thread, never the main one, so it may take seconds and must
// touch no Unity API. It watches the token and gives up soon after it's cancelled (Whisper has an
// abort callback for this); a call past its timeout is cancelled and left to end by itself, so the
// next call can start while a cancelled one still runs: an engine that can't run two at once waits
// inside itself. Anything it loads, such as a model, loads on that worker thread too.
public interface ITranscriber : IDisposable
{
    /// <summary>What it is, for the trace and the speech check.</summary>
    string Name { get; }

    /// <summary>The words in these 16 kHz mono samples, "" for none; on a worker thread.</summary>
    string Transcribe(float[] samples, CancellationToken cancel);
}
