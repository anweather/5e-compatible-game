using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

// `playtest build --ref <r>` (playtest/DESIGN.md, "Release candidates"): the
// bot's player for a release candidate (J-018: main or a tag, when asked; never
// just because dev moved), built in its own checkout
// (.claude/worktrees/playtest-build, detached at origin/<r>) so nobody's Editor
// is locked and its Library keeps builds incremental. It skips a commit it has
// already built (unless --force), and builds only when the preflight passes.
// The checkout is the bot's throwaway tree (COORDINATION.md): reset hard to the
// commit before each build, never committed or pushed; the yard it generates
// there stays there. The Asset
// Store packs are copied in from the main checkout (they aren't in git);
// PlaytestBuild.Build in the Unity project builds the yard and the player.
namespace Playtest.Runner
{
    public static class PlaytestBuild
    {
        public static int Build(Paths paths, bool force, string branch)
        {
            string wt = paths.Checkout;
            string log = Path.Combine(paths.Runs, "build.log");
            Directory.CreateDirectory(paths.Runs);
            void Say(string line)
            {
                string l = $"{DateTime.Now:s} {line}";
                Console.WriteLine(l);
                File.AppendAllText(log, l + Environment.NewLine);
            }

            Git.Run(paths.Main, $"fetch -q origin {branch}");
            string target = Git.Run(paths.Main, $"rev-parse origin/{branch}").Trim();
            string stamp = Path.Combine(wt, "unity", "Builds", "playtest-built-commit.txt");
            if (!force && File.Exists(stamp) && File.ReadAllText(stamp).Trim() == target)
            {
                Say($"up to date at {target}");
                return 0;
            }
            string why = Preflight.Why(Preflight.Now(), 10);
            if (why != null && !force)
            {
                Say($"skipped: {why}");
                return 0;
            }
            if (!Checkout(paths, wt, target, Say)) return 1;
            CopyPacks(paths.Main, wt, Say);
            return RunUnity(wt, target, stamp, Say);
        }

        // The bot's checkout at the commit: made the first time, else reset hard (it's only ever this tree).
        static bool Checkout(Paths paths, string wt, string target, Action<string> say)
        {
            if (Path.GetFileName(wt) != "playtest-build")
            {
                say($"FAILED: refusing to reset {wt}, which isn't the playtest-build checkout");
                return false;
            }
            if (!Directory.Exists(wt))
            {
                say($"making the playtest-build checkout at {wt}");
                return Git.Succeeds(paths.Main, $"worktree add --detach \"{wt}\" {target}");
            }
            if (Git.Succeeds(wt, $"checkout -q --detach {target}") && Git.Succeeds(wt, $"reset -q --hard {target}")) return true;
            say($"FAILED: couldn't reset {wt} to {target}");
            return false;
        }

        // The Asset Store packs (unity/AssetPacks.json), copied from the main checkout where missing.
        static void CopyPacks(string main, string wt, Action<string> say)
        {
            var packs = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(main, "unity", "AssetPacks.json")))["packs"];
            foreach (string folder in packs.Select(p => (string)p["folder"]))
            {
                string from = Path.Combine(main, "unity", folder), to = Path.Combine(wt, "unity", folder);
                if (Directory.Exists(to) || !Directory.Exists(from)) continue;
                say($"copying {folder}");
                CopyTree(from, to);
                if (File.Exists(from + ".meta")) File.Copy(from + ".meta", to + ".meta", true);
            }
        }

        static void CopyTree(string from, string to)
        {
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
        }

        static int RunUnity(string wt, string target, string stamp, Action<string> say)
        {
            string versionLine = File.ReadLines(Path.Combine(wt, "unity", "ProjectSettings", "ProjectVersion.txt")).First(l => l.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
            string version = versionLine.Substring("m_EditorVersion:".Length).Trim();
            string unity = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
            string unityLog = Path.Combine(wt, "unity", "Builds", "playtest-build.log");
            Directory.CreateDirectory(Path.GetDirectoryName(unityLog));
            say($"building {target} with Unity {version}");
            var clock = Stopwatch.StartNew();
            using var p = Process.Start(new ProcessStartInfo(unity,
                $"-batchmode -quit -projectPath \"{Path.Combine(wt, "unity")}\" -executeMethod PlaytestBuild.Build -logFile \"{unityLog}\"")
            { UseShellExecute = false });
            p.WaitForExit();
            int took = (int)clock.Elapsed.TotalSeconds;
            if (p.ExitCode != 0)
            {
                say($"FAILED after {took} s (exit {p.ExitCode}); see {unityLog}");
                return 1;
            }
            File.WriteAllText(stamp, target);
            say($"built {target} in {took} s");
            return 0;
        }
    }
}
