using System.Collections.Generic;
using UnityEngine;

// The coast south-east of the valley (docs/WORLD_LIFE.md), as functions of a
// point on the ground plane like ValleyShape: over a wooded hill from the lake,
// dunes, a sand beach and the sea, its bed shelving out to the horizon, and an
// island out in the bay with its own beaches and a low hill in the middle.
// The sea's surface is the lake's (LakeLevel), so every square under it is the
// lake's kind of water (CellKind) and swimming works there as it does there.
// A path leads from the yard over the river's bridge and the hill down to the
// beach. ValleyShape folds Shape into the valley's height; Ocean draws the sea.
public static class ValleyCoast
{
    public const float SeaLevel = ValleyShape.LakeLevel;

    public static readonly Vector2 ShoreOrigin = new(225f, -160f);
    public static readonly Vector2 Seaward = new Vector2(0.62f, -0.78f).normalized;
    static Vector2 Along => Vector2.Perpendicular(Seaward);

    public static readonly Vector2 IslandAt = new(350f, -285f);
    public const float IslandRadius = 58f;

    const float HillAt = -72f, HillHalf = 26f, HillHeight = 9f;
    const float CoastStart = -58f, DunesEnd = -40f, BeachTop = -18f;
    const float IslandBeach = 12f;

    public static float Out(Vector2 p)
    {
        float along = Vector2.Dot(p - ShoreOrigin, Along);
        float wander = 16f * (Mathf.PerlinNoise(along * 0.011f + 3.7f, 7.1f) - 0.5f) * 2f
            + 5f * (Mathf.PerlinNoise(along * 0.045f + 1.1f, 2.3f) - 0.5f) * 2f;
        return Vector2.Dot(p - ShoreOrigin, Seaward) + wander;
    }

    public static float InIsland(Vector2 p)
    {
        var q = p - IslandAt;
        float a = Mathf.Atan2(q.y, q.x);
        float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + 5.2f, Mathf.Sin(a) * 1.3f + 5.2f);
        float m = Mathf.PerlinNoise(Mathf.Cos(a) * 3.1f + 1.7f, Mathf.Sin(a) * 3.1f + 8.4f);
        float radius = IslandRadius * (1f + 0.22f * (n - 0.5f) * 2f + 0.08f * (m - 0.5f) * 2f);
        return radius - q.magnitude;
    }

    public static float Weight(Vector2 p) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CoastStart, DunesEnd, Out(p)));

    public static float Shape(Vector2 p, float land)
    {
        float o = Out(p);
        if (o < HillAt - HillHalf * 2.2f) return land;
        float bell = Mathf.Exp(-Mathf.Pow((o - HillAt) / HillHalf, 2f));
        float hill = land + HillHeight * bell * (0.8f + 0.4f * Mathf.PerlinNoise(p.x * 0.02f, p.y * 0.02f));
        float h = Mathf.Lerp(hill, Profile(p, o), Weight(p));
        return Mathf.Max(h, IslandHeight(p));
    }

    static float Profile(Vector2 p, float o)
    {
        if (o <= BeachTop)
        {
            float dune = 2.1f + 0.9f * Mathf.PerlinNoise(p.x * 0.09f + 3f, p.y * 0.09f) * Mathf.InverseLerp(DunesEnd, BeachTop - 6f, o);
            return dune;
        }
        if (o <= 0f) return SeaLevel + (-o) * 0.13f + Mathf.Lerp(0f, 0.25f, Mathf.InverseLerp(BeachTop, BeachTop + 4f, o));
        return SeaLevel - Mathf.Min(o * 0.085f + Mathf.Max(0f, o - 30f) * 0.07f, 26f);
    }

    static float IslandHeight(Vector2 p)
    {
        float s = InIsland(p);
        if (s < -40f) return float.MinValue;
        if (s < 0f) return SeaLevel + s * 0.18f;
        if (s < IslandBeach) return SeaLevel + s * 0.13f;
        float rise = (s - IslandBeach) * 0.07f;
        return SeaLevel + IslandBeach * 0.13f + Mathf.Min(rise, 2.5f) + 3.5f * Mathf.Clamp01(ValleyShape.Fbm(p * 0.03f + new Vector2(9f, 4f), 3) + 0.5f) * Mathf.InverseLerp(IslandBeach, IslandBeach + 18f, s);
    }

    public static bool OnSand(Vector2 p, float ground) =>
        ground >= SeaLevel && ((Out(p) > DunesEnd + 4f && Weight(p) > 0.9f) || (InIsland(p) >= 0f && InIsland(p) < IslandBeach + 2f));

    public static float Lush(Vector2 p) => Mathf.InverseLerp(IslandBeach, IslandBeach + 6f, InIsland(p));

    public static bool OnIsland(Vector2 p) => InIsland(p) >= 0f;

    public static bool NoGrass(Vector2 p)
    {
        float s = InIsland(p);
        return Out(p) > DunesEnd + 6f || (s > -40f && s < IslandBeach) || DistanceToPath(p) < PathHalfWidth + 0.5f;
    }

    public static bool Coastal(Vector2 p) => Out(p) > DunesEnd - 2f || InIsland(p) > -6f;

    public static float Sand(Vector2 p, float ground)
    {
        float o = Out(p);
        float coast = Mathf.InverseLerp(DunesEnd - 6f, DunesEnd + 6f, o) * Mathf.InverseLerp(IslandBeach + 4f, IslandBeach - 2f, InIsland(p));
        float island = Mathf.InverseLerp(IslandBeach + 4f, IslandBeach - 2f, InIsland(p)) * (InIsland(p) > -60f ? 1f : 0f);
        float bed = ground < SeaLevel + 0.2f && (o > 0f || InIsland(p) > -60f) ? 1f : 0f;
        return Mathf.Clamp01(Mathf.Max(Mathf.Max(coast, island), bed));
    }

    public readonly struct Plant
    {
        public readonly Vector2 At;
        public readonly bool Palm;
        public readonly int Seed;

        public Plant(Vector2 at, bool palm, int seed)
        {
            At = at;
            Palm = palm;
            Seed = seed;
        }
    }

    static List<Plant> plants;

    public static IReadOnlyList<Plant> Plants
    {
        get
        {
            if (plants == null) plants = LayPlants();
            return plants;
        }
    }

    static List<Plant> LayPlants()
    {
        var rng = new System.Random(31);
        var list = new List<Plant>();
        LayIslandPlants(list, rng);
        LayDunePalms(list, rng);
        return list;
    }

    static bool Room(List<Plant> list, Vector2 p, float gap)
    {
        foreach (var q in list)
            if ((q.At - p).sqrMagnitude < gap * gap) return false;
        return p.x < 438f && p.y > -362f && DistanceToPath(p) > 3f && ValleyShape.DistanceToVillage(p) > 2.5f;
    }

    static void LayIslandPlants(List<Plant> list, System.Random rng)
    {
        for (int i = 0; i < 4000 && list.Count < 110; i++)
        {
            var p = IslandAt + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * (IslandRadius * 3f);
            bool palm = rng.NextDouble() < 0.7;
            if (InIsland(p) >= (palm ? 3f : 10f) && Room(list, p, palm ? 4.5f : 2.5f)) list.Add(new Plant(p, palm, rng.Next()));
        }
    }

    static void LayDunePalms(List<Plant> list, System.Random rng)
    {
        int stop = list.Count + 60;
        for (int i = 0; i < 3000 && list.Count < stop; i++)
        {
            float along = ((float)rng.NextDouble() - 0.5f) * 560f;
            float o = Mathf.Lerp(DunesEnd - 10f, BeachTop - 2f, (float)rng.NextDouble());
            var p = ShoreOrigin + Along * along + Seaward * o;
            p += Seaward * (o - Out(p));
            if (Out(p) <= BeachTop && ValleyShape.HeightAt(p) >= SeaLevel + 0.5f && Room(list, p, 6f)) list.Add(new Plant(p, true, rng.Next()));
        }
    }

    public const float PathHalfWidth = 1.4f;

    static Vector2[] path = System.Array.Empty<Vector2>();

    public static IReadOnlyList<Vector2> PathLine => path;

    public static void PlanPath(Vector2 yardCorner, Vector2 westLanding, Vector2 eastLanding)
    {
        var toBridge = new List<Vector2> { yardCorner, Vector2.Lerp(yardCorner, westLanding, 0.5f) + new Vector2(0f, 8f), westLanding };
        var beach = ShoreOrigin + Seaward * BeachTop + Along * 10f;
        var crest = ShoreOrigin + Seaward * HillAt + Along * 4f;
        var toBeach = new List<Vector2> { eastLanding, eastLanding + new Vector2(22f, -30f), Vector2.Lerp(eastLanding, crest, 0.5f) + new Vector2(18f, 0f), crest, beach };
        path = Smooth(toBridge, 10).ToArray();
        eastPath = Smooth(toBeach, 12).ToArray();
        plants = null;
    }

    static Vector2[] eastPath = System.Array.Empty<Vector2>();

    public static IReadOnlyList<Vector2> BeachPath => eastPath;

    public static float DistanceToPath(Vector2 p) =>
        Mathf.Min(path.Length > 1 ? ValleyShape.DistanceToCourse(p, path) : float.MaxValue,
            eastPath.Length > 1 ? ValleyShape.DistanceToCourse(p, eastPath) : float.MaxValue);

    public static bool OnPath(Vector2 p) => DistanceToPath(p) < PathHalfWidth && !ValleyShape.InRiver(p);

    static List<Vector2> Smooth(List<Vector2> points, int per)
    {
        var line = new List<Vector2>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[Mathf.Max(0, i - 1)], b = points[i], c = points[i + 1], d = points[Mathf.Min(points.Count - 1, i + 2)];
            for (int k = 0; k < per; k++)
            {
                float t = k / (float)per, t2 = t * t, t3 = t2 * t;
                line.Add(0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3));
            }
        }
        line.Add(points[points.Count - 1]);
        return line;
    }
}
