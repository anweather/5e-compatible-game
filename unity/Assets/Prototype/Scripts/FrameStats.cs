using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Frame times summed up: how many, the mean, the 50th, 95th and 99th percentiles and the
/// worst, in milliseconds. Shared by FpsProbe and PerfScenario (player builds) and the
/// Editor probe scenarios (ProbeScenario, docs/FAST_LOOP.md).
/// </summary>
public readonly struct FrameStats
{
    public int Count { get; }
    public float MeanMs { get; }
    public float P50Ms { get; }
    public float P95Ms { get; }
    public float P99Ms { get; }
    public float WorstMs { get; }

    FrameStats(int count, float mean, float p50, float p95, float p99, float worst)
    {
        Count = count;
        MeanMs = mean;
        P50Ms = p50;
        P95Ms = p95;
        P99Ms = p99;
        WorstMs = worst;
    }

    /// <summary>The stats of a list of frame times in ms (left unsorted); all zero for no frames.</summary>
    public static FrameStats Of(IReadOnlyCollection<float> frameMs)
    {
        if (frameMs.Count == 0) return new FrameStats(0, 0f, 0f, 0f, 0f, 0f);
        var sorted = frameMs.OrderBy(ms => ms).ToList();
        float At(float share) => sorted[System.Math.Min(sorted.Count - 1, (int)(sorted.Count * share))];
        return new FrameStats(sorted.Count, sorted.Average(), At(0.5f), At(0.95f), At(0.99f), sorted[sorted.Count - 1]);
    }

    /// <summary>The slowest <paramref name="count"/> frames in ms, slowest first.</summary>
    public static List<float> Worst(IEnumerable<float> frameMs, int count) =>
        frameMs.OrderByDescending(ms => ms).Take(count).ToList();
}
