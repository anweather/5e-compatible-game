// punchy-sync: brings Punchy's newest creatures into rules/fauna.json and rules/parts.json
// (README.md). Without --apply it only reports.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PunchySync
{
    public static class Program
    {
        const string Upstream = "https://github.com/Skeleman/Punchy.git";

        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception e) when (e is InvalidOperationException || e is FormatException || e is IOException)
            {
                Console.Error.WriteLine($"punchy-sync: {e.Message}");
                return 1;
            }
        }

        static int Run(string[] args)
        {
            string Opt(string name) => Array.IndexOf(args, name) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("dotnet run --project tools/punchy-sync -- [--apply] [--punchy <clone>] [--rev <commit>] [--base <commit>] [--out <dir>] [--repo <dir>]");
                return 0;
            }
            string repo = Opt("--repo") ?? FindRepo(Directory.GetCurrentDirectory());
            string syncedFile = Path.Combine(repo, "tools", "punchy-sync", "synced.json");
            var synced = JsonNode.Parse(File.ReadAllText(syncedFile)).AsObject();
            string clone = Opt("--punchy") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "punchy-sync", "Punchy");
            Fetch(clone);
            var baseSnap = Snapshot.Read(clone, Opt("--base") ?? (string)synced["commit"]);
            var head = Snapshot.Read(clone, Opt("--rev") ?? "origin/HEAD");
            string outDir = Opt("--out") ?? Path.Combine(Path.GetTempPath(), "punchy-sync");
            Directory.CreateDirectory(outDir);

            var theirs = Export.Rows(head);
            File.WriteAllText(Path.Combine(outDir, "fauna.json"), Whole(theirs.Fauna, Layout.Species));
            File.WriteAllText(Path.Combine(outDir, "parts.json"), Whole(theirs.Parts, Layout.Part));

            string faunaPath = Path.Combine(repo, "rules", "fauna.json"), partsPath = Path.Combine(repo, "rules", "parts.json");
            string faunaText = File.ReadAllText(faunaPath), partsText = File.ReadAllText(partsPath);
            var sync = new Sync(Export.Rows(baseSnap), theirs, JsonNode.Parse(faunaText).AsObject(), JsonNode.Parse(partsText).AsObject());
            sync.Run();

            string report = sync.Report(baseSnap.Commit, head.Commit);
            File.WriteAllText(Path.Combine(outDir, "report.md"), report);
            Console.WriteLine(report);
            Console.WriteLine($"Candidate rows and this report: {outDir}");

            if (!args.Contains("--apply"))
            {
                Console.WriteLine("Nothing written to rules/ (run with --apply to write).");
                return 0;
            }
            File.WriteAllText(faunaPath, Layout.Rewrite(faunaText,
                sync.FaunaChanged.ToDictionary(id => id, id => Layout.Species(id, sync.Fauna[id].AsObject())),
                sync.FaunaAdded.Select(id => Layout.Species(id, sync.Fauna[id].AsObject())).ToList()));
            File.WriteAllText(partsPath, Layout.Rewrite(partsText,
                sync.PartsChanged.ToDictionary(id => id, id => Layout.Part(id, sync.Parts[id].AsObject())),
                sync.PartsAdded.Select(id => Layout.Part(id, sync.Parts[id].AsObject())).ToList()));
            synced["commit"] = head.Commit;
            synced["date"] = Snapshot.Git(clone, "show", "-s", "--format=%cs", head.Commit).Trim();
            File.WriteAllText(syncedFile, synced.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"Wrote rules/fauna.json, rules/parts.json and tools/punchy-sync/synced.json (now {head.Commit[..7]}).");
            return 0;
        }

        static void Fetch(string clone)
        {
            if (!Directory.Exists(Path.Combine(clone, ".git")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(clone));
                Console.Error.WriteLine($"Cloning {Upstream} into {clone} ...");
                Snapshot.Git(Path.GetDirectoryName(clone), "clone", "--quiet", "--no-checkout", Upstream, clone);
            }
            else
            {
                Console.Error.WriteLine($"Fetching Punchy in {clone} ...");
                Snapshot.Git(clone, "fetch", "--quiet", "origin");
                Snapshot.Git(clone, "remote", "set-head", "origin", "--auto");
            }
        }

        static string FindRepo(string dir)
        {
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "rules", "fauna.json"))) return d.FullName;
            throw new InvalidOperationException("run it inside the repo, or give --repo");
        }

        static string Whole(JsonObject rows, Func<string, JsonObject, string> row) =>
            "{\n" + string.Join(",\n", rows.Select(p => row(p.Key, p.Value.AsObject()))) + "\n}\n";
    }

    /// <summary>The merge of both tables and what it found.</summary>
    public sealed class Sync
    {
        readonly Exported baseRows, theirs;
        public readonly JsonObject Fauna, Parts;
        public readonly List<string> FaunaChanged = new(), FaunaAdded = new(), PartsChanged = new(), PartsAdded = new();
        readonly Dictionary<string, List<Change>> changes = new(), conflicts = new();
        readonly Dictionary<string, string> leftOut = new();
        readonly List<string> removedUpstream = new(), ours = new(), unusedParts = new(), notes = new();
        readonly Dictionary<string, List<string>> extras = new();

        public Sync(Exported baseRows, Exported theirs, JsonObject fauna, JsonObject parts)
        {
            this.baseRows = baseRows;
            this.theirs = theirs;
            Fauna = fauna;
            Parts = parts;
        }

        public void Run()
        {
            // New species: those with code we haven't got stay out, and so do variants of what's out.
            var candidates = theirs.Fauna.Select(p => p.Key).Where(id => !Fauna.ContainsKey(id)).ToList();
            foreach (var id in candidates)
                if (theirs.NeedsCode[id].Count > 0) leftOut[id] = "needs " + string.Join(", ", theirs.NeedsCode[id]);
            for (bool more = true; more;)
            {
                more = false;
                foreach (var id in candidates.Where(c => !leftOut.ContainsKey(c)))
                {
                    var of = (string)theirs.Fauna[id]["variantOf"];
                    if (of != null && !Fauna.ContainsKey(of) && (leftOut.ContainsKey(of) || !theirs.Fauna.ContainsKey(of)))
                    {
                        leftOut[id] = $"a variant of {of}, which is left out";
                        more = true;
                    }
                }
            }
            foreach (var id in candidates.Where(c => !leftOut.ContainsKey(c)))
            {
                Fauna[id] = theirs.Fauna[id].DeepClone();
                FaunaAdded.Add(id);
            }

            // Species we have: field by field, our spawn untouched.
            foreach (var (id, row) in Fauna.ToList())
            {
                if (FaunaAdded.Contains(id)) continue;
                var t = theirs.Fauna[id] as JsonObject;
                var b = baseRows.Fauna[id] as JsonObject;
                if (t == null)
                {
                    (b != null ? removedUpstream : ours).Add(id);
                    continue;
                }
                if (theirs.NeedsCode[id].Count > 0) notes.Add($"{id}: upstream's now needs {string.Join(", ", theirs.NeedsCode[id])}; ours draws without it");
                var mine = row.DeepClone().AsObject();
                var spawn = mine["spawn"]?.DeepClone();
                mine.Remove("spawn");
                List<Change> c = new(), x = new();
                var merged = Merge.Three(b, mine, t, id, c, x).AsObject();
                if (spawn != null) merged["spawn"] = spawn;
                if (x.Count > 0) conflicts[id] = x;
                if (c.Count == 0) continue;
                var wings = Slots(merged).Where(theirs.UnsupportedParts.Contains).ToList();
                if (wings.Count > 0)
                {
                    notes.Add($"{id}: upstream gave it {string.Join(", ", wings)}, a part our view can't draw; kept ours");
                    continue;
                }
                Fauna[id] = merged;
                changes[id] = c;
                FaunaChanged.Add(id);
            }

            // Hunts naming species we haven't got.
            foreach (var id in FaunaAdded.Concat(FaunaChanged))
                if (Fauna[id]["behave"]?["hunts"] is JsonArray hunts)
                {
                    var gone = hunts.Select(h => (string)h).Where(h => !Fauna.ContainsKey(h)).ToList();
                    if (gone.Count == 0) continue;
                    var kept = hunts.Select(h => (string)h).Where(Fauna.ContainsKey).ToList();
                    if (kept.Count > 0) Fauna[id]["behave"]["hunts"] = new JsonArray(kept.Select(h => (JsonNode)h).ToArray());
                    else Fauna[id]["behave"].AsObject().Remove("hunts");
                    notes.Add($"{id}: hunts {string.Join(", ", gone)} dropped (not in our rows)");
                }

            // Parts: changed upstream and not by us; new ones only as a species wears them.
            foreach (var (id, row) in Parts.ToList())
            {
                if (!(theirs.Parts[id] is JsonObject t)) continue;
                List<Change> c = new(), x = new();
                var merged = Merge.Three(baseRows.Parts[id], row, t, id, c, x);
                if (x.Count > 0) conflicts["part " + id] = x;
                if (c.Count == 0) continue;
                Parts[id] = merged;
                changes["part " + id] = c;
                PartsChanged.Add(id);
            }
            var worn = Fauna.SelectMany(p => Slots(p.Value.AsObject())).ToHashSet();
            foreach (var (id, row) in theirs.Parts)
            {
                if (Parts.ContainsKey(id)) continue;
                if (!worn.Contains(id))
                {
                    unusedParts.Add(id);
                    continue;
                }
                Parts[id] = row.DeepClone();
                PartsAdded.Add(id);
            }

            foreach (var id in FaunaAdded.Concat(FaunaChanged))
                if (theirs.Extras.TryGetValue(id, out var e))
                    foreach (var f in e) (extras.TryGetValue(f, out var l) ? l : extras[f] = new List<string>()).Add(id);
        }

        static IEnumerable<string> Slots(JsonObject row) =>
            (row["parts"] as JsonObject)?.Select(s => (string)s.Value?["part"]).Where(p => p != null) ?? Enumerable.Empty<string>();

        public string Report(string baseCommit, string headCommit)
        {
            var r = new StringBuilder();
            r.AppendLine($"# punchy-sync: Punchy {baseCommit[..7]} → {headCommit[..7]}");
            r.AppendLine();
            r.AppendLine($"- {FaunaAdded.Count} new species, {FaunaChanged.Count} species changed, {leftOut.Count} left out (need code), {conflicts.Count} rows with conflicts");
            r.AppendLine($"- {PartsAdded.Count} new parts, {PartsChanged.Count} parts changed, {unusedParts.Count} new parts unused");
            r.AppendLine();
            Section(r, "New species", FaunaAdded.Select(id => $"{id} ({Shape(id)})"));
            Section(r, "Left out: needs code we haven't got (ported by hand first, ThirdParty/Punchy/README.md)", leftOut.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {p.Value}"));
            Section(r, "Changed upstream, taken (we hadn't edited these)", changes.SelectMany(p => p.Value.SelectMany(Merge.Fields).Select(c => $"{c.Path}: {Show(c.Ours)} → {Show(c.Theirs)}")));
            Section(r, "Conflicts: both changed, ours kept", conflicts.SelectMany(p => p.Value.Select(c => $"{c.Path}: ours {Show(c.Ours)}, upstream {Show(c.Base)} → {Show(c.Theirs)}")));
            Section(r, "Notes", notes);
            Section(r, "Upstream features on these species that our rows don't carry (they work without them)", extras.OrderByDescending(p => p.Value.Count).Select(p => $"{p.Key}: {p.Value.Count} ({string.Join(", ", p.Value.Take(8))}{(p.Value.Count > 8 ? ", ..." : "")})"));
            Section(r, "New parts", PartsAdded);
            Section(r, "New upstream parts no species of ours wears (not added)", unusedParts);
            Section(r, "Gone upstream, kept here", removedUpstream);
            Section(r, "Ours alone (never from Punchy, untouched)", ours);
            return r.ToString();
        }

        string Shape(string id)
        {
            var row = Fauna[id];
            if (row["variantOf"] != null) return $"a variant of {row["variantOf"]}";
            var habitats = row["move"]?["habitats"] is JsonArray h ? " " + string.Join("/", h.Select(x => (string)x)) : "";
            return $"{(row["parts"] as JsonObject)?.Count ?? 0} slots{habitats}";
        }

        static void Section(StringBuilder r, string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return;
            r.AppendLine($"## {title}");
            r.AppendLine();
            foreach (var l in list) r.AppendLine($"- {l}");
            r.AppendLine();
        }

        static string Show(JsonNode n) => n == null ? "(none)" : Layout.Inline(n);
    }
}
