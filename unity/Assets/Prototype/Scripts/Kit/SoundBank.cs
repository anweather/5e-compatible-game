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
    public int recipes;
    Dictionary<string, Entry> byName;

    static SoundBank loaded;
    static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = null;
        looked = false;
    }

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

    public AudioClip Pick(string sound)
    {
        if (byName == null || byName.Count != entries.Count)
        {
            byName = new Dictionary<string, Entry>();
            foreach (var e in entries) byName[e.sound] = e;
        }
        if (!byName.TryGetValue(sound, out var entry) || entry.clips.Length == 0) return null;
        return entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
    }
}
