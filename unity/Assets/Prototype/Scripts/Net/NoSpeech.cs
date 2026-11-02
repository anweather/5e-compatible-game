// What a game with no recogniser for its platform gets: it says so, and a hold ends at once.
public sealed class NoSpeech : ISpeech
{
    bool done = true;

    public NoSpeech(string why) => Why = why;

    public SpeechState State => SpeechState.Unavailable;
    public string Why { get; }
    public string Source => null;
    public string ChangeSource => null;
    public float Level => 0f;
    public string Notice => null;

    public void Start() => done = false;

    public void Stop()
    {
    }

    public bool Take(out (SpeechHeard heard, string text) line)
    {
        line = (SpeechHeard.Done, "");
        if (done) return false;
        done = true;
        return true;
    }

    public void Dispose()
    {
    }
}
