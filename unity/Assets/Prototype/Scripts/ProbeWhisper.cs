using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// The transcribe probe with the real model (--only whisper), #58's two complaints in order: words come
// back, and the next press doesn't freeze, after an answer or after a timeout. Then every model it can
// find (the build's, and any in ~/.cache/spell-chemistry-valley/whisper) over the spoken samples, for
// how many it hears and how long it takes. Samples: tools/voice-samples/make.sh or make.ps1.
public partial class TranscribeProbe
{
    const float WhisperTimeout = 30f, TooShortTimeout = 0.05f;
    const int LeastHeard = 8, CancelAfterMs = 30;

    IEnumerator RealWhisper()
    {
        string samples = Path.Combine(ChemGrid.Folders.Repo, "tools", "voice-samples", "out");
        string[] wavs = Directory.Exists(samples) ? Directory.GetFiles(samples, "*.wav").OrderBy(f => f).ToArray() : new string[0];
        Check("the_model_is_here", File.Exists(SpeechChoice.ModelPath), SpeechChoice.ModelPath);
        Check("spoken_samples_are_here", wavs.Length > 0, samples);
        if (!File.Exists(SpeechChoice.ModelPath) || wavs.Length == 0) yield break;

        yield return Presses(wavs[0]);
        yield return Cancels(wavs[0]);
        foreach (string model in Models()) yield return Hears(model, wavs);
    }

    static IEnumerable<string> Models()
    {
        yield return SpeechChoice.ModelPath;
        string more = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "spell-chemistry-valley", "whisper");
        if (!Directory.Exists(more)) yield break;
        foreach (string file in Directory.GetFiles(more, "ggml-*.bin"))
            if (Path.GetFileName(file) != SpeechChoice.ModelFile) yield return file;
    }

    // What a sample says. Windows' make.ps1 gives short names and a transcript beside each ("0.100 make", a word a line,
    // under a "seconds" line); the Mac's make.sh names a file by its words.
    static string Wanted(string wav)
    {
        string transcript = Path.ChangeExtension(wav, ".txt");
        if (!File.Exists(transcript)) return StubTranscriber.TextFor(wav);
        return string.Join(" ", File.ReadAllLines(transcript).Skip(1).Select(l => l.Split(' ').Last()).Where(w => w.Length > 0));
    }

    static string Plain(string text) => new string((text ?? "").ToLowerInvariant().Where(c => char.IsLetter(c) || c == ' ').ToArray()).Trim();

    IEnumerator Presses(string wav)
    {
        string said = Wanted(wav);
        var engine = new WhisperTranscriber(SpeechChoice.ModelPath);
        using var speech = new TranscribedSpeech(new WavAudio(wav), engine, TimeSpan.FromSeconds(WhisperTimeout));

        var first = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, first);
        Check("whisper_words_come_back", first.Done && Plain(first.Text) == said, first.ToString());
        Check("whisper_keeps_frames_coming", first.Busy.Count > 10, first.Busy.ToString());
        Check("whisper_no_hitch", first.Busy.Worst < first.Idle.Worst + MostHitchMs, $"idle {first.Idle}, transcribing {first.Busy}");
        Metric("first_press_ms", first.TranscribeMs);
        Metric("first_press_load_ms", engine.LoadMs);
        Metric("transcribing_mean_ms", first.Busy.Mean);
        Metric("transcribing_worst_ms", first.Busy.Worst);
        Metric("idle_worst_ms", first.Idle.Worst);

        var second = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, second);
        Check("whisper_next_press_works", second.Done && Plain(second.Text) == said && speech.Why == null, second.ToString());
        Metric("next_press_ms", second.TranscribeMs);

        speech.Timeout = TimeSpan.FromSeconds(TooShortTimeout);
        var late = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, late);
        Check("whisper_timeout_says_so", late.Done && speech.Why != null && speech.Why.Contains("longer than"), speech.Why);

        speech.Timeout = TimeSpan.FromSeconds(WhisperTimeout);
        var after = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, after);
        Check("whisper_press_after_a_timeout_works", after.Done && Plain(after.Text) == said && speech.Why == null, after.ToString());
        Metric("press_after_timeout_ms", after.TranscribeMs);
        float clock = 0f;
        while (speech.Abandoned > 0 && clock < MostWait)
        {
            clock += Time.unscaledDeltaTime;
            yield return null;
        }
        Check("whisper_abandoned_work_ended", speech.Abandoned == 0, $"{speech.Abandoned} still out");
    }

    // A cancel can't stop whisper, so a cancelled call must end with no words, and the next must still work.
    IEnumerator Cancels(string wav)
    {
        var audio = new WavAudio(wav);
        audio.Open();
        float[] samples = audio.Close();
        audio.Dispose();
        using var engine = new WhisperTranscriber(SpeechChoice.ModelPath);
        using var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        var cut = Task.Run(() => engine.Transcribe(samples, token));
        var next = Task.Run(() =>
        {
            Thread.Sleep(CancelAfterMs);
            return engine.Transcribe(samples, CancellationToken.None);
        });
        cancel.Cancel();
        while (!cut.IsCompleted || !next.IsCompleted) yield return null;
        _ = cut.Exception;
        Check("whisper_cancelled_call_drops_its_words", cut.Status != TaskStatus.RanToCompletion, cut.Status.ToString());
        Check("whisper_call_behind_a_cancelled_one_works", next.Status == TaskStatus.RanToCompletion && Plain(next.Result) == Wanted(wav), next.Status.ToString());
    }

    IEnumerator Hears(string model, string[] wavs)
    {
        string name = Path.GetFileNameWithoutExtension(model).Replace("ggml-", "").Replace('.', '_');
        using var engine = new WhisperTranscriber(model);
        var heard = new List<string>();
        var times = new List<double>();
        int right = 0;
        float worst = 0f;
        foreach (string wav in wavs)
        {
            var audio = new WavAudio(wav);
            audio.Open();
            float[] samples = audio.Close();
            audio.Dispose();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var work = Task.Run(() => engine.Transcribe(samples, CancellationToken.None));
            while (!work.IsCompleted)
            {
                worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
                yield return null;
            }
            times.Add(clock.Elapsed.TotalMilliseconds);
            string said = work.Status == TaskStatus.RanToCompletion ? work.Result : "failed: " + work.Exception?.GetBaseException().Message;
            if (Plain(said) == Wanted(wav)) right++;
            else heard.Add($"{Wanted(wav)} -> {said}");
        }
        Check($"{name}_hears_the_samples", right >= LeastHeard, $"{right} of {wavs.Length}. {string.Join("; ", heard)}");
        Metric($"{name}_right", right);
        Metric($"{name}_load_ms", engine.LoadMs);
        Metric($"{name}_phrase_mean_ms", times.Skip(1).Average());
        Metric($"{name}_phrase_worst_ms", times.Skip(1).Max());
        Metric($"{name}_worst_frame_ms", worst);
    }
}
