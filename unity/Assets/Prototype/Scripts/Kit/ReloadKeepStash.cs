// What ReloadKeep holds through an Editor domain reload: each view that kept its objects, the plan and code they were
// made with, and the objects. A ScriptableSingleton outlives the reload with its fields, and the DontSave objects its
// entries point at outlive it too. Editor only.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class ReloadKeepStash : UnityEditor.ScriptableSingleton<ReloadKeepStash>
{
    [System.Serializable]
    struct Entry
    {
        public Object owner;
        public string key;
        public Object[] made;
    }

    [SerializeField] List<Entry> entries = new();
    readonly Dictionary<Object, int> at = new();

    public void Put(Object owner, string key, Object[] made)
    {
        entries.Add(new Entry { owner = owner, key = key, made = made });
        at.Clear();
    }

    public bool Holds(Object owner)
    {
        if (entries.Count == 0) return false;
        if (at.Count == 0)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].owner != null) at[entries[i].owner] = i;
        return at.ContainsKey(owner);
    }

    public Object[] Take(Object owner, string key, int least)
    {
        if (!Holds(owner)) return null;
        int index = at[owner];
        var entry = entries[index];
        at.Remove(owner);
        entries[index] = default;
        bool whole = entry.key == key && entry.made != null && entry.made.Length >= least && System.Array.TrueForAll(entry.made, m => m != null);
        if (whole) return entry.made;
        Drop(entry);
        return null;
    }

    public void DropUnclaimed()
    {
        foreach (var entry in entries) Drop(entry);
        entries.Clear();
        at.Clear();
    }

    static void Drop(Entry entry)
    {
        if (entry.made == null) return;
        foreach (var made in entry.made)
            if (made != null) DestroyImmediate(made);
    }
}
#endif
