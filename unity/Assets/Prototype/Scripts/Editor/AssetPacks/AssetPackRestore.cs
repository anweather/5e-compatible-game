using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

// Asset Store packs are dependencies, like Packages/manifest.json: the
// repo lists them in AssetPacks.json and ignores their folders, and this
// restores any that are missing. A pack comes from the local Asset Store
// cache (see CacheRoot) when it's there, otherwise it is
// downloaded first. Imports are non-interactive.
//
// Importing a pack with scripts reloads the domain, so the restore keeps
// its progress in SessionState and picks up again after each reload.
//
// With no Editor window (CI), FromCommandLine restores from the cache or
// -asset-cache <folder> (laid out like it), never downloading, and exits.
// ImportPackage only queues the import, so run it without -quit, and with
// -ignorecompilererrors: the game's scripts don't compile without the packs.
[InitializeOnLoad]
public static class AssetPackRestore
{
    const string ManifestPath = "AssetPacks.json";
    const string Active = "AssetPackRestore.active";
    const string Failed = "AssetPackRestore.failed";
    const string Attempted = "AssetPackRestore.attempted";
    const string CommandLine = "AssetPackRestore.commandLine";
    const string CacheOverride = "AssetPackRestore.cache";
    const double DownloadTimeout = 1800;

    [Serializable]
    internal class Pack
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
    // %APPDATA%\Unity\Asset Store-5.x on Windows. FromCommandLine's -asset-cache replaces it.
    static string CacheRoot => SessionState.GetString(CacheOverride, "") is { Length: > 0 } folder ? folder : EditorCache;

    static string EditorCache => Application.platform == RuntimePlatform.WindowsEditor
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Unity", "Asset Store-5.x");

    /// <summary>True while a restore is downloading or importing.</summary>
    public static bool Busy => SessionState.GetBool(Active, false);

    /// <summary>True when every pack in the manifest is in the project.</summary>
    public static bool AllPresent => Missing().Count == 0;

    /// <summary>Raised when a restore ends (with or without failures).</summary>
    public static event Action Finished;

    /// <summary>The folders the manifest's packs live in ("Assets/Starter Assets", ...).</summary>
    internal static List<string> Folders() =>
        File.Exists(ManifestPath)
            ? JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).packs.ConvertAll(p => p.folder)
            : new List<string>();

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

    /// <summary>
    /// For -executeMethod (no -quit): restores the missing packs from the cache, with no download, then logs
    /// ASSET PACKS: OK or FAILED and, in batch mode, exits 0 or 1.
    /// </summary>
    public static void FromCommandLine()
    {
        string cache = CacheArgument(Environment.GetCommandLineArgs()) ?? CacheRoot;
        var missing = Missing();
        string failed = CheckCache(missing, cache);
        if (failed != null || missing.Count == 0)
        {
            EndCommandLine(failed);
            return;
        }
        Debug.Log($"Asset packs: restoring {missing.Count} from {cache}");
        SessionState.SetString(CacheOverride, cache);
        SessionState.SetBool(CommandLine, true);
        Restore();
    }

    /// <summary>The folder after -asset-cache, made absolute, or null without one.</summary>
    internal static string CacheArgument(string[] args)
    {
        int at = Array.IndexOf(args, "-asset-cache");
        return at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : null;
    }

    /// <summary>Null when every pack in <paramref name="missing"/> has its file in <paramref name="cache"/>, otherwise what isn't there.</summary>
    internal static string CheckCache(List<Pack> missing, string cache)
    {
        if (missing.Count == 0) return null;
        if (!Directory.Exists(cache)) return $"no cache folder {cache}";
        var absent = missing.FindAll(p => !File.Exists(Path.Combine(cache, p.cache)));
        return absent.Count == 0 ? null : $"not in {cache}: {string.Join(", ", absent.ConvertAll(p => p.cache))}";
    }

    static void EndCommandLine(string failed)
    {
        SessionState.SetBool(CommandLine, false);
        SessionState.EraseString(CacheOverride);
        Debug.Log(failed == null ? "ASSET PACKS: OK" : $"ASSET PACKS: FAILED {failed}");
        if (Application.isBatchMode) EditorApplication.Exit(failed == null ? 0 : 1);
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
            Finished?.Invoke();
            if (SessionState.GetBool(CommandLine, false))
            {
                var still = Missing();
                EndCommandLine(still.Count == 0 ? null : $"still missing: {string.Join(", ", still.ConvertAll(p => p.folder))}");
            }
            return;
        }
        started = lastChange = EditorApplication.timeSinceStartup;
        lastSize = -1;
        if (!File.Exists(Path.Combine(CacheRoot, current.cache)))
        {
            if (SessionState.GetBool(CommandLine, false))
            {
                Debug.LogError($"Asset packs: {current.cache} isn't in {CacheRoot}, and the command line never downloads.");
                SessionState.SetString(Failed, failed + $"[{current.id}]");
                current = null;
                return;
            }
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
