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
    public sealed class RulesToolException : Exception
    {
        public RulesToolException(string message) : base(message) { }
    }

    public sealed partial class Workbench
    {
        public static readonly string[] MaterialKinds = { "terrain", "covers", "gases", Rules.LiquidsKey };

        readonly Dictionary<string, Draft> drafts = new Dictionary<string, Draft>(StringComparer.Ordinal);
        int nextDraft = 1;

        public Workbench(GameFolders folders)
        {
            Folders = folders;
            RulesDir = folders.Rules;
            ShippedModsDir = folders.ShippedMods;
            ModsDir = folders.Mods;
            Base = Rules.Load(RulesDir);
            Budgets = ModBudgets.Load(folders);
            baseRaw = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(RulesDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                baseRaw[Path.GetFileNameWithoutExtension(path)] = JToken.Parse(File.ReadAllText(path));
            PutLayers(Array.Empty<RoomMod>());
        }

        public GameFolders Folders { get; }
        public string RulesDir { get; }
        public string ModsDir { get; }
        public string ShippedModsDir { get; }
        public Rules Base { get; }
        public ModBudgets Budgets { get; }

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

        public string PatchJson(string draftOrMod)
        {
            var key = (draftOrMod ?? "").Trim();
            if (key.StartsWith('{')) return key;
            if (drafts.TryGetValue(key, out var d)) return d.Patch.ToString(Formatting.None);
            var file = key.Length > 0 && key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? ModCatalog.FileOf(key, ModsDir, ShippedModsDir) : null;
            if (file != null) return File.ReadAllText(file);
            throw new RulesToolException($"{(key.Length == 0 ? "Nothing" : key)} is neither a draft, a mod in rules/mods nor a patch.");
        }

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

        public IReadOnlyList<ModProblem> Validate(string draftOrMod)
        {
            drafts.TryGetValue((draftOrMod ?? "").Trim(), out var draft);
            return WithSite(PatchJson(draftOrMod), OwnMod(draft));
        }

        IReadOnlyList<ModProblem> WithSite(string json, string own = null)
        {
            var others = Temporary.Where(t => t.Value != own).ToDictionary(t => t.Key, t => t.Value);
            var problems = ModValidator.Validate(InForce, Budgets, json, others);
            string site = problems.Count == 0 ? SiteNote(json, own) : null;
            return site == null ? problems : problems.Append(new ModProblem(ModCheck.Site, site.TrimEnd('.') + ".")).ToList();
        }

        public static string Report(IReadOnlyList<ModProblem> problems)
        {
            if (problems.Count == 0) return "No problems: the mod passes shape, references, the design rules, determinism and the budgets.";
            return $"{problems.Count} problem{(problems.Count == 1 ? "" : "s")}:\n" +
                string.Join("\n", problems.Select((p, i) => $"{i + 1}. [{p.Check.ToString().ToLowerInvariant()}] {p.Message}"));
        }

        public Func<string> SaveRefused { get; set; }

        public string Save(string draftId, string title, string idea, string name = null, bool overwrite = false)
        {
            string refused = SaveRefused?.Invoke();
            if (refused != null) throw new RulesToolException(refused);
            string key = Required(draftId, "draft");
            var onNow = drafts.ContainsKey(key) ? null : Now()?.Room.Mods.Find(m => m.Id == key);
            var draft = onNow != null ? null : DraftOf(key);
            var patch = onNow != null ? ModPatch.Parse(onNow.Json).Json : draft.Patch;
            if (string.IsNullOrWhiteSpace(title)) throw new RulesToolException("A mod needs a title.");
            if (string.IsNullOrWhiteSpace(idea)) throw new RulesToolException("A mod needs its idea in words: that is what people vote on.");
            var problems = onNow != null ? Array.Empty<ModProblem>() : WithSite(patch.ToString(Formatting.None), OwnMod(draft));
            if (problems.Count > 0) throw new RulesToolException("The draft doesn't validate yet. " + Report(problems));
            name = string.IsNullOrWhiteSpace(name) ? Slug(title) : name.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
                throw new RulesToolException($"\"{name}\" can't be a file name; use letters, digits and dashes.");
            var path = Path.Combine(ModsDir, name + ".json");
            if (ModCatalog.FileOf(name, ModsDir, ShippedModsDir) != null && !overwrite) throw new RulesToolException($"There's already a mod called {name}; pick another name or save with overwrite.");
            var mod = new JObject { ["title"] = title.Trim(), ["idea"] = idea.Trim(), ["patch"] = patch.DeepClone() };
            try
            {
                Directory.CreateDirectory(ModsDir);
                File.WriteAllText(path, mod.ToString(Formatting.Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new RulesToolException($"The mod can't be written to {ModsDir}: {e.Message}");
            }
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

    public sealed class Draft
    {
        public Draft(string id) { Id = id; }

        public string Id { get; }
        public JObject Patch { get; } = new JObject();

        public ISet<string> Changed { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public string OnAs { get; private set; }

        public string OnJson { get; private set; }

        public void WentOn(string mod, string json)
        {
            OnAs = mod;
            OnJson = json;
        }

        public string Show() => $"Draft {Id}:\n{Patch.ToString(Formatting.Indented)}";

        public string Brief() => Changed.Count <= MostShown ? Show() : $"Draft {Id} holds {string.Join(", ", Changed)}.";

        const int MostShown = 2;
    }
}
