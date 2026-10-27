using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The players tools/ship-update.sh packs with Velopack: a macOS app and a Windows
// folder, both non-development builds stamped with the update's version. The
// version only lives in Player Settings for the build; it's put back afterwards,
// so the tree stays clean (a release still bumps bundleVersion in git itself).
public static class UpdateBuild
{
    /// <summary>
    /// Builds Builds/Update/osx/Spell Chemistry Valley.app (unless <paramref name="mac"/> is false:
    /// an Editor without Mac Build Support) and Builds/Update/win/SpellChemistryValley.exe at the
    /// given version. Refuses a dirty git tree. Returns a one-line summary that starts
    /// "Update build: OK" on success.
    /// </summary>
    public static string Players(string version, bool mac = true)
    {
        if (BuildVersion.Parse(version) == null) return Fail($"'{version}' isn't an X.Y.Z version.");
        var dirty = PrototypeBuild.DirtyFiles();
        if (dirty == null || dirty.Length > 0)
            return Fail(dirty == null ? "couldn't ask git whether the tree is clean." : $"the git tree has changes ({string.Join(", ", dirty.Take(8))}). Commit them first.");

        const string root = "Builds/Update";
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string was = PlayerSettings.bundleVersion;
        PlayerSettings.bundleVersion = version;
        PackRulesIntoBuild.VersionOnlyInMemory = was != version;
        try
        {
            string osx = mac ? BuildOnce(BuildTarget.StandaloneOSX, $"{root}/osx/{PrototypeBuild.ProductName}.app") : ": OK";
            if (!osx.Contains(": OK")) return Fail(osx);
            string win = BuildOnce(BuildTarget.StandaloneWindows64, $"{root}/win/SpellChemistryValley.exe");
            if (!win.Contains(": OK")) return Fail(win);
            // Burst's debug information is for us, not for players ("DoNotShip").
            foreach (var debug in Directory.GetDirectories($"{root}/win", "*_DoNotShip")) Directory.Delete(debug, true);
            File.WriteAllText($"{root}/win/CREDITS.txt", Credits.PlainText($"alpha {version}"));
        }
        finally
        {
            PackRulesIntoBuild.VersionOnlyInMemory = false;
            PlayerSettings.bundleVersion = was;
            AssetDatabase.SaveAssets();
        }
        var after = PrototypeBuild.DirtyFiles();
        string warning = after != null && after.Length > 0 ? $" WARNING: the build changed tracked files: {string.Join(", ", after)}" : "";
        string done = $"Update build: OK, {version} for {(mac ? "macOS and Windows" : "Windows")} -> {Path.GetFullPath(root)}{warning}";
        Debug.Log(done);
        return done;
    }

    // The first build after switching platform can fail on an import; once more fixes it.
    static string BuildOnce(BuildTarget target, string path)
    {
        string line = PrototypeBuild.Build(target, path);
        return line.Contains(": OK") ? line : PrototypeBuild.Build(target, path);
    }

    static string Fail(string why)
    {
        string line = "Update build: FAILED, " + why;
        Debug.LogError(line);
        return line;
    }
}
