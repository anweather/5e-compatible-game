// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3358
using UnityEngine;

namespace Punchy.Weather
{
    /// <summary>
    /// Synthesises a unique thunder sound for each strike. Distance shapes it the way air does: high frequencies die
    /// away over distance, so far thunder is a soft, low boom while only nearby strikes keep a sharp crack. Each sound
    /// has one to three swells of intensity at irregular intervals, often followed by a lingering, rolling rumble.
    /// </summary>
    public static class ThunderSynth
    {
        public const int Rate = 22050;

        public static AudioClip Create(float distance, System.Random rng) => ToClip(Generate(distance, rng.Next()));

        /// <summary>Wraps generated samples in an AudioClip (main thread only).</summary>
        public static AudioClip ToClip(float[] samples)
        {
            var clip = AudioClip.Create("Thunder", samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Generates the samples. Pure computation, safe to run on a worker thread.</summary>
        public static float[] Generate(float distance, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var km = distance / 1000f;
            var near = Mathf.Clamp01(1f - km / 1.5f);

            // Air removes the highs: ~320 Hz content up close, ~70 Hz from several kilometres.
            var cutoff = Mathf.Lerp(320f, 70f, Mathf.Clamp01(km / 6f));
            var seconds = Mathf.Min(Mathf.Lerp(5f, 12f, R()) + km * 0.6f, 16f);
            var samples = new float[(int)(seconds * Rate)];

            // 1–3 swells: a soft rise, then a long decay. Distant ones are slower and smoother.
            var peakCount = R() < 0.45f ? 1 : R() < 0.65f ? 2 : 3;
            var peaks = new (float time, float amp, float attack, float decay)[peakCount];
            var at = Mathf.Lerp(0.05f, 0.4f, R());
            for (int p = 0; p < peakCount; p++)
            {
                peaks[p] = (at, p == 0 ? 1f : Mathf.Lerp(0.45f, 1.1f, R()),
                            Mathf.Lerp(0.12f, 0.45f, R()) * (1f + km * 0.1f),
                            Mathf.Lerp(0.6f, 2.0f, R()) * (1f + km * 0.15f));
                at += Mathf.Lerp(0.7f, 2.8f, R());
            }
            var hasRumble = R() < 0.65f;
            var rumbleAmp = Mathf.Lerp(0.2f, 0.45f, R());
            var rumbleDecay = Mathf.Lerp(2.5f, 6f, R());
            var rollRate = Mathf.Lerp(0.4f, 1.3f, R());
            var rollPhase = R() * 6.28f;

            // Filters: three one-pole low-passes in series (steep, natural roll-off), plus a deeper sub layer.
            var a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
            var aSub = 1f - Mathf.Exp(-2f * Mathf.PI * 45f / Rate);
            var aCrack = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / Rate);
            float l1 = 0, l2 = 0, l3 = 0, s1 = 0, s2 = 0, c1 = 0;
            float peak = 1e-5f;

            for (int i = 0; i < samples.Length; i++)
            {
                var t = i / (float)Rate;
                var white = R() * 2f - 1f;
                l1 += (white - l1) * a; l2 += (l1 - l2) * a; l3 += (l2 - l3) * a;
                s1 += (white - s1) * aSub; s2 += (s1 - s2) * aSub;
                var body = l3 * 2.2f + s2 * 4f;

                var envelope = 0f;
                foreach (var pk in peaks)
                {
                    var dt = t - pk.time;
                    if (dt < 0f) continue;
                    var rise = dt < pk.attack ? (dt / pk.attack) * (dt / pk.attack) : 1f;
                    envelope += pk.amp * rise * Mathf.Exp(-Mathf.Max(0f, dt - pk.attack) / pk.decay);
                }
                if (hasRumble)
                {
                    var roll = 0.55f + 0.45f * Mathf.Sin(t * rollRate * 6.28f + rollPhase) * Mathf.Sin(t * rollRate * 2.3f + 1f);
                    envelope += rumbleAmp * Mathf.Clamp01(t / 1.2f) * Mathf.Exp(-t / rumbleDecay) * roll;
                }

                var sample = body * envelope;
                if (near > 0f)
                {
                    // Nearby strikes: a sharp, tearing crack before the boom.
                    c1 += (white - c1) * aCrack;
                    var crack = (white - c1) * Mathf.Exp(-t / 0.07f) + l1 * 3f * Mathf.Exp(-t / 0.35f);
                    sample += crack * near * 0.8f;
                }

                // Gentle fade in/out so no clicks.
                sample *= Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((seconds - t) / 0.5f);
                samples[i] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            for (int i = 0; i < samples.Length; i++)
                samples[i] *= 0.95f / peak;
            return samples;
        }
    }
}
