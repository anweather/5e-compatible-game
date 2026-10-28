using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

// `playtest run` (playtest/DESIGN.md, sections 1 and 5): the player with
// -playtest, off-screen and muted in the Windows mixer, watched by its
// heartbeat, into playtest/runs/<id>/. Before it starts, the preflight skips the
// run (logged in playtest/runs/skipped.log) when memory is short, a heavy Unity
// job is running or another playtest holds the lock. Afterwards a crash or a
// hang becomes a candidate (known or not per playtest/known-issues.json and the
// build's commit), and a run that isn't a replay counts its goals in coverage.
// Never -clean-quit: it brings back the old quit crash on purpose.
namespace Playtest.Runner
{
    public static class PlaytestRun
    {
        const int HeartbeatSeconds = 60;

        public static int Run(Paths paths, Dictionary<string, string> options)
        {
            Directory.CreateDirectory(paths.Runs);
            string exe = paths.Player(options.TryGetValue("player", out var given) ? given : null);
            string lockFile = Path.Combine(paths.Runs, ".lock");
            string why = File.Exists(exe) ? Preflight.Why(Preflight.Now(), 8) : $"no player build at {exe}";
            if (why == null && File.Exists(lockFile)) why = "another playtest is running";
            if (why != null && !options.ContainsKey("force"))
            {
                File.AppendAllText(Path.Combine(paths.Runs, "skipped.log"), $"{DateTime.Now:s} skipped: {why}{Environment.NewLine}");
                Console.WriteLine($"skipped: {why}");
                return 0;
            }
            File.WriteAllText(lockFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            try
            {
                return Play(paths, exe, options);
            }
            finally
            {
                File.Delete(lockFile);
            }
        }

        static int Play(Paths paths, string exe, Dictionary<string, string> options)
        {
            options.TryGetValue("replay", out string replay);
            int seconds = Program.IntOption(options, "seconds", 480);
            string id = RunId(options, replay != null);
            string outDir = Path.Combine(paths.Runs, id);
            Directory.CreateDirectory(outDir);

            var started = DateTime.Now;
            var clock = Stopwatch.StartNew();
            // The player's own console output (its boot settings) is read and dropped: it logs to Player.log.
            using var p = Process.Start(new ProcessStartInfo(exe, Arguments(paths, outDir, seconds, options))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p.OutputDataReceived += (_, _) => { };
            p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            var (hung, muted) = Watch(p, Path.Combine(outDir, "heartbeat.txt"), clock, Math.Max(300, seconds * 3));
            System.Threading.Thread.Sleep(2000); // let Windows write any crash report
            int took = (int)clock.Elapsed.TotalSeconds;

            var run = Report.Load(Path.Combine(outDir, "run.json"), id);
            string commit = Paths.BuildCommit(exe);
            var known = new KnownIssues(File.Exists(paths.KnownIssues) ? KnownIssues.FromJson(File.ReadAllText(paths.KnownIssues)) : null,
                fix => commit != "unknown" && Git.Succeeds(paths.Root, $"merge-base --is-ancestor {fix} {commit}"));
            var extra = Report.Ending(run, hung, Crashes.Find(exe, started, p.Id), p.HasExited ? p.ExitCode : -1, took);
            if (extra != null)
            {
                known.Mark(extra);
                run.Candidates.Add(extra);
                File.WriteAllText(Path.Combine(outDir, "run.json"), run.ToJson());
            }
            if (replay == null && run.Goals.Count > 0) Coverage.Update(paths.Coverage, run.Goals);

            Console.WriteLine($"{id}: {run.Ended} in {took} s, build {commit}, seed {run.Seed}, {run.Frames} frames, {run.Goals.Count} goals, {run.Candidates.Count} candidates, muted {muted}");
            foreach (var c in run.Candidates)
                Console.WriteLine($"  [{c.Category}] {c.Signature} at frame {c.Frame}: {c.Message}{(c.KnownIssue != null ? $" (known: {c.KnownIssue})" : "")}");
            return 0;
        }

        static string Arguments(Paths paths, string outDir, int seconds, Dictionary<string, string> options)
        {
            var a = new List<string>
            {
                "-playtest", "-playtest-out", Q(outDir), "-playtest-seconds", seconds.ToString(CultureInfo.InvariantCulture), "-playtest-offscreen",
                "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720", "-logFile", Q(Path.Combine(outDir, "Player.log")),
            };
            if (options.TryGetValue("seed", out var seed)) a.AddRange(new[] { "-playtest-seed", seed });
            if (options.TryGetValue("replay", out var replay)) a.AddRange(new[] { "-playtest-replay", Q(Path.GetFullPath(replay)) });
            if (options.TryGetValue("until-frame", out var until)) a.AddRange(new[] { "-playtest-until-frame", until });
            if (options.TryGetValue("goals", out var goals)) a.AddRange(new[] { "-playtest-goals", goals });
            if (File.Exists(paths.Coverage)) a.AddRange(new[] { "-playtest-coverage", Q(paths.Coverage) });
            // Performance findings only count from runs with no Unity Editor open.
            if (!Preflight.Now().Unity.Any(Preflight.IsEditor)) a.Add("-playtest-quiet");
            return string.Join(" ", a);
        }

        static string Q(string path) => "\"" + path + "\"";

        // The run's folder name: given, or the time it starts (a replay says so).
        static string RunId(Dictionary<string, string> options, bool replay)
        {
            if (options.TryGetValue("id", out var given)) return given;
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return replay ? id + "-replay" : id;
        }

        // Waits for the player, muting it as soon as it has a sound session; stops it if its
        // heartbeat goes quiet for a minute or it overruns. Whether it hung, and whether it was muted.
        static (bool hung, bool muted) Watch(Process p, string heartbeat, Stopwatch clock, int limitSeconds)
        {
            bool muted = false;
            while (!p.WaitForExit(250))
            {
                if (!muted && OperatingSystem.IsWindows()) muted = AppMute.Mute(p.Id) > 0;
                // The heartbeat file's age (a wall-clock difference, not a timing), or how long it has gone without one.
                double age = File.Exists(heartbeat)
                    ? DateTime.UtcNow.Subtract(File.GetLastWriteTimeUtc(heartbeat)).TotalSeconds
                    : clock.Elapsed.TotalSeconds - HeartbeatSeconds;
                if (age > HeartbeatSeconds || clock.Elapsed.TotalSeconds > limitSeconds)
                {
                    p.Kill();
                    return (true, muted);
                }
            }
            return (false, muted);
        }
    }
}
