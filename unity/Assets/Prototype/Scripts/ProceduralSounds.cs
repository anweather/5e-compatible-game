using System;
using System.Collections.Generic;
using UnityEngine;

// Sound effects made in code, like ProceduralTextures: shaped noise and
// tones with envelopes, so the game has sound without any recorded clips to
// license or store. Each clip is made once, with a fixed seed, and cached.
// Real recordings can replace them later (GameSounds picks the clip).
public static class ProceduralSounds
{
    const int Rate = 44100;
    static readonly Dictionary<string, AudioClip> cache = new();

    /// <summary>A soft whoosh as a spell leaves the hand.</summary>
    public static AudioClip Cast => Make("cast", 0.35f, Whoosh(0.35f, 900f, 2600f, 0.5f));

    /// <summary>A rushing gust, longer and lower than a cast.</summary>
    public static AudioClip Gust => Make("gust", 1.0f, Whoosh(1.0f, 300f, 1400f, 0.9f));

    /// <summary>Fire catching: a burst of noise over a low thump.</summary>
    public static AudioClip FireBurst => Make("fire", 0.7f, (t, r) =>
        Decay(t, 0.25f) * (Noise(r) * Lowpass(ref fireLp, 0.35f, Noise(r)) * 0.6f + Tone(t, 70f) * 0.5f));

    /// <summary>Frost: high inharmonic chimes and a little crackle.</summary>
    public static AudioClip Frost => Make("frost", 0.9f, (t, r) =>
        Decay(t, 0.35f) * (Tone(t, 2310f) * 0.25f + Tone(t, 3170f) * 0.2f + Tone(t, 4420f) * 0.12f)
        + Decay(t, 0.08f) * Noise(r) * 0.25f);

    /// <summary>A splash: falling filtered noise and a few bubbles.</summary>
    public static AudioClip Splash => Make("splash", 0.9f, (t, r) =>
        Decay(t, 0.22f) * Lowpass(ref splashLp, 0.5f - t * 0.4f, Noise(r)) * 0.9f
        + Decay(t - 0.15f, 0.06f) * Tone(t, 600f + 900f * t) * 0.2f
        + Decay(t - 0.35f, 0.05f) * Tone(t, 800f + 700f * t) * 0.15f);

    /// <summary>Grease: a wet, low squelch.</summary>
    public static AudioClip Squelch => Make("squelch", 0.45f, (t, r) =>
        Decay(t, 0.15f) * Lowpass(ref squelchLp, 0.08f + 0.06f * Mathf.Sin(t * 60f), Noise(r)) * 2.2f);

    /// <summary>Lightning: a crackling electric buzz.</summary>
    public static AudioClip Zap => Make("zap", 0.55f, (t, r) =>
        Decay(t, 0.2f) * (Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * t)) * 0.25f + (r.NextDouble() < 0.08 ? Noise(r) : 0f) * 0.9f));

    /// <summary>A thunderclap: a deep boom with a rolling tail.</summary>
    public static AudioClip Thunder => Make("thunder", 1.8f, (t, r) =>
        Decay(t, 0.5f) * (Tone(t, 48f) * 0.6f + Lowpass(ref thunderLp, 0.05f, Noise(r)) * 2.5f)
        + Decay(t, 0.03f) * Noise(r) * 0.5f);

    /// <summary>Earth moving: low gravelly rumble.</summary>
    public static AudioClip Rumble => Make("rumble", 1.2f, (t, r) =>
        Swell(t, 1.2f) * (Lowpass(ref rumbleLp, 0.03f, Noise(r)) * 3f + (r.NextDouble() < 0.01 ? Noise(r) * 0.4f : 0f)));

    /// <summary>
    /// Magic without an element (levitate, blink, the spectral hand), when the SoundBank has no
    /// recording: a soft, airy swell, breathy noise rising and falling over a low two-note pad.
    /// </summary>
    public static AudioClip Shimmer => Make("shimmer", 1.0f, (t, r) =>
        Swell(t, 1.0f) * (Lowpass(ref shimmerLp, 0.04f + 0.08f * Swell(t, 1.0f), Noise(r)) * 2.2f
        + (Tone(t, 220f) + Tone(t, 330f * (1f + 0.003f * Mathf.Sin(t * 9f)))) * 0.12f));

    /// <summary>An explosion: a big boom.</summary>
    public static AudioClip Explosion => Make("explosion", 2.0f, (t, r) =>
        Decay(t, 0.6f) * (Tone(t, 40f + 30f * Decay(t, 0.1f)) * 0.7f + Lowpass(ref boomLp, 0.12f, Noise(r)) * 2.2f)
        + Decay(t, 0.05f) * Noise(r) * 0.7f);

    /// <summary>Something breaking: a short crack and thud.</summary>
    public static AudioClip Break => Make("break", 0.35f, (t, r) =>
        Decay(t, 0.03f) * Noise(r) * 0.8f + Decay(t, 0.12f) * Tone(t, 110f) * 0.5f);

    /// <summary>A fire burning, made to loop: a low roar with random pops.</summary>
    public static AudioClip FireLoop => MakeLoop("fire-loop", 3f, (t, r) =>
        Lowpass(ref loopLp, 0.06f, Noise(r)) * 1.6f + (r.NextDouble() < 0.0015 ? 1f : 0f) * Noise(r) * 0.9f);

    /// <summary>Rain, made to loop: a hiss of fine noise (the noise less its low end) with scattered drops.</summary>
    public static AudioClip RainLoop => MakeLoop("rain-loop", 4f, (t, r) =>
    {
        float n = Noise(r);
        float hiss = n - Lowpass(ref rainLp, 0.15f, n);
        float drop = r.NextDouble() < 0.004 ? Noise(r) * 1.4f : 0f;
        return Lowpass(ref rainSoftLp, 0.5f, hiss * 0.8f + drop);
    });

    /// <summary>Wind, made to loop: dark noise that gusts up and down.</summary>
    public static AudioClip WindLoop => MakeLoop("wind-loop", 6f, (t, r) =>
        Lowpass(ref windLp, 0.012f + 0.02f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * 0.47f)), Noise(r))
        * (0.6f + 0.4f * Mathf.Sin(t * 0.9f + 1f) * Mathf.Sin(t * 0.31f)) * 4f);

    // --- building blocks -------------------------------------------------------

    static float fireLp, splashLp, squelchLp, thunderLp, rumbleLp, boomLp, loopLp, shimmerLp, rainLp, rainSoftLp, windLp;

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
    static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
    static float Decay(float t, float seconds) => t < 0f ? 0f : Mathf.Exp(-t / seconds);
    static float Swell(float t, float length) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / length));

    // One-pole low-pass; k near 0 is darker, near 1 brighter. State lives in the caller's field.
    static float Lowpass(ref float state, float k, float input)
    {
        state += Mathf.Clamp01(k) * (input - state);
        return state;
    }

    // Band-limited noise whose brightness rises from lowHz to highHz and falls back, under a swell.
    static Func<float, System.Random, float> Whoosh(float length, float lowHz, float highHz, float gain)
    {
        float lp = 0f;
        return (t, r) =>
        {
            float hz = Mathf.Lerp(lowHz, highHz, Swell(t, length));
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);
            lp += k * (Noise(r) - lp);
            return Swell(t, length) * lp * gain * 3f;
        };
    }

    static AudioClip Make(string name, float seconds, Func<float, System.Random, float> sample) =>
        Build(name, seconds, sample, loop: false);

    static AudioClip MakeLoop(string name, float seconds, Func<float, System.Random, float> sample) =>
        Build(name, seconds, sample, loop: true);

    static AudioClip Build(string name, float seconds, Func<float, System.Random, float> sample, bool loop)
    {
        // A clip only this cache holds counts as unused: Unity destroys it when a scene loads, so make it again.
        if (cache.TryGetValue(name, out var clip) && clip != null) return clip;
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        var random = new System.Random(Seed(name));
        for (int i = 0; i < n; i++) data[i] = sample(i / (float)Rate, random);
        if (loop) data = CrossfadeEnds(data, Rate / 5);
        Normalize(data, 0.8f);
        clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        cache[name] = clip;
        return clip;
    }

    // A stable seed from the clip's name (string.GetHashCode can differ between runs).
    static int Seed(string name)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        return h;
    }

    // Blends the last `fade` samples into the first ones and drops them, so the clip loops without a click.
    static float[] CrossfadeEnds(float[] data, int fade)
    {
        int n = data.Length - fade;
        var looped = new float[n];
        Array.Copy(data, looped, n);
        for (int i = 0; i < fade; i++)
        {
            float w = i / (float)fade;
            looped[i] = data[i] * w + data[n + i] * (1f - w);
        }
        return looped;
    }

    static void Normalize(float[] data, float peak)
    {
        float max = 0f;
        foreach (var s in data) max = Mathf.Max(max, Mathf.Abs(s));
        if (max <= 0f) return;
        float k = peak / max;
        for (int i = 0; i < data.Length; i++) data[i] *= k;
    }
}
