using System;
using Chem;

/// <summary>What a spell's leaving the hands sounds like: its kind of cast.</summary>
public enum CastKind
{
    /// <summary>A shot along a line or at a point: fire bolt, lightning, sand blast.</summary>
    Bolt,
    /// <summary>A lobbed blast: fireball, steam, forge.</summary>
    Burst,
    /// <summary>The ground moved: earth raised, walls, the boulder.</summary>
    Ground,
    /// <summary>Wind or water pushed out: gust, wave, whirlwind.</summary>
    Flow,
    /// <summary>Called from above: a falling star, a storm.</summary>
    Sky,
    /// <summary>Quiet magic: light, shapeshift, hasten time.</summary>
    Quiet,
}

// The casting sounds in one table (docs/SPELL_COMBOS.md, "Sound"): for each, the
// clips it can be (Files, in git's CC0 folder, so every machine has them; else Make,
// made in code), how the bank masters them, how loud it plays and how far it carries,
// plus which one goes with which element, stage and kind of cast. The hums, the slam
// and the whoosh are rendered from the preset Andrew picked (J-044, cast-deep-hum-pulse,
// the sound-design skill's render_game.py). SoundBankBuilder masters these rows after
// its own recipes; a row a bank lacks is made from Make when it's first played
// (GameSounds). The sound pass can add its own rows here.
public static class CastSoundTable
{
    public sealed class Row
    {
        public string Sound;
        /// <summary>The mastering's loudness (dB, SoundMastering) and the most it may run (s).</summary>
        public float TargetDb = -20f, MaxSeconds = 1f;
        /// <summary>How loud it plays (0 to 1), before the Effects setting, and how much its pitch varies.</summary>
        public float Volume = 1f, PitchJitter = 0.04f;
        /// <summary>How far it carries (m).</summary>
        public float Reach = MediumReach;
        /// <summary>Recordings, one variation each. Any missing is skipped.</summary>
        public string[] Files = Array.Empty<string>();
        /// <summary>Made in code: the variations when there are no Files, and the stand-in without a bank.</summary>
        public Func<float[]>[] Make = Array.Empty<Func<float[]>>();
        /// <summary>Played backwards, so it's drawn in rather than let out.</summary>
        public bool Reverse;
        /// <summary>Made in code and mixed under each recording.</summary>
        public Func<float[]> Under;
        public bool Loop;
    }

    /// <summary>A cast carries about this far (the sound pass's "medium").</summary>
    public const float MediumReach = 40f;

    const string CC0 = "Assets/Prototype/Audio/CC0";
    public const string
        GatherFire = "gather-fire", GatherWater = "gather-water", GatherEarth = "gather-earth", GatherAir = "gather-air", GatherPlain = "gather-plain",
        Slam = "slam", Fizzle = "fizzle",
        ReleaseBolt = "release-bolt", ReleaseBurst = "release-burst", ReleaseGround = "release-ground",
        ReleaseFlow = "release-flow", ReleaseSky = "release-sky", ReleaseQuiet = "release-quiet";

    const string Rendered = CC0 + "/cast-deep-hum-pulse";

    /// <summary>The rendered whoosh, each its own noise: every release, and a cast from the radial or the mouse.</summary>
    public static readonly string[] Whoosh = { Rendered + "/whoosh-1.ogg", Rendered + "/whoosh-2.ogg", Rendered + "/whoosh-3.ogg" };

    // The mix: a hum stays under the landing (Explosion is -12, a splash -18 in SoundBankBuilder) and two
    // together with the slam don't clip. The numbers are measured: docs/SPELL_COMBOS.md, "Sound".
    const float HumDb = -20f, SlamDb = -21.4f, WhooshDb = -29f;

    // Each loop as an element's hum at a stage of a cast: 0 is gathering, then the charge levels.
    static readonly string[,] Hums = Named();

    public static readonly Row[] Rows = Built();

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
            // One whoosh for now (J-040); the kinds stay so the sound pass can give each its own.
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
                Sound = hum, TargetDb = HumDb, MaxSeconds = 30f, PitchJitter = 0f, Loop = true,
                Files = new[] { $"{Rendered}/{hum}.ogg" }, Make = new Func<float[]>[] { () => ProceduralCast.ChargeLoop() },
            });
        return rows.ToArray();
    }

    static Row ReleaseRow(string sound, Func<float[]> make) =>
        new() { Sound = sound, TargetDb = WhooshDb, MaxSeconds = 0.5f, Files = Whoosh, Make = new[] { make } };

    /// <summary>How fast a sound in a wizard's hands fades when another takes its place (s): quick, but no click.</summary>
    public const float CutSeconds = 0.05f;
    /// <summary>The hum's swell in, its fade out when it just stops, how fast the whoosh cuts it off, and how long a rise takes it (s).</summary>
    public const float HumIn = 0.12f, HumOut = 0.08f, HumCutOff = 0.015f, HumGlide = 0.15f;
    /// <summary>Where in its loop the second hand's hum starts (s), so a pair of one element isn't one voice twice over.</summary>
    public const float SecondHumAt = 1.37f;

    // By stage: gathering, then charge levels 1 to World.MaxCharge. Combining rises 3 semitones, the
    // steps 4 and 7 above that, each 1.2 times louder, loudest at 1. Each stage's loop is rendered at
    // its pitch, so these say where the glide into it starts, and the voice ends at a pitch of 1.
    static readonly float[] HumPitch = { 1f, 1.1892f, 1.4983f, 1.7818f };
    static readonly float[] HumLoudness = { 0.694f, 0.694f, 0.833f, 1f };
    static readonly float[] ReleasePitch = { 1f, 0.93f, 0.86f };
    static readonly float[] ReleaseLoudness = { 0.8f, 0.9f, 1f };

    /// <summary>The pitch a stage's hum is rendered at, against the gathering's.</summary>
    public static float HumPitchAt(int stage) => HumPitch[Stage(stage)];
    public static float HumLoudnessAt(int stage) => HumLoudness[Stage(stage)];
    /// <summary>A charged release is heavier: lower and louder.</summary>
    public static float ReleasePitchAt(int charge) => ReleasePitch[Index(charge)];
    public static float ReleaseLoudnessAt(int charge) => ReleaseLoudness[Index(charge)];

    static int Index(int charge) => World.ClampCharge(charge) - 1;
    static int Stage(int stage) => stage <= 0 ? 0 : World.ClampCharge(stage);

    /// <summary>An element's hum at a stage: 0 gathering, then the charge level. A cast with no element (the d-pad's) has its own.</summary>
    public static string HumOf(string element, int stage) => Hums[element switch
    {
        ElementIds.Fire => 0,
        ElementIds.Water => 1,
        ElementIds.Earth => 2,
        ElementIds.Air => 3,
        _ => 4,
    }, Stage(stage)];

    /// <summary>The sound of an element gathering in a hand.</summary>
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

    /// <summary>The kind of a spell, from its rules; <paramref name="fromSky"/> is its look's (a meteor, a storm).</summary>
    public static CastKind KindOf(SpellDef def, bool fromSky)
    {
        if (def == null) return CastKind.Quiet;
        if (fromSky) return CastKind.Sky;
        if (def.Effect == SpellShapes.TerraformEffect || def.Raise > 0 || def.Dig > 0 || def.Height != null || def.Element is "earth" or "mud") return CastKind.Ground;
        if (def.Element is "air" or "water") return CastKind.Flow;
        if (def.Element is null or "growth" or "thorns" && def.Damage is null or { Length: 0 }) return CastKind.Quiet;
        return def.Shape is SpellShapes.Line or SpellShapes.Point ? CastKind.Bolt : CastKind.Burst;
    }

    /// <summary>The row of a sound, or null. No garbage: a short walk down the table.</summary>
    public static Row Of(string sound)
    {
        foreach (var row in Rows)
            if (row.Sound == sound) return row;
        return null;
    }
}
