using System.Collections.Generic;
using UnityEngine;

// Speech audio from the default microphone, through Unity's Microphone: it asks for 16 kHz into a
// looping clip, copies what's new each frame (Pump) and converts once at the end if the device gave
// another rate or more channels. A hold longer than MostSeconds keeps its first MostSeconds.
public sealed class MicrophoneAudio : ISpeechAudio
{
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
        if (Microphone.devices.Length == 0) return "No microphone found. Plug one in, or type.";
        // This can block the main thread: in the hidden Editor on a Mac it took 450 s and failed, then 391 s
        // and opened (2026-11-03, docs/design/speech-check.md).
        clip = Microphone.Start(null, true, MostSeconds, SpeechSamples.Rate);
        read = 0;
        if (clip != null)
        {
            rate = clip.frequency;
            channels = clip.channels;
        }
        if (clip != null && Microphone.IsRecording(null)) return null;
        Close();
        return "The microphone wouldn't open. Check it isn't off for this game, or type.";
    }

    public void Pump()
    {
        if (clip == null) return;
        int at = Microphone.GetPosition(null);
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
        if (Microphone.IsRecording(null)) Pump();
        Microphone.End(null);
        if (Application.isPlaying) Object.Destroy(clip);
        else Object.DestroyImmediate(clip);
        clip = null;
        Level = 0f;
        return SpeechSamples.Resample(SpeechSamples.Mono(recorded, channels), rate, SpeechSamples.Rate);
    }

    public void Dispose() => Close();
}
