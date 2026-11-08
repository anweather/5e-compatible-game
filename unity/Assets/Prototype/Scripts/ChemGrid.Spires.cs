using System.Collections.Generic;
using Chem;
using UnityEngine;

// Earth Spires, shown: the caster's view says when each cell of the line bursts
// up (SpiresRiseAt, one after another away from the caster), and the pillar the
// core raises there rises out of the ground at that moment (SpireRise) rather
// than standing at once. Where a raider stood the core raises nothing and throws
// it off its feet: here a spire bursts up under it anyway, carries it up on its
// point, crumbles back into the ground and lets it fall (SpireUnder, SpireRide).
// Only the show: the core decides what's hit and what stands.
public partial class ChemGrid
{
    const float SpireRiseSeconds = 0.3f, SpireHoldSeconds = 1.1f, SpireSinkSeconds = 0.55f, SpireFall = 9.8f, SpireLate = 1f;

    readonly Dictionary<int, float> spireRises = new();
    readonly Dictionary<int, (float start, float tip)> spireRides = new();
    readonly List<int> spiresGone = new();

    /// <summary>The spire the core raises on this cell bursts up at <paramref name="when"/> (Time.time), not at once.</summary>
    public void SpiresRiseAt(int x, int y, float when)
    {
        spiresGone.Clear();
        foreach (var kv in spireRises)
            if (kv.Value < Time.time - 5f) spiresGone.Add(kv.Key);
        foreach (int i in spiresGone) spireRises.Remove(i);
        spireRises[World.Idx(x, y)] = when;
    }

    /// <summary>A new spire dressing: rises out of the ground if it was told to, else stands as it is.</summary>
    public void RiseSpires(Transform cluster, int x, int y)
    {
        // Online the core's tick may come a little after the caster's view said: it rises late rather than not at all.
        if (!spireRises.TryGetValue(World.Idx(x, y), out float when) || Time.time > when + SpireLate) return;
        cluster.gameObject.AddComponent<SpireRise>().Begin(Mathf.Max(when, Time.time), SpireRiseSeconds, SpireDepth, SpireRock, (x + y) % 3 == 0);
    }

    /// <summary>A puff of the dust earth shaping throws up, where a spire breaks the ground.</summary>
    public void SpireDust(Vector3 at) => Dust(at);

    float SpireDepth => CellDressing.Look(World.Rules, TerrainIds.Pillar).height * 3.2f;
    Color SpireRock => CellDressing.Look(World.Rules, TerrainIds.Pillar).color;

    /// <summary>A spire bursts up under the raider at <paramref name="when"/>, carries it up on its point and crumbles away under it.</summary>
    public void SpireUnder(Raider raider, int x, int y, float when)
    {
        var c = World.At(x, y);
        if (raider == null || c == null || dressings == null) return;
        var surface = CellCenter(x, y);
        surface.y = GroundTop(x, y, c);
        var root = new GameObject($"Spire under raider {raider.Id}");
        root.transform.SetParent(transform, false);
        root.transform.position = surface;
        var (cluster, tip) = dressings.Spires(root.transform, CellDressing.Look(World.Rules, TerrainIds.Pillar).height, SpireRock, surface, x, y, true);
        var rise = cluster.gameObject.AddComponent<SpireRise>();
        rise.Begin(when, SpireRiseSeconds, SpireDepth, SpireRock, true);
        rise.SinkAt(when + SpireRiseSeconds + SpireHoldSeconds, SpireSinkSeconds);
        Destroy(root, when - Time.time + SpireRiseSeconds + SpireHoldSeconds + SpireSinkSeconds + 0.1f);
        spireRides[raider.Id] = (when, tip);
    }

    /// <summary>How high a raider riding a spire is drawn above its ground now, or null when it rides none.</summary>
    public float? SpireRide(int raiderId)
    {
        if (!spireRides.TryGetValue(raiderId, out var ride)) return null;
        float t = Time.time - ride.start;
        if (t < 0f) return 0f;
        if (t < SpireRiseSeconds)
        {
            float u = 1f - t / SpireRiseSeconds;
            return ride.tip * (1f - u * u);
        }
        float falling = t - SpireRiseSeconds - SpireHoldSeconds;
        if (falling <= 0f) return ride.tip;
        float high = ride.tip - 0.5f * SpireFall * falling * falling;
        if (high > 0f) return high;
        spireRides.Remove(raiderId);
        return null;
    }
}
