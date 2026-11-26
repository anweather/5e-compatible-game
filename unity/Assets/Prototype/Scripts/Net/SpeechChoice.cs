using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// Which recogniser the game uses (J-061), the one place adapters are named: a setting kept between
// sessions (PlayerPrefs) and a factory. Developers can pick one for a run with -speech <name>, which
// wins over the setting and isn't saved; Whisper is the default. A transcriber (TranscribedSpeech)
// records from the default microphone, or with -speech-wav <file> from that WAV file; -speech-timeout
// <s> sets how long it may take, -speech-model <file> which model Whisper loads, -speech-gpu puts
// Whisper on the GPU, and -speech-stub-seconds <s> says how long the stub transcriber works. The stub
// only answers in an Editor or a development build: a release gets a NoSpeech that says so.
// docs/design/speech-check.md.
public static class SpeechChoice
{
    public const string Whisper = "whisper", Windows = "windows", StandIn = "stand-in", Recorded = "recorded", Stub = "stub-transcriber";
    public const string Flag = "-speech", WavFlag = "-speech-wav", TimeoutFlag = "-speech-timeout", StubSecondsFlag = "-speech-stub-seconds";
    public const string ModelFlag = "-speech-model", GpuFlag = "-speech-gpu";

    public const string ModelFile = "ggml-base.en.bin";
    const string Key = "speech.adapter";
    const float DefaultStubSeconds = 0.5f;

    public static string Adapter
    {
        get => Chosen(Environment.GetCommandLineArgs(), PlayerPrefs.GetString(Key, ""));
        set
        {
            PlayerPrefs.SetString(Key, value);
            PlayerPrefs.Save();
        }
    }

    public static string Wav { get; set; } = Arg(Environment.GetCommandLineArgs(), WavFlag);

    public static TimeSpan Timeout { get; set; } = Seconds(TimeoutFlag, TranscribedSpeech.DefaultTimeout);

    public static TimeSpan StubDelay { get; set; } = Seconds(StubSecondsFlag, TimeSpan.FromSeconds(DefaultStubSeconds));

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Wav = Arg(Environment.GetCommandLineArgs(), WavFlag);
        Timeout = Seconds(TimeoutFlag, TranscribedSpeech.DefaultTimeout);
        StubDelay = Seconds(StubSecondsFlag, TimeSpan.FromSeconds(DefaultStubSeconds));
    }

    public static string ModelPath =>
        Arg(Environment.GetCommandLineArgs(), ModelFlag) ?? Path.Combine(Application.streamingAssetsPath, "Whisper", ModelFile);

    public static string Chosen(string[] args, string saved)
    {
        string flag = Arg(args, Flag);
        if (!string.IsNullOrEmpty(flag)) return flag;
        return string.IsNullOrEmpty(saved) ? Whisper : saved;
    }

    public static bool Allowed(string adapter, bool editor, bool developmentBuild) =>
        adapter != Stub || editor || developmentBuild;

    public static ISpeechAudio Audio() => string.IsNullOrEmpty(Wav) ? new MicrophoneAudio() : new WavAudio(Wav);

    public static StubTranscriber MakeStub() => new(StubDelay, string.IsNullOrEmpty(Wav) ? null : StubTranscriber.TextFor(Wav));

    static string Arg(string[] args, string flag)
    {
        int at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static TimeSpan Seconds(string flag, TimeSpan otherwise) =>
        float.TryParse(Arg(Environment.GetCommandLineArgs(), flag), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s > 0f
            ? TimeSpan.FromSeconds(s) : otherwise;

    public static ISpeech Make() => Make(Adapter);

    public static ISpeech Make(string adapter)
    {
        if (!Allowed(adapter, Application.isEditor, Debug.isDebugBuild))
            return new NoSpeech("That voice setting is for testing only. Press Enter to type to Claude.");
        switch (adapter)
        {
            case Whisper:
                if (!File.Exists(ModelPath)) return new NoSpeech("The voice model is missing from this copy. Press Enter to type to Claude.");
                bool gpu = Array.IndexOf(Environment.GetCommandLineArgs(), GpuFlag) >= 0;
                return new TranscribedSpeech(Audio(), new WhisperTranscriber(ModelPath, gpu), Timeout);
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
