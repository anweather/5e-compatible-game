using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Store packs are dependencies, like Packages/manifest.json: the
// repo lists them in AssetPacks.json and ignores their folders, and this
// restores any that are missing. A pack comes from the local Asset Store
// cache (see CacheRoot) when it's there, otherwise it is
// downloaded first. Imports are non-interactive.
//
// Importing a pack with scripts reloads the domain, so the restore keeps
// its progress in SessionState and picks up again after each reload.
[InitializeOnLoad]
public static class AssetPackRestore
{
    const string ManifestPath = "AssetPacks.json";
    const string Active = "AssetPackRestore.active";
    const string Failed = "AssetPackRestore.failed";
    const string Attempted = "AssetPackRestore.attempted";
    const double DownloadTimeout = 1800;

    [Serializable]
    class Pack
    {
        public long id;
        public string name;
        public string version;
        public string folder;
        public string cache;
        [Tooltip("Optional: import only these folders and what they reference (see UnityPackageSubset).")]
        public string[] include;
    }
    [Serializable] class Manifest { public List<Pack> packs = new List<Pack>(); }

    static Pack current;
    static double started, lastChange;
    static long lastSize = -1;

    static AssetPackRestore()
    {
        if (SessionState.GetBool(Active, false))
        {
            EditorApplication.update += Pump;
            return;
        }
        EditorApplication.delayCall += () =>
        {
            var missing = Missing();
            if (missing.Count > 0)
                Debug.LogWarning($"Missing Asset Store packs: {string.Join(", ", missing.ConvertAll(p => p.name))}. Run Prototype > Restore Asset Packs.");
        };
    }

    // Where the Editor keeps downloaded packs: ~/Library/Unity/Asset Store-5.x on macOS,
    // %APPDATA%\Unity\Asset Store-5.x on Windows.
    static string CacheRoot => Application.platform == RuntimePlatform.WindowsEditor
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Unity", "Asset Store-5.x");

    static List<Pack> Missing()
    {
        var missing = new List<Pack>();
        if (!File.Exists(ManifestPath)) return missing;
        foreach (var p in JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).packs)
            if (!HasFiles(p.folder)) missing.Add(p);
        return missing;
    }

    [MenuItem("Prototype/Restore Asset Packs")]
    public static void Restore()
    {
        if (Missing().Count == 0)
        {
            Debug.Log("Asset packs: all present.");
            return;
        }
        SessionState.SetBool(Active, true);
        SessionState.SetString(Failed, "");
        SessionState.SetString(Attempted, "");
        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
    }

    static void Pump()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        if (current == null)
        {
            StartNextPack();
            return;
        }
        ImportWhenDownloaded();
    }

    // Picks the next missing pack and starts its download if it isn't cached; finishes the restore when none are left.
    static void StartNextPack()
    {
        var failed = SessionState.GetString(Failed, "");
        current = Missing().Find(p => !failed.Contains($"[{p.id}]"));
        // One import attempt per pack per restore: still missing afterwards means it failed.
        if (current != null && SessionState.GetString(Attempted, "").Contains($"[{current.id}]"))
        {
            Debug.LogError($"Asset packs: {current.name} imported but {current.folder} still doesn't exist.");
            SessionState.SetString(Failed, failed + $"[{current.id}]");
            current = null;
            return;
        }
        if (current == null)
        {
            EditorApplication.update -= Pump;
            SessionState.SetBool(Active, false);
            Debug.Log(failed.Length == 0 ? "Asset packs: restore finished." : "Asset packs: restore finished with failures; see the errors above.");
            return;
        }
        started = lastChange = EditorApplication.timeSinceStartup;
        lastSize = -1;
        if (!File.Exists(Path.Combine(CacheRoot, current.cache)))
        {
            Debug.Log($"Asset packs: downloading {current.name}...");
            AssetStoreTools.Download(current.id);
        }
    }

    // Waits for the current pack's file to finish downloading, then imports it (or the subset it asks for).
    static void ImportWhenDownloaded()
    {
        var file = Path.Combine(CacheRoot, current.cache);
        if (EditorApplication.timeSinceStartup - started > DownloadTimeout)
        {
            Debug.LogError($"Asset packs: gave up waiting for {current.name}. Is it in My Assets for this Unity account?");
            SessionState.SetString(Failed, SessionState.GetString(Failed, "") + $"[{current.id}]");
            current = null;
            return;
        }
        if (!File.Exists(file)) return;

        // The download writes the file in place; import once its size has held for 3 seconds.
        long size = new FileInfo(file).Length;
        if (size != lastSize)
        {
            lastSize = size;
            lastChange = EditorApplication.timeSinceStartup;
            return;
        }
        if (EditorApplication.timeSinceStartup - lastChange < 3) return;

        if (current.include != null && current.include.Length > 0)
        {
            var subset = Path.Combine("Temp", $"asset-pack-{current.id}-subset.unitypackage");
            int count = UnityPackageSubset.Create(file, subset, current.include, IsInProject);
            Debug.Log($"Asset packs: importing {count} assets from {current.name} {current.version} ({string.Join(", ", current.include)})");
            file = subset;
        }
        else Debug.Log($"Asset packs: importing {current.name} {current.version}");
        SessionState.SetString(Attempted, SessionState.GetString(Attempted, "") + $"[{current.id}]");
        AssetDatabase.ImportPackage(file, false);
        current = null;
    }

    // Unity remembers deleted assets' GUIDs for a while, so check the file is really there.
    static bool IsInProject(string guid)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));
    }

    // A pack counts as present only if its folder holds real files: an empty
    // folder (say, one Unity kept after an interrupted import) doesn't count.
    static bool HasFiles(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(f => !f.EndsWith(".meta", StringComparison.Ordinal));
}
