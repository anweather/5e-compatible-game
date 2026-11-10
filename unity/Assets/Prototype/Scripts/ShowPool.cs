using System;
using System.Collections.Generic;
using UnityEngine;

// Spell shows kept ready, so a cast copies nothing (#189). A pack effect costs milliseconds to copy
// (BigExplosion, eight prewarmed particle systems: 1-16 ms in a player), all of it on the frame the
// spell lands. So each spell's impact is copied ahead of its first cast (Ready, then one a frame from
// the caster's Update: ReadyNext), and the copy is handed out again and again: taken (put where it's
// wanted and switched on, so its systems start the way a new copy's do, prewarm and all), and put back
// when its time is up (stopped, cleared, switched off). A copy keeps its look's dressing (LookTint.Dress,
// a tinted colour), so copies are kept per prefab and look; its size and scale are set afresh at each
// take, from what it was made with (PooledShow). Only shows of particle systems and lights are kept: one
// with a script of its own (the Respawn mist's SpawnEffect) keeps its own time and is copied and
// destroyed as before. The code-made bursts (BurstFx) are readied here too. Presentation only.
public static class ShowPool
{
    /// <summary>What a kept copy was made as: the prefab, the look's tint and, for a tinted show, its colour.</summary>
    public readonly struct Look : IEquatable<Look>
    {
        public readonly GameObject Prefab;
        public readonly LookTint Tint;
        public readonly Color Colour;

        public Look(GameObject prefab, LookTint tint, Color colour)
        {
            Prefab = prefab;
            Tint = tint;
            Colour = colour;
        }

        public bool Equals(Look other) => Prefab == other.Prefab && Tint.Equals(other.Tint) && Colour == other.Colour;
        public override bool Equals(object obj) => obj is Look other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Prefab != null ? Prefab.GetInstanceID() : 0, Tint, Colour);
    }

    // Copies kept free for one look; more than this back at once and the rest go.
    const int MostKept = 4;
    // Where a copy is made ahead of its cast, out of sight.
    static readonly Vector3 Away = new(0f, -10000f, 0f);

    static readonly Dictionary<Look, Stack<PooledShow>> free = new();
    static readonly Dictionary<GameObject, bool> keepable = new();
    static readonly Queue<Action> toReady = new();
    static readonly HashSet<Look> readied = new();
    static bool burstsQueued;
    static Transform holder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        free.Clear();
        keepable.Clear();
        toReady.Clear();
        readied.Clear();
        burstsQueued = false;
        holder = null;
    }

    /// <summary>
    /// A show of <paramref name="look"/> at a point: a kept copy if one is free, else a new one, dressed by
    /// <paramref name="dress"/> (only a new one: a kept copy is dressed already). Its scale is the prefab's, or
    /// <paramref name="size"/> all round, then <paramref name="scale"/> times that (SpellVisuals.Scale).
    /// </summary>
    public static GameObject Take(Look look, Vector3 at, Quaternion turn, Action<GameObject> dress, float scale = 1f, float? size = null)
    {
        if (!Keeps(look.Prefab))
        {
            var made = SpellVisuals.Spawn(look.Prefab, at, turn);
            dress?.Invoke(made);
            Size(made, scale, size);
            return made;
        }
        var show = Pop(look);
        if (show != null)
        {
            PerfCounts.Add("show.reused");
            show.Unscale();
            show.transform.SetPositionAndRotation(at, turn);
            show.gameObject.SetActive(true);
            show.Play();
        }
        else
        {
            PerfCounts.Add("show.made");
            show = Make(look, dress, at, turn);
        }
        Size(show.gameObject, scale, size);
        return show.gameObject;
    }

    /// <summary>Ends a show after <paramref name="seconds"/>: a kept copy goes back to be taken again, any other is destroyed.</summary>
    public static void Release(GameObject show, float seconds)
    {
        if (show == null) return;
        if (show.TryGetComponent(out PooledShow kept)) kept.BackIn(seconds);
        else UnityEngine.Object.Destroy(show, seconds);
    }

    /// <summary>Has a copy of <paramref name="look"/> made ahead of its first cast (by ReadyNext), unless one is already.</summary>
    public static void Ready(Look look, Action<GameObject> dress)
    {
        if (look.Prefab == null || !Keeps(look.Prefab) || !readied.Add(look)) return;
        toReady.Enqueue(() =>
        {
            if (Pop(look) is { } there) Back(there);
            else Back(Make(look, dress, Away, Quaternion.identity));
        });
        if (burstsQueued) return;
        burstsQueued = true;
        foreach (BurstFx.Kind kind in Enum.GetValues(typeof(BurstFx.Kind))) toReady.Enqueue(() => BurstFx.Ready(kind));
    }

    /// <summary>Makes the next copy waiting to be readied, if any: one a frame, so readying never makes a long frame.</summary>
    public static void ReadyNext()
    {
        if (toReady.Count > 0) toReady.Dequeue()();
    }

    /// <summary>How many copies of a look are free now (for the tests).</summary>
    public static int FreeCount(Look look) => free.TryGetValue(look, out var kept) ? kept.Count : 0;

    /// <summary>Puts a copy back now: stopped, cleared and switched off, or destroyed if enough are free already.</summary>
    public static void Back(PooledShow show)
    {
        if (show == null) return;
        show.StopAndClear();
        show.gameObject.SetActive(false);
        if (!free.TryGetValue(show.Look, out var kept)) free[show.Look] = kept = new Stack<PooledShow>();
        if (kept.Count >= MostKept)
        {
            UnityEngine.Object.Destroy(show.gameObject);
            return;
        }
        if (holder == null) holder = new GameObject("Spell shows kept").transform;
        show.transform.SetParent(holder, false);
        kept.Push(show);
    }

    // Whether a prefab's show can be kept: nothing in it but particle systems, lights and meshes (colliders are removed).
    static bool Keeps(GameObject prefab)
    {
        if (prefab == null) return false;
        if (keepable.TryGetValue(prefab, out bool keeps)) return keeps;
        keeps = true;
        foreach (var part in prefab.GetComponentsInChildren<Component>(true))
            keeps &= part is Transform or ParticleSystem or ParticleSystemRenderer or Light or MeshFilter or MeshRenderer or Collider
                || part.GetType().Name == "UniversalAdditionalLightData";
        return keepable[prefab] = keeps;
    }

    // A free copy of a look, skipping any a scene change destroyed.
    static PooledShow Pop(Look look)
    {
        if (!free.TryGetValue(look, out var kept)) return null;
        while (kept.Count > 0)
        {
            var show = kept.Pop();
            if (show != null)
            {
                show.transform.SetParent(null, true);
                return show;
            }
        }
        return null;
    }

    static PooledShow Make(Look look, Action<GameObject> dress, Vector3 at, Quaternion turn)
    {
        var go = SpellVisuals.Spawn(look.Prefab, at, turn);
        var show = go.AddComponent<PooledShow>();
        show.Keep(look);
        dress?.Invoke(go);
        return show;
    }

    static void Size(GameObject show, float scale, float? size)
    {
        if (size.HasValue) show.transform.localScale = Vector3.one * size.Value;
        SpellVisuals.Scale(show, scale);
    }
}
