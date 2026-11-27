using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

// One playtest run as written to <run>/run.json (playtest/DESIGN.md): the
// seed and build that make it replayable, the trace of what the bot did frame
// by frame, the candidates its detectors saw, and a few numbers. A replay is
// the same seed on the same build; ReplayCheck says whether it stayed the
// same run up to a frame, and whether a candidate turned up again there.
namespace Playtest
{
    public sealed class TraceStep
    {
        public int Frame { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0,6} {1} {2}", Frame, Action, Detail);
    }

    public sealed class RunReport
    {
        public string RunId { get; set; }
        public ulong Seed { get; set; }
        public string Build { get; set; }
        public string Started { get; set; }
        public string ReplayOf { get; set; }
        public int Frames { get; set; }
        public float RealSeconds { get; set; }
        public bool Quiet { get; set; }
        public List<string> Goals { get; set; } = new List<string>();
        public Dictionary<string, int> Coverage { get; set; } = new Dictionary<string, int>();
        public List<TraceStep> Trace { get; set; } = new List<TraceStep>();
        public List<Candidate> Candidates { get; set; } = new List<Candidate>();
        public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, float> Metrics { get; set; } = new Dictionary<string, float>();
        public string Ended { get; set; }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static RunReport FromJson(string json) => JsonConvert.DeserializeObject<RunReport>(json);
    }

    public static class ReplayCheck
    {
        public const int Slack = 30;

        public const string Sync = "sync";
        public const int SyncEvery = 30;

        public static string SyncDetail(long tick, float x, float y, float z) =>
            string.Format(CultureInfo.InvariantCulture, "tick {0} at {1:F2},{2:F2},{3:F2}", tick, x, y, z);

        public static int UnitySeed(ulong seed) => unchecked((int)(seed ^ (seed >> 32)));

        public const string Shot = "shot";

        public static TraceStep FirstDifference(RunReport original, RunReport replay, int upToFrame)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            var a = original.Trace.Where(s => s.Frame <= upToFrame && s.Action != Shot).ToList();
            var b = replay.Trace.Where(s => s.Frame <= upToFrame && s.Action != Shot).ToList();
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
                if (a[i].Frame != b[i].Frame || a[i].Action != b[i].Action || a[i].Detail != b[i].Detail) return b[i];
            if (a.Count == b.Count) return null;
            return a.Count > b.Count ? a[b.Count] : b[a.Count];
        }

        public static bool Reproduced(Candidate original, RunReport replay)
        {
            if (original == null || replay == null) return false;
            return replay.Candidates.Any(c => c.Signature == original.Signature &&
                (Math.Abs(c.Frame - original.Frame) <= Slack || (c.Goal != null && c.Goal == original.Goal)));
        }

        public static IEnumerable<string> Steps(RunReport run, int upToFrame, int lastActions = 6)
        {
            if (run == null) yield break;
            var before = run.Trace.Where(s => s.Frame <= upToFrame).ToList();
            foreach (var g in before.Where(s => s.Action == "goal")) yield return Seconds(g.Frame) + " " + g.Detail;
            var actions = before.Where(s => s.Action != "goal" && s.Action != Shot && s.Action != Sync).ToList();
            foreach (var s in actions.Skip(Math.Max(0, actions.Count - lastActions)))
                yield return Seconds(s.Frame) + "   then " + s.Action + " " + s.Detail;
        }

        static string Seconds(int frame) => string.Format(CultureInfo.InvariantCulture, "[{0:0.0} s]", frame / 30.0);
    }
}
