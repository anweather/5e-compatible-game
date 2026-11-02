// What the rules MCP's tools do, without the MCP: the rules in force, the drafts in
// memory, and the read, check and save steps (docs/RULES_MCP.md). RulesTools
// wraps it for the server; the tests in Game.Core.Tests call it directly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>A tool call that can't be done, with one sentence saying why.</summary>
    public sealed class RulesToolException : Exception
    {
        public RulesToolException(string message) : base(message) { }
    }

    public sealed partial class Workbench
    {
        /// <summary>materials.json's three tables, the kinds of material.</summary>
        public static readonly string[] MaterialKinds = { "terrain", "covers", "gases" };

        readonly Dictionary<string, Draft> drafts = new Dictionary<string, Draft>(StringComparer.Ordinal);
        int nextDraft = 1;

        public Workbench(string rulesDir, string modsDir = null)
        {
            RulesDir = rulesDir;
            ModsDir = modsDir ?? Path.Combine(rulesDir, "mods");
            Base = Rules.Load(rulesDir);
            Budgets = ModBudgets.Load(rulesDir);
            baseRaw = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(rulesDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                baseRaw[Path.GetFileNameWithoutExtension(path)] = JToken.Parse(File.ReadAllText(path));
            PutLayers(Array.Empty<RoomMod>());
        }

        public string RulesDir { get; }
        public string ModsDir { get; }
        public Rules Base { get; }
        public ModBudgets Budgets { get; }

        /// <summary>
        /// The folder holding rules/ found by walking up from here (the repo root's rules/), for a
        /// server started anywhere inside the repo.
        /// </summary>
        public static string FindRulesDir(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "rules");
                if (File.Exists(Path.Combine(candidate, "materials.json"))) return candidate;
            }
            throw new DirectoryNotFoundException("No rules/ folder above " + start + "; pass --rules <folder>.");
        }

        // ---------- drafts ----------

        public Draft DraftOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                var d = new Draft("d" + nextDraft++);
                drafts[d.Id] = d;
                return d;
            }
            if (drafts.TryGetValue(id, out var draft)) return draft;
            throw new RulesToolException($"There is no draft {id}; leave the draft out to start a new one.");
        }

        /// <summary>
        /// The patch to check or run: a draft's id, a mod's name in rules/mods, or the patch JSON itself
        /// (bare or {"title", "idea", "patch"}).
        /// </summary>
        public string PatchJson(string draftOrMod)
        {
            var key = (draftOrMod ?? "").Trim();
            if (key.StartsWith('{')) return key;
            if (drafts.TryGetValue(key, out var d)) return d.Patch.ToString(Formatting.None);
            var file = Path.Combine(ModsDir, key + ".json");
            if (key.Length > 0 && key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(file)) return File.ReadAllText(file);
            throw new RulesToolException($"{(key.Length == 0 ? "Nothing" : key)} is neither a draft, a mod in rules/mods nor a patch.");
        }

        /// <summary>The rules in force with a patch on as well, or the reason it can't switch on.</summary>
        public Rules RulesWith(string patchJson)
        {
            try
            {
                return InForce.WithMods(new[] { ModPatch.Parse(patchJson) });
            }
            catch (ModException e)
            {
                throw new RulesToolException("The patch can't switch on: " + string.Join(" ", e.Errors.Select(m => m.TrimEnd('.') + ".")));
            }
        }

        // ---------- validate ----------

        public IReadOnlyList<ModProblem> Validate(string draftOrMod) => WithSite(PatchJson(draftOrMod));

        // The mod's checks, then whether its buildings can stand on the world as it is now.
        IReadOnlyList<ModProblem> WithSite(string json)
        {
            var problems = ModValidator.Validate(InForce, Budgets, json);
            string site = problems.Count == 0 ? SiteNote(json) : null;
            return site == null ? problems : problems.Append(new ModProblem(ModCheck.Site, site.TrimEnd('.') + ".")).ToList();
        }

        public static string Report(IReadOnlyList<ModProblem> problems)
        {
            if (problems.Count == 0) return "No problems: the mod passes shape, references, the design rules, determinism and the budgets.";
            return $"{problems.Count} problem{(problems.Count == 1 ? "" : "s")}:\n" +
                string.Join("\n", problems.Select((p, i) => $"{i + 1}. [{p.Check.ToString().ToLowerInvariant()}] {p.Message}"));
        }

        // ---------- save ----------

        /// <summary>
        /// Writes the draft as rules/mods/{name}.json, {"title", "idea", "patch"}, once it validates.
        /// Returns the path written.
        /// </summary>
        public string Save(string draftId, string title, string idea, string name = null, bool overwrite = false)
        {
            var draft = DraftOf(Required(draftId, "draft"));
            if (string.IsNullOrWhiteSpace(title)) throw new RulesToolException("A mod needs a title.");
            if (string.IsNullOrWhiteSpace(idea)) throw new RulesToolException("A mod needs its idea in words: that is what people vote on.");
            var problems = WithSite(draft.Patch.ToString(Formatting.None));
            if (problems.Count > 0) throw new RulesToolException("The draft doesn't validate yet. " + Report(problems));
            name = string.IsNullOrWhiteSpace(name) ? Slug(title) : name.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
                throw new RulesToolException($"\"{name}\" can't be a file name; use letters, digits and dashes.");
            var path = Path.Combine(ModsDir, name + ".json");
            if (File.Exists(path) && !overwrite) throw new RulesToolException($"rules/mods/{name}.json already exists; pick another name or save with overwrite.");
            var mod = new JObject { ["title"] = title.Trim(), ["idea"] = idea.Trim(), ["patch"] = draft.Patch.DeepClone() };
            Directory.CreateDirectory(ModsDir);
            File.WriteAllText(path, mod.ToString(Formatting.Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            Desk?.Saved(path);
            return path;
        }

        static string Slug(string title) =>
            string.Join("-", new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        internal static string Required(string value, string what)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new RulesToolException($"Say which {what}.");
            return value.Trim();
        }
    }

    /// <summary>A mod patch held in memory under an id while it's composed; Changed is what it holds, by "table id".</summary>
    public sealed class Draft
    {
        public Draft(string id) { Id = id; }

        public string Id { get; }
        public JObject Patch { get; } = new JObject();

        /// <summary>The entries this draft added or changed, as "table id" (sim parameters as "sim path").</summary>
        public ISet<string> Changed { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public string Show() => $"Draft {Id}:\n{Patch.ToString(Formatting.Indented)}";
    }
}
