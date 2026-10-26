using System;
using System.Threading.Tasks;
using UnityEngine;
using Velopack;
using Velopack.Sources;

// Updates the game in place with Velopack, when it was installed by Velopack's
// installer (a zip copy, a development build and the Editor can't, and keep the
// "Open download page" path in UpdateCheck). Velopack starts first thing at
// launch, so its install and update hooks run before the game does; the check
// looks at the newest pre-release on GitHub for this install's channel (or the
// folder or URL given with -update-source, for testing), downloads only what
// changed when it can, then quits and lets Velopack swap the files and start the
// new version. tools/ship-update.sh publishes those updates.
// Velopack's work runs off the main thread; its results land in plain fields
// that UpdateCheck and the Esc menu read each frame.
public static class SelfUpdate
{
    const string SourceFlag = "-update-source";

    static UpdateManager manager;
    static UpdateInfo pending;

    /// <summary>True when this copy was installed by Velopack and can update itself.</summary>
    public static bool Installed { get; private set; }

    /// <summary>
    /// Whether an update found at launch is downloaded and applied straight away (the
    /// default: everyone playing together needs the same version); -no-auto-update
    /// leaves it to the Esc menu's button.
    /// </summary>
    public static bool AutoUpdate => Array.IndexOf(Environment.GetCommandLineArgs(), "-no-auto-update") < 0;

    /// <summary>The version Velopack installed, or null when this copy isn't a Velopack install.</summary>
    public static string CurrentVersion => Installed ? manager.CurrentVersion?.ToString() : null;

    /// <summary>Download progress, 0 to 100, while an update is downloading; -1 otherwise.</summary>
    public static int Progress { get; private set; } = -1;

    /// <summary>Why the last check or download failed, or null.</summary>
    public static string Error { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Boot()
    {
        if (Application.isEditor) return;
        try
        {
            // Handles Velopack's install/update hooks (it exits the process for those) and
            // finishes an update that was waiting for a restart.
            VelopackApp.Build().Run();
            manager = new UpdateManager(Source(), null, null);
            Installed = manager.IsInstalled;
            Debug.Log(Installed
                ? $"Self update: installed by Velopack as {manager.AppId} {manager.CurrentVersion}."
                : "Self update: not a Velopack install; updates come from the download page.");
        }
        catch (Exception e)
        {
            Installed = false;
            Debug.Log($"Self update: Velopack didn't start ({e.Message}); updates come from the download page.");
        }
    }

    // GitHub's pre-releases, or the folder or URL after -update-source.
    static IUpdateSource Source()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == SourceFlag)
                return args[i + 1].StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new SimpleWebSource(args[i + 1])
                    : new SimpleFileSource(new System.IO.DirectoryInfo(args[i + 1]));
        return new GithubSource(BuildVersion.RepoUrl, null, true, null);
    }

    /// <summary>
    /// Asks the update source for a newer version: its X.Y.Z, or null when there is none.
    /// Velopack runs on a worker (off Unity's main-thread context), and a check that takes
    /// longer than CheckSeconds fails rather than waiting forever.
    /// </summary>
    public static async Task<string> CheckAsync()
    {
        if (!Installed) return null;
        Error = null;
        var check = Task.Run(() => manager.CheckForUpdatesAsync());
        if (await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(CheckSeconds))).ConfigureAwait(false) != check)
            throw new TimeoutException($"no answer from the update source in {CheckSeconds} s");
        pending = await check.ConfigureAwait(false);
        return pending?.TargetFullRelease.Version.ToString();
    }

    const int CheckSeconds = 30;

    /// <summary>
    /// Downloads the update found by CheckAsync, then quits so Velopack can apply it and
    /// restart the game. Call it from the main thread (a button): the await comes back
    /// there, where Application.Quit is allowed.
    /// </summary>
    public static async Task DownloadAndRestart()
    {
        if (pending == null || Progress >= 0) return;
        Error = null;
        Progress = 0;
        try
        {
            await Task.Run(() => manager.DownloadUpdatesAsync(pending, p => Progress = p, default));
            Progress = 100;
            Debug.Log($"Self update: downloaded {pending.TargetFullRelease.Version}; restarting into it.");
            manager.WaitExitThenApplyUpdates(pending.TargetFullRelease, false, true, Array.Empty<string>());
            Application.Quit();
        }
        catch (Exception e)
        {
            Progress = -1;
            Error = e.Message;
            Debug.Log($"Self update: the download failed ({e.Message}).");
        }
    }
}
