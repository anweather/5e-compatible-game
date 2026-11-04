using System.Collections.Generic;
using UnityEngine;

// Speech audio from the default microphone, through Unity's Microphone: it asks for 16 kHz into a
// looping clip, copies what's new each frame (Pump) and converts once at the end if the device gave
// another rate or more channels. A hold longer than MostSeconds keeps its first MostSeconds.
// A Mac player names Unity's Microphone too: it builds because Player Settings has a Microphone Usage
// Description, the line macOS shows when it asks the player to allow it.
public sealed class MicrophoneAudio : ISpeechAudio
{
    public static string[] Devices => Microphone.devices;
    public static bool IsRecording => Microphone.IsRecording(null);
    static AudioClip StartRecording() => Microphone.Start(null, true, MostSeconds, SpeechSamples.Rate);
    static int Position => Microphone.GetPosition(null);
    static void EndRecording() => Microphone.End(null);

    /// <summary>The longest recording kept.</summary>
    public const int MostSeconds = 30;

    readonly List<float> recorded = new();
    float[] buffer = new float[0];
    AudioClip clip;
    int read, rate, channels;

    public string Name => "the default microphone";

    public string ChangeSource => Application.platform switch
    {
        RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "Settings > System > Sound > Input",
        RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "System Settings > Sound > Input",
        _ => null,
    };

    public float Level { get; private set; }
    public bool Recording => clip != null;

    public string Open()
    {
        Close();
        recorded.Clear();
        if (Devices.Length == 0) return "No microphone found. Plug one in, or type.";
        // This can block the main thread: in the hidden Editor on a Mac it took 450 s and failed, then 391 s
        // and opened (2026-11-03, docs/design/speech-check.md).
        clip = StartRecording();
        read = 0;
        if (clip != null)
        {
            rate = clip.frequency;
            channels = clip.channels;
        }
        if (clip != null && IsRecording) return null;
        Close();
        return "The microphone wouldn't open. Check it isn't off for this game, or type.";
    }

    public void Pump()
    {
        if (clip == null) return;
        int at = Position;
        int frames = (at - read + clip.samples) % clip.samples;
        if (frames == 0) return;
        int room = MostSeconds * rate * channels - recorded.Count;
        int count = Mathf.Min(frames * channels, room);
        read = at;
        if (count <= 0) return;
        if (buffer.Length != count) buffer = new float[count];
        clip.GetData(buffer, (at - frames + clip.samples) % clip.samples); // wraps round the clip by itself
        Level = SpeechSamples.Peak(buffer);
        recorded.AddRange(buffer);
    }

    public float[] Close()
    {
        if (clip == null) return new float[0];
        if (IsRecording) Pump();
        EndRecording();
        if (Application.isPlaying) Object.Destroy(clip);
        else Object.DestroyImmediate(clip);
        clip = null;
        Level = 0f;
        return SpeechSamples.Resample(SpeechSamples.Mono(recorded, channels), rate, SpeechSamples.Rate);
    }

    public void Dispose() => Close();
}
