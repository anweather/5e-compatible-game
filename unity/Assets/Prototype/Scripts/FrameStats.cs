using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Frame times summed up: how many, the mean, the 95th percentile and the worst, in
/// milliseconds. Shared by FpsProbe (player builds) and the Editor probe scenarios
/// (ProbeScenario, docs/FAST_LOOP.md).
/// </summary>
public readonly struct FrameStats
{
    public int Count { get; }
    public float MeanMs { get; }
    public float P95Ms { get; }
    public float WorstMs { get; }

    FrameStats(int count, float mean, float p95, float worst)
    {
        Count = count;
        MeanMs = mean;
        P95Ms = p95;
        WorstMs = worst;
    }

    /// <summary>The stats of a list of frame times in ms (left unsorted); all zero for no frames.</summary>
    public static FrameStats Of(IReadOnlyCollection<float> frameMs)
    {
        if (frameMs.Count == 0) return new FrameStats(0, 0f, 0f, 0f);
        var sorted = frameMs.OrderBy(ms => ms).ToList();
        int p95 = System.Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f));
        return new FrameStats(sorted.Count, sorted.Average(), sorted[p95], sorted[sorted.Count - 1]);
    }
}
