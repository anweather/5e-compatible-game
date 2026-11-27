using System.Diagnostics;
using Debug = UnityEngine.Debug;

// One line in the player log for each step of a hold to talk, timed from the press, so a report of
// "no text" can say where the words went. Never per frame. An Editor or a Development build shows
// what was said, as it's the point of them; a release shows only how much, as a friend's log gets
// sent to us (#53). Every word in a trace goes through Words, so that's the one place that decides.
public static class VoiceTrace
{
    static readonly Stopwatch Clock = new();
    // Read once, as Line comes from any thread.
    static readonly bool KeepWords = KeepsWords(UnityEngine.Application.isEditor, Debug.isDebugBuild);

    /// <summary>A hold has started: the clock starts over.</summary>
    public static void Begin() => Clock.Restart();

    /// <summary>A step of the hold, from any thread.</summary>
    public static void Line(string step) => Debug.Log($"VOICE +{Clock.ElapsedMilliseconds} ms {step}");

    /// <summary>Words as this copy's trace shows them.</summary>
    public static string Words(string text) => Words(text, KeepWords);

    /// <summary>Words as a trace shows them: their length, with them only if they may be kept.</summary>
    public static string Words(string text, bool keep) => keep ? $"\"{text}\" ({text.Length})" : $"({text.Length})";

    /// <summary>Whether a copy may log what was said: never a release.</summary>
    public static bool KeepsWords(bool editor, bool developmentBuild) => editor || developmentBuild;
}
