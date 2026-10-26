using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Player builds of the Chemistry Yard for macOS and Windows, into Builds/
// (gitignored). The game reads rules/*.json and world/*.json at runtime, so
// every build gets a copy in StreamingAssets/rules and StreamingAssets/world
// first (also gitignored: rules/ and world/ are the source of truth), plus
// StreamingAssets/build.json with the commit and date (BuildInfo reads it).
//
// Release builds the zip a player downloads (docs/RELEASING.md): a
// non-development Windows build from a clean git tree, with README.txt and
// CREDITS.txt, zipped into Builds/Release/.
public static class PrototypeBuild
{
    public const string ProductName = "Spell Chemistry Valley";
    const string Scene = "Assets/Prototype/Scenes/ChemistryYard.unity";

    [MenuItem("Prototype/Build/macOS")]
    public static void MacOS() => Build(BuildTarget.StandaloneOSX, $"Builds/macOS/{ProductName}.app");

    [MenuItem("Prototype/Build/Windows")]
    public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SpellChemistryValley.exe");

    const string Exe = "SpellChemistryValley.exe";
    const string ReadmeTemplate = "ReleaseReadme.txt"; // in the project folder, next to Assets/

    /// <summary>
    /// The release zip for Windows players: refuses a dirty git tree (untracked _Recovery*
    /// aside) and unstable network ids, builds a non-development player into
    /// Builds/Release/SpellChemistryValley-alpha-VERSION/, adds README.txt and CREDITS.txt,
    /// and zips it to Builds/Release/SpellChemistryValley-alpha-VERSION-windows.zip.
    /// Returns a one-line summary (also logged).
    /// </summary>
    [MenuItem("Prototype/Build/Release (Windows zip)")]
    public static string Release()
    {
        var dirty = DirtyFiles();
        if (dirty == null || dirty.Length > 0)
        {
            string more = dirty != null && dirty.Length > 8 ? ", ..." : "";
            string refused = dirty == null
                ? "Release: FAILED, couldn't ask git whether the tree is clean."
                : $"Release: FAILED, the git tree has changes ({string.Join(", ", dirty.Take(8))}{more}). Commit or remove them first.";
            Debug.LogError(refused);
            return refused;
        }
        string version = PlayerSettings.bundleVersion;
        string name = $"SpellChemistryValley-alpha-{version}";
        string folder = Path.Combine("Builds", "Release", name);
        string zip = Path.Combine("Builds", "Release", name + "-windows.zip");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        if (File.Exists(zip)) File.Delete(zip);

        string line = Build(BuildTarget.StandaloneWindows64, Path.Combine(folder, Exe));
        if (!line.Contains(": OK")) return "Release: FAILED, " + line;

        // Burst's debug information is for us, not for players ("DoNotShip").
        foreach (var debug in Directory.GetDirectories(folder, "*_DoNotShip")) Directory.Delete(debug, true);
        var stamp = ReadStamp();
        File.WriteAllText(Path.Combine(folder, "CREDITS.txt"), Credits.PlainText($"alpha {version} ({stamp.commit})"));
        File.WriteAllText(Path.Combine(folder, "README.txt"), Readme(version, stamp.commit));
        Zip(folder, name, zip);

        var after = DirtyFiles();
        string warning = after != null && after.Length > 0 ? $" WARNING: the build changed tracked files: {string.Join(", ", after)}" : "";
        string done = $"Release: OK, alpha {version} ({stamp.commit}), {new FileInfo(zip).Length / (1024f * 1024f):0.0} MB -> {Path.GetFullPath(zip)}{warning}";
        Debug.Log(done);
        return done;
    }

    // Changed or untracked files git reports, without Unity's crash-recovery copies; null if git failed.
    internal static string[] DirtyFiles()
    {
        string status = BuildInfo.RunGit("status --porcelain");
        if (status == null) return null;
        return status.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 3 && !(l.StartsWith("??", StringComparison.Ordinal) && l.Contains("_Recovery")))
            .Select(l => l.Substring(3))
            .ToArray();
    }

    static string Readme(string version, string commit)
    {
        string text = File.ReadAllText(ReadmeTemplate).Replace("{VERSION}", version).Replace("{COMMIT}", commit);
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n"); // read in Notepad
    }

    // Zips the folder with its contents under one top-level folder, so unzipping makes one folder.
    static void Zip(string folder, string topLevel, string zip)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string entry = topLevel + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entry, System.IO.Compression.CompressionLevel.Optimal);
        }
    }

    static BuildInfo.StampFile ReadStamp()
    {
        string path = Path.Combine(Application.dataPath, "StreamingAssets", BuildInfo.FileName);
        return File.Exists(path) ? JsonUtility.FromJson<BuildInfo.StampFile>(File.ReadAllText(path)) : new BuildInfo.StampFile { commit = "unknown" };
    }

    /// <summary>Builds the yard for a platform and returns a one-line summary (also logged).</summary>
    public static string Build(BuildTarget target, string path)
    {
        // Another machine can only join a build whose network ids are the stable ones.
        var unstable = ChemYardBuilder.UnstableNetworkIds();
        if (unstable.Count > 0)
        {
            string refused = $"Build {target}: FAILED, the yard's network ids aren't stable ({string.Join(", ", unstable)}). Run Prototype > Build Chemistry Yard first.";
            Debug.LogError(refused);
            return refused;
        }
        PlayerSettings.productName = ProductName;
        PlayerSettings.companyName = "anweather";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.anweather.spellchemistryvalley");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });
        var s = report.summary;
        // Unity can report Succeeded for a build that threw and wrote nothing (seen with a
        // platform module installed but not yet loaded), so check the output too.
        bool written = File.Exists(path) || Directory.Exists(path);
        bool ok = s.result == BuildResult.Succeeded && s.totalErrors == 0 && written;
        string line = $"Build {target}: {(ok ? "OK" : "FAILED")} (Unity says {s.result}), {s.totalErrors} errors, " +
                      $"{s.totalWarnings} warnings, {s.totalSize / (1024f * 1024f):0} MB, {s.totalTime.TotalSeconds:0} s -> " +
                      $"{path}{(written ? "" : " (nothing written)")}";
        if (ok) Debug.Log(line);
        else Debug.LogError(line);
        return line;
    }
}

// Copies rules/*.json, rules/mods/*.json and world/*.json (the props and items,
// world/things.json) into StreamingAssets before any player build,
// and writes StreamingAssets/build.json: the version, the commit (with
// "-dirty" when the tree has changes) and the build date, for BuildInfo.
class PackRulesIntoBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Pack("rules");
        Pack(Path.Combine("rules", "mods")); // the M panel's mods (RuleSwitcher)
        Pack("world");
        WriteStamp();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Set by UpdateBuild while it builds at a version that's only in memory: Unity saves
    /// that version into ProjectSettings.asset during the build, which isn't a real change.
    /// </summary>
    internal static bool VersionOnlyInMemory;

    static void WriteStamp()
    {
        string commit = BuildInfo.RunGit("rev-parse --short=7 HEAD") ?? "unknown";
        string changes = BuildInfo.RunGit("status --porcelain --untracked-files=no" +
            (VersionOnlyInMemory ? " -- . :(exclude)ProjectSettings/ProjectSettings.asset" : ""));
        if (!string.IsNullOrEmpty(changes)) commit += "-dirty";
        var stamp = new BuildInfo.StampFile
        {
            version = PlayerSettings.bundleVersion,
            commit = commit,
            date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
        };
        string target = Path.Combine(Application.dataPath, "StreamingAssets");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, BuildInfo.FileName), JsonUtility.ToJson(stamp, true));
        Debug.Log($"Stamped the build: alpha {stamp.version} ({stamp.commit}), {stamp.date}");
    }

    static void Pack(string folder)
    {
        var source = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", folder));
        var target = Path.Combine(Application.dataPath, "StreamingAssets", folder);
        Directory.CreateDirectory(target);
        foreach (var old in Directory.GetFiles(target, "*.json")) File.Delete(old);
        var files = Directory.GetFiles(source, "*.json");
        foreach (var file in files) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        Debug.Log($"Packed {files.Length} {folder} files into the build: {string.Join(", ", files.Select(Path.GetFileName))}");
    }
}
