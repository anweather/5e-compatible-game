using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Player builds of the Chemistry Yard for macOS and Windows, into Builds/
// (gitignored). The game reads rules/*.json and world/*.json at runtime, so
// every build gets a copy in StreamingAssets/rules and StreamingAssets/world
// first (also gitignored: rules/ and world/ are the source of truth).
public static class PrototypeBuild
{
    public const string ProductName = "Spell Chemistry Valley";
    const string Scene = "Assets/Prototype/Scenes/ChemistryYard.unity";

    [MenuItem("Prototype/Build/macOS")]
    public static void MacOS() => Build(BuildTarget.StandaloneOSX, $"Builds/macOS/{ProductName}.app");

    [MenuItem("Prototype/Build/Windows")]
    public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SpellChemistryValley.exe");

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

// Copies rules/*.json and world/*.json (the props and items, world/things.json)
// into StreamingAssets/rules and StreamingAssets/world before any player build.
class PackRulesIntoBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Pack("rules");
        Pack("world");
        AssetDatabase.Refresh();
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
