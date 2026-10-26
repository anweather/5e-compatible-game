using Chem;
using UnityEngine;

// Where the game's sound effects come from and how they play: which clip a
// spell makes when it's cast and when it lands, and one-shot 3D sounds at a
// point. Presentation only: every machine plays what it sees, so online
// players hear each other's spells. The clips are ProceduralSounds for now.
public static class GameSounds
{
    /// <summary>The sound of a spell leaving the hand.</summary>
    public static AudioClip CastOf(SpellDef spell) =>
        spell != null && spell.Element == "air" ? null : ProceduralSounds.Cast;

    /// <summary>The sound where a spell takes effect: by its element, else by what it does.</summary>
    public static AudioClip LandingOf(SpellDef spell)
    {
        if (spell == null) return null;
        switch (spell.Element)
        {
            case "fire": return ProceduralSounds.FireBurst;
            case "cold": return ProceduralSounds.Frost;
            case "water": return ProceduralSounds.Splash;
            case "grease": return ProceduralSounds.Squelch;
            case "lightning": return ProceduralSounds.Zap;
            case "air": return ProceduralSounds.Gust;
            case "thunder": return ProceduralSounds.Thunder;
        }
        return spell.Effect == "terraform" ? ProceduralSounds.Rumble : ProceduralSounds.Shimmer;
    }

    /// <summary>Plays a clip once at a point in the world, with a little pitch variety.</summary>
    public static void Play(AudioClip clip, Vector3 at, float volume = 1f, float pitchJitter = 0.06f)
    {
        if (clip == null) return;
        var go = new GameObject("Sound " + clip.name);
        go.transform.position = at;
        var source = go.AddComponent<AudioSource>();
        Configure(source, clip, volume);
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.5f, source.pitch) + 0.1f);
    }

    /// <summary>A looping 3D source (a fire burning), stopped until it's played.</summary>
    public static AudioSource Loop(AudioClip clip, Transform parent, float volume)
    {
        var source = parent.gameObject.AddComponent<AudioSource>();
        Configure(source, clip, volume);
        source.loop = true;
        source.playOnAwake = false;
        // Not all fires in step: each loop starts at its own place.
        source.time = Random.Range(0f, clip.length * 0.9f);
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
