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

    public static AudioClip Cast => Make("cast", 0.35f, Whoosh(0.35f, 900f, 2600f, 0.5f));

    public static AudioClip Gust => Make("gust", 1.0f, Whoosh(1.0f, 300f, 1400f, 0.9f));

    public static AudioClip FireBurst => Make("fire", 0.7f, (t, r) =>
        Decay(t, 0.25f) * (Noise(r) * Lowpass(ref fireLp, 0.35f, Noise(r)) * 0.6f + Tone(t, 70f) * 0.5f));

    public static AudioClip Frost => Make("frost", 0.5f, (t, r) => Decay(t, 0.12f) * Noise(r) * 0.4f);

    public static AudioClip Splash => Make("splash", 0.9f, (t, r) =>
        Decay(t, 0.22f) * Lowpass(ref splashLp, 0.5f - t * 0.4f, Noise(r)) * 0.9f
        + Decay(t - 0.15f, 0.06f) * Tone(t, 600f + 900f * t) * 0.2f
        + Decay(t - 0.35f, 0.05f) * Tone(t, 800f + 700f * t) * 0.15f);

    public static AudioClip Squelch => Make("squelch", 0.45f, (t, r) =>
        Decay(t, 0.15f) * Lowpass(ref squelchLp, 0.08f + 0.06f * Mathf.Sin(t * 60f), Noise(r)) * 2.2f);

    public static AudioClip Zap => Make("zap", 0.55f, (t, r) =>
        Decay(t, 0.2f) * (Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * t)) * 0.25f + (r.NextDouble() < 0.08 ? Noise(r) : 0f) * 0.9f));

    public static AudioClip Thunder => Make("thunder", 1.8f, (t, r) =>
        Decay(t, 0.5f) * (Tone(t, 48f) * 0.6f + Lowpass(ref thunderLp, 0.05f, Noise(r)) * 2.5f)
        + Decay(t, 0.03f) * Noise(r) * 0.5f);

    public static AudioClip Rumble => Make("rumble", 1.2f, (t, r) =>
        Swell(t, 1.2f) * (Lowpass(ref rumbleLp, 0.03f, Noise(r)) * 3f + (r.NextDouble() < 0.01 ? Noise(r) * 0.4f : 0f)));

    public static AudioClip Shimmer => Make("shimmer", 1.0f, (t, r) =>
        Swell(t, 1.0f) * (Lowpass(ref shimmerLp, 0.04f + 0.08f * Swell(t, 1.0f), Noise(r)) * 2.2f
        + (Tone(t, 220f) + Tone(t, 330f * (1f + 0.003f * Mathf.Sin(t * 9f)))) * 0.12f));

    public static AudioClip Explosion => Make("explosion", 2.0f, (t, r) =>
        Decay(t, 0.6f) * (Tone(t, 40f + 30f * Decay(t, 0.1f)) * 0.7f + Lowpass(ref boomLp, 0.12f, Noise(r)) * 2.2f)
        + Decay(t, 0.05f) * Noise(r) * 0.7f);

    public static AudioClip Break => Make("break", 0.35f, (t, r) =>
        Decay(t, 0.03f) * Noise(r) * 0.8f + Decay(t, 0.12f) * Tone(t, 110f) * 0.5f);

    public static AudioClip Shatter => Make("shatter", 0.8f, (t, r) =>
        Decay(t, 0.02f) * Noise(r) * 0.9f
        + Decay(t, 0.25f) * (r.NextDouble() < 0.004 ? 1f : 0f) * 0.8f);

    public static AudioClip Hiss => Make("hiss", 1.2f, (t, r) =>
        Decay(t, 0.45f) * (1f - Decay(t, 0.02f)) * (Noise(r) - Lowpass(ref hissLp, 0.2f, Noise(r))) * 0.6f
        + (r.NextDouble() < 0.002 ? Decay(t, 0.5f) * 0.5f : 0f));

    public static AudioClip Horn => Make("horn", 4.5f, (t, r) =>
    {
        float note = t < 1.6f ? 92.5f : 123.5f, level = Swell(t < 1.6f ? t : t - 1.6f, t < 1.6f ? 1.6f : 2.9f);
        float brass = Tone(t, note) + 0.6f * Tone(t, note * 2f) + 0.4f * Tone(t, note * 3f) + 0.25f * Tone(t, note * 4f);
        return level * brass * 0.3f;
    });

    public static AudioClip Roar => Make("roar", 3.2f, (t, r) =>
    {
        float pitch = Mathf.Lerp(120f, 52f, t / 3.2f) * (1f + 0.04f * Tone(t, 23f));
        float growl = Mathf.Sign(Tone(t, pitch)) * 0.5f + Tone(t, pitch * 0.5f) * 0.6f;
        return Swell(t, 3.2f) * (growl * 0.45f + Lowpass(ref roarLp, 0.12f, Noise(r)) * 1.5f);
    });

    public static AudioClip FireLoop => MakeLoop("fire-loop", 3f, (t, r) =>
        Lowpass(ref loopLp, 0.06f, Noise(r)) * 1.6f + (r.NextDouble() < 0.0015 ? 1f : 0f) * Noise(r) * 0.9f);

    public static AudioClip RainLoop => MakeLoop("rain-loop", 4f, (t, r) =>
    {
        float n = Noise(r);
        float hiss = n - Lowpass(ref rainLp, 0.15f, n);
        float drop = r.NextDouble() < 0.004 ? Noise(r) * 1.4f : 0f;
        return Lowpass(ref rainSoftLp, 0.5f, hiss * 0.8f + drop);
    });

    public static AudioClip WindLoop => MakeLoop("wind-loop", 6f, (t, r) =>
        Lowpass(ref windLp, 0.012f + 0.02f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * 0.47f)), Noise(r))
        * (0.6f + 0.4f * Mathf.Sin(t * 0.9f + 1f) * Mathf.Sin(t * 0.31f)) * 4f);

    static float roarLp, fireLp, splashLp, squelchLp, thunderLp, rumbleLp, boomLp, loopLp, shimmerLp, rainLp, rainSoftLp, windLp, hissLp;

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
    static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
    static float Decay(float t, float seconds) => t < 0f ? 0f : Mathf.Exp(-t / seconds);
    static float Swell(float t, float length) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / length));

    static float Lowpass(ref float state, float k, float input)
    {
        state += Mathf.Clamp01(k) * (input - state);
        return state;
    }

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

    static int Seed(string name)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        return h;
    }

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
