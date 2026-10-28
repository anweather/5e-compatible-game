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
        /// <summary>What had been done lately when the run began (coverage.json then): a replay picks its goals against this, not today's.</summary>
        public Dictionary<string, int> Coverage { get; set; } = new Dictionary<string, int>();
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
        /// The trace step the bot writes every SyncEvery frames: the world tick and where the wizard
        /// is. Two runs of one seed should match on every one, so the first that differs is where
        /// they drifted apart.
        /// </summary>
        public const string Sync = "sync";
        public const int SyncEvery = 30;

        /// <summary>A sync step's detail: the chemistry tick and the wizard to the centimetre.</summary>
        public static string SyncDetail(long tick, float x, float y, float z) =>
            string.Format(CultureInfo.InvariantCulture, "tick {0} at {1:F2},{2:F2},{3:F2}", tick, x, y, z);

        /// <summary>
        /// The seed for UnityEngine.Random in a playtest run, from the run's seed: the game's own
        /// random touches (a thrown item's spin, fire spreading between props, dice for damage) then
        /// come out the same in a replay.
        /// </summary>
        public static int UnitySeed(ulong seed) => unchecked((int)(seed ^ (seed >> 32)));

        /// <summary>A screenshot step: taken for a goal or a candidate, and a frame spike's is timed by the wall clock, so it isn't compared.</summary>
        public const string Shot = "shot";

        /// <summary>
        /// The first trace step where a replay did something else (a different action or detail, or
        /// at a different frame) up to the given frame, screenshots aside; null if it matches all the way.
        /// </summary>
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

        /// <summary>
        /// Whether the replay saw the same signature within Slack frames of the original, or during
        /// the same goal. Replays aren't bit-exact (loading and physics run on real time in places, so
        /// a long walk can drift), so the same paper cut met while doing the same thing counts too.
        /// </summary>
        public static bool Reproduced(Candidate original, RunReport replay)
        {
            if (original == null || replay == null) return false;
            return replay.Candidates.Any(c => c.Signature == original.Signature &&
                (Math.Abs(c.Frame - original.Frame) <= Slack || (c.Goal != null && c.Goal == original.Goal)));
        }

        /// <summary>Steps to reproduce, in words, from the trace up to a frame: the goals in order, then the last few actions.</summary>
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
