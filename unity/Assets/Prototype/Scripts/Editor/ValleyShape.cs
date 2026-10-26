using UnityEngine;

// The valley's shape as functions of a point on the ground plane (x, z in
// metres): the flat meadow where the old yard was, the ridged mountain to the
// north, the lake to the south, hills closing the sides, level pads for the
// cabins, and which terrain layers each point gets. ValleyBuilder and
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
    public static Vector2 Cabin => Cabins[0].At;

    /// <summary>Where a cabin stands (x, z) and which way its door faces (a direction on the ground plane).</summary>
    public readonly struct CabinSite
    {
        public readonly Vector2 At, Door;
        public CabinSite(Vector2 at, Vector2 door)
        {
            At = at;
            Door = door.normalized;
        }
    }

    /// <summary>
    /// The cabins: the first west of the yard, its door to the yard; then one on the lake's west
    /// shore, one on the river's east bank and one on the rise toward the standing stones.
    /// </summary>
    public static CabinSite[] Cabins { get; private set; }

    // The ground each cabin's pad is levelled to: the first at the yard's level, the rest at the
    // valley's height where they stand.
    static float[] padLevels;

    /// <summary>Lays the valley out around a yard of <paramref name="width"/> × <paramref name="height"/> cells.</summary>
    public static void Plan(int width, int height, float cellSize)
    {
        float half = cellSize / 2f;
        yard = Rect.MinMaxRect(-half, -half, (width - 1) * cellSize + half, (height - 1) * cellSize + half);
        Centre = yard.center;
        LakeCentre = Centre + new Vector2(25f, -95f);
        Cabins = new[]
        {
            new CabinSite(new Vector2(yard.xMin - 16f, Centre.y + 4f), Vector2.right),
            new CabinSite(new Vector2(-30f, -40f), new Vector2(1f, -1f)),
            new CabinSite(new Vector2(155f, 62f), Vector2.left),
            new CabinSite(new Vector2(-95f, 140f), new Vector2(0.6f, -0.8f)),
        };
        padLevels = new float[Cabins.Length];
        for (int i = 1; i < Cabins.Length; i++) padLevels[i] = Mathf.Max(Unlevelled(Cabins[i].At), 0f);
        PondLevel = Unlevelled(Falls) - 2f;
    }

    // --- the falls ----------------------------------------------------------------

    /// <summary>The middle of the pond at the foot of the falls, where the river starts (x, z).</summary>
    public static readonly Vector2 Falls = new Vector2(138f, 292f);

    /// <summary>The pond's radius (m): its squares are river (a spring), so it stays full and feeds the river.</summary>
    public const float PondRadius = 8f;

    /// <summary>How far north of the pond's middle the cliff stands (m), and how high it rises over the pond.</summary>
    public const float CliffNorth = 10f, CliffHeight = 26f;

    /// <summary>The height of the pond's floor (the ground under its water), set by Plan.</summary>
    public static float PondLevel { get; private set; }

    /// <summary>The top of the cliff, where the stream goes over.</summary>
    public static float CliffTop => PondLevel + CliffHeight;

    /// <summary>The stream on the cliff top that feeds the falls, from up the mountain to the lip.</summary>
    public static readonly Vector2[] Stream = { Falls + new Vector2(-4f, 34f), Falls + new Vector2(0f, CliffNorth + 2.2f) };

    // The basin the pond sits in, and the cliff behind it: north of the pond the ground leaps up to
    // the cliff top and runs on as a shelf until the mountain rises past it; to the sides it eases back.
    static float FallsShape(Vector2 p, float h)
    {
        var q = p - Falls;
        float d = q.magnitude;
        // A flat floor out to the pond's edge, banks up to the meadow by 15 m.
        if (d < 15f) h = Mathf.Lerp(PondLevel, Mathf.Min(h, PondLevel + 6f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PondRadius, 15f, d)));
        float side = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 26f, Mathf.Abs(q.x)));
        float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CliffNorth - 0.5f, CliffNorth + 0.5f, q.y)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 48f, q.y)));
        return Mathf.Max(h, Mathf.Lerp(h, CliffTop, side * shelf));
    }

    /// <summary>Whether a point is in the pond at the foot of the falls.</summary>
    public static bool InPond(Vector2 p) => (p - Falls).magnitude < PondRadius;

    /// <summary>The rotation of a cabin's root: its local -z (the door) toward the site's door direction.</summary>
    public static Quaternion CabinRotation(CabinSite site) => Quaternion.LookRotation(new Vector3(-site.Door.x, 0f, -site.Door.y));

    /// <summary>Whether a point is inside a cabin's walls (its 6 m width along local x, 4.5 m depth along z, and the log ends).</summary>
    public static bool InCabin(Vector2 p)
    {
        foreach (var site in Cabins)
        {
            var q = Quaternion.Inverse(CabinRotation(site)) * new Vector3(p.x - site.At.x, 0f, p.y - site.At.y);
            if (Mathf.Abs(q.x) <= 3.3f && Mathf.Abs(q.z) <= 2.55f) return true;
        }
        return false;
    }

    /// <summary>The distance from a point to the nearest cabin.</summary>
    public static float DistanceToCabin(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var site in Cabins) best = Mathf.Min(best, (p - site.At).magnitude);
        return best;
    }

    /// <summary>World height (metres above the yard) at a point on the ground plane.</summary>
    public static float HeightAt(Vector2 p)
    {
        float h = Unlevelled(p);
        // A level pad for each cabin, easing back into the meadow.
        for (int i = 0; i < Cabins.Length; i++)
        {
            float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, (p - Cabins[i].At).magnitude));
            h = Mathf.Lerp(padLevels[i], h, pad);
        }
        // Near the yard and the first cabin the ground never dips below their level.
        if (DistanceToYard(p) < 45f || (p - Cabin).magnitude < 20f) h = Mathf.Max(h, 0f);
        return (p - Falls).sqrMagnitude < 70f * 70f ? FallsShape(p, h) : h;
    }

    // The valley's height before the cabins' pads.
    static float Unlevelled(Vector2 p)
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
        return Mathf.Max(meadow + mountain + sides + south, 0f) * rise - lake;
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
    /// The river's course, from the pond at the foot of the falls under the northern mountain down into the
    /// lake's north-east shore, east of the yard and the court (world x, z).
    /// </summary>
    public static readonly Vector2[] River =
    {
        Falls, new(150f, 250f), new(128f, 170f), new(142f, 95f), new(120f, 30f), new(100f, -45f),
    };

    /// <summary>How far from its middle the river's squares reach (metres): two or three squares across.</summary>
    public const float RiverHalfWidth = 1.6f;

    /// <summary>Distance from a point to the river's middle line.</summary>
    public static float DistanceToRiver(Vector2 p)
    {
        float best = Mathf.Min(DistanceToCourse(p, Stream), Mathf.Max(0f, (p - Falls).magnitude - PondRadius));
        return Mathf.Min(best, DistanceToCourse(p, River));
    }

    static float DistanceToCourse(Vector2 p, Vector2[] course)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < course.Length; i++)
        {
            var a = course[i];
            var ab = course[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
        }
        return best;
    }

    /// <summary>Kept clear of trees and rocks: near the yard, the cabins' pads, and the lake.</summary>
    public static bool KeepClear(Vector2 p, float margin) =>
        DistanceToYard(p) < margin || KeepClearOfCabinAndLake(p) || DistanceToRiver(p) < RiverHalfWidth + 3f;

    /// <summary>Kept clear of grass and plants: the cabins' pads, the lake and the falls (its basin, cliff and shelf).</summary>
    public static bool KeepClearOfCabinAndLake(Vector2 p) => DistanceToCabin(p) < 12f || LakeDepth(p) > 0.05f || NearFalls(p);

    /// <summary>The pond's basin, the cliff and the shelf above it: nothing grows or stands there.</summary>
    public static bool NearFalls(Vector2 p)
    {
        var q = p - Falls;
        return q.magnitude < 16f || (Mathf.Abs(q.x) < 22f && q.y > 0f && q.y < CliffNorth + 6f);
    }

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
