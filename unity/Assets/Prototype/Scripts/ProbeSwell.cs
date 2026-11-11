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
        var xz = SeaSpot(ocean);
        Metric("sea_x", xz.x);
        Metric("sea_z", xz.y);
        var surface = wizard.GetComponent<SurfaceEffects>();
        Teleport(new Vector3(xz.x, ocean.level + 0.3f, xz.y));
        Cut(new Vector3(xz.x, ocean.level, xz.y) + V(-7f, 5.5f, -7f), new Vector3(xz.x, ocean.level, xz.y));
        yield return Wait(4f);
        Check("swims_at_sea", surface.Swimming, "SurfaceEffects.Swimming");

        // Six seconds of the wizard's height against the wave under it.
        var heights = new List<float>();
        var waves = new List<float>();
        var times = new List<float>();
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            var p = wizard.transform.position;
            times.Add(t);
            heights.Add(p.y);
            waves.Add(ocean.WaveOffset(new Vector2(p.x, p.z)));
            yield return null;
        }
        Shot("riding");
        float rise = Range(heights), wave = Range(waves);
        float r = Correlation(heights, waves, times);
        Metric("height_range_m", rise);
        Metric("wave_range_m", wave);
        Metric("correlation", r);
        Check("rides_the_swell", wave > 0.2f && rise > wave * 0.5f && r > 0.6f,
            $"the wizard rose and fell {rise:0.00} m over a {wave:0.00} m swell (correlation {r:0.00})");
    }

    // Where to float: 30 m seaward of the map's shore, which is the sea on the valley. A map whose shore origin isn't its sea (the nine
    // keeps the valley's (0, 0), in the middle of its land: the wizard was put 107 m under a mountain and fell 1,330 m, #215)
    // gets a spot in its sea with open sea round it, a floor below the surface, and the swell's full reach (it fades toward the
    // shore map's edge): the first by a scan from its middle row outward, else the first of its open sea.
    Vector2 SeaSpot(Ocean ocean)
    {
        var shore = ocean.shoreOrigin + ocean.seaward.normalized * 30f;
        var land = MapLand.Current;
        if (land == null || land.Sea.SeaCells == 0) return shore;
        var here = grid.CellAt(new Vector3(shore.x, 0f, shore.y));
        if (OpenSea(land, here.x, here.y)) return shore;
        return Scan(land, ocean, true) ?? Scan(land, ocean, false) ?? shore;
    }

    Vector2? Scan(MapLand land, Ocean ocean, bool deepWithSwell)
    {
        int w = land.Map.W, h = land.Map.H;
        for (int k = 0; k < h; k += 1)
        {
            int y = h / 2 + (k % 2 == 0 ? k / 2 : -(k + 1) / 2);
            if (y < 0 || y >= h) continue;
            for (int x = 0; x < w; x += 4)
            {
                if (!OpenSea(land, x, y)) continue;
                var p = grid.CellCenter(x, y);
                var xz = new Vector2(p.x, p.z);
                if (deepWithSwell && !(ocean.shoreMap != null && ocean.shoreMap.At(xz).reach > 0.95f && grid.GroundAt(p) < ocean.level - 1f)) continue;
                return xz;
            }
        }
        return null;
    }

    // A cell of the sea with sea round it, 8 cells each way.
    static bool OpenSea(MapLand land, int cx, int cy)
    {
        for (int y = cy - 8; y <= cy + 8; y += 4)
            for (int x = cx - 8; x <= cx + 8; x += 4)
                if (x < 0 || y < 0 || x >= land.Map.W || y >= land.Map.H || !land.Sea.IsSea(x, y)) return false;
        return true;
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

    // Pearson correlation, with the wave a quarter second ahead (the float lags behind its target): by the clock, not by frames,
    // since a frame is 30 ms on the valley and over 250 ms in the Editor on the nine (#215).
    static float Correlation(List<float> a, List<float> b, List<float> times)
    {
        const float Lag = 0.25f;
        var xs = new List<float>();
        var ys = new List<float>();
        int j = 0;
        for (int i = 0; i < a.Count; i++)
        {
            while (j < a.Count - 1 && times[j] < times[i] + Lag) j++;
            if (times[j] < times[i] + Lag) break;
            xs.Add(a[j]);
            ys.Add(b[i]);
        }
        int n = xs.Count;
        if (n < 3) return 0f;
        float ma = 0f, mb = 0f;
        for (int i = 0; i < n; i++)
        {
            ma += xs[i];
            mb += ys[i];
        }
        ma /= n;
        mb /= n;
        float sab = 0f, saa = 0f, sbb = 0f;
        for (int i = 0; i < n; i++)
        {
            float da = xs[i] - ma, db = ys[i] - mb;
            sab += da * db;
            saa += da * da;
            sbb += db * db;
        }
        return saa > 0f && sbb > 0f ? sab / Mathf.Sqrt(saa * sbb) : 0f;
    }
}
