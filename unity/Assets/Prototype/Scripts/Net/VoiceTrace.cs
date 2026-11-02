using System.Diagnostics;
using Debug = UnityEngine.Debug;

// One line in the player log for each step of a hold to talk, timed from the press, so a report of
// "no text" can say where the words went. Never per frame. What was said is in these lines, as
// it's the point of them, so the log holds the player's words.
public static class VoiceTrace
{
    static readonly Stopwatch Clock = new();

    /// <summary>A hold has started: the clock starts over.</summary>
    public static void Begin() => Clock.Restart();

    /// <summary>A step of the hold, from any thread.</summary>
    public static void Line(string step) => Debug.Log($"VOICE +{Clock.ElapsedMilliseconds} ms {step}");

    /// <summary>Words as a trace shows them, with their length.</summary>
    public static string Words(string text) => $"\"{text}\" ({text.Length})";
}
