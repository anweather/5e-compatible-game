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
        internal static readonly string[] Tables = { "terrain", "covers", "gases", Rules.LiquidsKey, "entities", "items", "machines", "groups", "components", FaunaTables.SpeciesKey, FaunaTables.PartsKey };

        ModPatch(string title, JObject patch)
        {
            Title = title;
            Json = patch;
        }

        public string Title { get; }

        internal JObject Json { get; }

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
            Expect(Rules.TriggersKey, RowsOrNulls(p[Rules.TriggersKey]),
                "an object of triggers by id, such as {\"meteor-steps\": {\"on\": \"step\", \"who\": \"Ann\", \"do\": [{\"cast\": \"meteor\", \"at\": \"ahead\", \"by\": 2}]}}; null takes one out");
            Expect(BuildingTables.Key, p[BuildingTables.Key] is JObject b && BuildingTables.Tables.Where(t => b[t] != null).All(t => b[t] is JObject rows && rows.PropertyValues().All(v => v is JObject)),
                "an object of tables (openings, floors, roofs, details, styles), each of rows by id");
            UnknownBuildingTables(p[BuildingTables.Key] as JObject, errors);
            Expect("world", p["world"] is JObject w && w.Properties().All(x => x.Name == "buildings" || x.Name == "add" || x.Name == "lay")
                    && (w["buildings"] == null || (w["buildings"] is JObject bs && bs.PropertyValues().All(v => v is JObject)))
                    && (w["add"] == null || ListOf(w["add"], JTokenType.Object))
                    && (w["lay"] == null || ListOf(w["lay"], JTokenType.Object)),
                "{\"buildings\": {\"<id>\": {a whole building row}}, \"add\": [{a thing: kind, cell}], \"lay\": [{terrain or cover, cells}]}: an id the world has edits that building, a new one adds one, add puts things in the world and lay sets cells");
            return errors;
        }

        static void UnknownBuildingTables(JObject given, List<string> errors)
        {
            if (given == null) return;
            string tables = string.Join(", ", BuildingTables.Tables);
            foreach (var name in given.Properties().Select(x => x.Name).Where(n => !BuildingTables.Tables.Contains(n)))
                errors.Add(name == "ground"
                    ? $"\"buildings\" can't change ground, the terrain the world's buildings stand on; a mod adds rows to {tables}"
                    : $"\"buildings\" has no table {name}; a mod adds rows to {tables}");
        }

        static bool RowsOrNulls(JToken t) => t is JObject rows && rows.PropertyValues().All(v => v is JObject || v.Type == JTokenType.Null);

        static void ReactionShapeErrors(JObject rx, List<string> errors)
        {
            if (Present(rx, "add") && !ListOf(rx["add"], JTokenType.Object)) errors.Add("\"reactions.add\" must be a list of rules");
            if (Present(rx, "addEntity") && !ListOf(rx["addEntity"], JTokenType.Object)) errors.Add("\"reactions.addEntity\" must be a list of rules");
            if (Present(rx, "remove") && !ListOf(rx["remove"], JTokenType.String)) errors.Add("\"reactions.remove\" must be a list of rule ids");
        }

        internal static IEnumerable<string> ElementNames(JToken elements) => elements switch
        {
            JArray list => list.Where(x => x.Type == JTokenType.String).Select(x => (string)x),
            JObject entries => entries.Properties().Select(x => x.Name),
            _ => Enumerable.Empty<string>(),
        };

        static bool Present(JObject o, string key) => o[key] != null && o[key].Type != JTokenType.Null;
        static bool ListOf(JToken t, JTokenType type) => t is JArray a && a.All(x => x.Type == type);
    }

    public sealed class ModException : Exception
    {
        public ModException(IReadOnlyList<string> errors) : base(string.Join("; ", errors)) { Errors = errors; }
        public ModException(string error) : this(new[] { error }) { }

        public IReadOnlyList<string> Errors { get; }
    }
}
