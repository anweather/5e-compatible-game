using System;
using UnityEngine;

// Fire and lightning made in code, modelled on how they really sound rather
// than as shaped noise. A fire is a low, flickering roar of burning gas, a
// faint hiss, and crackle: a stream of tiny resonant ticks (fibres and sap
// pockets bursting), arriving in irregular clumps, with now and then a louder
// pop. Their loudness follows a power law: many quiet ticks, a few big snaps.
// Lightning is a sharp strike, a dense sizzle of very short high crackles, a
// few branching sub-arcs, an electric buzz and a short low thump. Each comes
// in seeded variations, so repeats don't sound identical. The levels here are
// rough; SoundBank evens out the loudness against the recorded clips.
public static class ProceduralFire
{
    const int Rate = 44100;

    /// <summary>A fire burning, made to loop (seconds long, crossfaded ends).</summary>
    public static float[] Loop(int seed, float seconds = 6f)
    {
        var r = new System.Random(seed);
        int n = (int)(seconds * Rate);
        var data = new float[n];
        AddRoar(data, r, 0.35f);
        AddHiss(data, r, 0.04f);
        AddCrackle(data, r, 0f, seconds, rate: _ => 22f, small: 0.32f, pops: 2.2f);
        return CrossfadeLoop(data, Rate * 3 / 10);
    }

    /// <summary>Fire catching where a spell lands: a rushing whoomp, a thump, a burst of crackle and a short roar.</summary>
    public static float[] Burst(int seed)
    {
        var r = new System.Random(seed);
        const float seconds = 1.2f;
        var data = new float[(int)(seconds * Rate)];
        var bp = new Biquad();
        float pink = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float centre = t < 0.35f ? Mathf.Lerp(300f, 1500f, t / 0.35f) : Mathf.Lerp(1500f, 500f, Mathf.Clamp01((t - 0.35f) / 0.6f));
            if (i % 32 == 0) bp.BandPass(centre, 0.9f);
            pink = 0.97f * pink + 0.03f * Noise(r);
            float env = Attack(t, 0.03f) * Mathf.Exp(-t / 0.45f);
            data[i] += bp.Process(pink * 6f) * env * 0.9f;
            data[i] += Mathf.Sin(2f * Mathf.PI * (60f - 20f * t) * t) * Mathf.Exp(-t / 0.09f) * 0.45f;
        }
        AddCrackle(data, r, 0.02f, 0.9f, rate: t => 140f * Mathf.Exp(-t / 0.3f) + 15f, small: 0.4f, pops: 4f);
        return data;
    }

    /// <summary>A lightning zap (Shocking Grasp): strike, sizzle, sub-arcs, buzz and thump.</summary>
    public static float[] Zap(int seed)
    {
        var r = new System.Random(seed);
        const float seconds = 0.7f;
        var data = new float[(int)(seconds * Rate)];
        Strike(data, r, 0f, 1f);
        int arcs = 2 + r.Next(3);
        for (int a = 0; a < arcs; a++) Strike(data, r, 0.03f + (float)r.NextDouble() * 0.25f, 0.35f + 0.4f * (float)r.NextDouble());
        AddSizzle(data, r);
        AddBuzz(data, r);
        var lp = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            lp += 0.02f * (Noise(r) - lp);
            data[i] += lp * 3f * Mathf.Exp(-t / 0.1f) * 0.5f;
        }
        return data;
    }

    // --- fire parts ----------------------------------------------------------------

    // Burning gas: pink-ish noise, low-passed twice, its level flickering slowly at random.
    static void AddRoar(float[] data, System.Random r, float level)
    {
        float lpA = 0f, lpB = 0f, flicker = 0.8f, flickerTarget = 0.8f;
        for (int i = 0; i < data.Length; i++)
        {
            if (i % 2205 == 0) flickerTarget = 0.55f + 0.45f * (float)r.NextDouble(); // a new target every 50 ms
            flicker += (flickerTarget - flicker) * 0.0004f;
            float w = Noise(r);
            lpA += 0.06f * (w - lpA);   // ~430 Hz
            lpB += 0.015f * (w - lpB);  // ~105 Hz
            data[i] += (lpA * 2.2f + lpB * 5f) * flicker * level;
        }
    }

    // The hiss of the flames: high-passed noise, faint, flickering faster than the roar.
    static void AddHiss(float[] data, System.Random r, float level)
    {
        float lp = 0f, am = 1f;
        for (int i = 0; i < data.Length; i++)
        {
            if (i % 441 == 0) am = 0.4f + 0.6f * (float)r.NextDouble();
            float w = Noise(r);
            lp += 0.35f * (w - lp);
            data[i] += (w - lp) * am * level;
        }
    }

    // Crackle events between from and to seconds. rate(t) is ticks per second; ticks clump, since
    // their rate is scaled by a slowly wandering "burstiness". pops are big snaps per second.
    static void AddCrackle(float[] data, System.Random r, float from, float to, Func<float, float> rate, float small, float pops)
    {
        float t = from, burst = 1f;
        while (t < to)
        {
            burst = Mathf.Clamp(burst + ((float)r.NextDouble() - 0.5f) * 0.6f, 0.25f, 2.5f);
            float perSecond = Mathf.Max(0.5f, rate(t) * burst + pops);
            t += -Mathf.Log(1f - (float)r.NextDouble()) / perSecond; // Poisson arrivals
            if (t >= to) break;
            if (r.NextDouble() < pops / perSecond) Pop(data, r, t);
            else Tick(data, r, t, small * Pareto(r));
        }
    }

    // A tiny resonant tick: a fibre bursting. Damped sinusoid, 1.5 to 7 kHz, under a millisecond, with a noise edge.
    static void Tick(float[] data, System.Random r, float at, float amplitude)
    {
        float f = 1500f + 5500f * (float)r.NextDouble();
        float tau = 0.0002f + 0.0009f * (float)r.NextDouble();
        Ring(data, r, at, f, tau, amplitude, noise: 0.5f);
    }

    // A louder pop: a sap pocket bursting, lower and longer; sometimes a quick double or triple snap.
    static void Pop(float[] data, System.Random r, float at)
    {
        int snaps = r.NextDouble() < 0.3 ? 2 + r.Next(2) : 1;
        for (int s = 0; s < snaps; s++)
        {
            float f = 250f + 650f * (float)r.NextDouble();
            float tau = 0.004f + 0.008f * (float)r.NextDouble();
            float amp = (0.35f + 0.45f * (float)r.NextDouble()) * (s == 0 ? 1f : 0.6f);
            Ring(data, r, at + s * (0.01f + 0.02f * (float)r.NextDouble()), f, tau, amp, noise: 0.35f);
        }
    }

    // --- lightning parts -----------------------------------------------------------

    // The strike: a few milliseconds of full-band noise and an N-shaped pressure click.
    static void Strike(float[] data, System.Random r, float at, float amplitude)
    {
        int start = (int)(at * Rate), len = (int)(0.004f * Rate);
        for (int k = 0; k < len && start + k < data.Length; k++)
        {
            float u = k / (float)len;
            float n = Noise(r) * (1f - u);
            float click = u < 0.5f ? 1f - 2f * u : -(2f * u - 1f) * 0.8f;
            data[start + k] += (n * 0.9f + click * 0.7f) * amplitude;
        }
    }

    // The sizzle: hundreds of very short, high crackles a second, dying away over a few tenths of a second.
    static void AddSizzle(float[] data, System.Random r)
    {
        float seconds = data.Length / (float)Rate, t = 0f;
        while (t < seconds)
        {
            float density = 700f * Mathf.Exp(-t / 0.14f) + 25f;
            t += -Mathf.Log(1f - (float)r.NextDouble()) / density;
            if (t >= seconds) break;
            float f = 2000f + 7000f * (float)r.NextDouble();
            float tau = 0.00015f + 0.0004f * (float)r.NextDouble();
            Ring(data, r, t, f, tau, 0.5f * Pareto(r) * Mathf.Exp(-t / 0.25f), noise: 0.7f);
        }
    }

    // The electric buzz: a jittery sawtooth near mains hum, band-passed, flickering, short.
    static void AddBuzz(float[] data, System.Random r)
    {
        var bp = new Biquad();
        bp.BandPass(900f, 0.7f);
        float phase = 0f, am = 1f, hz = 110f + 30f * (float)r.NextDouble();
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            if (i % 220 == 0) am = 0.3f + 0.7f * (float)r.NextDouble();
            phase += hz * (1f + 0.03f * Noise(r)) / Rate;
            phase -= Mathf.Floor(phase);
            float saw = 2f * phase - 1f;
            data[i] += bp.Process(saw) * am * Mathf.Exp(-t / 0.16f) * 0.5f;
        }
    }

    // --- building blocks -----------------------------------------------------------

    // A damped sinusoid with a noise edge, added at `at` seconds.
    static void Ring(float[] data, System.Random r, float at, float hz, float tau, float amplitude, float noise)
    {
        int start = (int)(at * Rate), len = Mathf.Min((int)(tau * 6f * Rate) + 1, data.Length - start);
        float phase = (float)r.NextDouble() * Mathf.PI * 2f, step = 2f * Mathf.PI * hz / Rate;
        for (int k = 0; k < len; k++)
        {
            float env = Mathf.Exp(-k / (tau * Rate));
            data[start + k] += amplitude * env * (Mathf.Sin(phase + step * k) * (1f - noise) + Noise(r) * noise);
        }
    }

    // Loudness for a crackle: Pareto-distributed, so most are quiet and a few are loud (capped at 1).
    static float Pareto(System.Random r) => Mathf.Min(1f, 0.08f / Mathf.Pow(1f - (float)r.NextDouble() * 0.999f, 1f / 1.6f));

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
    static float Attack(float t, float seconds) => Mathf.Clamp01(t / seconds);

    // Blends the last `fade` samples into the first ones and drops them, so the clip loops without a seam.
    static float[] CrossfadeLoop(float[] data, int fade)
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

    // A biquad band-pass (RBJ cookbook), for sweeping or fixed resonances.
    sealed class Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void BandPass(float hz, float q)
        {
            float w = 2f * Mathf.PI * hz / Rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
            b0 = alpha / a0;
            b1 = 0f;
            b2 = -alpha / a0;
            a1 = -2f * Mathf.Cos(w) / a0;
            a2 = (1f - alpha) / a0;
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }
    }
}
