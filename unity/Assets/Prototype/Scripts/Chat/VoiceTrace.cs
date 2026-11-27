using System.Diagnostics;
using Debug = UnityEngine.Debug;

// One line in the player log for each step of a hold to talk, timed from the press, so a report of
// "no text" can say where the words went. Never per frame. An Editor or a Development build shows
// what was said, as it's the point of them; a release shows only how much, as a friend's log gets
// sent to us (#53). Every word in a trace goes through Words, so that's the one place that decides.
public static class VoiceTrace
{
    static readonly Stopwatch Clock = new();
    static readonly bool KeepWords = KeepsWords(UnityEngine.Application.isEditor, Debug.isDebugBuild);

    public static void Begin() => Clock.Restart();

    public static void Line(string step) => Debug.Log($"VOICE +{Clock.ElapsedMilliseconds} ms {step}");

    public static string Words(string text) => Words(text, KeepWords);

    public static string Words(string text, bool keep) => keep ? $"\"{text}\" ({text.Length})" : $"({text.Length})";

    public static bool KeepsWords(bool editor, bool developmentBuild) => editor || developmentBuild;
}
