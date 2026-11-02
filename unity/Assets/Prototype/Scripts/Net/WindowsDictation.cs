#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Windows.Speech;

// The Windows recogniser (J-061): Unity's DictationRecognizer, which sends the audio to Microsoft,
// and so works only online and with Windows' online speech recognition on. A new recogniser for
// each hold, so one that went wrong can't spoil the next. The old one stays until the next hold,
// never going inside its own event, so a word arriving after the end shows in the trace as lost.
// One that never finished is left behind, not disposed: Dispose on a stuck recogniser stopped the
// game for 20 s (#58), and nothing here may block the main thread. Windows' device name comes off
// the main thread for the same reason.
// Dictation takes a moment to start: until then the state says so, and a Stop waits for it, as a
// recogniser stopped while starting has heard nothing. Unity's Microphone reads the level beside
// it, Windows sharing the device; -voice-no-meter leaves that out.
public sealed class WindowsDictation : ISpeech
{
    // SPERR_SPEECH_PRIVACY_POLICY_NOT_ACCEPTED: Windows' online speech recognition setting is off.
    const int SettingOff = unchecked((int)0x80045509);
    const string TurnItOn = "Voice needs Windows' online speech recognition: turn it on in Settings > Privacy & security > Speech.";
    const string StillBusy = "Windows dictation is still busy with the last try. Give it a moment.";
    const string NoAnswer = "Windows dictation didn't answer, so nothing came through. Try again, or type.";
    const float SilenceSeconds = 60f, MostStopSeconds = 4f;
    const int MeterRate = 16000, MeterSeconds = 1, MeterWindow = 800;

    /// <summary>Whether the level meter runs beside dictation.</summary>
    public static bool Metered { get; set; } = !Environment.GetCommandLineArgs().Contains("-voice-no-meter");

    readonly ConcurrentQueue<(SpeechHeard heard, string text)> heard = new();
    readonly float[] samples = new float[MeterWindow];
    // Those that never said they'd finished, kept so no finaliser disposes them mid-game; those that
    // since have, to dispose at the next press, never inside their own event. Windows runs one
    // dictation at a time, so a new one can't start until a stuck one is done.
    static readonly List<DictationRecognizer> LeftBehind = new(), Spent = new();

    DictationRecognizer recogniser;
    Task<string> deviceName;
    AudioClip meter;
    bool holding, stopSent, finished;
    float stoppedAt = -1f, peak;

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Source { get; private set; }
    public string ChangeSource => "Settings > System > Sound > Input";
    public float Level { get; private set; }
    public string Notice => "Voice uses Windows dictation, which sends what you say to Microsoft.";

    public void Start()
    {
        Drop();
        Why = null;
        holding = true;
        stopSent = false;
        stoppedAt = -1f;
        finished = false;
        VoiceTrace.Line($"microphones: {string.Join(", ", Microphone.devices)}");
        deviceName = Task.Run(WindowsCaptureDevice.Default);
        try
        {
            recogniser = new DictationRecognizer { InitialSilenceTimeoutSeconds = SilenceSeconds, AutoSilenceTimeoutSeconds = SilenceSeconds };
            recogniser.DictationHypothesis += OnHypothesis;
            recogniser.DictationResult += OnResult;
            recogniser.DictationComplete += OnComplete;
            recogniser.DictationError += OnError;
            VoiceTrace.Line("recogniser made, start called");
            recogniser.Start();
            VoiceTrace.Line($"start returned, status {recogniser.Status}");
            State = SpeechState.Starting;
            OpenMeter();
        }
        catch (Exception e) when (e is UnityException or InvalidOperationException)
        {
            Debug.LogWarning("Voice: " + e.Message);
            finished = true; // it never started, so there's nothing to wait for
            Fail(LeftBehind.Count > 0 ? StillBusy : "Windows speech isn't available on this PC.");
        }
    }

    public void Stop()
    {
        if (!holding || stoppedAt >= 0f) return;
        stoppedAt = Time.unscaledTime;
        VoiceTrace.Line($"stop asked, status {recogniser?.Status}, state {State}, loudest {peak:0.000}");
        SendStop();
    }

    // Only a running recogniser can stop; Take tries again until it is.
    void SendStop()
    {
        if (stopSent || recogniser == null || recogniser.Status != SpeechSystemStatus.Running) return;
        stopSent = true;
        recogniser.Stop();
        VoiceTrace.Line("stop called");
    }

    // Stop should bring DictationComplete; if none comes, the hold still ends.
    public bool Take(out (SpeechHeard heard, string text) line)
    {
        ShowDevice();
        if (State == SpeechState.Starting && recogniser != null && recogniser.Status == SpeechSystemStatus.Running)
        {
            State = SpeechState.Listening;
            VoiceTrace.Line("status Running: listening");
        }
        if (holding && stoppedAt >= 0f)
        {
            SendStop();
            if (Time.unscaledTime - stoppedAt > MostStopSeconds)
            {
                VoiceTrace.Line($"no DictationComplete in {MostStopSeconds} s: ending the hold");
                Fail(NoAnswer);
            }
        }
        if (holding) ReadMeter();
        else CloseMeter();
        return heard.TryDequeue(out line);
    }

    public void Dispose() => Drop();

    void ShowDevice()
    {
        if (deviceName == null || !deviceName.IsCompleted) return;
        Source = WindowsCaptureDevice.Short(deviceName.Status == TaskStatus.RanToCompletion ? deviceName.Result : null);
        VoiceTrace.Line($"Windows default {Source ?? "unknown"}");
        deviceName = null;
    }

    void OnHypothesis(string text)
    {
        VoiceTrace.Line($"partial {VoiceTrace.Words(text)}{(holding ? "" : " LOST: after the end")}");
        if (holding) heard.Enqueue((SpeechHeard.Partial, text));
    }

    void OnResult(string text, ConfidenceLevel confidence)
    {
        VoiceTrace.Line($"final {VoiceTrace.Words(text)} confidence {confidence}{(holding ? "" : " LOST: after the end")}");
        if (holding) heard.Enqueue((SpeechHeard.Final, text));
    }

    void OnComplete(DictationCompletionCause cause)
    {
        VoiceTrace.Line($"DictationComplete {cause}");
        finished = true;
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
        VoiceTrace.Line($"error {result:X8}: {error}");
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

    void OpenMeter()
    {
        peak = 0f;
        if (!Metered || Microphone.devices.Length == 0) return;
        meter = Microphone.Start(null, true, MeterSeconds, MeterRate);
        VoiceTrace.Line("level meter open");
    }

    void ReadMeter()
    {
        if (meter == null) return;
        int at = Microphone.GetPosition(null);
        if (at < MeterWindow) return;
        meter.GetData(samples, at - MeterWindow);
        Level = samples.Max(Mathf.Abs);
        peak = Mathf.Max(peak, Level);
    }

    void CloseMeter()
    {
        Level = 0f;
        if (meter == null) return;
        VoiceTrace.Line($"level meter closed, loudest {peak:0.000}");
        Microphone.End(null);
        UnityEngine.Object.Destroy(meter);
        meter = null;
    }

    void Drop()
    {
        CloseMeter();
        foreach (var spent in Spent) spent.Dispose();
        Spent.Clear();
        if (recogniser == null) return;
        var old = recogniser;
        recogniser = null;
        old.DictationHypothesis -= OnHypothesis;
        old.DictationResult -= OnResult;
        old.DictationComplete -= OnComplete;
        old.DictationError -= OnError;
        if (finished)
        {
            old.Dispose();
            return;
        }
        LeftBehind.Add(old);
        old.DictationComplete += _ =>
        {
            LeftBehind.Remove(old);
            Spent.Add(old);
            VoiceTrace.Line($"a recogniser left behind finished, {LeftBehind.Count} still out");
        };
        VoiceTrace.Line($"left a stuck recogniser behind, {LeftBehind.Count} now");
    }
}
#endif
