using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Watches rules/*.json and rules/mods/*.json while the game runs (in the
// Editor and development builds; RuleSwitcher adds it) and hands each saved
// file to RuleSwitcher, which reloads and checks it and switches the rules at
// the next tick. Solo or host only: a client's rules come from the host.
//
// FileSystemWatcher reports on a worker thread, often several times for one
// save (editors write a temp file, then rename). Events only note the path and
// the time here; Update, on the main thread, passes a path on once it has been
// quiet for DebounceSeconds.
[RequireComponent(typeof(RuleSwitcher))]
public class RulesWatcher : MonoBehaviour
{
    const float DebounceSeconds = 0.3f;

    readonly ConcurrentDictionary<string, DateTime> touched = new();
    readonly List<FileSystemWatcher> watchers = new();
    readonly List<string> due = new();
    RuleSwitcher switcher;

    /// <summary>The folders being watched, for the log and the panel.</summary>
    public string RulesDir { get; private set; }

    void Awake() => switcher = GetComponent<RuleSwitcher>();

    void OnEnable()
    {
        RulesDir = ChemGrid.FindRulesDir();
        Watch(RulesDir);
        Watch(Path.Combine(RulesDir, "mods"));
    }

    void Watch(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var w = new FileSystemWatcher(dir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        w.Changed += OnFileEvent;
        w.Created += OnFileEvent;
        w.Deleted += OnFileEvent;
        w.Renamed += OnRenamed;
        w.EnableRaisingEvents = true;
        watchers.Add(w);
    }

    // Worker thread: just remember the path and when.
    void OnFileEvent(object sender, FileSystemEventArgs e) => touched[e.FullPath] = DateTime.UtcNow;

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        touched[e.OldFullPath] = DateTime.UtcNow;
        if (e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) touched[e.FullPath] = DateTime.UtcNow;
    }

    void Update()
    {
        if (touched.IsEmpty) return;
        var quietSince = DateTime.UtcNow.AddSeconds(-DebounceSeconds);
        due.Clear();
        foreach (var pair in touched)
            if (pair.Value <= quietSince) due.Add(pair.Key);
        foreach (var path in due)
        {
            // Only if nothing touched it again since we looked.
            if (!touched.TryGetValue(path, out var when) || when > quietSince || !touched.TryRemove(path, out _)) continue;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && switcher.OwnsRules) switcher.FileChanged(path);
        }
    }

    void OnDisable()
    {
        foreach (var w in watchers)
        {
            w.EnableRaisingEvents = false;
            w.Changed -= OnFileEvent;
            w.Created -= OnFileEvent;
            w.Deleted -= OnFileEvent;
            w.Renamed -= OnRenamed;
            w.Dispose();
        }
        watchers.Clear();
        touched.Clear();
    }
}
