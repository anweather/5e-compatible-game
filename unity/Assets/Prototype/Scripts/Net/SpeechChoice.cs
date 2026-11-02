using UnityEngine;

// Which recogniser the game uses (J-061), the one place adapters are named: a setting kept between
// sessions (PlayerPrefs) and a factory. Adding Whisper is a line here and its adapter.
//
// What a Whisper adapter will need, none of it from the game:
// - Audio: Microphone.Start(null, true, seconds, 16000) records into an AudioClip ring; whisper.cpp
//   wants 16 kHz mono 32-bit float, which GetData gives, once Microphone.GetPosition says how far
//   it has written and the wrap is handled. Ask for 16000, then check the clip's frequency: a
//   device may not offer it (GetDeviceCaps), and then the adapter resamples. AudioClip and
//   Microphone are main-thread only, so copy samples in Take, which runs every frame.
// - Threading: transcription runs on the adapter's own thread and fills a ConcurrentQueue that
//   Take reads, as ClaudeChat does. The model must load off the main thread, and never in Start.
// - Stop: transcribe what was recorded, then Final and Done, however long it takes (GameVoice
//   holds the box meanwhile). Partial is optional: nothing breaks if there are none, the box just
//   shows Listening until the words come. Notice stays null, as nothing leaves the machine.
public static class SpeechChoice
{
    public const string Windows = "windows", StandIn = "stand-in";
    const string Key = "speech.adapter";

    /// <summary>The setting: which adapter to use, by name.</summary>
    public static string Adapter
    {
        get => PlayerPrefs.GetString(Key, Windows);
        set
        {
            PlayerPrefs.SetString(Key, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The chosen adapter, or a NoSpeech that says why there isn't one here.</summary>
    public static ISpeech Make() => Make(Adapter);

    /// <summary>The adapter of this name.</summary>
    public static ISpeech Make(string adapter)
    {
        switch (adapter)
        {
            case StandIn:
                return new StandInSpeech();
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            case Windows:
                return new WindowsDictation();
#endif
            default:
                return new NoSpeech("Voice chat isn't on this system yet. Press Enter to type to Claude.");
        }
    }
}
