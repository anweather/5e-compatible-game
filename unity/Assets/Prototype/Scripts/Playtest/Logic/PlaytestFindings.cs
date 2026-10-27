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
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    /// <summary>One entry of playtest/known-issues.json.</summary>
    public sealed class KnownIssue
    {
        public string Name { get; set; }
        public string Detector { get; set; }
        /// <summary>Text the candidate's message contains (any case).</summary>
        public string MessageContains { get; set; }
        /// <summary>The commit that fixed it: on a build with it, the issue is a regression and is filed. Null while unfixed.</summary>
        public string FixedIn { get; set; }
        public string Note { get; set; }

        public bool Matches(Candidate c) =>
            c != null && c.Detector == Detector &&
            (string.IsNullOrEmpty(MessageContains) || (c.Message != null && c.Message.Contains(MessageContains, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Issues already decided (playtest/known-issues.json, the same file tools/playtest/run.ps1
    /// reads): seen and counted, not filed, on builds that don't have their fix yet.
    /// </summary>
    public sealed class KnownIssues
    {
        readonly IReadOnlyList<KnownIssue> all;
        readonly Func<string, bool> buildHas;

        /// <param name="all">The known issues (FromJson of playtest/known-issues.json).</param>
        /// <param name="buildHas">Whether the build has a commit (git merge-base --is-ancestor); unknown counts as not.</param>
        public KnownIssues(IReadOnlyList<KnownIssue> all, Func<string, bool> buildHas = null)
        {
            this.all = all ?? Array.Empty<KnownIssue>();
            this.buildHas = buildHas ?? (_ => false);
        }

        public static IReadOnlyList<KnownIssue> FromJson(string json) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<List<KnownIssue>>(json) ?? new List<KnownIssue>();

        /// <summary>Marks the candidate if a known issue covers it on this build; true if one does.</summary>
        public bool Mark(Candidate c)
        {
            if (c == null) return false;
            var hit = all.FirstOrDefault(k => k.Matches(c) && (k.FixedIn == null || !buildHas(k.FixedIn)));
            c.KnownIssue = hit?.Name;
            return hit != null;
        }
    }

    /// <summary>Keeps one candidate per signature in a run (the first), counting the rest.</summary>
    public sealed class CandidateLog
    {
        readonly List<Candidate> firsts = new List<Candidate>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly KnownIssues known;

        public CandidateLog(KnownIssues known = null) => this.known = known;

        public IReadOnlyList<Candidate> Candidates => firsts;
        public int CountOf(string signature) => counts.TryGetValue(signature, out int n) ? n : 0;

        /// <summary>Adds it; true if its signature is new in this run (so worth a screenshot).</summary>
        public bool Add(Candidate c)
        {
            if (c == null) return false;
            known?.Mark(c);
            counts[c.Signature] = CountOf(c.Signature) + 1;
            if (counts[c.Signature] > 1) return false;
            firsts.Add(c);
            return true;
        }
    }
}
