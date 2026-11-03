using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// The speech check (docs/design/speech-check.md): start the game with -speech-check [seconds] and it
// records one hold through the chosen transcriber, from the default microphone after a key press, or
// from -speech-wav <file> at once. A "SPEECH CHECK" line in the log then gives the text, the
// timings (record, transcribe, total) and the frame times idle and while transcribing; a player
// quits after it, an Editor stays in Play. The words show on screen; the log takes them through
// VoiceTrace.Words, so a release logs only how much was said. With no transcriber chosen (-speech),
// it uses the stub. Windows dictation has its own check, -voice-check (VoiceCheck).
public class SpeechCheck : MonoBehaviour
{
    public const string CheckFlag = "-speech-check";
    const float DefaultSeconds = 4f, IdleSeconds = 1f, MostWaitForKey = 10f, MostEnd = 120f, ShowResult = 4f;

    /// <summary>Frame times over a stretch, in ms.</summary>
    public sealed class Frames
    {
        readonly List<float> ms = new();

        public void Add(float seconds) => ms.Add(seconds * 1000f);
        public int Count => ms.Count;
        public float Mean => ms.Count == 0 ? 0f : ms.Average();
        public float Worst => ms.Count == 0 ? 0f : ms.Max();
        public override string ToString() => $"mean {Mean:0.0} ms, worst {Worst:0.0} ms over {Count}";
    }

    /// <summary>What one check heard, how long it took, and the frames meanwhile.</summary>
    public sealed class Result
    {
        public string Source, Transcriber, Ended = "no end", Text = "";
        public double OpenMs, RecordMs, TranscribeMs, TotalMs;
        public float Heard;
        public bool Done;
        public readonly Frames Idle = new(), Busy = new();

        public override string ToString() =>
            $"{Transcriber} from {Source}: {Ended}, {VoiceTrace.Words(Text)}, {Heard:0.0} s of sound; open {OpenMs:0} ms, record {RecordMs:0} ms, transcribe {TranscribeMs:0} ms, total {TotalMs:0} ms; frames idle {Idle}, transcribing {Busy}";
    }

    /// <summary>What the check is doing, for the screen.</summary>
    public static string Showing { get; private set; }

    float seconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, CheckFlag);
        if (at < 0) return;
        float seconds = at + 1 < args.Length && float.TryParse(args[at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : DefaultSeconds;
        var check = new GameObject("Speech check").AddComponent<SpeechCheck>();
        DontDestroyOnLoad(check.gameObject);
        check.seconds = Mathf.Max(1f, seconds);
    }

    /// <summary>The chosen adapter if it's a transcriber, otherwise the stub; null where neither may run.</summary>
    public static TranscribedSpeech Chosen()
    {
        var made = SpeechChoice.Make();
        if (made is TranscribedSpeech chosen) return chosen;
        made.Dispose();
        return SpeechChoice.Make(SpeechChoice.Stub) as TranscribedSpeech;
    }

    IEnumerator Start()
    {
        using var speech = Chosen();
        if (speech == null)
        {
            UnityEngine.Debug.Log("SPEECH CHECK no transcriber here: the stub runs only in an Editor or a development build");
            Quit();
            yield break;
        }
        var result = new Result();
        bool microphone = string.IsNullOrEmpty(SpeechChoice.Wav);
        yield return Run(speech, microphone ? seconds : 0f, microphone, result);
        UnityEngine.Debug.Log("SPEECH CHECK " + result);
        Showing = $"{(result.Text.Length > 0 ? "\"" + result.Text + "\"" : "(no words)")}\n{result.Ended}: record {result.RecordMs:0} ms, transcribe {result.TranscribeMs:0} ms, worst frame {result.Busy.Worst:0} ms";
        yield return new WaitForSecondsRealtime(ShowResult);
        Quit();
    }

    static void Quit()
    {
        if (!Application.isEditor) Application.Quit();
    }

    void OnGUI()
    {
        if (Showing == null) return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUI.Box(new Rect(40, 40, Screen.width - 80, 160), Showing, style);
    }

    /// <summary>One hold of these seconds (0 for a WAV file, which is all there at once) through this adapter, after a key press if asked, into result.</summary>
    public static IEnumerator Run(TranscribedSpeech speech, float seconds, bool waitForKey, Result result)
    {
        result.Source = speech.Source;
        result.Transcriber = speech.Transcriber.Name;
        VoiceTrace.Begin();
        VoiceTrace.Line($"speech check: {result.Transcriber} from {result.Source}, {seconds:0.#} s, timeout {speech.Timeout.TotalSeconds:0.#} s");
        for (var idle = Stopwatch.StartNew(); idle.Elapsed.TotalSeconds < IdleSeconds;)
        {
            yield return null;
            result.Idle.Add(Time.unscaledDeltaTime);
        }
        if (waitForKey)
        {
            for (var wait = Stopwatch.StartNew(); wait.Elapsed.TotalSeconds < MostWaitForKey && !KeyPressed();)
            {
                Showing = $"Speech check: press Space to record {seconds:0} s (starts by itself in {Mathf.CeilToInt(MostWaitForKey - (float)wait.Elapsed.TotalSeconds)} s)";
                yield return null;
            }
        }
        var total = Stopwatch.StartNew();
        speech.Start();
        result.OpenMs = total.Elapsed.TotalMilliseconds;
        for (var held = Stopwatch.StartNew(); held.Elapsed.TotalSeconds < seconds && speech.State == SpeechState.Listening;)
        {
            Showing = $"Speech check: talk now, {Mathf.CeilToInt(seconds - (float)held.Elapsed.TotalSeconds)} s";
            Read(speech, result);
            yield return null;
        }
        Showing = "Speech check: making out the words...";
        speech.Stop();
        for (var ending = Stopwatch.StartNew(); !result.Done && ending.Elapsed.TotalSeconds < MostEnd;)
        {
            Read(speech, result);
            if (result.Done) break;
            yield return null;
            result.Busy.Add(Time.unscaledDeltaTime);
        }
        result.TotalMs = total.Elapsed.TotalMilliseconds;
        result.RecordMs = speech.RecordMs;
        result.TranscribeMs = speech.TranscribeMs;
        result.Heard = speech.Heard;
        if (!result.Done) result.Ended = "no Done";
        else result.Ended = speech.State == SpeechState.Unavailable ? speech.Why : "done";
        VoiceTrace.Line($"speech check {result}");
        Showing = null;
    }

    static void Read(TranscribedSpeech speech, Result result)
    {
        while (speech.Take(out var line))
        {
            if (line.heard == SpeechHeard.Final) result.Text = result.Text.Length == 0 ? line.text : result.Text + " " + line.text;
            else if (line.heard == SpeechHeard.Done) result.Done = true;
        }
    }

    static bool KeyPressed() =>
        (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
}
