using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The probe's swell scenario (docs/FAST_LOOP.md): the wizard dropped into the
// sea 30 m out from the shore, floating for ten seconds. Checks it swims,
// and that it rises and falls with the swell (Ocean.WaveOffset, the waves the
// sea's shader draws) rather than lying on the calm level.
public class SwellProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var ocean = Ocean.Instance;
        Check("sea_built", ocean != null, ocean != null ? "an Ocean" : "no Ocean in the scene");
        if (ocean == null) yield break;
        var xz = ocean.shoreOrigin + ocean.seaward.normalized * 30f;
        var surface = wizard.GetComponent<SurfaceEffects>();
        Teleport(new Vector3(xz.x, ocean.level + 0.3f, xz.y));
        Cut(new Vector3(xz.x, ocean.level, xz.y) + V(-7f, 5.5f, -7f), new Vector3(xz.x, ocean.level, xz.y));
        yield return Wait(4f);
        Check("swims_at_sea", surface.Swimming, "SurfaceEffects.Swimming");

        // Six seconds of the wizard's height against the wave under it.
        var heights = new List<float>();
        var waves = new List<float>();
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            var p = wizard.transform.position;
            heights.Add(p.y);
            waves.Add(ocean.WaveOffset(new Vector2(p.x, p.z)));
            yield return null;
        }
        Shot("riding");
        float rise = Range(heights), wave = Range(waves);
        float r = Correlation(heights, waves);
        Metric("height_range_m", rise);
        Metric("wave_range_m", wave);
        Metric("correlation", r);
        Check("rides_the_swell", wave > 0.2f && rise > wave * 0.5f && r > 0.6f,
            $"the wizard rose and fell {rise:0.00} m over a {wave:0.00} m swell (correlation {r:0.00})");
    }

    static float Range(List<float> xs)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (float x in xs)
        {
            lo = Mathf.Min(lo, x);
            hi = Mathf.Max(hi, x);
        }
        return hi - lo;
    }

    // Pearson correlation, with the wave a quarter second ahead (the float lags behind its target).
    static float Correlation(List<float> a, List<float> b)
    {
        int lag = Mathf.Min(8, a.Count / 4), n = a.Count - lag;
        float ma = 0f, mb = 0f;
        for (int i = 0; i < n; i++)
        {
            ma += a[i + lag];
            mb += b[i];
        }
        ma /= n;
        mb /= n;
        float sab = 0f, saa = 0f, sbb = 0f;
        for (int i = 0; i < n; i++)
        {
            float da = a[i + lag] - ma, db = b[i] - mb;
            sab += da * db;
            saa += da * da;
            sbb += db * db;
        }
        return saa > 0f && sbb > 0f ? sab / Mathf.Sqrt(saa * sbb) : 0f;
    }
}
