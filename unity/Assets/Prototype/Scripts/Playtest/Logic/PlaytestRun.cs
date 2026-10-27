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
    /// <summary>One thing the bot did, on the frame it did it.</summary>
    public sealed class TraceStep
    {
        public int Frame { get; set; }
        /// <summary>"goal", "move", "jump", "cast", "teleport", "pickup", "throw", "sit", "menu", "shot".</summary>
        public string Action { get; set; }
        public string Detail { get; set; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0,6} {1} {2}", Frame, Action, Detail);
    }

    public sealed class RunReport
    {
        public string RunId { get; set; }
        public ulong Seed { get; set; }
        /// <summary>The build's commit and date (StreamingAssets/build.json).</summary>
        public string Build { get; set; }
        public string Started { get; set; }
        /// <summary>Set for a replay: the run it replays.</summary>
        public string ReplayOf { get; set; }
        public int Frames { get; set; }
        public float RealSeconds { get; set; }
        /// <summary>No Unity Editor was open (performance findings count only from quiet runs).</summary>
        public bool Quiet { get; set; }
        public List<string> Goals { get; set; } = new List<string>();
        public List<TraceStep> Trace { get; set; } = new List<TraceStep>();
        public List<Candidate> Candidates { get; set; } = new List<Candidate>();
        public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, float> Metrics { get; set; } = new Dictionary<string, float>();
        /// <summary>How it ended: "done", "until-frame", "error".</summary>
        public string Ended { get; set; }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static RunReport FromJson(string json) => JsonConvert.DeserializeObject<RunReport>(json);
    }

    public static class ReplayCheck
    {
        /// <summary>Frames either side of the original a replayed candidate may land and still count.</summary>
        public const int Slack = 30;

        /// <summary>
        /// The first trace step where a replay did something else (a different action or detail, or
        /// at a different frame) up to the given frame; null if it matches all the way.
        /// </summary>
        public static TraceStep FirstDifference(RunReport original, RunReport replay, int upToFrame)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            var a = original.Trace.Where(s => s.Frame <= upToFrame).ToList();
            var b = replay.Trace.Where(s => s.Frame <= upToFrame).ToList();
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
                if (a[i].Frame != b[i].Frame || a[i].Action != b[i].Action || a[i].Detail != b[i].Detail) return b[i];
            if (a.Count == b.Count) return null;
            return a.Count > b.Count ? a[b.Count] : b[a.Count];
        }

        /// <summary>Whether the replay saw the same signature within Slack frames of the original.</summary>
        public static bool Reproduced(Candidate original, RunReport replay)
        {
            if (original == null || replay == null) return false;
            return replay.Candidates.Any(c => c.Signature == original.Signature && Math.Abs(c.Frame - original.Frame) <= Slack);
        }

        /// <summary>Steps to reproduce, in words, from the trace up to a frame: the goals in order, then the last few actions.</summary>
        public static IEnumerable<string> Steps(RunReport run, int upToFrame, int lastActions = 6)
        {
            if (run == null) yield break;
            var before = run.Trace.Where(s => s.Frame <= upToFrame).ToList();
            foreach (var g in before.Where(s => s.Action == "goal")) yield return Seconds(g.Frame) + " " + g.Detail;
            foreach (var s in before.Where(s => s.Action != "goal" && s.Action != "shot").Skip(Math.Max(0, before.Count(s => s.Action != "goal" && s.Action != "shot") - lastActions)))
                yield return Seconds(s.Frame) + "   then " + s.Action + " " + s.Detail;
        }

        static string Seconds(int frame) => string.Format(CultureInfo.InvariantCulture, "[{0:0.0} s]", frame / 30.0);
    }
}
