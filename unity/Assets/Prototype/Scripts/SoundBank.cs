using System;
using System.Collections.Generic;
using UnityEngine;

// The game's sounds as data: for each sound (a cast, a fire catching, an
// explosion...), the clips that can play for it, already mastered to sit
// together in the mix (SoundMastering). One is picked at random each time, so
// repeats vary. Built by SoundBankBuilder with the yard, into
// Generated/Resources (out of git: some clips come from an Asset Store pack),
// and loaded at runtime; a sound it doesn't have falls back to
// ProceduralSounds (GameSounds).
public class SoundBank : ScriptableObject
{
    public const string ResourceName = "SoundBank";

    [Serializable]
    public class Entry
    {
        public string sound;
        public AudioClip[] clips = Array.Empty<AudioClip>();
    }

    public List<Entry> entries = new();

    static SoundBank loaded;
    static bool looked;

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = null;
        looked = false;
    }

    /// <summary>The built bank, or null if the yard hasn't been built with one.</summary>
    public static SoundBank Loaded
    {
        get
        {
            if (!looked)
            {
                looked = true;
                loaded = Resources.Load<SoundBank>(ResourceName);
            }
            return loaded;
        }
    }

    /// <summary>One of the sound's clips, at random, or null if the bank has none for it.</summary>
    public AudioClip Pick(string sound)
    {
        var entry = entries.Find(e => e.sound == sound);
        if (entry == null || entry.clips.Length == 0) return null;
        return entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
    }
}
