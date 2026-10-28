using System;
using System.IO;
using System.Linq;

// playtest compare a/run.json b/run.json: whether two runs of one seed did the
// same thing, frame by frame (playtest/DESIGN.md, exact replays). The sync
// steps (the tick and the wizard every second) show the first frame where
// they parted, and what each was doing then.
namespace Playtest.Runner
{
    public static class Compare
    {
        public static int Run(string a, string b)
        {
            if (a == null || b == null || !File.Exists(a) || !File.Exists(b))
            {
                Console.Error.WriteLine("usage: playtest compare <run.json> <run.json>");
                return 2;
            }
            var (same, text) = Describe(RunReport.FromJson(File.ReadAllText(a)), RunReport.FromJson(File.ReadAllText(b)));
            Console.WriteLine(text);
            return same ? 0 : 1;
        }

        /// <summary>Whether the runs match, and in words: how far they matched and, if they parted, where and doing what.</summary>
        public static (bool same, string text) Describe(RunReport a, RunReport b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            int upTo = Math.Min(a.Frames, b.Frames);
            if (a.Seed != b.Seed) return (false, $"different seeds ({a.Seed}, {b.Seed}): not the same run");
            var diff = ReplayCheck.FirstDifference(a, b, upTo);
            if (diff == null) return (true, $"the same for all {upTo} frames ({a.Trace.Count(s => s.Frame <= upTo && s.Action == ReplayCheck.Sync)} sync steps)");
            string Doing(RunReport r) => r.Trace.LastOrDefault(s => s.Frame <= diff.Frame && s.Action == "goal")?.Detail ?? "starting";
            string At(RunReport r) => r.Trace.FirstOrDefault(s => s.Frame >= diff.Frame)?.ToString().Trim() ?? "(nothing)";
            return (false, $"parted at frame {diff.Frame} ({diff.Frame / 30.0:0.0} s)\n  a: {At(a)}   while: {Doing(a)}\n  b: {At(b)}   while: {Doing(b)}");
        }
    }
}
