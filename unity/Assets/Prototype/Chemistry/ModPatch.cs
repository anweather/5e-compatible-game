// A live mod: a data-only patch over the rule tables, in the web sandbox's
// format (docs/MODDING.md, ModPatch in sandbox/src/rules.ts). Parse reads one
// and checks its shape; Rules.WithMods (Rules.Mods.cs) applies mods and checks
// that the result fits together.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class ModPatch
    {
        /// <summary>The id tables a mod adds to or replaces entries in, whole entries at a time.</summary>
        internal static readonly string[] Tables = { "terrain", "covers", "gases", "entities", "items", "machines", "groups", "components", FaunaTables.SpeciesKey, FaunaTables.PartsKey };

        ModPatch(string title, JObject patch)
        {
            Title = title;
            Json = patch;
        }

        /// <summary>The title when the JSON was {"title", "patch"}; null for a bare patch.</summary>
        public string Title { get; }

        /// <summary>Never modified: composing clones whatever it takes from here.</summary>
        internal JObject Json { get; }

        /// <summary>
        /// Reads a mod. Like the sandbox's Experiments panel it takes a bare patch or
        /// {"title", "patch"} (what an AI answering the brief replies, and how saved mods look).
        /// Throws <see cref="ModException"/> if it isn't JSON or a section has the wrong type.
        /// </summary>
        public static ModPatch Parse(string json)
        {
            JToken token;
            try
            {
                token = JToken.Parse(json ?? "");
            }
            catch (JsonReaderException e)
            {
                throw new ModException($"That isn't valid JSON: {e.Message}");
            }
            if (!(token is JObject patch)) throw new ModException("A patch is a JSON object; see docs/MODDING.md.");
            string title = null;
            if (patch["patch"] is JObject inner)
            {
                title = patch["title"]?.Type == JTokenType.String ? (string)patch["title"] : null;
                patch = inner;
            }
            var errors = ShapeErrors(patch);
            if (errors.Count > 0) throw new ModException(errors);
            return new ModPatch(title, (JObject)patch.DeepClone());
        }

        /// <summary>
        /// Sections of the wrong type. The TS doesn't check these (a wrong one breaks its merge); a
        /// missing or null section means "no change" on both sides, and unknown ones are ignored.
        /// </summary>
        static List<string> ShapeErrors(JObject p)
        {
            var errors = new List<string>();
            void Expect(string key, bool ok, string what)
            {
                if (Present(p, key) && !ok) errors.Add($"\"{key}\" must be {what}");
            }

            Expect("elements", ListOf(p["elements"], JTokenType.String) || (p["elements"] is JObject es && es.PropertyValues().All(v => v is JObject)),
                "a list of names, or an object of entries by id such as {\"fire\": {\"look\": {\"color\": [0.3, 0.5, 1]}}}");
            foreach (var table in Tables.Where(t => t != "components"))
                Expect(table, p[table] is JObject t && t.PropertyValues().All(v => v is JObject), "an object of entries by id");
            Expect("components", p["components"] is JObject cs && cs.PropertyValues().All(v => v.Type == JTokenType.String), "an object of descriptions by name");
            Expect("reactions", p["reactions"] is JObject, "an object");
            if (p["reactions"] is JObject rx) ReactionShapeErrors(rx, errors);
            Expect("spells", ListOf(p["spells"], JTokenType.Object), "a list of spells");
            Expect("critters", ListOf(p["critters"], JTokenType.Object), "a list of critters");
            Expect("sim", p["sim"] is JObject, "an object");
            Expect("combos", p["combos"] is JObject, "an object");
            Expect(BuildingTables.Key, p[BuildingTables.Key] is JObject b && BuildingTables.Tables.Where(t => b[t] != null).All(t => b[t] is JObject rows && rows.PropertyValues().All(v => v is JObject)),
                "an object of tables (openings, floors, roofs, details, styles), each of rows by id");
            Expect("world", p["world"] is JObject w && w.Properties().All(x => x.Name == "buildings") && w["buildings"] is JObject bs && bs.PropertyValues().All(v => v is JObject),
                "{\"buildings\": {\"<id>\": {a whole building row}}}: an id the world has edits that building, a new one adds one");
            return errors;
        }

        // The reactions section's lists: rules to add, entity rules to add, ids to remove.
        static void ReactionShapeErrors(JObject rx, List<string> errors)
        {
            if (Present(rx, "add") && !ListOf(rx["add"], JTokenType.Object)) errors.Add("\"reactions.add\" must be a list of rules");
            if (Present(rx, "addEntity") && !ListOf(rx["addEntity"], JTokenType.Object)) errors.Add("\"reactions.addEntity\" must be a list of rules");
            if (Present(rx, "remove") && !ListOf(rx["remove"], JTokenType.String)) errors.Add("\"reactions.remove\" must be a list of rule ids");
        }

        /// <summary>
        /// The elements a patch names: a list of names (new ones join the rules), or entries by id, an
        /// element's look ({"fire": {"look": ...}}), which adds the element too if it's new.
        /// </summary>
        internal static IEnumerable<string> ElementNames(JToken elements) => elements switch
        {
            JArray list => list.Where(x => x.Type == JTokenType.String).Select(x => (string)x),
            JObject entries => entries.Properties().Select(x => x.Name),
            _ => Enumerable.Empty<string>(),
        };

        static bool Present(JObject o, string key) => o[key] != null && o[key].Type != JTokenType.Null;
        static bool ListOf(JToken t, JTokenType type) => t is JArray a && a.All(x => x.Type == type);
    }

    /// <summary>A mod that can't be read, or mods that don't fit the rules they're applied over.</summary>
    public sealed class ModException : Exception
    {
        public ModException(IReadOnlyList<string> errors) : base(string.Join("; ", errors)) { Errors = errors; }
        public ModException(string error) : this(new[] { error }) { }

        /// <summary>Every problem found, one sentence each, in the sandbox's wording where it has one.</summary>
        public IReadOnlyList<string> Errors { get; }
    }
}
