using Chem;
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
        PlanBridge();
        var cabinSpots = new Vector2[Cabins.Length];
        for (int i = 0; i < Cabins.Length; i++) cabinSpots[i] = Cabins[i].At;
        LayVillages();
        var flat = new (Vector2, float)[Villages.Length];
        for (int i = 0; i < Villages.Length; i++) flat[i] = (villageAreas[i].center, villageAreas[i].size.magnitude / 2f + 15f);
        ValleySurroundings.Plan(Centre, LakeCentre, Falls, cabinSpots, flat, cellSize);
        padLevels = new float[Cabins.Length];
        for (int i = 1; i < Cabins.Length; i++) padLevels[i] = Mathf.Max(Unlevelled(Cabins[i].At), 0f);
        for (int i = 0; i < Villages.Length; i++) VillageLevels[i] = Mathf.Max(Unlevelled(villageAreas[i].center), 0f);
        PondLevel = Unlevelled(Falls) - 2f;
    }

    // --- villages (world/villages.json, D12) -------------------------------------------

    /// <summary>The villages, laid out on cells by the core (VillageLayout); empty without a villages file.</summary>
    public static VillagePlan[] Villages { get; private set; } = System.Array.Empty<VillagePlan>();

    /// <summary>The ground each village's pad is levelled to, by Villages' index.</summary>
    public static float[] VillageLevels { get; private set; } = System.Array.Empty<float>();

    // Each village's cells' extent on the ground plane.
    static Rect[] villageAreas = System.Array.Empty<Rect>();

    static void LayVillages()
    {
        var layout = ValleyTerrain.Layout;
        var specs = VillageLayout.Load(ChemGrid.FindWorldDir());
        Villages = new VillagePlan[specs.Count];
        villageAreas = new Rect[specs.Count];
        VillageLevels = new float[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            Villages[i] = VillageLayout.Lay(specs[i], (x, y) => layout.InGrid(x, y) && VillageMayStand(CellWorld(x, y)));
            villageAreas[i] = AreaOf(Villages[i].Bounds);
        }
    }

    // Whether a building or a street may go on a point: not in the lake, the river or the falls,
    // and clear of the yard, the cabins and the court.
    static bool VillageMayStand(Vector2 p) =>
        LakeDepth(p) <= 0f && DistanceToLake(p) > 8f && DistanceToRiver(p) > RiverHalfWidth + 4f && !NearFalls(p)
        && DistanceToYard(p) > 8f && DistanceToCabin(p) > 10f && !InCourt(p, -8f);

    /// <summary>The centre of a world cell on the ground plane (x, z).</summary>
    public static Vector2 CellWorld(int x, int y)
    {
        var (wx, wz) = ValleyTerrain.Layout.CellCenter(x, y);
        return new Vector2(wx, wz);
    }

    /// <summary>A rectangle of cells' extent on the ground plane, to the cells' outer edges.</summary>
    public static Rect AreaOf(CellRect cells)
    {
        float half = ValleyTerrain.Layout.CellSize / 2f;
        var a = CellWorld(cells.X, cells.Y);
        var b = CellWorld(cells.Right - 1, cells.Bottom - 1);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y) - half, Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + half);
    }

    /// <summary>The distance from a point to the nearest village's cells (0 inside one).</summary>
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

    // --- the ball court -------------------------------------------------------------

    /// <summary>The middle of the ball court east of the yard (x, z), where the ball starts (ValleyBuilder.BuildCourt).</summary>
    public static readonly Vector2 CourtCentre = new Vector2(67f, 5f);

    /// <summary>The court inside its rails (m): its length, west to east, and its width, south to north.</summary>
    public const float CourtLength = 40f, CourtWidth = 26f;

    /// <summary>Whether a point is on the court's floor, at least <paramref name="margin"/> metres inside its rails (a negative margin reaches past them).</summary>
    public static bool InCourt(Vector2 p, float margin = 0f) =>
        Mathf.Abs(p.x - CourtCentre.x) < CourtLength / 2f - margin && Mathf.Abs(p.y - CourtCentre.y) < CourtWidth / 2f - margin;

    /// <summary>The distance from a point to the nearest cabin.</summary>
    public static float DistanceToCabin(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var site in Cabins) best = Mathf.Min(best, (p - site.At).magnitude);
        return best;
    }

    /// <summary>World height (metres above the yard) at a point on the ground plane: the ground, the river's channel cut in.</summary>
    public static float HeightAt(Vector2 p) => BankHeightAt(p) - ChannelDepth(p);

    /// <summary>
    /// The ground's height before the river's channel is cut into it: over the river's squares, where
    /// its water stands (ValleyChemistry draws it there).
    /// </summary>
    public static float BankHeightAt(Vector2 p)
    {
        float h = Unlevelled(p);
        // A level pad for each cabin, easing back into the meadow.
        for (int i = 0; i < Cabins.Length; i++)
        {
            float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, (p - Cabins[i].At).magnitude));
            h = Mathf.Lerp(padLevels[i], h, pad);
        }
        // And one for each village, out to a few metres past its cells.
        for (int i = 0; i < villageAreas.Length; i++)
        {
            float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 14f, DistanceToRect(p, villageAreas[i])));
            h = Mathf.Lerp(VillageLevels[i], h, pad);
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

        float meadow = 2.2f * Fbm(p * 0.012f, 3) * rise;
        // The valley floor climbs gently north into the foothills, so the falls stand high and the
        // river runs down from them to the lake.
        float foothills = 45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 300f, (p - Centre).y));
        // Past the valley floor the generated land rises (ValleySurroundings).
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

    /// <summary>
    /// The river's course, from the pond at the foot of the falls under the northern mountain down into the
    /// lake's north-east shore, east of the yard and the court (world x, z): the points it bends through.
    /// </summary>
    public static readonly Vector2[] River =
    {
        Falls, new(150f, 250f), new(128f, 170f), new(142f, 95f), new(120f, 30f), new(100f, -45f),
    };

    /// <summary>
    /// The river's middle line: a smooth curve through the River points (Catmull-Rom), so it bends
    /// rather than turning a sharp corner at each point.
    /// </summary>
    static readonly Vector2[] RiverCourse = Smooth(River, 16);

    /// <summary>How far from its middle the river's squares reach (metres): six squares across.</summary>
    public const float RiverHalfWidth = 4.5f;

    /// <summary>How deep the river's channel is cut along its middle and under the pond (metres), under the river square's own step.</summary>
    public const float RiverBedDepth = 2f;

    // The stream on the cliff top and the pond's rim keep the old river's narrow reach. The channel
    // is its full depth this far either side of the middle line before the banks rise.
    const float StreamHalfWidth = 1.6f, ChannelCore = 0.75f;

    // Where the river's squares can be: a quick test before measuring the distance to its course.
    static readonly Rect RiverBounds = BoundsOf(RiverCourse, RiverHalfWidth);

    /// <summary>Whether a point is one of the river's squares: the river, the stream that feeds the falls, or the pond.</summary>
    public static bool InRiver(Vector2 p) =>
        (RiverBounds.Contains(p) && DistanceToCourse(p, RiverCourse) < RiverHalfWidth)
        || DistanceToCourse(p, Stream) < StreamHalfWidth || (p - Falls).magnitude < PondRadius + StreamHalfWidth;

    /// <summary>
    /// How deep the river's channel is cut into the ground at a point (metres): its full depth along the
    /// middle and over the pond, the banks easing up to nothing at the edge of its squares, so the
    /// wizard can walk out.
    /// </summary>
    public static float ChannelDepth(Vector2 p)
    {
        float depth = RiverBounds.Contains(p) ? Channel(DistanceToCourse(p, RiverCourse), ChannelCore, RiverHalfWidth) : 0f;
        float pond = (p - Falls).magnitude;
        if (pond < PondRadius + StreamHalfWidth) depth = Mathf.Max(depth, Channel(pond, PondRadius - 3f, PondRadius + StreamHalfWidth));
        return depth;
    }

    static float Channel(float d, float core, float edge) =>
        RiverBedDepth * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, edge, d)));

    /// <summary>Distance from a point to the river's middle line (the stream and the pond's rim count too).</summary>
    public static float DistanceToRiver(Vector2 p)
    {
        float best = Mathf.Min(DistanceToCourse(p, Stream), Mathf.Max(0f, (p - Falls).magnitude - PondRadius));
        return Mathf.Min(best, DistanceToCourse(p, RiverCourse));
    }

    internal static float DistanceToCourse(Vector2 p, Vector2[] course) => (p - NearestOnCourse(p, course, out _)).magnitude;

    // The point of a course nearest a point, and the course's direction there.
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

    // A Catmull-Rom curve through the points, each stretch between two of them in <paramref name="steps"/> straight pieces.
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

    // --- the bridge -------------------------------------------------------------------

    /// <summary>
    /// The middle of the bridge over the river (x, z): where the river passes nearest the cabin on its
    /// east bank, whose door faces back across it to the court and the yard. Set by Plan.
    /// </summary>
    public static Vector2 Bridge { get; private set; }

    /// <summary>The way across the bridge, from the west bank to the east (a unit direction on the ground plane), square to the river.</summary>
    public static Vector2 BridgeAcross { get; private set; }

    /// <summary>How long each ramp down from the bridge's deck is (metres).</summary>
    public const float BridgeRamp = 6.5f;

    /// <summary>How far the bridge's deck reaches either side of its middle (metres): over the river and a step onto each bank.</summary>
    public const float BridgeSpan = RiverHalfWidth + 1.5f;

    // The bridge's line, from the foot of one ramp to the other.
    static Vector2[] bridgeLine;

    static void PlanBridge()
    {
        var cabin = Cabins[2].At;
        Bridge = NearestOnCourse(cabin, RiverCourse, out var along);
        var across = Vector2.Perpendicular(along).normalized; // which side, the next line settles
        BridgeAcross = Vector2.Dot(across, cabin - Bridge) >= 0f ? across : -across;
        float reach = BridgeSpan + BridgeRamp;
        bridgeLine = new[] { Bridge - BridgeAcross * reach, Bridge + BridgeAcross * reach };
    }

    /// <summary>Whether a point is within <paramref name="margin"/> of the bridge's line.</summary>
    public static bool NearBridge(Vector2 p, float margin) => bridgeLine != null && DistanceToCourse(p, bridgeLine) < margin;

    /// <summary>Whether a point is within <paramref name="margin"/> of the climbing tower's round (ValleyTower).</summary>
    public static bool NearTower(Vector2 p, float margin) => (p - ValleyTower.Site).magnitude < ValleyTower.Reach + margin;

    /// <summary>Kept clear of trees and rocks: near the yard, the cabins' pads, the lake, the river's banks and the bridge.</summary>
    public static bool KeepClear(Vector2 p, float margin) =>
        DistanceToYard(p) < margin || KeepClearOfCabinAndLake(p) || DistanceToRiver(p) < RiverHalfWidth + 3f || NearBridge(p, 3f);

    /// <summary>Kept clear of grass and plants: the cabins' pads, the lake, the river, the falls (its basin, cliff and shelf) and the villages.</summary>
    public static bool KeepClearOfCabinAndLake(Vector2 p) =>
        DistanceToCabin(p) < 12f || LakeDepth(p) > 0.05f || NearFalls(p) || InRiver(p) || DistanceToVillage(p) < 1f;

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

    /// <summary>
    /// Grass, moss, rock, snow, sand, soil (ValleyBuilder.LayerNames order) at a point with this slope,
    /// summing to 1: the valley floor's own mix, blending into the generated land's surfaces past it.
    /// </summary>
    public static float[] LayerWeights(Vector2 w, float slope)
    {
        var floor = FloorLayerWeights(w, slope);
        float t = ValleySurroundings.Mask(w);
        if (t <= 0f) return floor;
        var land = ValleySurroundings.LayerWeights(w, HeightAt(w), slope);
        for (int k = 0; k < floor.Length; k++) floor[k] = Mathf.Lerp(floor[k], land[k], t);
        return floor;
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
