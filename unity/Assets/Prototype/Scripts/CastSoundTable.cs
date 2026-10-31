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
// clips it can be (Files, CC0 only, so every machine has them; else Make, made in
// code), how the bank masters them, how loud it plays and how far it carries, plus
// which one goes with which element and kind of cast and what each charge level
// does to them. SoundBankBuilder masters these rows after its own recipes; a row a
// bank lacks is made from Make when it's first played (GameSounds). The sound pass
// can add its own rows here.
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
        GatherFire = "gather-fire", GatherWater = "gather-water", GatherEarth = "gather-earth", GatherAir = "gather-air", GatherPlain = "gather",
        Slam = "slam", ChargeLoop = "charge-loop", ChargeLevel = "charge-level", Fizzle = "fizzle",
        ReleaseBolt = "release-bolt", ReleaseBurst = "release-burst", ReleaseGround = "release-ground",
        ReleaseFlow = "release-flow", ReleaseSky = "release-sky", ReleaseQuiet = "release-quiet";

    // The mix, quiet to loud: gathering stays under the hum, the slam and the release punch through,
    // and all of it sits under the landing (Explosion is -12, a splash -18 in SoundBankBuilder).
    public static readonly Row[] Rows =
    {
        new() { Sound = GatherFire, TargetDb = -24f, MaxSeconds = 0.5f, Volume = 0.8f, Reverse = true,
            Make = new Func<float[]>[] { () => ProceduralFire.Burst(4), () => ProceduralFire.Burst(5) } },
        new() { Sound = GatherWater, TargetDb = -24f, MaxSeconds = 0.5f, Volume = 0.8f, Reverse = true,
            Files = new[] { CC0 + "/rubberduck/splash_01.ogg", CC0 + "/rubberduck/splash_02.ogg" },
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(21) } },
        new() { Sound = GatherEarth, TargetDb = -23f, MaxSeconds = 0.5f, Volume = 0.8f,
            Make = new Func<float[]>[] { () => ProceduralCast.Gravel(31), () => ProceduralCast.Gravel(32) } },
        new() { Sound = GatherAir, TargetDb = -25f, MaxSeconds = 0.5f, Volume = 0.8f, Reverse = true,
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(41), () => ProceduralCast.Rush(42) } },
        new() { Sound = GatherPlain, TargetDb = -26f, MaxSeconds = 0.6f, Volume = 0.8f,
            Files = new[] { CC0 + "/jaggedstone/magical_6_0.ogg" },
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(51) } },
        new() { Sound = Slam, TargetDb = -15f, MaxSeconds = 0.3f, PitchJitter = 0.05f,
            Files = new[] { CC0 + "/kenney/impactWood_heavy_000.ogg", CC0 + "/kenney/impactWood_heavy_001.ogg", CC0 + "/kenney/impactWood_heavy_002.ogg", CC0 + "/kenney/impactWood_heavy_003.ogg", CC0 + "/kenney/impactWood_heavy_004.ogg" },
            Under = () => ProceduralCast.Sub(), Make = new Func<float[]>[] { () => ProceduralCast.Sub() } },
        new() { Sound = ChargeLoop, TargetDb = -25f, MaxSeconds = 30f, Volume = 0.8f, PitchJitter = 0f, Loop = true,
            Make = new Func<float[]>[] { () => ProceduralCast.ChargeLoop() } },
        new() { Sound = ChargeLevel, TargetDb = -21f, MaxSeconds = 0.6f, PitchJitter = 0f,
            Make = new Func<float[]>[] { () => ProceduralCast.Ping() } },
        new() { Sound = Fizzle, TargetDb = -24f, MaxSeconds = 0.45f, Volume = 0.8f, PitchJitter = 0.08f,
            Make = new Func<float[]>[] { () => ProceduralCast.Fizzle() } },
        new() { Sound = ReleaseBolt, TargetDb = -18f, MaxSeconds = 0.4f,
            Make = new Func<float[]>[] { () => ProceduralCast.Snap(61), () => ProceduralCast.Snap(62), () => ProceduralCast.Snap(63) } },
        new() { Sound = ReleaseBurst, TargetDb = -17f, MaxSeconds = 0.7f,
            Files = new[] { CC0 + "/rubberduck/cannon_03.ogg", CC0 + "/rubberduck/cannon_05.ogg" },
            Make = new Func<float[]>[] { () => ProceduralCast.Heave(71) } },
        new() { Sound = ReleaseGround, TargetDb = -18f, MaxSeconds = 0.7f,
            Make = new Func<float[]>[] { () => ProceduralCast.Heave(81), () => ProceduralCast.Heave(82) } },
        new() { Sound = ReleaseFlow, TargetDb = -21f, MaxSeconds = 0.55f,
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(91), () => ProceduralCast.Rush(92) } },
        new() { Sound = ReleaseSky, TargetDb = -21f, MaxSeconds = 1.2f,
            Files = new[] { CC0 + "/jaggedstone/magical_3.ogg", CC0 + "/jaggedstone/magical_5.ogg" },
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(101, 1f) } },
        new() { Sound = ReleaseQuiet, TargetDb = -24f, MaxSeconds = 1f, Volume = 0.8f,
            Files = new[] { CC0 + "/jaggedstone/magical_2.ogg", CC0 + "/jaggedstone/magical_7_0.ogg" },
            Make = new Func<float[]>[] { () => ProceduralCast.Rush(111) } },
    };

    /// <summary>How fast a sound in a wizard's hands fades when another takes its place (s): quick, but no click.</summary>
    public const float CutSeconds = 0.05f;
    /// <summary>The hum's fade in, its fade out, and how long a level's rise takes it (s).</summary>
    public const float HumIn = 0.15f, HumOut = 0.08f, HumGlide = 0.25f;

    // By charge level, 1 to World.MaxCharge. Each level's rise: a third, then a fifth above the start.
    static readonly float[] HumPitch = { 1f, 1.19f, 1.5f };
    static readonly float[] HumLoudness = { 0.55f, 0.75f, 1f };
    static readonly float[] LevelPitch = { 1f, 1.19f, 1.5f };
    static readonly float[] ReleasePitch = { 1f, 0.93f, 0.86f };
    static readonly float[] ReleaseLoudness = { 0.8f, 0.9f, 1f };

    /// <summary>The hum's pitch at a charge level.</summary>
    public static float HumPitchAt(int charge) => HumPitch[Index(charge)];
    public static float HumLoudnessAt(int charge) => HumLoudness[Index(charge)];
    /// <summary>The pitch of the tone on reaching a level.</summary>
    public static float LevelPitchAt(int charge) => LevelPitch[Index(charge)];
    /// <summary>A charged release is heavier: lower and louder.</summary>
    public static float ReleasePitchAt(int charge) => ReleasePitch[Index(charge)];
    public static float ReleaseLoudnessAt(int charge) => ReleaseLoudness[Index(charge)];

    static int Index(int charge) => World.ClampCharge(charge) - 1;

    /// <summary>The sound of an element gathering in a hand; a cast with none (the d-pad's) has its own.</summary>
    public static string GatherOf(string element) => element switch
    {
        ElementIds.Fire => GatherFire,
        ElementIds.Water => GatherWater,
        ElementIds.Earth => GatherEarth,
        ElementIds.Air => GatherAir,
        _ => GatherPlain,
    };

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
