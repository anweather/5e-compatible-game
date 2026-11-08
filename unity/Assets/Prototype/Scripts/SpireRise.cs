using UnityEngine;

// A cluster of Earth Spires' rock (CellDressing.Spires.cs) bursting up out of
// the ground: held under it until its moment, then shot up past where it stands
// and settling back, with a jolt of rock and dust as it breaks the surface. One
// stood up under a raider (ChemGrid.SpireUnder) also sinks back into the ground
// after, crumbling. Only the show: the core's cell is a pillar (or, under a
// raider, the ground) from the tick the spell lands. Removes itself once done.
public class SpireRise : MonoBehaviour
{
    const float Overshoot = 1.7f;

    float start, rise, depth, sinkAt = float.PositiveInfinity, sink;
    bool broke, sinking, loud;
    Color dust;

    /// <summary>Starts it under the ground, <paramref name="depth"/> down, to burst up at <paramref name="when"/> over <paramref name="seconds"/>;
    /// a <paramref name="loud"/> one rumbles as it breaks the ground (every spire of a line doing so would only be noise).</summary>
    public void Begin(float when, float seconds, float depth, Color dust, bool loud)
    {
        this.loud = loud;
        start = when;
        rise = Mathf.Max(0.05f, seconds);
        this.depth = depth;
        this.dust = dust;
        transform.localPosition = Vector3.down * depth;
        Update();
    }

    /// <summary>Sinks it back into the ground from <paramref name="when"/>, over <paramref name="seconds"/>.</summary>
    public void SinkAt(float when, float seconds)
    {
        sinkAt = when;
        sink = Mathf.Max(0.05f, seconds);
    }

    void Update()
    {
        float now = Time.time;
        if (now >= sinkAt)
        {
            if (!sinking) Burst(0.8f);
            sinking = true;
            float s = Mathf.Clamp01((now - sinkAt) / sink);
            transform.SetLocalPositionAndRotation(Vector3.down * (depth * s * s), Quaternion.Euler(Mathf.Sin(now * 53f) * 2f * (1f - s), 0f, Mathf.Cos(now * 47f) * 2f * (1f - s)));
            return;
        }
        float t = (now - start) / rise;
        if (t < 0f)
        {
            transform.localPosition = Vector3.down * depth;
            return;
        }
        if (!broke) Burst(1.2f);
        broke = true;
        t = Mathf.Clamp01(t);
        // Back-out easing: past its height, then back down onto it.
        float u = t - 1f;
        float up = 1f + (Overshoot + 1f) * u * u * u + Overshoot * u * u;
        transform.localPosition = Vector3.down * (depth * (1f - up));
        if (t >= 1f && float.IsPositiveInfinity(sinkAt))
        {
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Destroy(this);
        }
    }

    void Burst(float size)
    {
        var at = transform.parent != null ? transform.parent.position : transform.position;
        // Rock and dust, not Debris' burning rock: nothing here is on fire.
        BurstFx.Play(BurstFx.Kind.Chips, at + Vector3.up * 0.3f, size * 1.3f, tint: dust);
        var grid = ChemGrid.Current?.Grid;
        if (grid != null) grid.SpireDust(at + Vector3.up * 0.3f);
        if (loud) GameSounds.Play(SoundIds.Earth, at, 0.7f, 0.15f);
    }
}
