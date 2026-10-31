using System;
using UnityEngine;

// The casting sounds no CC0 pack has, made in code like ProceduralFire: pure
// functions that return samples, with a fixed seed, so every machine gets the
// same sounds from nothing but this file. CastSoundTable says where each one is
// used; SoundBankBuilder masters them into the bank, and without a bank
// GameSounds makes them on the spot.
public static class ProceduralCast
{
    public const int Rate = 44100;

    /// <summary>The weight under a slam: a kick that drops from about 130 Hz to 45 Hz in a moment.</summary>
    public static float[] Sub(float seconds = 0.3f)
    {
        var data = new float[(int)(seconds * Rate)];
        float phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (45f + 85f * Mathf.Exp(-t / 0.035f)) / Rate;
            data[i] = Mathf.Sin(phase) * Mathf.Exp(-t / 0.09f);
        }
        return data;
    }

    /// <summary>Stones drawn together, for earth gathering: a dark rumble with a scatter of small knocks, rising.</summary>
    public static float[] Gravel(int seed, float seconds = 0.5f)
    {
        var r = new System.Random(seed);
        var data = new float[(int)(seconds * Rate)];
        float dark = 0f, knock = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            dark += 0.02f * (Noise(r) - dark);
            if (r.NextDouble() < 0.0016) knock = 0.5f + 0.5f * (float)r.NextDouble();
            knock *= 0.992f;
            data[i] = t * t * (dark * 5f + knock * Noise(r));
        }
        return data;
    }

    /// <summary>A bolt leaving the hand: a whip of bright noise and a crack at the front of it.</summary>
    public static float[] Snap(int seed, float seconds = 0.4f)
    {
        var r = new System.Random(seed);
        var data = new float[(int)(seconds * Rate)];
        float lp = 0f;
        float hz = 2600f + 700f * (float)r.NextDouble();
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * (300f + hz * Mathf.Exp(-t / 0.09f)) / Rate);
            lp += k * (Noise(r) - lp);
            data[i] = lp * 2.4f * Mathf.Exp(-t / 0.12f) * Mathf.Clamp01(t / 0.004f) + Noise(r) * 0.7f * Mathf.Exp(-t / 0.008f);
        }
        return data;
    }

    /// <summary>Earth heaved up: a slow low push with grit in it.</summary>
    public static float[] Heave(int seed, float seconds = 0.7f)
    {
        var r = new System.Random(seed);
        var data = new float[(int)(seconds * Rate)];
        float dark = 0f, grit = 0f, phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (70f - 25f * t) / Rate;
            dark += 0.03f * (Noise(r) - dark);
            grit += 0.2f * (Noise(r) - grit);
            float body = Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t / 0.22f);
            data[i] = body * (Mathf.Sin(phase) * 0.7f + dark * 3.5f + grit * 0.5f);
        }
        return data;
    }

    /// <summary>Something pushed away from the hand, wind or water: a soft rush that brightens and falls.</summary>
    public static float[] Rush(int seed, float seconds = 0.55f)
    {
        var r = new System.Random(seed);
        var data = new float[(int)(seconds * Rate)];
        float lp = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            float swell = Mathf.Sqrt(Mathf.Clamp01(t / 0.18f)) * Mathf.Pow(1f - t, 1.6f);
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * (500f + 2200f * swell) / Rate);
            lp += k * (Noise(r) - lp);
            data[i] = lp * swell * 3f;
        }
        return data;
    }

    /// <summary>The energy between the hands, made to loop: a low hum in fifths, slowly beating, with air on top.</summary>
    public static float[] ChargeLoop(float seconds = 3f)
    {
        var r = new System.Random(7);
        int fade = Rate / 4;
        var data = new float[(int)(seconds * Rate) + fade];
        float air = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            air += 0.05f * (Noise(r) - air);
            float beat = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 5f * t);
            data[i] = beat * (Tone(t, 98f) * 0.5f + Tone(t, 99.5f) * 0.4f + Tone(t, 147f) * 0.32f + Tone(t, 196.7f) * 0.18f + Tone(t, 294f) * 0.08f)
                + air * 1.1f;
        }
        var looped = new float[data.Length - fade];
        Array.Copy(data, looped, looped.Length);
        for (int i = 0; i < fade; i++)
        {
            float w = i / (float)fade;
            looped[i] = data[i] * w + data[looped.Length + i] * (1f - w);
        }
        return looped;
    }

    /// <summary>A charge level reached: a struck, glassy tone. The level's pitch comes from CastSoundTable.</summary>
    public static float[] Ping(float seconds = 0.6f)
    {
        var data = new float[(int)(seconds * Rate)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            data[i] = Mathf.Exp(-t / 0.16f) * (Tone(t, 440f) * 0.6f + Tone(t, 880.6f) * 0.3f)
                + Mathf.Exp(-t / 0.05f) * (Tone(t, 1321f) * 0.25f + Tone(t, 2212f) * 0.15f);
        }
        return data;
    }

    /// <summary>A cast dropped: a falling sputter that dies out.</summary>
    public static float[] Fizzle(float seconds = 0.45f)
    {
        var r = new System.Random(11);
        var data = new float[(int)(seconds * Rate)];
        float lp = 0f, pop = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * (3200f - 2700f * t) / Rate);
            lp += k * (Noise(r) - lp);
            if (r.NextDouble() < 0.0012) pop = 1f;
            pop *= 0.985f;
            data[i] = Mathf.Pow(1f - t, 2f) * (lp * 1.4f + pop * Noise(r) * 0.8f);
        }
        return data;
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
    static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
}
