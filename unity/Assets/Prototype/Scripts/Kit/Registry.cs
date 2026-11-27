using System;
using System.Collections.Generic;
using UnityEngine;

// A registry: the live components of one type, kept by the type itself, in place of a scene search
// (FindAnyObjectByType, FindObjectsByType). A type holds one in a static field; each instance joins in
// Awake and leaves in OnDestroy, the span a scene search sees it in. What it hands out is what the search
// would: only components on active GameObjects (a disabled component still counts, as it does for
// Find*), never a destroyed one, in the order they joined (Find*'s order, FindObjectsSortMode.None, is
// no order at all, so no caller can have depended on one; the registry keeps join order anyway).
//
// Where callers ask what's near a place, the type also places each member in a chunk (Place) and they
// ask by chunk (Near), which reads only the chunks asked for, never every member or every chunk (J-086).
// A member not placed yet, or Unplaced, is in every Near answer, so Near is always a superset of the
// members in those chunks (the caller keeps its own exact test of position), provided a placed member is
// re-placed at least once per physics step.
//
// Join, Leave, Place and Unplace are O(1): the members are linked lists (join order, and each chunk's
// placing order), with a map from each member to its node, so thousands of props joining in Awake or
// leaving in OnDestroy at once cost O(n), not O(n^2). The maps go by reference, not by Unity's ==, which
// calls every destroyed object equal.
//
// The project doesn't reload the domain on entering Play mode (J-023), so a registry would keep the last
// Play's members: they leave as Play ends (OnDestroy), and every registry is emptied again as Play starts,
// the way ChemGrid.Current is. Edit mode runs no Awake, so there a registry is empty (tests join by hand).
public sealed class Registry<T> where T : Component
{
    readonly LinkedList<T> joined = new();
    readonly Dictionary<T, LinkedListNode<T>> joinedAt = new(ByReference.Instance);
    readonly Dictionary<Vector2Int, LinkedList<T>> byChunk = new();
    readonly Dictionary<T, Vector2Int> placed = new(ByReference.Instance);
    readonly LinkedList<T> loose = new();
    readonly Dictionary<T, LinkedListNode<T>> spot = new(ByReference.Instance);

    public Registry() => Registries.Track(Forget);

    public void Join(T member)
    {
        if (member == null || joinedAt.ContainsKey(member)) return;
        joinedAt[member] = joined.AddLast(member);
        spot[member] = loose.AddLast(member);
    }

    public void Leave(T member)
    {
        if (member is null || !joinedAt.Remove(member, out var node)) return;
        joined.Remove(node);
        var at = spot[member];
        at.List.Remove(at);
        spot.Remove(member);
        placed.Remove(member);
    }

    public T First
    {
        get
        {
            foreach (var m in joined)
                if (Live(m)) return m;
            return null;
        }
    }

    public T[] All()
    {
        var all = new List<T>(joined.Count);
        AddLive(all, joined);
        return all.ToArray();
    }

    public T[] AllByInstanceId()
    {
        var all = All();
        Array.Sort(all, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return all;
    }

    public void Place(T member, Vector2Int chunk)
    {
        if (member == null || !spot.TryGetValue(member, out var at)) return;
        if (placed.TryGetValue(member, out var was) && was == chunk) return;
        at.List.Remove(at);
        placed[member] = chunk;
        if (!byChunk.TryGetValue(chunk, out var list)) byChunk[chunk] = list = new LinkedList<T>();
        list.AddLast(at);
    }

    public void Unplace(T member)
    {
        if (member is null || !placed.Remove(member)) return;
        var at = spot[member];
        at.List.Remove(at);
        loose.AddLast(at);
    }

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

    static void AddLive(List<T> to, LinkedList<T> from)
    {
        foreach (var m in from)
            if (Live(m)) to.Add(m);
    }

    public T[] InChunk(Vector2Int chunk)
    {
        if (!byChunk.TryGetValue(chunk, out var list)) return Array.Empty<T>();
        var live = new List<T>(list.Count);
        AddLive(live, list);
        return live.ToArray();
    }

    static bool Live(T m) => m != null && m.gameObject.activeInHierarchy;

    void Forget()
    {
        joined.Clear();
        joinedAt.Clear();
        byChunk.Clear();
        placed.Clear();
        loose.Clear();
        spot.Clear();
    }

    sealed class ByReference : IEqualityComparer<T>
    {
        public static readonly ByReference Instance = new();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T m) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m);
    }
}

public static class Registries
{
    static readonly List<Action> forgets = new();

    internal static void Track(Action forget) => forgets.Add(forget);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ForgetAll()
    {
        foreach (var forget in forgets) forget();
    }
}
