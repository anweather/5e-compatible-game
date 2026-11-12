using System;
using UnityEngine;

/// <summary>A sound under way in SoundVoices. The default is none; one that has ended just stops counting.</summary>
public readonly struct Voice
{
    public readonly int Slot, Serial;

    public Voice(int slot, int serial)
    {
        Slot = slot;
        Serial = serial;
    }
}

/// <summary>What to play: a sound, its clip, its volume before the setting, its pitch and how far it carries (m).</summary>
public readonly struct Cue
{
    public readonly string Sound;
    public readonly AudioClip Clip;
    public readonly float Volume, Pitch, Reach;

    public Cue(string sound, AudioClip clip, float volume, float pitch, float reach)
    {
        Sound = sound;
        Clip = clip;
        Volume = volume;
        Pitch = pitch;
        Reach = reach;
    }
}

// The game's pool of sound voices: a fixed set of 3D AudioSources, so playing a
// sound makes no object and no garbage. A voice can follow a transform; when
// they're all in use the oldest one-shot gives way, never a loop. A loop has to
// be held every frame (Hold) or it fades out by itself: whatever stops a wizard
// holding it (a reset, a despawn, a disabled component, a scene change) can't
// leave it playing. Volume comes through GameVolume.Effects every frame, so the
// slider moves sounds already going. Starts itself on first use and lasts the
// session. There's no AudioMixer in the game yet: when the sound pass adds one,
// its group goes on the sources in Build and nothing else changes, and
// GameSounds.Play can move onto this pool the same way (for now only the casting
// sounds use it).
[DefaultExecutionOrder(210)] // after CastBeats has held its loops
public class SoundVoices : MonoBehaviour
{
    const int Count = 24;
    /// <summary>A loop not held for this long (s) lets go.</summary>
    const float Lease = 0.25f;
    const float DefaultFade = 0.08f;

    struct State
    {
        public AudioSource source;
        public Transform follow;
        public Vector3 offset;
        public float volume, gain, gainStep, pitch, pitchStep, ends, held, started;
        public int serial;
        public bool busy, loop;
    }

    static SoundVoices instance;
    readonly State[] voices = new State[Count];
    int serials;

    /// <summary>Each sound as it starts: its name, where, its volume before the setting and its pitch (for probes).</summary>
    public static event Action<string, Vector3, float, float> Started;

    // How a sound falls away over its reach: full close by, nothing at the far end.
    static readonly AnimationCurve Falloff = new(new Keyframe(0f, 1f), new Keyframe(0.12f, 1f), new Keyframe(0.3f, 0.45f), new Keyframe(0.6f, 0.14f), new Keyframe(1f, 0f));

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        Started = null;
    }

    /// <summary>How much of a sound of that reach gets to an ear this far away.</summary>
    public static float GainAt(float distance, float reach) => Falloff.Evaluate(distance / reach);

    /// <summary>How many voices are sounding, and how many of them are loops.</summary>
    public static (int all, int loops) Sounding
    {
        get
        {
            int all = 0, loops = 0;
            if (instance == null) return (0, 0);
            foreach (var v in instance.voices)
            {
                if (!v.busy) continue;
                all++;
                if (v.loop) loops++;
            }
            return (all, loops);
        }
    }

    /// <summary>Plays the clip once at a point, or at <paramref name="at"/> from <paramref name="follow"/> and with it. Beyond its reach of the listener it isn't played.</summary>
    public static Voice Play(in Cue cue, Vector3 at, Transform follow = null) => Start(cue, at, follow, false, 0f, 0f);

    /// <summary>Starts the clip looping from <paramref name="startAt"/> (s), fading in, on <paramref name="follow"/>. Hold it every frame, or it stops.</summary>
    public static Voice Loop(in Cue cue, Vector3 offset, Transform follow, float fadeIn, float startAt = 0f) => Start(cue, offset, follow, true, fadeIn, startAt);

    /// <summary>Keeps a loop going, gliding to this volume and pitch. False if it's gone.</summary>
    public static bool Hold(Voice voice, float volume, float pitch, float glideSeconds)
    {
        if (!Live(voice)) return false;
        ref var v = ref instance.voices[voice.Slot];
        v.held = Time.unscaledTime;
        v.gainStep = Mathf.Abs(v.gainStep);
        v.volume = volume;
        if (!Mathf.Approximately(pitch, v.pitch)) v.pitchStep = Mathf.Abs(pitch - v.source.pitch) / Mathf.Max(0.01f, glideSeconds);
        v.pitch = pitch;
        return true;
    }

    /// <summary>Fades a voice out and frees it.</summary>
    public static void Stop(Voice voice, float seconds = DefaultFade)
    {
        if (!Live(voice)) return;
        instance.voices[voice.Slot].gainStep = -1f / Mathf.Max(0.01f, seconds);
    }

    /// <summary>The voice is still sounding, and not on its way out.</summary>
    public static bool Playing(Voice voice) => Live(voice) && instance.voices[voice.Slot].gainStep >= 0f;

    /// <summary>The pitch a voice has now, and its volume after the setting; for the probe.</summary>
    public static (float pitch, float volume) Now(Voice voice)
    {
        if (!Live(voice)) return (0f, 0f);
        ref var v = ref instance.voices[voice.Slot];
        return (v.source.pitch, v.source.volume);
    }

    /// <summary>Stops everything at once.</summary>
    public static void StopAll()
    {
        if (instance == null) return;
        for (int i = 0; i < Count; i++)
            if (instance.voices[i].busy) instance.Free(i);
    }

    static bool Live(Voice voice) =>
        instance != null && voice.Serial != 0 && instance.voices[voice.Slot].serial == voice.Serial && instance.voices[voice.Slot].busy;

    static Voice Start(in Cue cue, Vector3 at, Transform follow, bool loop, float fadeIn, float startAt)
    {
        if (cue.Clip == null) return default;
        var place = follow != null ? follow.position + at : at;
        var ear = Camera.main;
        if (ear != null && (place - ear.transform.position).sqrMagnitude > cue.Reach * cue.Reach) return default;
        if (instance == null) Build();
        int slot = instance.TakeSlot();
        if (slot < 0) return default;
        ref var v = ref instance.voices[slot];
        v.serial = ++instance.serials;
        v.busy = true;
        v.loop = loop;
        v.follow = follow;
        v.offset = follow != null ? at : Vector3.zero;
        v.volume = cue.Volume;
        v.pitch = cue.Pitch;
        v.pitchStep = 0f;
        v.gain = fadeIn > 0f ? 0f : 1f;
        v.gainStep = fadeIn > 0f ? 1f / fadeIn : 0f;
        v.started = v.held = Time.unscaledTime;
        v.ends = v.started + cue.Clip.length / Mathf.Max(0.1f, cue.Pitch);
        var source = v.source;
        source.transform.position = place;
        source.clip = cue.Clip;
        source.time = startAt;
        source.loop = loop;
        source.pitch = cue.Pitch;
        source.maxDistance = cue.Reach;
        source.volume = v.volume * v.gain * GameVolume.Effects;
        source.Play();
        Started?.Invoke(cue.Sound, place, cue.Volume, cue.Pitch);
        return new Voice(slot, v.serial);
    }

    static void Build()
    {
        var go = new GameObject("Sound voices");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundVoices>();
        for (int i = 0; i < Count; i++)
        {
            var part = new GameObject("Voice");
            part.transform.SetParent(go.transform, false);
            var source = part.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Falloff);
            source.minDistance = 1f;
            instance.voices[i].source = source;
        }
    }

    // A free voice, else the oldest one-shot's; -1 if they're all loops.
    int TakeSlot()
    {
        int oldest = -1;
        for (int i = 0; i < Count; i++)
        {
            ref var v = ref voices[i];
            if (!v.busy) return i;
            if (!v.loop && (oldest < 0 || v.started < voices[oldest].started)) oldest = i;
        }
        if (oldest >= 0) Free(oldest);
        return oldest;
    }

    void Free(int slot)
    {
        ref var v = ref voices[slot];
        v.source.Stop();
        v.source.clip = null;
        v.follow = null;
        v.busy = false;
    }

    static readonly Unity.Profiling.ProfilerMarker DIAG_LateUpdateMarker = new("SoundVoices.LateUpdate");

    void LateUpdate()
    {

        using var diagMarked = DIAG_LateUpdateMarker.Auto();
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime, setting = GameVolume.Effects;
        for (int i = 0; i < Count; i++)
        {
            ref var v = ref voices[i];
            if (!v.busy) continue;
            if (v.loop && now - v.held > Lease && v.gainStep >= 0f) v.gainStep = -1f / DefaultFade;
            v.gain = Mathf.Min(1f, v.gain + v.gainStep * dt);
            if (v.gain <= 0f || (!v.loop && now >= v.ends))
            {
                Free(i);
                continue;
            }
            v.source.volume = v.volume * v.gain * setting;
            if (v.pitchStep > 0f) v.source.pitch = Mathf.MoveTowards(v.source.pitch, v.pitch, v.pitchStep * dt);
            if (v.follow != null) v.source.transform.position = v.follow.position + v.offset;
        }
    }

    void OnApplicationQuit() => StopAll();
}
