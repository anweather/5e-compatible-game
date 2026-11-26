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

        public string Detector { get; }
        public string Key { get; }
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public Category Category { get; }
        public int Frame { get; }
        public string Message { get; }
        public bool LowConfidence { get; }
        public string Goal { get; set; }
        public string Screenshot { get; set; }
        public string KnownIssue { get; set; }

        public string Signature => $"{Detector}:{Key}";

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "[{0}] {1} at frame {2}: {3}", Category, Signature, Frame, Message);
    }

    public sealed class KnownIssue
    {
        public string Name { get; set; }
        public string Detector { get; set; }
        public string MessageContains { get; set; }
        public string FixedIn { get; set; }
        public string Note { get; set; }

        public bool Matches(Candidate c) =>
            c != null && c.Detector == Detector &&
            (string.IsNullOrEmpty(MessageContains) || (c.Message != null && c.Message.Contains(MessageContains, StringComparison.OrdinalIgnoreCase)));
    }

    public sealed class KnownIssues
    {
        readonly IReadOnlyList<KnownIssue> all;
        readonly Func<string, bool> buildHas;

        public KnownIssues(IReadOnlyList<KnownIssue> all, Func<string, bool> buildHas = null)
        {
            this.all = all ?? Array.Empty<KnownIssue>();
            this.buildHas = buildHas ?? (_ => false);
        }

        public static IReadOnlyList<KnownIssue> FromJson(string json) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<List<KnownIssue>>(json) ?? new List<KnownIssue>();

        public bool Mark(Candidate c)
        {
            if (c == null) return false;
            var hit = all.FirstOrDefault(k => k.Matches(c) && (k.FixedIn == null || !buildHas(k.FixedIn)));
            c.KnownIssue = hit?.Name;
            return hit != null;
        }
    }

    public sealed class CandidateLog
    {
        readonly List<Candidate> firsts = new List<Candidate>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly KnownIssues known;

        public CandidateLog(KnownIssues known = null) => this.known = known;

        public IReadOnlyList<Candidate> Candidates => firsts;
        public int CountOf(string signature) => counts.TryGetValue(signature, out int n) ? n : 0;

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
