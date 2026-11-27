using UnityEngine;

// Evens out sounds from different sources so they sit together in the mix:
// the recordings come from several packs, at loudness levels 30 dB apart, and
// the procedural ones are different again. Each sound is made mono (for 3D
// placement), cut to the length the game needs with a short fade, brought to
// a target loudness for its kind of sound, and soft-limited so its peaks
// don't clip. Pure functions on samples; SoundBankBuilder runs them when the
// yard is built.
public static class SoundMastering
{
    /// <summary>Interleaved samples with <paramref name="channels"/> channels, averaged to one.</summary>
    public static float[] ToMono(float[] interleaved, int channels)
    {
        if (channels <= 1) return interleaved;
        var mono = new float[interleaved.Length / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>Cuts to at most <paramref name="seconds"/>, fading the last part out, and drops leading silence.</summary>
    public static float[] Trim(float[] data, int rate, float seconds, float fadeSeconds = 0.25f)
    {
        int start = 0;
        while (start < data.Length && Mathf.Abs(data[start]) < 0.002f) start++;
        int length = Mathf.Min(data.Length - start, Mathf.Max(1, (int)(seconds * rate)));
        var cut = new float[length];
        System.Array.Copy(data, start, cut, 0, length);
        if (data.Length - start > length)
        {
            int fade = Mathf.Min(length, (int)(fadeSeconds * rate));
            for (int i = 0; i < fade; i++) cut[length - fade + i] *= 1f - i / (float)fade;
        }
        return cut;
    }

    /// <summary>
    /// How loud a sound is where it matters: the RMS level, in dB below full scale, of its loudest
    /// 0.4 s, so a silent tail doesn't make a short bang read as quiet. A loop is measured whole.
    /// </summary>
    public static float LoudnessDb(float[] data, int rate, bool whole = false)
    {
        int window = whole ? data.Length : Mathf.Min(data.Length, (int)(0.4f * rate));
        if (window == 0) return -120f;
        double sum = 0;
        for (int i = 0; i < window; i++) sum += data[i] * data[i];
        double best = sum;
        for (int i = window; i < data.Length; i++)
        {
            sum += data[i] * data[i] - data[i - window] * data[i - window];
            if (sum > best) best = sum;
        }
        return 20f * Mathf.Log10((float)System.Math.Sqrt(System.Math.Max(best, 0) / window) + 1e-9f);
    }

    /// <summary>Scales to <paramref name="targetDb"/> loudness (at most +36 dB, so near-silence isn't blown up), then soft-limits.</summary>
    public static void Normalize(float[] data, int rate, float targetDb, bool whole = false)
    {
        float gainDb = Mathf.Min(36f, targetDb - LoudnessDb(data, rate, whole));
        float gain = Mathf.Pow(10f, gainDb / 20f);
        for (int i = 0; i < data.Length; i++) data[i] = SoftLimit(data[i] * gain);
    }

    // Leaves anything below 0.8 alone and bends louder peaks smoothly towards 1, instead of clipping.
    static float SoftLimit(float x)
    {
        float a = Mathf.Abs(x);
        if (a <= 0.8f) return x;
        return Mathf.Sign(x) * (0.8f + 0.2f * (float)System.Math.Tanh((a - 0.8f) / 0.2f));
    }
}
