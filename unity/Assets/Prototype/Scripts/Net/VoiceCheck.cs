using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using UnityEngine.Windows.Speech;
#endif

// A check of Windows dictation with a real voice (#58): start the game with -voice-check [seconds]
// and talk through three holds, each told on screen and in the log. The first is Unity's own
// recogniser as an empty project has it, the second the same with the game's 60 s timeouts, the
// third the game's WindowsDictation. Then a "VOICE CHECK" line in the player log says what each
// heard, how it ended and how long its stop took, and the game quits. If none hears, it's Windows'
// side; if only the game's misses, it's the game's code. Words go through VoiceTrace.Words, so a
// release logs only how much was said. The dictation probe runs it short, with nobody talking.
public class VoiceCheck : MonoBehaviour
{
    const float DefaultSeconds = 12f, Rest = 2f, MostEnd = 20f;

    /// <summary>What one hold heard and how it ended.</summary>
    public sealed class Pass
    {
        public string Name, Ended = "no end";
        public int Partials, Finals, Said;
        public float StopMs = -1f, Loudest;
        // Loud: the game's meter heard more than a quiet room, so someone talked.
        public bool Done, Loud;

        public override string ToString() =>
            $"{Name}: {Partials} partials, {Finals} finals ({Said} chars), {Ended}, stop {StopMs:0} ms" + (Name == "game" ? $", loudest {Loudest:0.000}" : "");
    }

    /// <summary>The pass going and its seconds left, for the screen.</summary>
    public static string Showing { get; private set; }

    float seconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-voice-check");
        if (at < 0) return;
        float seconds = at + 1 < args.Length && float.TryParse(args[at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : DefaultSeconds;
        var check = new GameObject("Voice check").AddComponent<VoiceCheck>();
        check.seconds = Mathf.Max(2f, seconds);
    }

    IEnumerator Start()
    {
        var passes = new List<Pass>();
        yield return Run(seconds, passes);
        UnityEngine.Debug.Log("VOICE CHECK " + string.Join(" | ", passes) + " | " + Verdict(passes));
        Application.Quit();
    }

    void OnGUI()
    {
        if (Showing == null) return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUI.Box(new Rect(40, 40, Screen.width - 80, 140), Showing, style);
    }

    /// <summary>What the passes say, in a line.</summary>
    public static string Verdict(IReadOnlyList<Pass> passes)
    {
        bool Heard(Pass p) => p.Finals + p.Partials > 0;
        if (passes.Count == 0) return "no passes";
        if (passes.All(Heard)) return "every recogniser heard words";
        if (!passes.Any(Heard))
            return passes.Any(p => p.Loud) ? "no recogniser heard words though the microphone heard sound: Windows' side, not the game's code" : "nothing loud reached the microphone, so this proves nothing: talk through every hold";
        var game = passes.FirstOrDefault(p => p.Name == "game");
        return game != null && !Heard(game) ? "Unity's recogniser heard words and the game's didn't: the game's code" : "some heard and some didn't: " + string.Join(", ", passes.Where(Heard).Select(p => p.Name));
    }

    /// <summary>The three holds of these seconds each, into passes.</summary>
    public static IEnumerator Run(float seconds, List<Pass> passes)
    {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        VoiceTrace.Begin();
        VoiceTrace.Line($"check: windows {WindowsSpeechSettings.Read()}, {seconds:0} s a hold");
        yield return Unity("unity", seconds, false, passes);
        yield return new WaitForSecondsRealtime(Rest);
        yield return Unity("unity-60s", seconds, true, passes);
        yield return new WaitForSecondsRealtime(Rest);
        yield return Game(seconds, passes);
        Showing = null;
#else
        passes.Add(new Pass { Name = "none", Ended = "Windows only" });
        yield break;
#endif
    }

    static void Show(string name, int index, float left) =>
        Showing = left > 0f ? $"Voice check {index} of 3 ({name}): talk now, {Mathf.CeilToInt(left)} s" : $"Voice check {index} of 3 ({name}): stop talking, waiting for the words";

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
    static IEnumerator Unity(string name, float seconds, bool gameTimeouts, List<Pass> passes)
    {
        var pass = new Pass { Name = name };
        passes.Add(pass);
        int index = passes.Count;
        DictationRecognizer recogniser;
        try
        {
            recogniser = new DictationRecognizer();
            if (gameTimeouts)
            {
                recogniser.InitialSilenceTimeoutSeconds = 60f;
                recogniser.AutoSilenceTimeoutSeconds = 60f;
            }
            recogniser.DictationHypothesis += _ => pass.Partials++;
            recogniser.DictationResult += (text, confidence) =>
            {
                pass.Finals++;
                pass.Said += text.Length + 1;
                VoiceTrace.Line($"check {name} final {VoiceTrace.Words(text)} confidence {confidence}");
            };
            recogniser.DictationComplete += cause =>
            {
                pass.Done = true;
                pass.Ended = cause.ToString();
            };
            recogniser.DictationError += (error, result) => pass.Ended = $"error {result:X8}: {error}";
            recogniser.Start();
        }
        catch (Exception e) when (e is UnityException or InvalidOperationException)
        {
            pass.Ended = "didn't start: " + e.Message;
            VoiceTrace.Line($"check {pass}");
            yield break;
        }
        VoiceTrace.Line($"check {name}: talk now");
        for (var held = Stopwatch.StartNew(); held.Elapsed.TotalSeconds < seconds && !pass.Done;)
        {
            Show(name, index, seconds - (float)held.Elapsed.TotalSeconds);
            yield return null;
        }
        Show(name, index, 0f);
        var stop = Stopwatch.StartNew();
        if (recogniser.Status == SpeechSystemStatus.Running) recogniser.Stop();
        while (!pass.Done && stop.Elapsed.TotalSeconds < MostEnd) yield return null;
        pass.StopMs = (float)stop.Elapsed.TotalMilliseconds;
        VoiceTrace.Line($"check {pass}");
        // One that never finished stays undisposed, as Dispose on a stuck one blocks (#58).
        if (pass.Done) recogniser.Dispose();
    }

    static IEnumerator Game(float seconds, List<Pass> passes)
    {
        var pass = new Pass { Name = "game" };
        passes.Add(pass);
        int index = passes.Count;
        var speech = new WindowsDictation();
        speech.Start();
        VoiceTrace.Line("check game: talk now");
        string said = "";
        void Read()
        {
            while (speech.Take(out var line))
            {
                if (line.heard == SpeechHeard.Partial) pass.Partials++;
                else if (line.heard == SpeechHeard.Final)
                {
                    pass.Finals++;
                    said += line.text + " ";
                }
                else pass.Done = true;
            }
            pass.Loudest = Mathf.Max(pass.Loudest, speech.Level);
        }
        for (var held = Stopwatch.StartNew(); held.Elapsed.TotalSeconds < seconds && !pass.Done;)
        {
            Show("the game's", index, seconds - (float)held.Elapsed.TotalSeconds);
            Read();
            yield return null;
        }
        Show("the game's", index, 0f);
        var stop = Stopwatch.StartNew();
        speech.Stop();
        while (!pass.Done && stop.Elapsed.TotalSeconds < MostEnd)
        {
            Read();
            yield return null;
        }
        pass.StopMs = (float)stop.Elapsed.TotalMilliseconds;
        pass.Said = said.Length;
        pass.Loud = pass.Loudest >= WindowsDictation.HeardSound;
        pass.Ended = pass.Done ? speech.Why ?? "Done" : "no Done";
        VoiceTrace.Line($"check {pass}");
        speech.Dispose();
    }
#endif
}
