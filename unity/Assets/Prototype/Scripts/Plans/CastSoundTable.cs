// What a spell's leaving the hands sounds like: its kind of cast.
using System;
using Chem;

public enum CastKind
{
    Bolt,
    Burst,
    Ground,
    Flow,
    Sky,
    Quiet,
}

public static class CastSoundTable
{
    public sealed class Row
    {
        public string Sound;
        public float TargetDb = -20f, MaxSeconds = 1f;
        public float Volume = 1f, PitchJitter = 0.04f;
        public float Reach = MediumReach;
        public string[] Files = Array.Empty<string>();
        public Func<float[]>[] Make = Array.Empty<Func<float[]>>();
        public bool Reverse;
        public Func<float[]> Under;
        public bool Loop;
    }

    public const float MediumReach = 40f;

    const string CC0 = "Assets/Prototype/Audio/CC0";
    public const string
        GatherFire = "gather-fire", GatherWater = "gather-water", GatherEarth = "gather-earth", GatherAir = "gather-air", GatherPlain = "gather-plain",
        Slam = "slam", Fizzle = "fizzle",
        ReleaseBolt = "release-bolt", ReleaseBurst = "release-burst", ReleaseGround = "release-ground",
        ReleaseFlow = "release-flow", ReleaseSky = "release-sky", ReleaseQuiet = "release-quiet";

    const string Rendered = CC0 + "/cast-deep-hum-pulse";

    public static readonly string[] Whoosh = { Rendered + "/whoosh-1.ogg", Rendered + "/whoosh-2.ogg", Rendered + "/whoosh-3.ogg" };

    const float HumDb = -20f, SlamDb = -21.4f, WhooshDb = -27f;

    static readonly string[,] Hums = Named();

    public static Row[] Rows { get; } = Built();

    static string[,] Named()
    {
        var elements = new[] { ElementIds.Fire, ElementIds.Water, ElementIds.Earth, ElementIds.Air, "plain" };
        var hums = new string[elements.Length, World.MaxCharge + 1];
        for (int e = 0; e < elements.Length; e++)
        {
            hums[e, 0] = $"gather-{elements[e]}";
            for (int level = 1; level <= World.MaxCharge; level++) hums[e, level] = $"charge-{elements[e]}-{level}";
        }
        return hums;
    }

    static Row[] Built()
    {
        var rows = new System.Collections.Generic.List<Row>
        {
            new() { Sound = Slam, TargetDb = SlamDb, MaxSeconds = 0.6f, PitchJitter = 0.05f,
                Files = new[] { Rendered + "/slam.ogg" }, Make = new Func<float[]>[] { () => ProceduralCast.Sub() } },
            new() { Sound = Fizzle, TargetDb = -24f, MaxSeconds = 0.45f, Volume = 0.8f, PitchJitter = 0.08f,
                Make = new Func<float[]>[] { () => ProceduralCast.Fizzle() } },
            ReleaseRow(ReleaseBolt, () => ProceduralCast.Snap(61)),
            ReleaseRow(ReleaseBurst, () => ProceduralCast.Heave(71)),
            ReleaseRow(ReleaseGround, () => ProceduralCast.Heave(81)),
            ReleaseRow(ReleaseFlow, () => ProceduralCast.Rush(91)),
            ReleaseRow(ReleaseSky, () => ProceduralCast.Rush(101, 1f)),
            ReleaseRow(ReleaseQuiet, () => ProceduralCast.Rush(111)),
        };
        foreach (var hum in Hums)
            rows.Add(new Row
            {
                Sound = hum,
                TargetDb = HumDb,
                MaxSeconds = 30f,
                PitchJitter = 0f,
                Loop = true,
                Files = new[] { $"{Rendered}/{hum}.ogg" },
                Make = new Func<float[]>[] { () => ProceduralCast.ChargeLoop() },
            });
        return rows.ToArray();
    }

    static Row ReleaseRow(string sound, Func<float[]> make) =>
        new() { Sound = sound, TargetDb = WhooshDb, MaxSeconds = 0.5f, Files = Whoosh, Make = new[] { make } };

    public const float CutSeconds = 0.05f;
    public const float HumIn = 0.12f, HumOut = 0.08f, HumCutOff = 0.015f, HumGlide = 0.15f;
    public const float SecondHumAt = 1.37f;

    static readonly float[] HumPitch = { 1f, 1.1892f, 1.4983f, 1.7818f };
    static readonly float[] HumLoudness = { 0.694f, 0.694f, 0.833f, 1f };
    static readonly float[] ReleasePitch = { 1f, 0.93f, 0.86f };
    static readonly float[] ReleaseLoudness = { 0.8f, 0.9f, 1f };

    public static float HumPitchAt(int stage) => HumPitch[Stage(stage)];
    public static float HumLoudnessAt(int stage) => HumLoudness[Stage(stage)];
    public static float ReleasePitchAt(int charge) => ReleasePitch[Index(charge)];
    public static float ReleaseLoudnessAt(int charge) => ReleaseLoudness[Index(charge)];

    static int Index(int charge) => World.ClampCharge(charge) - 1;
    static int Stage(int stage) => stage <= 0 ? 0 : World.ClampCharge(stage);

    public static string HumOf(string element, int stage) => Hums[element switch
    {
        ElementIds.Fire => 0,
        ElementIds.Water => 1,
        ElementIds.Earth => 2,
        ElementIds.Air => 3,
        _ => 4,
    }, Stage(stage)];

    public static string GatherOf(string element) => HumOf(element, 0);

    public static string ReleaseOf(CastKind kind) => kind switch
    {
        CastKind.Bolt => ReleaseBolt,
        CastKind.Burst => ReleaseBurst,
        CastKind.Ground => ReleaseGround,
        CastKind.Flow => ReleaseFlow,
        CastKind.Sky => ReleaseSky,
        _ => ReleaseQuiet,
    };

    public static CastKind KindOf(SpellDef def, bool fromSky)
    {
        if (def == null) return CastKind.Quiet;
        if (fromSky) return CastKind.Sky;
        if (def.Effect == SpellShapes.TerraformEffect || def.Raise > 0 || def.Dig > 0 || def.Height != null || def.Element is "earth" or "mud") return CastKind.Ground;
        if (def.Element is "air" or "water") return CastKind.Flow;
        if (def.Element is null or "growth" or "thorns" && def.Damage is null or { Length: 0 }) return CastKind.Quiet;
        return def.Shape is SpellShapes.Line or SpellShapes.Point ? CastKind.Bolt : CastKind.Burst;
    }

    public static Row Of(string sound)
    {
        foreach (var row in Rows)
            if (row.Sound == sound) return row;
        return null;
    }
}
