using System.Collections.Generic;

// A recogniser with nothing behind it, for the probe: it hears what the probe tells it. It's the
// seam's test, since it's an adapter and nothing else (J-061). With Slow, Stop leaves Done to the
// probe, the way a recogniser that transcribes after the hold would take its time.
public sealed class StandInSpeech : ISpeech
{
    readonly Queue<(SpeechHeard heard, string text)> heard = new();

    /// <summary>Set by the probe: the line Start fails with, or null to work.</summary>
    public string FailWith { get; set; }

    /// <summary>Set by the probe: Stop waits for Finish.</summary>
    public bool Slow { get; set; }

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Notice { get; set; } = "The stand-in sends what you say nowhere.";

    public void Start()
    {
        Why = FailWith;
        State = FailWith == null ? SpeechState.Listening : SpeechState.Unavailable;
        if (FailWith != null) heard.Enqueue((SpeechHeard.Done, ""));
    }

    public void Stop()
    {
        if (State != SpeechState.Listening || Slow) return;
        Finish();
    }

    /// <summary>A guess at the words so far, as if spoken.</summary>
    public void Partial(string text) => heard.Enqueue((SpeechHeard.Partial, text));

    /// <summary>A phrase it's sure of, as if spoken.</summary>
    public void Final(string text) => heard.Enqueue((SpeechHeard.Final, text));

    /// <summary>Ends the hold.</summary>
    public void Finish()
    {
        State = SpeechState.Idle;
        heard.Enqueue((SpeechHeard.Done, ""));
    }

    public bool Take(out (SpeechHeard heard, string text) line) => heard.TryDequeue(out line);

    public void Dispose() => heard.Clear();
}
