using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Asset Store packs are dependencies, like Packages/manifest.json: the
// repo lists them in AssetPacks.json and ignores their folders, and this
// restores any that are missing. A pack comes from the local Asset Store
// cache (~/Library/Unity/Asset Store-5.x) when it's there, otherwise it is
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
    const double DownloadTimeout = 1800;

    [Serializable] class Pack { public long id; public string name; public string version; public string folder; public string cache; }
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

    static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Unity/Asset Store-5.x");

    static List<Pack> Missing()
    {
        var missing = new List<Pack>();
        if (!File.Exists(ManifestPath)) return missing;
        foreach (var p in JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).packs)
            if (!Directory.Exists(p.folder)) missing.Add(p);
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
        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
    }

    static void Pump()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        if (current == null)
        {
            var failed = SessionState.GetString(Failed, "");
            current = Missing().Find(p => !failed.Contains($"[{p.id}]"));
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
            return;
        }

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

        Debug.Log($"Asset packs: importing {current.name} {current.version}");
        AssetDatabase.ImportPackage(file, false);
        current = null;
    }
}
