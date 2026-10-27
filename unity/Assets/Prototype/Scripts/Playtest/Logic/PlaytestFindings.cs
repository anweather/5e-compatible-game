using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// What the playtest bot reports (playtest/DESIGN.md, sections 3 and 4): a
// candidate is something a detector saw, with a signature that stays the same
// each time the same paper cut turns up, so triage can count it and dedupe
// it. Known issues (already decided, being fixed) are recorded as such and
// never filed again.
namespace Playtest
{
    /// <summary>The backlog's categories, most severe first (the order findings are ranked in).</summary>
    public enum Category { GameBreaking, Gameplay, Physics, Performance, Visual, Sound }

    public sealed class Candidate
    {
        public Candidate(string detector, string key, Category category, int frame, string message, bool lowConfidence = false)
        {
            Detector = detector;
            Key = key;
            Category = category;
            Frame = frame;
            Message = message;
            LowConfidence = lowConfidence;
        }

        /// <summary>Which detector saw it ("exception", "wizard-fell", ...).</summary>
        public string Detector { get; }
        /// <summary>What it saw, stable across runs: an exception's type and top frame, a place, a spell.</summary>
        public string Key { get; }
        public Category Category { get; }
        /// <summary>The frame it was seen on (frames are locked to 1/30 s of game time, so replays line up).</summary>
        public int Frame { get; }
        public string Message { get; }
        /// <summary>Probably the bot's own doing (stuck, say): filed only after a Claude look.</summary>
        public bool LowConfidence { get; }
        /// <summary>The run's goal when it was seen, and the screenshot taken for it (set by the bot).</summary>
        public string Goal { get; set; }
        public string Screenshot { get; set; }
        /// <summary>Set when it matches a known issue: recorded, never filed.</summary>
        public string KnownIssue { get; set; }

        /// <summary>The same paper cut gives the same signature in every run.</summary>
        public string Signature => $"{Detector}:{Key}";

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "[{0}] {1} at frame {2}: {3}", Category, Signature, Frame, Message);
    }

    /// <summary>Issues already decided and being fixed (COORDINATION.md / JUDGMENT_CALLS.md): seen, counted, not filed.</summary>
    public static class KnownIssues
    {
        static readonly (string name, Func<Candidate, bool> matches)[] All =
        {
            // The D3D12 fault while the player quits; Gameplay is making the game end itself on quit.
            ("quit-crash-d3d12", c => c.Detector == "crash" && c.Message != null && c.Message.IndexOf("D3D12Core", StringComparison.OrdinalIgnoreCase) >= 0),
        };

        /// <summary>Marks the candidate if a known issue covers it; true if one does.</summary>
        public static bool Mark(Candidate c)
        {
            if (c == null) return false;
            var hit = All.FirstOrDefault(k => k.matches(c));
            c.KnownIssue = hit.name;
            return hit.name != null;
        }
    }

    /// <summary>Keeps one candidate per signature in a run (the first), counting the rest.</summary>
    public sealed class CandidateLog
    {
        readonly List<Candidate> firsts = new List<Candidate>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public IReadOnlyList<Candidate> Candidates => firsts;
        public int CountOf(string signature) => counts.TryGetValue(signature, out int n) ? n : 0;

        /// <summary>Adds it; true if its signature is new in this run (so worth a screenshot).</summary>
        public bool Add(Candidate c)
        {
            if (c == null) return false;
            KnownIssues.Mark(c);
            counts[c.Signature] = CountOf(c.Signature) + 1;
            if (counts[c.Signature] > 1) return false;
            firsts.Add(c);
            return true;
        }
    }
}
