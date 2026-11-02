#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Windows.Speech;

// The Windows recogniser (J-061): Unity's DictationRecognizer, which sends the audio to Microsoft,
// and so works only online and with Windows' online speech recognition on. A new recogniser for
// each hold, so one that went wrong can't spoil the next; the old one goes in Take, never inside
// its own event.
public sealed class WindowsDictation : ISpeech
{
    // SPERR_SPEECH_PRIVACY_POLICY_NOT_ACCEPTED: Windows' online speech recognition setting is off.
    const int SettingOff = unchecked((int)0x80045509);
    const string TurnItOn = "Voice needs Windows' online speech recognition: turn it on in Settings > Privacy & security > Speech.";
    const float SilenceSeconds = 60f, MostStopSeconds = 4f;

    readonly ConcurrentQueue<(SpeechHeard heard, string text)> heard = new();
    DictationRecognizer recogniser;
    bool holding;
    float stoppedAt = -1f;

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Notice => "Voice uses Windows dictation, which sends what you say to Microsoft.";

    public void Start()
    {
        Drop();
        Why = null;
        holding = true;
        try
        {
            recogniser = new DictationRecognizer { InitialSilenceTimeoutSeconds = SilenceSeconds, AutoSilenceTimeoutSeconds = SilenceSeconds };
            recogniser.DictationHypothesis += OnHypothesis;
            recogniser.DictationResult += OnResult;
            recogniser.DictationComplete += OnComplete;
            recogniser.DictationError += OnError;
            recogniser.Start();
            State = SpeechState.Listening;
        }
        catch (Exception e) when (e is UnityException or InvalidOperationException)
        {
            Debug.LogWarning("Voice: " + e.Message);
            Fail("Windows speech isn't available on this PC.");
        }
    }

    public void Stop()
    {
        if (!holding) return;
        stoppedAt = Time.unscaledTime;
        if (recogniser != null && recogniser.Status == SpeechSystemStatus.Running) recogniser.Stop();
    }

    // Stop should bring DictationComplete; if none comes, the hold still ends.
    public bool Take(out (SpeechHeard heard, string text) line)
    {
        if (stoppedAt >= 0f && Time.unscaledTime - stoppedAt > MostStopSeconds) End();
        if (!holding) Drop();
        return heard.TryDequeue(out line);
    }

    public void Dispose() => Drop();

    void OnHypothesis(string text) => heard.Enqueue((SpeechHeard.Partial, text));

    void OnResult(string text, ConfidenceLevel confidence) => heard.Enqueue((SpeechHeard.Final, text));

    void OnComplete(DictationCompletionCause cause)
    {
        switch (cause)
        {
            case DictationCompletionCause.Complete:
            case DictationCompletionCause.Canceled:
            case DictationCompletionCause.TimeoutExceeded:
                End();
                break;
            case DictationCompletionCause.NetworkFailure:
                Fail("Voice couldn't reach Windows' speech service. Check the connection.");
                break;
            case DictationCompletionCause.MicrophoneUnavailable:
                Fail("No microphone. Check Settings > Privacy & security > Microphone.");
                break;
            default:
                Fail($"Windows dictation stopped ({cause}).");
                break;
        }
    }

    void OnError(string error, int result)
    {
        Debug.LogWarning($"Voice: {error} ({result:X8})");
        Fail(result == SettingOff ? TurnItOn : "Windows dictation failed: " + error);
    }

    void Fail(string why)
    {
        if (!holding) return;
        Why = why;
        End();
        State = SpeechState.Unavailable;
    }

    // Once a hold: Done comes out after anything it heard.
    void End()
    {
        if (!holding) return;
        holding = false;
        stoppedAt = -1f;
        State = SpeechState.Idle;
        heard.Enqueue((SpeechHeard.Done, ""));
    }

    void Drop()
    {
        if (recogniser == null) return;
        var old = recogniser;
        recogniser = null;
        old.DictationHypothesis -= OnHypothesis;
        old.DictationResult -= OnResult;
        old.DictationComplete -= OnComplete;
        old.DictationError -= OnError;
        old.Dispose();
    }
}
#endif
