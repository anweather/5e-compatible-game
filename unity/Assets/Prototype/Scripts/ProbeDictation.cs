using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

// The probe's dictation scenario: the real Windows recogniser on the real default microphone, held
// and let go a few times with the meter on and off. A room with nobody talking still shows whether a
// hold ends and whether the next press stops the game. Real words need a real voice in the room.
public class DictationProbe : ProbeScenario
{
    const float HoldSeconds = 3f, MostSeconds = 2f, Recover = 2f, MostPressMs = 50f, FirstPressMs = 600f, MostEnd = 12f;
    const int Holds = 3;

    readonly List<string> traces = new();

    protected override IEnumerator Run()
    {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        Application.logMessageReceived += Note;
        yield return Series("meter", true);
        yield return Series("no_meter", false);
        yield return LeftUnfinished();
        Application.logMessageReceived -= Note;
        Check("trace", true, string.Join(" | ", traces));
#else
        Check("needs_windows", true);
        yield break;
#endif
    }

    void Note(string line, string stack, LogType type)
    {
        if (line.StartsWith("VOICE", System.StringComparison.Ordinal) && !line.Contains("microphones")) traces.Add(line.Substring(6));
    }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
    // A press straight after a stop: the old recogniser hasn't finished, as a stuck one never does.
    // Leaving it must not stop the game, and the next hold must still work.
    IEnumerator LeftUnfinished()
    {
        WindowsDictation.Metered = true;
        var speech = new WindowsDictation();
        speech.Start();
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            speech.Take(out _);
            yield return null;
        }
        speech.Stop();
        var clock = Stopwatch.StartNew();
        speech.Start();
        float press = (float)clock.Elapsed.TotalMilliseconds;
        Metric("left_unfinished_press_ms", press);
        Check("leaving_an_unfinished_recogniser_blocks_nothing", press < MostPressMs, $"{press:0} ms");
        Check("a_busy_recogniser_is_said_plainly", speech.Why == null || speech.Why.Contains("busy"), speech.Why);
        yield return Hold(speech, 0f);
        yield return Wait(Recover);
        yield return Hold(speech, HoldSeconds);
        Check("voice_comes_back_by_itself", speech.State == SpeechState.Idle && speech.Why == null, $"{speech.State}, {speech.Why}");
        speech.Dispose();
    }

    // A hold of these seconds, to its Done.
    static IEnumerator Hold(WindowsDictation speech, float seconds)
    {
        if (seconds > 0f) speech.Start();
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            speech.Take(out _);
            yield return null;
        }
        speech.Stop();
        bool done = false;
        for (float t = 0f; t < MostSeconds && !done; t += Time.unscaledDeltaTime)
        {
            while (speech.Take(out var line)) done |= line.heard == SpeechHeard.Done;
            yield return null;
        }
    }

    IEnumerator Series(string name, bool meter)
    {
        WindowsDictation.Metered = meter;
        var speech = new WindowsDictation();
        for (int hold = 0; hold < Holds; hold++)
        {
            var clock = Stopwatch.StartNew();
            VoiceTrace.Begin();
            speech.Start();
            float press = (float)clock.Elapsed.TotalMilliseconds;
            Metric($"{name}_press_{hold}_ms", press);
            // The first recogniser of a run loads Windows' speech library, once.
            Check($"{name}_press_{hold}_blocks_nothing", press < (hold == 0 && meter ? FirstPressMs : MostPressMs), $"{press:0} ms");
            float worst = 0f;
            for (float t = 0f; t < HoldSeconds; t += Time.unscaledDeltaTime)
            {
                speech.Take(out _);
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                yield return null;
            }
            speech.Stop();
            bool done = false;
            var ended = Stopwatch.StartNew();
            string words = "";
            while (!done && ended.Elapsed.TotalSeconds < MostEnd)
            {
                while (speech.Take(out var line))
                {
                    if (line.heard == SpeechHeard.Done) done = true;
                    else words += line.text + " ";
                }
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                yield return null;
            }
            Metric($"{name}_end_{hold}_ms", ended.Elapsed.TotalMilliseconds);
            Metric($"{name}_worst_frame_{hold}_ms", worst * 1000f);
            Check($"{name}_hold_{hold}_ends", done, $"{speech.State}, heard \"{words.Trim()}\", {speech.Why}");
        }
        var clock2 = Stopwatch.StartNew();
        speech.Dispose();
        Metric($"{name}_dispose_ms", clock2.Elapsed.TotalMilliseconds);
    }
#endif
}
