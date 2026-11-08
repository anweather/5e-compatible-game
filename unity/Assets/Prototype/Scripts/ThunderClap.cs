using UnityEngine;

// The thunder of a strike right overhead (Thunder Strike, ChemGrid.Whirls.cs):
// a sharp, tearing crack, a thump in the chest, then a deep boom rolling away
// over seconds. The boom and its roll are Punchy's ThunderSynth at close range
// (a new one each strike); the crack, its crackle and the thump go on top, and
// the lot is driven a little into saturation and mastered well above the mix's
// explosions (SoundBankBuilder's -12 dB), so it's the loudest thing the game
// plays. Pure computation: safe on a worker thread while the bolt comes down.
public static class ThunderClap
{
    /// <summary>The clap's loudness: its loudest 0.4 s, in dB below full scale (SoundMastering.LoudnessDb).</summary>
    public const float TargetDb = -7f;
    const float BoomDistance = 40f, BoomDelay = 0.06f, Drive = 1.8f;

    /// <summary>Its samples, mono at ThunderSynth.Rate.</summary>
    public static float[] Generate(int seed)
    {
        int rate = Punchy.Weather.ThunderSynth.Rate;
        var boom = Punchy.Weather.ThunderSynth.Generate(BoomDistance, seed);
        int delay = (int)(BoomDelay * rate);
        var samples = new float[boom.Length + delay];
        var rng = new System.Random(seed ^ 0x5bd1e995);
        float R() => (float)rng.NextDouble();

        // The crackle: a burst of sharp ticks over the first third of a second, thinning out.
        const int Ticks = 40;
        var tickAt = new int[Ticks];
        var tickAmp = new float[Ticks];
        for (int k = 0; k < Ticks; k++)
        {
            float t = 0.32f * R() * R();
            tickAt[k] = (int)(t * rate);
            tickAmp[k] = (0.4f + 0.6f * R()) * Mathf.Exp(-t / 0.12f);
        }
        float aHigh = 1f - Mathf.Exp(-2f * Mathf.PI * 1800f / rate), aTear = 1f - Mathf.Exp(-2f * Mathf.PI * 4500f / rate);
        float low = 0f, tear = 0f, phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float white = R() * 2f - 1f;
            low += (white - low) * aHigh;
            tear += (white - tear) * aTear;
            // The crack: bright noise, gone in a twentieth of a second, and a tearing band under it a little longer.
            float crack = (white - low) * Mathf.Exp(-t / 0.05f) * 1.4f + (tear - low) * Mathf.Exp(-t / 0.16f) * 1.1f;
            float crackle = 0f;
            for (int k = 0; k < Ticks && t < 0.34f; k++)
            {
                int d = i - tickAt[k];
                if (d >= 0 && d < 90) crackle += (white - low) * tickAmp[k] * (1f - d / 90f) * 2.2f;
            }
            // The thump: a falling sine, 75 Hz to 32, under the crack.
            float pitch = Mathf.Lerp(32f, 75f, Mathf.Exp(-t / 0.25f));
            phase += 2f * Mathf.PI * pitch / rate;
            float thump = Mathf.Sin(phase) * Mathf.Exp(-t / 0.4f) * Mathf.Clamp01(t / 0.004f) * 0.9f;
            float roll = i >= delay ? boom[i - delay] * 1.15f : 0f;
            float x = crack + crackle + thump + roll;
            samples[i] = (float)System.Math.Tanh(x * Drive) / (float)System.Math.Tanh(Drive);
        }
        // No click at the end.
        int fade = Mathf.Min(samples.Length, rate / 2);
        for (int i = 0; i < fade; i++) samples[samples.Length - 1 - i] *= i / (float)fade;
        SoundMastering.Normalize(samples, rate, TargetDb);
        return samples;
    }

    /// <summary>Wraps the samples in a clip (main thread only).</summary>
    public static AudioClip ToClip(float[] samples)
    {
        var clip = AudioClip.Create(SoundIds.ThunderClap, samples.Length, 1, Punchy.Weather.ThunderSynth.Rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
