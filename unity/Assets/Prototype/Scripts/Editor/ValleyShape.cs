using UnityEngine;

// The valley's shape as functions of a point on the ground plane (x, z in
// metres): the flat meadow where the old yard was, the ridged mountain to the
// north, the lake to the south, hills closing the sides, a level pad for the
// cabin, and which terrain layers each point gets. ValleyBuilder and
// ValleyTerrain sample it; it never touches assets. Set by Plan.
public static class ValleyShape
{
    public const float LakeLevel = -0.6f;

    // Flat ground at exactly 0 this far around the yard's cells, then the meadow rises.
    const float FlatAroundYard = 6f;

    // The yard's rectangle in world space and its centre, set by Plan.
    static Rect yard;

    public static Vector2 Centre { get; private set; }
    public static Vector2 LakeCentre { get; private set; }
    public static Vector2 Cabin { get; private set; }

    /// <summary>Lays the valley out around a yard of <paramref name="width"/> × <paramref name="height"/> cells.</summary>
    public static void Plan(int width, int height, float cellSize)
    {
        float half = cellSize / 2f;
        yard = Rect.MinMaxRect(-half, -half, (width - 1) * cellSize + half, (height - 1) * cellSize + half);
        Centre = yard.center;
        LakeCentre = Centre + new Vector2(25f, -95f);
        Cabin = new Vector2(yard.xMin - 16f, Centre.y + 4f);
    }

    /// <summary>World height (metres above the yard) at a point on the ground plane.</summary>
    public static float HeightAt(Vector2 p)
    {
        float d = DistanceToYard(p);
        if (d <= FlatAroundYard) return 0f;
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatAroundYard, 30f, d));

        var rel = p - Centre;
        float meadow = 2.2f * Fbm(p * 0.012f, 3) * rise;
        // The mountain starts well back from the yard and climbs gradually, so it reads as a
        // mountain across the valley rather than a wall.
        float north = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 430f, rel.y)), 1.4f);
        float mountain = north * (110f + 120f * Ridged(p * 0.0045f));
        float sides = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(220f, 440f, Mathf.Abs(rel.x))) * (55f + 45f * Fbm(p * 0.008f + Vector2.one * 7f, 3));
        float south = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(260f, 440f, -rel.y)) * (40f + 35f * Fbm(p * 0.01f + Vector2.one * 3f, 3));

        float lake = LakeDepth(p);
        float h = Mathf.Max(meadow + mountain + sides + south, 0f) * rise - lake;
        // A level pad for the cabin, easing back into the meadow.
        float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, (p - Cabin).magnitude));
        h = Mathf.Lerp(0f, h, pad);
        // Near the yard and the cabin the ground never dips below their level.
        if (d < 45f || (p - Cabin).magnitude < 20f) h = Mathf.Max(h, 0f);
        return h;
    }

    public static float LakeDepth(Vector2 p)
    {
        var q = p - LakeCentre;
        float e = Mathf.Sqrt(q.x * q.x / (95f * 95f) + q.y * q.y / (55f * 55f));
        e += 0.12f * (Mathf.PerlinNoise(p.x * 0.03f, p.y * 0.03f) - 0.5f);
        return 7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.05f, 0.55f, e));
    }

    public static float DistanceToYard(Vector2 p)
    {
        float dx = Mathf.Max(yard.xMin - p.x, 0f, p.x - yard.xMax);
        float dy = Mathf.Max(yard.yMin - p.y, 0f, p.y - yard.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceToLake(Vector2 p) => Mathf.Max(0f, (p - LakeCentre).magnitude - 70f);

    /// <summary>
    /// The river's course, from a spring in the foothills under the northern mountain down into the
    /// lake's north-east shore, east of the yard and the court (world x, z).
    /// </summary>
    public static readonly Vector2[] River =
    {
        new(132f, 330f), new(150f, 250f), new(128f, 170f), new(142f, 95f), new(120f, 30f), new(100f, -45f),
    };

    /// <summary>How far from its middle the river's squares reach (metres): two or three squares across.</summary>
    public const float RiverHalfWidth = 1.6f;

    /// <summary>Distance from a point to the river's middle line.</summary>
    public static float DistanceToRiver(Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < River.Length; i++)
        {
            var a = River[i];
            var ab = River[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
        }
        return best;
    }

    /// <summary>Kept clear of trees and rocks: near the yard, the cabin's pad, and the lake.</summary>
    public static bool KeepClear(Vector2 p, float margin) =>
        DistanceToYard(p) < margin || KeepClearOfCabinAndLake(p) || DistanceToRiver(p) < RiverHalfWidth + 3f;

    /// <summary>Kept clear of grass and plants: the cabin's pad and the lake.</summary>
    public static bool KeepClearOfCabinAndLake(Vector2 p) => (p - Cabin).magnitude < 12f || LakeDepth(p) > 0.05f;

    public static float Fbm(Vector2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < octaves; i++, p *= 2.03f, amp *= 0.5f) sum += amp * (Mathf.PerlinNoise(p.x, p.y) * 2f - 1f);
        return sum;
    }

    static float Ridged(Vector2 p)
    {
        float sum = 0f, amp = 0.55f;
        for (int i = 0; i < 5; i++, p *= 2.1f, amp *= 0.5f) sum += amp * (1f - Mathf.Abs(Mathf.PerlinNoise(p.x, p.y) * 2f - 1f));
        return sum;
    }

    /// <summary>Grass, moss, rock, snow, sand, soil (ValleyBuilder.LayerNames order) at a point with this slope, before normalising.</summary>
    public static float[] LayerWeights(Vector2 w, float slope)
    {
        float h = HeightAt(w);
        float shore = Mathf.InverseLerp(1.5f, 0f, Mathf.Abs(h - LakeLevel)) * Mathf.InverseLerp(30f, 0f, DistanceToLake(w));
        float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 38f, slope));
        float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 175f, h + 20f * Fbm(w * 0.02f, 2))) * (1f - rock * 0.6f);
        float moss = Mathf.Clamp01(0.5f + Fbm(w * 0.03f, 3)) * (1f - rock);
        float soil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 120f, h)) * (1f - rock) * (1f - snow);
        float sand = shore;
        float grass = Mathf.Max(0f, 1f - rock - snow - moss * 0.6f - sand - soil);
        return new[] { grass, moss * 0.6f, rock, snow, sand, soil };
    }
}
