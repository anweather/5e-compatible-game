using System;
using System.IO;
using UnityEngine;

// Which copy of the game this is: the version (Application.version, from
// bundleVersion in Player Settings), the commit it was built from and when.
// A player build reads the commit and date from StreamingAssets/build.json,
// which PrototypeBuild writes at build time; the Editor asks git, and counts
// as "Editor" in the join handshake (BuildVersion.Mismatch). Logged once at
// startup, shown in the Esc menu (GameInfoMenu).
public static class BuildInfo
{
    [Serializable]
    public class StampFile
    {
        public string version;
        public string commit;
        public string date;
    }

    static BuildVersion.Stamp stamp;
    static string builtOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        stamp = null;
        builtOn = null;
    }

    public const string FileName = "build.json";

    public static BuildVersion.Stamp Stamp
    {
        get
        {
            if (stamp == null) Load();
            return stamp;
        }
    }

    public static string Label => Stamp.Label;

    public static void Pretend(string version, string commit, bool editor)
    {
        stamp = new BuildVersion.Stamp { Version = version, Commit = commit, Editor = editor };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LogAtStartup()
    {
        Load();
        Debug.Log($"{Application.productName} {Label}{(stamp.Editor ? ", in the Editor" : "")}, built {builtOn ?? "now"}, Unity {Application.unityVersion}");
    }

    static void Load()
    {
        if (Application.isEditor)
        {
            stamp = new BuildVersion.Stamp { Version = Application.version, Commit = GitCommit(), Editor = true };
            return;
        }
        StampFile file = null;
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (File.Exists(path)) file = JsonUtility.FromJson<StampFile>(File.ReadAllText(path));
        }
        catch (IOException e) { Debug.LogWarning($"Couldn't read {FileName}: {e.Message}"); }
        catch (ArgumentException e) { Debug.LogWarning($"Couldn't read {FileName}: {e.Message}"); }
        builtOn = file?.date;
        stamp = new BuildVersion.Stamp { Version = Application.version, Commit = file?.commit ?? "unknown", Editor = false };
    }

    static string GitCommit()
    {
#if UNITY_EDITOR
        return RunGit("rev-parse --short=7 HEAD") ?? "";
#else
        return "";
#endif
    }

#if UNITY_EDITOR
    static readonly string[] GitHomes =
    {
        "C:/Program Files/Git/cmd/git.exe", "C:/Program Files (x86)/Git/cmd/git.exe",
        "/opt/homebrew/bin/git", "/usr/local/bin/git", "/usr/bin/git",
    };

    static string GitExe()
    {
        string name = Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer ? "git.exe" : "git";
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0 || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
            if (File.Exists(Path.Combine(dir, name))) return "git";
        }
        foreach (string home in GitHomes)
            if (File.Exists(home)) return home;
        return "git";
    }

    public static string RunGit(string arguments)
    {
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GitExe(), arguments)
            {
                WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            string output = git.StandardOutput.ReadToEnd();
            git.WaitForExit(5000);
            return git.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
        {
            return null;
        }
    }
#endif
}
