using System;
using System.Collections.Generic;
using UnityEngine;

// A registry: the live components of one type, kept by the type itself, in place of a scene search
// (FindAnyObjectByType, FindObjectsByType). A type holds one in a static field; each instance joins in
// Awake and leaves in OnDestroy, the span a scene search sees it in. What it hands out is what the search
// would: only components on active GameObjects (a disabled component still counts, as it does for
// Find*), never a destroyed one, in the order they joined (Find*'s order, FindObjectsSortMode.None, is
// no order at all, so no caller can have depended on one).
//
// Where callers ask what's near a place, the type also places each member in a chunk (Place) and they
// ask by chunk (Near), which reads only the chunks asked for, never every member or every chunk (J-086).
// A member not placed yet, or Unplaced, is in every Near answer, so Near is always a superset of the
// members in those chunks (the caller keeps its own exact test of position), provided a placed member is
// re-placed at least once per physics step.
//
// The project doesn't reload the domain on entering Play mode (J-023), so a registry would keep the last
// Play's members: they leave as Play ends (OnDestroy), and every registry is emptied again as Play starts,
// the way ChemGrid.Current is. Edit mode runs no Awake, so there a registry is empty (tests join by hand).
public sealed class Registry<T> where T : Component
{
    readonly List<T> joined = new();
    readonly Dictionary<Vector2Int, List<T>> byChunk = new();
    readonly Dictionary<T, Vector2Int> placed = new();
    // Joined but in no chunk: not placed yet, or unplaced.
    readonly List<T> loose = new();

    public Registry() => Registries.Track(Forget);

    /// <summary>In Awake.</summary>
    public void Join(T member)
    {
        if (member == null || joined.Contains(member)) return;
        joined.Add(member);
        loose.Add(member);
    }

    /// <summary>In OnDestroy.</summary>
    public void Leave(T member)
    {
        if (!joined.Remove(member)) return;
        if (placed.TryGetValue(member, out var was))
        {
            byChunk[was].Remove(member);
            placed.Remove(member);
        }
        loose.Remove(member);
    }

    /// <summary>The first live member (FindAnyObjectByType), or null.</summary>
    public T First
    {
        get
        {
            foreach (var m in joined)
                if (Live(m)) return m;
            return null;
        }
    }

    /// <summary>Every live member, as a new array (FindObjectsByType): safe to destroy members while walking it.</summary>
    public T[] All() => joined.FindAll(Live).ToArray();

    /// <summary>Every live member by instance id, lowest first (FindObjectsByType with FindObjectsSortMode.InstanceID).</summary>
    public T[] AllByInstanceId()
    {
        var all = All();
        Array.Sort(all, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return all;
    }

    /// <summary>Puts a joined member in a chunk (its chunk now). Cheap when it's already there.</summary>
    public void Place(T member, Vector2Int chunk)
    {
        if (member == null) return;
        if (placed.TryGetValue(member, out var was))
        {
            if (was == chunk) return;
            byChunk[was].Remove(member);
        }
        else if (!loose.Remove(member)) return;
        placed[member] = chunk;
        if (!byChunk.TryGetValue(chunk, out var list)) byChunk[chunk] = list = new List<T>();
        list.Add(member);
    }

    /// <summary>Takes a member out of its chunk: it's in every Near answer until placed again.</summary>
    public void Unplace(T member)
    {
        if (member is null || !placed.TryGetValue(member, out var was)) return;
        byChunk[was].Remove(member);
        placed.Remove(member);
        loose.Add(member);
    }

    /// <summary>
    /// The live members placed in the chunks between <paramref name="from"/> and <paramref name="to"/> (corners, inclusive, either way round) and the
    /// ring of chunks around them (a member re-placed each physics step can be a step behind), chunk by chunk, then
    /// every live member in no chunk. It reads only those chunks, never every member.
    /// </summary>
    public T[] Near(Vector2Int from, Vector2Int to)
    {
        var lo = Vector2Int.Min(from, to);
        var hi = Vector2Int.Max(from, to);
        var near = new List<T>();
        for (int cy = lo.y - 1; cy <= hi.y + 1; cy++)
            for (int cx = lo.x - 1; cx <= hi.x + 1; cx++)
                if (byChunk.TryGetValue(new Vector2Int(cx, cy), out var list)) AddLive(near, list);
        AddLive(near, loose);
        return near.ToArray();
    }

    static void AddLive(List<T> to, List<T> from)
    {
        foreach (var m in from)
            if (Live(m)) to.Add(m);
    }

    /// <summary>The live members placed in one chunk, in the order they were placed there.</summary>
    public T[] InChunk(Vector2Int chunk) =>
        byChunk.TryGetValue(chunk, out var list) ? list.FindAll(Live).ToArray() : Array.Empty<T>();

    static bool Live(T m) => m != null && m.gameObject.activeInHierarchy;

    void Forget()
    {
        joined.Clear();
        byChunk.Clear();
        placed.Clear();
        loose.Clear();
    }
}

/// <summary>Every registry, to empty them all as Play mode starts (see Registry).</summary>
public static class Registries
{
    static readonly List<Action> forgets = new();

    internal static void Track(Action forget) => forgets.Add(forget);

    /// <summary>Empties every registry. As Play mode starts; tests call it too.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ForgetAll()
    {
        foreach (var forget in forgets) forget();
    }
}
