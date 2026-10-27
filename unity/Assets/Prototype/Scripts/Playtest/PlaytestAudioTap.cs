using UnityEngine;

// The playtest bot's ear (playtest/DESIGN.md): sits on the AudioListener and
// reads the mixed output as it goes by, keeping its loudest sample, its RMS
// and whether a sample was ever NaN. Runs on the audio thread, so it only
// stores numbers.
public class PlaytestAudioTap : MonoBehaviour
{
    float peak, recentPeak;
    double sumSquares;
    long samples, frames;
    bool sawNaN;

    public float Peak => peak;
    public float Rms => samples == 0 ? 0f : (float)System.Math.Sqrt(sumSquares / samples);
    public bool SawNaN => sawNaN;
    /// <summary>Audio frames heard (samples per channel), to tell silence from no audio at all.</summary>
    public long Frames => frames;

    /// <summary>The loudest sample since the last call (once a frame, from the main thread).</summary>
    public float TakePeak()
    {
        float p = recentPeak;
        recentPeak = 0f;
        return p;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++)
        {
            float v = data[i];
            if (float.IsNaN(v))
            {
                sawNaN = true;
                continue;
            }
            float a = v < 0 ? -v : v;
            if (a > peak) peak = a;
            if (a > recentPeak) recentPeak = a;
            sumSquares += v * v;
        }
        samples += data.Length;
        frames += data.Length / channels;
    }
}
