// The game's sounds, by name: the entries of the SoundBank (SoundBankBuilder's recipe).
using Chem;
using UnityEngine;

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
    public const string ThunderClap = "thunderclap";
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
    public const string Horn = "horn";
    public const string Roar = "roar";
}

public static class GameSounds
{
    static readonly System.Collections.Generic.Dictionary<string, AudioClip> made = new();

    public static string CastOf(SpellDef spell) =>
        spell != null && spell.Element == "air" ? null : SoundIds.Cast;

    public static string LandingOf(SpellDef spell)
    {
        if (spell == null) return null;
        if (spell.Delay > 0 && spell.Then == "thunder") return null;
        if (spell.Jagged > 0) return SoundIds.Earth;
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

    public static AudioClip Clip(string sound)
    {
        if (sound == null) return null;
        var clip = SoundBank.Loaded != null ? SoundBank.Loaded.Pick(sound) : null;
        return clip != null ? clip : Fallback(sound);
    }

    static AudioClip Fallback(string sound)
    {
        if (made.TryGetValue(sound, out var clip) && clip != null) return clip;
        return made[sound] = CastSoundTable.Of(sound) is { } row ? Stand(row) : Named(sound);
    }

    static AudioClip Stand(CastSoundTable.Row row)
    {
        var samples = row.Loop ? row.Make[0]() : SoundMastering.Trim(row.Make[0](), ProceduralCast.Rate, row.MaxSeconds);
        if (row.Reverse) System.Array.Reverse(samples);
        var clip = AudioClip.Create(row.Sound, samples.Length, 1, ProceduralCast.Rate, false);
        SoundMastering.Normalize(samples, ProceduralCast.Rate, row.TargetDb, whole: row.Loop);
        clip.SetData(samples, 0);
        return clip;
    }

    static AudioClip Named(string sound) => sound switch
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
        SoundIds.Horn => ProceduralSounds.Horn,
        SoundIds.Roar => ProceduralSounds.Roar,
        _ => ProceduralSounds.Shimmer,
    };

    public static event System.Action<string, Vector3> Played;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Played = null;
    }

    public static void Play(string sound, Vector3 at, float volume = 1f, float pitchJitter = 0.06f)
    {
        var clip = Clip(sound);
        if (clip == null) return;
        Played?.Invoke(sound, at);
        var go = new GameObject("Sound " + sound);
        go.transform.position = at;
        var source = go.AddComponent<AudioSource>();
        Configure(source, clip, volume * GameVolume.Effects);
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.5f, source.pitch) + 0.1f);
    }

    public static void PlayMade(string sound, AudioClip clip, Vector3 at, float volume, float spread)
    {
        if (clip == null) return;
        Played?.Invoke(sound, at);
        var go = new GameObject("Sound " + sound);
        go.transform.position = at;
        var source = go.AddComponent<AudioSource>();
        Configure(source, clip, volume * GameVolume.Effects);
        source.spatialBlend = spread;
        source.maxDistance = 400f;
        source.priority = 0;
        source.Play();
        Object.Destroy(go, clip.length + 0.1f);
        Object.Destroy(clip, clip.length + 1f);
    }

    public static AudioSource Loop(string sound, Transform parent, float volume)
    {
        var clip = Clip(sound);
        var source = parent.gameObject.AddComponent<AudioSource>();
        Configure(source, clip, volume);
        source.loop = true;
        source.playOnAwake = false;
        if (clip != null) source.time = Random.Range(0f, clip.length * 0.9f);
        return source;
    }

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
