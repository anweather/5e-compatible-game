using Chem;
using UnityEngine;

/// <summary>The game's sounds, by name: the entries of the SoundBank (SoundBankBuilder's recipe).</summary>
public static class SoundIds
{
    public const string Cast = "cast";
    public const string Fire = "fire";
    public const string Frost = "frost";
    public const string Splash = "splash";
    public const string Grease = "grease";
    public const string Zap = "zap";
    public const string Gust = "gust";
    public const string Thunder = "thunder";
    public const string Earth = "earth";
    public const string Arcane = "arcane";
    public const string Blink = "blink";
    public const string Explosion = "explosion";
    public const string Break = "break";
    public const string Shatter = "shatter";
    public const string Hiss = "hiss";
    public const string FireLoop = "fire-loop";
    public const string RainLoop = "rain-loop";
    public const string WindLoop = "wind-loop";
}

// Where the game's sound effects come from and how they play: which sound a
// spell makes when it's cast and when it lands, and 3D sounds at a point.
// Presentation only: every machine plays what it sees, so online players hear
// each other's spells. Clips come from the SoundBank (recordings and
// procedural sounds, mastered to sit together in the mix), one of a sound's
// variations at random; a sound the bank lacks (no bank built yet) falls back
// to ProceduralSounds.
public static class GameSounds
{
    /// <summary>The sound of a spell leaving the hand (Gust of Wind is all whoosh already).</summary>
    public static string CastOf(SpellDef spell) =>
        spell != null && spell.Element == "air" ? null : SoundIds.Cast;

    /// <summary>The sound where a spell takes effect: by its element, else by what it does.</summary>
    public static string LandingOf(SpellDef spell)
    {
        if (spell == null) return null;
        switch (spell.Element)
        {
            case "fire": return spell.Shape == SpellShapes.Burst ? SoundIds.Explosion : SoundIds.Fire;
            case "cold": return SoundIds.Frost;
            case "water": return SoundIds.Splash;
            case "grease": return SoundIds.Grease;
            case "lightning": return SoundIds.Zap;
            case "air": return SoundIds.Gust;
            case "thunder": return SoundIds.Thunder;
            case "acid": return SoundIds.Hiss;
        }
        if (spell.Effect == "terraform") return SoundIds.Earth;
        return spell.Effect == "teleport" ? SoundIds.Blink : SoundIds.Arcane;
    }

    /// <summary>A clip for the sound: a bank variation if there is one, else the procedural stand-in.</summary>
    public static AudioClip Clip(string sound)
    {
        if (sound == null) return null;
        var clip = SoundBank.Loaded != null ? SoundBank.Loaded.Pick(sound) : null;
        return clip != null ? clip : Fallback(sound);
    }

    static AudioClip Fallback(string sound) => sound switch
    {
        SoundIds.Cast => ProceduralSounds.Cast,
        SoundIds.Fire => ProceduralSounds.FireBurst,
        SoundIds.Frost => ProceduralSounds.Frost,
        SoundIds.Splash => ProceduralSounds.Splash,
        SoundIds.Grease => ProceduralSounds.Squelch,
        SoundIds.Zap => ProceduralSounds.Zap,
        SoundIds.Gust => ProceduralSounds.Gust,
        SoundIds.Thunder => ProceduralSounds.Thunder,
        SoundIds.Earth => ProceduralSounds.Rumble,
        SoundIds.Explosion => ProceduralSounds.Explosion,
        SoundIds.Break => ProceduralSounds.Break,
        SoundIds.Shatter => ProceduralSounds.Shatter,
        SoundIds.Hiss => ProceduralSounds.Hiss,
        SoundIds.FireLoop => ProceduralSounds.FireLoop,
        SoundIds.RainLoop => ProceduralSounds.RainLoop,
        SoundIds.WindLoop => ProceduralSounds.WindLoop,
        _ => ProceduralSounds.Shimmer,
    };

    /// <summary>Plays the sound once at a point in the world, with a little pitch variety.</summary>
    public static void Play(string sound, Vector3 at, float volume = 1f, float pitchJitter = 0.06f)
    {
        var clip = Clip(sound);
        if (clip == null) return;
        var go = new GameObject("Sound " + sound);
        go.transform.position = at;
        var source = go.AddComponent<AudioSource>();
        Configure(source, clip, volume * GameVolume.Effects);
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.5f, source.pitch) + 0.1f);
    }

    /// <summary>A looping 3D source (a fire burning), stopped until it's played.</summary>
    public static AudioSource Loop(string sound, Transform parent, float volume)
    {
        var clip = Clip(sound);
        var source = parent.gameObject.AddComponent<AudioSource>();
        Configure(source, clip, volume);
        source.loop = true;
        source.playOnAwake = false;
        // Not all fires in step: each loop starts at its own place.
        if (clip != null) source.time = Random.Range(0f, clip.length * 0.9f);
        return source;
    }

    // Fully 3D, audible across the yard, fading over tens of metres.
    static void Configure(AudioSource source, AudioClip clip, float volume)
    {
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 4f;
        source.maxDistance = 90f;
        source.dopplerLevel = 0f;
    }
}
