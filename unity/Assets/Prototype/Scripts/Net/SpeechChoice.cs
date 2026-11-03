using System;
using System.Globalization;
using UnityEngine;

// Which recogniser the game uses (J-061), the one place adapters are named: a setting kept between
// sessions (PlayerPrefs) and a factory. Adding Whisper is a line here and its adapter. Developers can
// pick one for a run with -speech <name>, which wins over the setting and isn't saved; Windows
// dictation stays the default. A transcriber (TranscribedSpeech) records from the default microphone,
// or with -speech-wav <file> from that WAV file; -speech-timeout <s> sets how long it may take, and
// -speech-stub-seconds <s> how long the stub transcriber works. The stub only answers in an Editor or
// a development build: a release gets a NoSpeech that says so.
//
// What Whisper will still need: the recording (MicrophoneAudio: 16 kHz mono floats, resampled if
// the device gives another rate), the worker thread, the timeout and the hand-off are built and
// tested with the stub (TranscribedSpeech). Whisper is an ITranscriber, a WhisperTranscriber that
// loads its model on the worker thread at its first call and passes the token to whisper's abort
// callback, plus a case here making TranscribedSpeech with it. Its test set: tools/voice-samples
// (make.ps1 on Windows, make.sh on a Mac) renders WAVs it will transcribe. docs/design/speech-check.md.
public static class SpeechChoice
{
    public const string Windows = "windows", StandIn = "stand-in", Recorded = "recorded", Stub = "stub-transcriber";
    public const string Flag = "-speech", WavFlag = "-speech-wav", TimeoutFlag = "-speech-timeout", StubSecondsFlag = "-speech-stub-seconds";
    const string Key = "speech.adapter";
    const float DefaultStubSeconds = 0.5f;

    /// <summary>The setting: which adapter to use, by name; -speech wins over it for a run.</summary>
    public static string Adapter
    {
        get => Chosen(Environment.GetCommandLineArgs(), PlayerPrefs.GetString(Key, ""));
        set
        {
            PlayerPrefs.SetString(Key, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>A WAV file a transcriber hears in place of the microphone; null for the microphone.</summary>
    public static string Wav { get; set; } = Arg(Environment.GetCommandLineArgs(), WavFlag);

    /// <summary>How long a transcriber may take.</summary>
    public static TimeSpan Timeout { get; set; } = Seconds(TimeoutFlag, TranscribedSpeech.DefaultTimeout);

    /// <summary>How long the stub transcriber works before it answers.</summary>
    public static TimeSpan StubDelay { get; set; } = Seconds(StubSecondsFlag, TimeSpan.FromSeconds(DefaultStubSeconds));

    /// <summary>The adapter these arguments and this saved setting pick: the flag, the setting, or Windows.</summary>
    public static string Chosen(string[] args, string saved)
    {
        string flag = Arg(args, Flag);
        if (!string.IsNullOrEmpty(flag)) return flag;
        return string.IsNullOrEmpty(saved) ? Windows : saved;
    }

    /// <summary>Whether this copy may use the adapter: the stub only in an Editor or a development build.</summary>
    public static bool Allowed(string adapter, bool editor, bool developmentBuild) =>
        adapter != Stub || editor || developmentBuild;

    /// <summary>The speech audio a transcriber records from: the WAV file if one is set, otherwise the microphone.</summary>
    public static ISpeechAudio Audio() => string.IsNullOrEmpty(Wav) ? new MicrophoneAudio() : new WavAudio(Wav);

    /// <summary>The stub transcriber as set: it says the WAV file's name, or how long the sound was.</summary>
    public static StubTranscriber MakeStub() => new(StubDelay, string.IsNullOrEmpty(Wav) ? null : StubTranscriber.TextFor(Wav));

    static string Arg(string[] args, string flag)
    {
        int at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static TimeSpan Seconds(string flag, TimeSpan otherwise) =>
        float.TryParse(Arg(Environment.GetCommandLineArgs(), flag), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s > 0f
            ? TimeSpan.FromSeconds(s) : otherwise;

    /// <summary>The chosen adapter, or a NoSpeech that says why there isn't one here.</summary>
    public static ISpeech Make() => Make(Adapter);

    /// <summary>The adapter of this name.</summary>
    public static ISpeech Make(string adapter)
    {
        if (!Allowed(adapter, Application.isEditor, Debug.isDebugBuild))
            return new NoSpeech("That voice setting is for testing only. Press Enter to type to Claude.");
        switch (adapter)
        {
            case Stub:
                return new TranscribedSpeech(Audio(), MakeStub(), Timeout);
            case StandIn:
                return new StandInSpeech();
            case Recorded:
                return new RecordedSpeech(RecordedSpeech.SamplesFolder);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            case Windows:
                return new WindowsDictation();
#endif
            default:
                return new NoSpeech("Voice chat isn't on this system yet. Press Enter to type to Claude.");
        }
    }
}
