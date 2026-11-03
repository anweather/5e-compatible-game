using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem.Mcp;
using UnityEngine;

// The probe's transcribe scenario: a transcriber (TranscribedSpeech) with the stub standing in for
// Whisper, in Play. The speech check from a WAV file with the stub busy on its own thread for 1.5 s,
// frame times idle and meanwhile; one slower than its timeout, then the next; a silent file; a hold
// through GameVoice to the chat, the way dictation's words go. With --only microphone, the default
// microphone opened and closed instead: on a Mac its open blocked the Editor for minutes (#58), so no
// ordinary run touches it. No model, no recording kept and no Claude: a stand-in harness takes the
// chat's message.
public class TranscribeProbe : ProbeScenario
{
    const string Said = "make the deer three times bigger", MicrophoneKind = "microphone";
    const float BusySeconds = 1.5f, SlowSeconds = 3f, ShortTimeout = 1f, MicSeconds = 1f, MostWait = 10f;
    // A frame while transcribing may be this much worse than the worst idle one before it's a hitch.
    const float MostHitchMs = 20f;

    string folder;

    protected override IEnumerator Run()
    {
        folder = Path.Combine(Application.temporaryCachePath, "transcribe-probe");
        Directory.CreateDirectory(folder);
        string ask = Wav(Said.Replace(' ', '-'), Tone(2f));
        string silence = Wav("silence", new float[SpeechSamples.Rate]);
        if (OnlyKind == MicrophoneKind)
        {
            yield return Microphone();
            Directory.Delete(folder, true);
            yield break;
        }
        yield return FromWavCheck(ask);
        yield return TimesOut(ask);
        yield return Silent(silence);
        yield return ThroughTheChat(ask);
        Directory.Delete(folder, true);
    }

    string Wav(string name, float[] samples)
    {
        string path = Path.Combine(folder, name + ".wav");
        File.WriteAllBytes(path, SpeechSamples.ToWav(samples, SpeechSamples.Rate));
        return path;
    }

    static float[] Tone(float seconds)
    {
        var samples = new float[(int)(seconds * SpeechSamples.Rate)];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.3f * Mathf.Sin(2f * Mathf.PI * 220f * i / SpeechSamples.Rate);
        return samples;
    }

    static TranscribedSpeech FromWav(string wav, float workSeconds, float timeoutSeconds) =>
        new(new WavAudio(wav), new StubTranscriber(TimeSpan.FromSeconds(workSeconds), StubTranscriber.TextFor(wav)), TimeSpan.FromSeconds(timeoutSeconds));

    // The speech check as -speech-check -speech-wav runs it, with the stub working 1.5 s.
    IEnumerator FromWavCheck(string wav)
    {
        using var speech = FromWav(wav, BusySeconds, 10f);
        var result = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, result);
        Check("the_check_hears_the_words", result.Done && result.Text == Said, result.ToString());
        Check("the_stub_worked_off_the_main_thread", ((StubTranscriber)speech.Transcriber).Thread != System.Threading.Thread.CurrentThread.ManagedThreadId);
        Check("transcribing_took_the_stubs_time", result.TranscribeMs >= BusySeconds * 1000f && result.TranscribeMs < BusySeconds * 1000f + 500f, $"{result.TranscribeMs:0} ms");
        Check("frames_kept_coming_while_transcribing", result.Busy.Count > 10, result.Busy.ToString());
        Check("no_hitch_while_transcribing", result.Busy.Worst < result.Idle.Worst + MostHitchMs, $"idle {result.Idle}, transcribing {result.Busy}");
        Metric("idle_mean_ms", result.Idle.Mean);
        Metric("idle_worst_ms", result.Idle.Worst);
        Metric("transcribing_mean_ms", result.Busy.Mean);
        Metric("transcribing_worst_ms", result.Busy.Worst);
        Metric("transcribing_frames", result.Busy.Count);
        Metric("record_ms", result.RecordMs);
        Metric("transcribe_ms", result.TranscribeMs);
        Metric("total_ms", result.TotalMs);
    }

    // A stub slower than the timeout: the hold ends at the timeout with a line, and the next works.
    IEnumerator TimesOut(string wav)
    {
        using var speech = FromWav(wav, SlowSeconds, ShortTimeout);
        var slow = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, slow);
        Check("a_slow_transcriber_ends_at_the_timeout", slow.Done && slow.TotalMs < ShortTimeout * 1000f + 500f && slow.Text.Length == 0, slow.ToString());
        Check("the_timeout_says_so", speech.Why != null && speech.Why.Contains("longer than"), speech.Why);
        Check("no_hitch_at_the_timeout", slow.Busy.Worst < slow.Idle.Worst + MostHitchMs, $"idle {slow.Idle}, transcribing {slow.Busy}");
        Metric("timeout_total_ms", slow.TotalMs);
        ((StubTranscriber)speech.Transcriber).Delay = TimeSpan.FromSeconds(0.2);
        var next = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, next);
        Check("the_next_press_works", next.Done && next.Text == Said && speech.Why == null, next.ToString());
    }

    IEnumerator Silent(string wav)
    {
        using var speech = FromWav(wav, 0.1f, ShortTimeout);
        var result = new SpeechCheck.Result();
        yield return SpeechCheck.Run(speech, 0f, false, result);
        Check("silence_says_nothing_within_the_timeout", result.Done && result.Text.Length == 0 && result.TotalMs < ShortTimeout * 1000f && speech.Why == null, result.ToString());
    }

    // A hold through GameVoice with the stub chosen: the words reach the chat as dictation's do.
    IEnumerator ThroughTheChat(string wav)
    {
        var chat = GameChat.Instance;
        var voice = GameVoice.Instance;
        Check("voice_here", chat != null && voice != null);
        if (voice == null) yield break;
        string adapterWas = SpeechChoice.Adapter, wavWas = SpeechChoice.Wav;
        var delayWas = SpeechChoice.StubDelay;
        var harnessWas = chat.Harness;
        var standIn = new StandInHarness();
        try
        {
            chat.Harness = standIn;
            SpeechChoice.Adapter = SpeechChoice.Stub;
            SpeechChoice.Wav = wav;
            SpeechChoice.StubDelay = TimeSpan.FromSeconds(0.3);
            voice.Drop();
            Check("a_hold_starts", voice.Press());
            Check("the_switch_picked_the_stub", voice.Speech is TranscribedSpeech { Transcriber: StubTranscriber }, voice.Speech?.GetType().Name);
            yield return null;
            voice.Release();
            for (float t = 0f; t < MostWait && voice.Talking; t += Time.unscaledDeltaTime) yield return null;
            Check("the_hold_ends", !voice.Talking && !chat.Open);
            Check("the_words_show_in_the_chat", chat.Lines.Contains("You: " + Said), string.Join(" | ", chat.Lines));
            Check("the_words_go_to_claude_once", standIn.Sent.Count == 1 && standIn.Sent[0] == Said, string.Join(" | ", standIn.Sent));
        }
        finally
        {
            voice.Drop();
            chat.Harness = harnessWas;
            SpeechChoice.Adapter = adapterWas;
            SpeechChoice.Wav = wavWas;
            SpeechChoice.StubDelay = delayWas;
        }
    }

    // The default microphone opens and closes; whatever the room gave, the hold ends within the timeout.
    IEnumerator Microphone()
    {
        Metric("microphones", MicrophoneAudio.Devices.Length);
        using var speech = new TranscribedSpeech(new MicrophoneAudio(), new StubTranscriber(TimeSpan.FromSeconds(0.1)), TimeSpan.FromSeconds(ShortTimeout));
        var result = new SpeechCheck.Result();
        bool opened = false;
        IEnumerator Watch()
        {
            for (float t = 0f; t < MicSeconds + 2f; t += Time.unscaledDeltaTime)
            {
                opened |= MicrophoneAudio.IsRecording;
                yield return null;
            }
        }
        StartCoroutine(Watch());
        yield return SpeechCheck.Run(speech, MicSeconds, false, result);
        if (MicrophoneAudio.Devices.Length == 0 || speech.State == SpeechState.Unavailable)
        {
            Check("a_microphone_that_wont_open_says_why_and_ends", result.Done && speech.Why != null && speech.Why.Contains("icrophone"), result.ToString());
            Check("opening_the_microphone_blocks_nothing", result.OpenMs < 500, $"{result.OpenMs:0} ms");
            Metric("microphone_open_ms", result.OpenMs);
            yield break;
        }
        Check("the_microphone_opened", opened || result.Heard > 0f, result.ToString());
        Check("the_microphone_closed", !MicrophoneAudio.IsRecording);
        Check("the_microphone_hold_ends_within_the_timeout", result.Done && speech.Why == null, result.ToString());
        Check("opening_the_microphone_blocks_nothing", result.OpenMs < 500, $"{result.OpenMs:0} ms");
        Metric("microphone_open_ms", result.OpenMs);
        Metric("microphone_seconds", result.Heard);
    }

    // Takes the chat's messages in Claude's place and answers none.
    sealed class StandInHarness : IChatHarness
    {
        public readonly List<string> Sent = new();
        public bool Busy => false;

        public bool Take(out (ClaudeSaid said, string text) line)
        {
            line = default;
            return false;
        }

        public bool Send(string message, string folder, string url, ChatRecorder recorder, Func<long> clock)
        {
            Sent.Add(message);
            return true;
        }

        public void Cancel()
        {
        }
    }
}
