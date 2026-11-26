using System.Collections.Generic;

// A recogniser with nothing behind it, for the probe: it hears what the probe tells it. It's the
// seam's test, since it's an adapter and nothing else (J-061). With Slow, Stop leaves Done to the
// probe, the way a recogniser that transcribes after the hold would take its time.
public sealed class StandInSpeech : ISpeech
{
    readonly Queue<(SpeechHeard heard, string text)> heard = new();

    public string FailWith { get; set; }

    public bool Slow { get; set; }

    public bool SlowStart { get; set; }

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Source => "the stand-in";
    public string ChangeSource => "the stand-in's settings";
    public float Level { get; set; }
    public string Notice { get; set; } = "The stand-in sends what you say nowhere.";

    public void Start()
    {
        Why = FailWith;
        State = FailWith != null ? SpeechState.Unavailable : SlowStart ? SpeechState.Starting : SpeechState.Listening;
        if (FailWith != null) heard.Enqueue((SpeechHeard.Done, ""));
    }

    public void Ready() => State = SpeechState.Listening;

    public void Stop()
    {
        if (State != SpeechState.Listening || Slow) return;
        Finish();
    }

    public void Partial(string text) => heard.Enqueue((SpeechHeard.Partial, text));

    public void Final(string text) => heard.Enqueue((SpeechHeard.Final, text));

    public void Finish()
    {
        State = SpeechState.Idle;
        heard.Enqueue((SpeechHeard.Done, ""));
    }

    public bool Take(out (SpeechHeard heard, string text) line) => heard.TryDequeue(out line);

    public void Dispose() => heard.Clear();
}
