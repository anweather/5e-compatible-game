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
    /// an Editor without Mac Build Support) and Builds/Update/win/SpellChemistryValley.exe (unless
    /// <paramref name="windows"/> is false: the Mac's half of a release the PC builds for Windows)
    /// at the given version. Refuses a dirty git tree. Returns a one-line summary that starts
    /// "Update build: OK" on success.
    /// </summary>
    public static string Players(string version, bool mac = true, bool windows = true)
    {
        string refused = Refusal(version, mac, windows);
        if (refused != null) return Fail(refused);

        const string root = "Builds/Update";
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string was = PlayerSettings.bundleVersion;
        PlayerSettings.bundleVersion = version;
        PackRulesIntoBuild.VersionOnlyInMemory = was != version;
        try
        {
            string osx = mac ? BuildOnce(BuildTarget.StandaloneOSX, $"{root}/osx/{PrototypeBuild.ProductName}.app") : ": OK";
            if (!osx.Contains(": OK")) return Fail(osx);
            string win = windows ? BuildWindows($"{root}/win", version) : ": OK";
            if (!win.Contains(": OK")) return Fail(win);
        }
        finally
        {
            PackRulesIntoBuild.VersionOnlyInMemory = false;
            PlayerSettings.bundleVersion = was;
            AssetDatabase.SaveAssets();
        }
        var after = PrototypeBuild.DirtyFiles();
        string warning = after != null && after.Length > 0 ? $" WARNING: the build changed tracked files: {string.Join(", ", after)}" : "";
        string done = $"Update build: OK, {version} for {Platforms(mac, windows)} -> {Path.GetFullPath(root)}{warning}";
        Debug.Log(done);
        return done;
    }

    // Why the build can't start, or null when it can.
    static string Refusal(string version, bool mac, bool windows)
    {
        if (!mac && !windows) return "nothing to build: both macOS and Windows are off.";
        if (BuildVersion.Parse(version) == null) return $"'{version}' isn't an X.Y.Z version.";
        var dirty = PrototypeBuild.DirtyFiles();
        if (dirty == null) return "couldn't ask git whether the tree is clean.";
        return dirty.Length > 0 ? $"the git tree has changes ({string.Join(", ", dirty.Take(8))}). Commit them first." : null;
    }

    // The Windows folder, without Burst's debug information (for us, not players: "DoNotShip")
    // and with the credits beside the exe.
    static string BuildWindows(string folder, string version)
    {
        string line = BuildOnce(BuildTarget.StandaloneWindows64, $"{folder}/SpellChemistryValley.exe");
        if (!line.Contains(": OK")) return line;
        foreach (var debug in Directory.GetDirectories(folder, "*_DoNotShip")) Directory.Delete(debug, true);
        File.WriteAllText($"{folder}/CREDITS.txt", Credits.PlainText($"alpha {version}"));
        return line;
    }

    static string Platforms(bool mac, bool windows)
    {
        if (mac && windows) return "macOS and Windows";
        return mac ? "macOS" : "Windows";
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
