using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

// The probe's crowd scenario (docs/FAST_LOOP.md): what a full valley costs a frame. 120 animals
// and folk of a fixed mix (more folk than the wilds would bring, as they've the most segments)
// round the wizard, for the before and after of a change to the creatures. Metrics: the main thread's p50 and p95,
// and ValleyFauna's own markers.
public class CrowdProbe : ProbeScenario
{
    const int Crowd = 120;
    const float SettleSeconds = 6f, MeasureSeconds = 20f;
    static readonly string[] Mix = { "human", "human", "elf", "deer", "rabbit", "red-fox", "wolf", "turkey", "squirrel", "black-bear" };

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null && fauna.SpeciesCount > 0, "ValleyFauna");
        if (fauna == null) yield break;
        var w = wizard.transform.position;
        var at = new Unity.Mathematics.Random(0x51ED270Bu);
        for (int i = 0; fauna.Count < Crowd && i < Crowd * 4; i++)
        {
            var p = w + (Vector3)new float3(at.NextFloat2Direction() * at.NextFloat(6f, 30f), 0f).xzy;
            int kind = fauna.SpeciesIndex(Mix[i % Mix.Length]);
            if (kind >= 0 && fauna.Walkable(new float2(p.x, p.z))) fauna.Spawn(kind, new float3(p.x, grid.GroundAt(p), p.z));
        }
        Check("crowd_out", fauna.Count >= Crowd, $"{fauna.Count} out");
        yield return Wait(SettleSeconds);

        var frames = new List<double>();
        var markers = new[] { "ValleyFauna.Update", "ValleyFauna.Think", "ValleyFauna.Draw" };
        var samples = markers.Select(_ => new List<double>()).ToArray();
        var recorders = markers.Select(m => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, m)).ToArray();
        using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        float until = Time.realtimeSinceStartup + MeasureSeconds;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            frames.Add(main.LastValue / 1e6);
            for (int i = 0; i < markers.Length; i++) samples[i].Add(recorders[i].LastValue / 1e6);
        }
        foreach (var r in recorders) r.Dispose();
        Metric("animals", fauna.Count);
        Metric("frames", frames.Count);
        Metric("main_p50_ms", Percentile(frames, 0.5));
        Metric("main_p95_ms", Percentile(frames, 0.95));
        Metric("fauna_update_p50_ms", Percentile(samples[0], 0.5));
        Metric("fauna_think_p50_ms", Percentile(samples[1], 0.5));
        Metric("fauna_draw_p50_ms", Percentile(samples[2], 0.5));
        Shot("crowd");
    }

    static double Percentile(List<double> values, double at)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[(int)(at * (sorted.Count - 1))];
    }
}
