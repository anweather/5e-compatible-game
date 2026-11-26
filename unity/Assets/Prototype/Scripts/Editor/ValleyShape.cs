using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The valley's shape as functions of a point on the ground plane (x, z in
// metres): the flat meadow where the old yard was, the lake to the south, the
// river and the falls (the river's channel cut deep into the ground), the
// bridge over the river, level pads for the cabins, and which terrain layers
// each point gets. Past the valley floor the land is generated
// (ValleySurroundings: Punchy's biomes, mountains to the north). ValleyBuilder
// and ValleyTerrain sample it; it never touches assets. Set by Plan, which also
// lays out the villages (world/villages.json) and levels a pad for each.
public static class ValleyShape
{
    public const float LakeLevel = -0.6f;

    const float FlatAroundYard = 6f;

    static Rect yard;

    public static Vector2 Centre { get; private set; }
    public static Vector2 LakeCentre { get; private set; }
    public static Vector2 Cabin => Cabins[0].At;

    public readonly struct CabinSite
    {
        public readonly Vector2 At, Door;
        public CabinSite(Vector2 at, Vector2 door)
        {
            At = at;
            Door = door.normalized;
        }
    }

    public static CabinSite[] Cabins { get; private set; }

    static float[] padLevels;

    static CabinSite[] ReadCabins() => WorldBuildings.Load(ChemGrid.Folders.World).FileRows
        .Where(row => row["site"] != null)
        .Select(row => new CabinSite(Site(row["site"]["metres"]), Site(row["site"]["door"])))
        .ToArray();

    static Vector2 Site(JToken pair) => new((float)pair[0], (float)pair[1]);

    public static void Plan(int width, int height, float cellSize)
    {
        float half = cellSize / 2f;
        yard = Rect.MinMaxRect(-half, -half, (width - 1) * cellSize + half, (height - 1) * cellSize + half);
        Centre = yard.center;
        LakeCentre = Centre + new Vector2(25f, -95f);
        Cabins = ReadCabins();
        PlanBridge();
        ValleyCoast.PlanPath(new Vector2(yard.xMax + 3f, yard.yMax - 4f), bridgeLine[0], bridgeLine[1]);
        var cabinSpots = new Vector2[Cabins.Length];
        for (int i = 0; i < Cabins.Length; i++) cabinSpots[i] = Cabins[i].At;
        LayVillages();
        var flat = new (Vector2, float)[Villages.Length];
        for (int i = 0; i < Villages.Length; i++) flat[i] = (villageAreas[i].center, villageAreas[i].size.magnitude / 2f + 15f);
        ValleySurroundings.Plan(Centre, LakeCentre, Falls, cabinSpots, flat, cellSize);
        padLevels = new float[Cabins.Length];
        for (int i = 1; i < Cabins.Length; i++) padLevels[i] = Mathf.Max(Unlevelled(Cabins[i].At), 0f);
        for (int i = 0; i < Villages.Length; i++)
        {
            var at = villageAreas[i].center;
            VillageLevels[i] = Mathf.Max(ValleyCoast.Shape(at, Unlevelled(at)), 0f);
        }
        PondLevel = Unlevelled(Falls) - 2f;
    }

    public static VillagePlan[] Villages { get; private set; } = System.Array.Empty<VillagePlan>();

    public static float[] VillageLevels { get; private set; } = System.Array.Empty<float>();

    static Rect[] villageAreas = System.Array.Empty<Rect>();

    static void LayVillages()
    {
        var layout = EditorGround.Layout;
        var specs = VillageLayout.Load(ChemGrid.Folders.World);
        Villages = new VillagePlan[specs.Count];
        villageAreas = new Rect[specs.Count];
        VillageLevels = new float[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            Villages[i] = VillageLayout.Lay(specs[i], (x, y) => layout.InGrid(x, y) && VillageMayStand(CellWorld(x, y)));
            villageAreas[i] = AreaOf(Villages[i].Bounds);
        }
    }

    static bool VillageMayStand(Vector2 p) =>
        LakeDepth(p) <= 0f && DistanceToLake(p) > 8f && DistanceToRiver(p) > RiverHalfWidth + 4f && !NearFalls(p)
        && DistanceToYard(p) > 8f && DistanceToCabin(p) > 10f && !InCourt(p, -8f)
        && (ValleyCoast.InIsland(p) > 13f || (ValleyCoast.Out(p) < -95f && ValleyCoast.DistanceToPath(p) > 4f));

    public static Vector2 CellWorld(int x, int y)
    {
        var (wx, wz) = EditorGround.Layout.CellCenter(x, y);
        return new Vector2(wx, wz);
    }

    public static Rect AreaOf(CellRect cells)
    {
        float half = EditorGround.Layout.CellSize / 2f;
        var a = CellWorld(cells.X, cells.Y);
        var b = CellWorld(cells.Right - 1, cells.Bottom - 1);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y) - half, Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + half);
    }

    public static float DistanceToVillage(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var r in villageAreas) best = Mathf.Min(best, DistanceToRect(p, r));
        return best;
    }

    static float DistanceToRect(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    public static readonly Vector2 Falls = new Vector2(138f, 292f);

    public const float PondRadius = 10f;

    public const float CliffNorth = 12f, CliffHeight = 36f;

    public static float PondLevel { get; private set; }

    public static float CliffTop => PondLevel + CliffHeight;

    public static readonly Vector2[] Stream = { Falls + new Vector2(-4f, 34f), Falls + new Vector2(0f, CliffNorth + 2.2f) };

    static float FallsShape(Vector2 p, float h)
    {
        var q = p - Falls;
        float d = q.magnitude;
        if (d < 18f) h = Mathf.Lerp(PondLevel, Mathf.Min(h, PondLevel + 6f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PondRadius, 18f, d)));
        float side = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 32f, Mathf.Abs(q.x)));
        float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CliffNorth - 0.5f, CliffNorth + 0.5f, q.y)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 48f, q.y)));
        h = Mathf.Max(h, Mathf.Lerp(h, CliffTop, side * shelf));
        return Mathf.Max(h, ScreeRamp(q));
    }

    public static readonly Vector2 RampFoot = new(36f, -14f), RampTop = new(24f, 34f);
    const float RampHalfWidth = 3.5f;

    static float ScreeRamp(Vector2 q)
    {
        var along = RampTop - RampFoot;
        float t = Mathf.Clamp01(Vector2.Dot(q - RampFoot, along) / along.sqrMagnitude);
        float off = (q - (RampFoot + along * t)).magnitude;
        if (off > RampHalfWidth + 7f) return float.MinValue;
        float crown = Mathf.Lerp(PondLevel + 0.5f, CliffTop + 0.5f, t);
        return crown - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RampHalfWidth, RampHalfWidth + 7f, off)) * 30f;
    }

    public static bool InPond(Vector2 p) => (p - Falls).magnitude < PondRadius;

    public static Quaternion CabinRotation(CabinSite site) => Quaternion.LookRotation(new Vector3(-site.Door.x, 0f, -site.Door.y));

    public static readonly Vector2 CabinWindow = new Vector2(-1.6f, -2.45f);

    public static bool OnCabinWindow(Vector2 p)
    {
        foreach (var site in Cabins)
        {
            var w = CabinRotation(site) * new Vector3(CabinWindow.x, 0f, CabinWindow.y);
            if (Mathf.Abs(p.x - site.At.x - w.x) <= 0.75f && Mathf.Abs(p.y - site.At.y - w.z) <= 0.75f) return true;
        }
        return false;
    }

    public static bool InCabin(Vector2 p)
    {
        foreach (var site in Cabins)
        {
            var q = Quaternion.Inverse(CabinRotation(site)) * new Vector3(p.x - site.At.x, 0f, p.y - site.At.y);
            if (Mathf.Abs(q.x) <= 3.3f && Mathf.Abs(q.z) <= 2.55f) return true;
        }
        return false;
    }

    public static readonly Vector2 CourtCentre = new Vector2(67f, 5f);

    public const float CourtLength = 40f, CourtWidth = 26f;

    public static bool InCourt(Vector2 p, float margin = 0f) =>
        Mathf.Abs(p.x - CourtCentre.x) < CourtLength / 2f - margin && Mathf.Abs(p.y - CourtCentre.y) < CourtWidth / 2f - margin;

    public static float DistanceToCabin(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var site in Cabins) best = Mathf.Min(best, (p - site.At).magnitude);
        return best;
    }

    public static float HeightAt(Vector2 p) => BankHeightAt(p) - ChannelDepth(p);

    public static float BankHeightAt(Vector2 p)
    {
        float h = ValleyCoast.Shape(p, Unlevelled(p));
        for (int i = 0; i < Cabins.Length; i++)
        {
            float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, (p - Cabins[i].At).magnitude));
            h = Mathf.Lerp(padLevels[i], h, pad);
        }
        for (int i = 0; i < villageAreas.Length; i++)
        {
            float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 14f, DistanceToRect(p, villageAreas[i])));
            h = Mathf.Lerp(VillageLevels[i], h, pad);
        }
        if (DistanceToYard(p) < 45f || (p - Cabin).magnitude < 20f) h = Mathf.Max(h, 0f);
        return (p - Falls).sqrMagnitude < 70f * 70f ? FallsShape(p, h) : h;
    }

    static float Unlevelled(Vector2 p)
    {
        float d = DistanceToYard(p);
        if (d <= FlatAroundYard) return 0f;
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatAroundYard, 30f, d));

        float meadow = 2.2f * Fbm(p * 0.012f, 3) * rise;
        float foothills = 45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 300f, (p - Centre).y));
        float t = ValleySurroundings.Mask(p);
        float land = t > 0f ? t * ValleySurroundings.Height(p) : 0f;

        float lake = LakeDepth(p);
        return Mathf.Max(meadow + foothills + land, 0f) * rise - lake;
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

    public static readonly Vector2[] River =
    {
        Falls, new(150f, 250f), new(128f, 170f), new(142f, 95f), new(120f, 30f), new(100f, -45f),
    };

    static readonly Vector2[] RiverCourse = Smooth(River, 16);

    public static Vector2[] RiverLine => (Vector2[])RiverCourse.Clone();

    public const float RiverHalfWidth = 4.5f;

    public const float RiverBedDepth = 2f;

    const float StreamHalfWidth = 2.6f, ChannelCore = 0.75f;

    static readonly Rect RiverBounds = BoundsOf(RiverCourse, RiverHalfWidth);

    public static bool InRiver(Vector2 p) =>
        (RiverBounds.Contains(p) && DistanceToCourse(p, RiverCourse) < RiverHalfWidth)
        || DistanceToCourse(p, Stream) < StreamHalfWidth || (p - Falls).magnitude < PondRadius + StreamHalfWidth;

    public static float ChannelDepth(Vector2 p)
    {
        float depth = RiverBounds.Contains(p) ? Channel(DistanceToCourse(p, RiverCourse), ChannelCore, RiverHalfWidth) : 0f;
        float pond = (p - Falls).magnitude;
        if (pond < PondRadius + StreamHalfWidth) depth = Mathf.Max(depth, Channel(pond, PondRadius - 3f, PondRadius + StreamHalfWidth));
        return depth;
    }

    static float Channel(float d, float core, float edge) =>
        RiverBedDepth * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, edge, d)));

    public static float DistanceToRiver(Vector2 p)
    {
        float best = Mathf.Min(DistanceToCourse(p, Stream), Mathf.Max(0f, (p - Falls).magnitude - PondRadius));
        return Mathf.Min(best, DistanceToCourse(p, RiverCourse));
    }

    internal static float DistanceToCourse(Vector2 p, Vector2[] course) => (p - NearestOnCourse(p, course, out _)).magnitude;

    static Vector2 NearestOnCourse(Vector2 p, Vector2[] course, out Vector2 along)
    {
        float best = float.MaxValue;
        var nearest = course[0];
        along = course[1] - course[0];
        for (int i = 0; i + 1 < course.Length; i++)
        {
            var a = course[i];
            var ab = course[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            var q = a + ab * t;
            float d = (p - q).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            nearest = q;
            along = ab;
        }
        return nearest;
    }

    static Vector2[] Smooth(Vector2[] points, int steps)
    {
        int last = points.Length - 1;
        var curve = new Vector2[last * steps + 1];
        for (int i = 0; i < last; i++)
        {
            Vector2 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(i + 2, last)];
            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                curve[i * steps + k] = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
            }
        }
        curve[last * steps] = points[last];
        return curve;
    }

    static Rect BoundsOf(Vector2[] points, float margin)
    {
        Vector2 min = points[0], max = points[0];
        foreach (var q in points)
        {
            min = Vector2.Min(min, q);
            max = Vector2.Max(max, q);
        }
        return Rect.MinMaxRect(min.x - margin, min.y - margin, max.x + margin, max.y + margin);
    }

    public static Vector2 Bridge { get; private set; }

    public static Vector2 BridgeAcross { get; private set; }

    public const float BridgeRamp = 6.5f;

    public const float BridgeSpan = RiverHalfWidth + 1.5f;

    static Vector2[] bridgeLine;

    static void PlanBridge()
    {
        var cabin = Cabins[2].At;
        Bridge = NearestOnCourse(cabin, RiverCourse, out var along);
        var across = Vector2.Perpendicular(along).normalized;
        BridgeAcross = Vector2.Dot(across, cabin - Bridge) >= 0f ? across : -across;
        float reach = BridgeSpan + BridgeRamp;
        bridgeLine = new[] { Bridge - BridgeAcross * reach, Bridge + BridgeAcross * reach };
    }

    public static bool NearBridge(Vector2 p, float margin) => bridgeLine != null && DistanceToCourse(p, bridgeLine) < margin;

    public static bool NearTower(Vector2 p, float margin) => (p - ValleyTower.Site).magnitude < ValleyTower.Reach + margin;

    public static bool KeepClear(Vector2 p, float margin) =>
        DistanceToYard(p) < margin || KeepClearOfCabinAndLake(p) || DistanceToRiver(p) < RiverHalfWidth + 3f || NearBridge(p, 3f);

    public static bool KeepClearOfCabinAndLake(Vector2 p) =>
        DistanceToCabin(p) < 12f || LakeDepth(p) > 0.05f || NearFalls(p) || InRiver(p) || DistanceToVillage(p) < 1f || ValleyCoast.NoGrass(p);

    public static bool NearFalls(Vector2 p)
    {
        var q = p - Falls;
        return q.magnitude < 19f || (Mathf.Abs(q.x) < 27f && q.y > 0f && q.y < CliffNorth + 6f) || DistanceToCourse(q, new[] { RampFoot, RampTop }) < RampHalfWidth + 3f;
    }

    public static float Fbm(Vector2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < octaves; i++, p *= 2.03f, amp *= 0.5f) sum += amp * (Mathf.PerlinNoise(p.x, p.y) * 2f - 1f);
        return sum;
    }

    public static float[] LayerWeights(Vector2 w, float slope)
    {
        var floor = FloorLayerWeights(w, slope);
        float t = ValleySurroundings.Mask(w);
        if (t > 0f)
        {
            var land = ValleySurroundings.LayerWeights(w, HeightAt(w), slope);
            for (int k = 0; k < floor.Length; k++) floor[k] = Mathf.Lerp(floor[k], land[k], t);
        }
        return WithSand(WithIsland(floor, ValleyCoast.Lush(w)), ValleyCoast.Sand(w, HeightAt(w)));
    }

    static float[] WithIsland(float[] mix, float lush)
    {
        if (lush <= 0f) return mix;
        for (int k = 0; k < mix.Length; k++) mix[k] *= 1f - lush;
        mix[0] += lush * 0.45f;
        mix[1] += lush * 0.55f;
        return mix;
    }

    static float[] WithSand(float[] mix, float sand)
    {
        if (sand <= 0f) return mix;
        for (int k = 0; k < mix.Length; k++) mix[k] *= 1f - sand;
        mix[4] += sand;
        return mix;
    }

    static float[] FloorLayerWeights(Vector2 w, float slope)
    {
        float h = HeightAt(w);
        float shore = Mathf.InverseLerp(1.5f, 0f, Mathf.Abs(h - LakeLevel)) * Mathf.InverseLerp(30f, 0f, DistanceToLake(w));
        float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 38f, slope));
        float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 175f, h + 20f * Fbm(w * 0.02f, 2))) * (1f - rock * 0.6f);
        float moss = Mathf.Clamp01(0.5f + Fbm(w * 0.03f, 3)) * (1f - rock);
        float soil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 120f, h)) * (1f - rock) * (1f - snow);
        float sand = shore;
        float grass = Mathf.Max(0f, 1f - rock - snow - moss * 0.6f - sand - soil);
        var mix = new[] { grass, moss * 0.6f, rock, snow, sand, soil };
        float total = 0f;
        foreach (var x in mix) total += x;
        if (total > 0f)
            for (int k = 0; k < mix.Length; k++) mix[k] /= total;
        return mix;
    }
}
