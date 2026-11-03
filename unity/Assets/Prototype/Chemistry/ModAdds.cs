// What each mod in a room's rules adds (#88): every row of an id table the rules
// in force have and the base rules don't (a material, a style or another building
// table's row, a building row, a species, a part, a trigger, a liquid once there
// is that table), owned by the first mod on that adds it. Another mod naming one,
// by a row of that id or a string anywhere in it, is that mod's dependent and goes
// with it (World.Lapse.cs). Pure: a RuleSet in, a list out, in mod order, then
// table order (Paths), then the patch's own order, so every peer lists them alike.
// TempMaterials is the materials among them, with their lapses.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A row a mod adds: the word for its table ("material", "style", ...), its id and the mod.</summary>
    public readonly struct ModAdd
    {
        public ModAdd(string kind, string id, string mod)
        {
            Kind = kind;
            Id = id;
            Mod = mod;
        }

        public string Kind { get; }
        public string Id { get; }
        public string Mod { get; }
    }

    public static class ModAdds
    {
        /// <summary>An id table: where its rows are in a patch, where in the composed rules, and the word for one.</summary>
        sealed class IdTable
        {
            public string[] InPatch, InRules;
            public string Kind;
        }

        // The id tables, in the order they're listed: the top-level ones (materials among them), the building
        // tables, the building rows and the triggers.
        static readonly IdTable[] Paths = ModPatch.Tables.Select(t => Top(t, Rules.MaterialTables.Contains(t) ? "material" : Word(t)))
            .Concat(BuildingTables.Tables.Select(t => new IdTable { InPatch = new[] { BuildingTables.Key, t }, InRules = new[] { BuildingTables.Key, t }, Kind = t.TrimEnd('s') }))
            .Append(new IdTable { InPatch = new[] { "world", "buildings" }, InRules = new[] { Rules.WorldRowsKey }, Kind = "building" })
            .Append(Top(Rules.TriggersKey, "trigger"))
            .ToArray();

        static IdTable Top(string table, string kind) => new IdTable { InPatch = new[] { table }, InRules = new[] { table }, Kind = kind };

        static string Word(string table) => table switch
        {
            FaunaTables.SpeciesKey => "species",
            "entities" => "entity",
            _ => table.TrimEnd('s'),
        };

        /// <summary>What the mods of these rules add, in mod, table and patch order; empty with no mods on.</summary>
        public static IReadOnlyList<ModAdd> Of(RuleSet set)
        {
            var list = new List<ModAdd>();
            if (set == null || set.Mods.Count == 0) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in set.Mods)
                if (TryPatch(m.Json, out var patch))
                    foreach (var path in Paths) AddOwn(set, m.Id, patch, path, seen, list);
            return list;
        }

        // A mod's rows in one id table that are new over the base and still in force, unless an earlier mod added them.
        static void AddOwn(RuleSet set, string mod, JObject patch, IdTable path, HashSet<string> seen, List<ModAdd> list)
        {
            JObject under = Rows(set.Base.Written, path.InRules), over = Rows(set.Effective.Written, path.InRules);
            foreach (var id in Keys(patch, path.InPatch))
                if (under?[id] == null && over?[id] != null && seen.Add(path.Kind + ":" + id))
                    list.Add(new ModAdd(path.Kind, id, mod));
        }

        /// <summary>
        /// The ids among these that a patch (bare or {"title", "patch"}) names, in ordinal order: a row of one
        /// of its id tables by that id, or a string anywhere in it. Empty for a patch that can't be read.
        /// </summary>
        public static SortedSet<string> Named(string json, ICollection<string> ids)
        {
            var named = new SortedSet<string>(StringComparer.Ordinal);
            if (ids.Count > 0 && TryPatch(json, out var patch)) Named(patch, ids, named);
            return named;
        }

        /// <summary>Named, on a patch already read.</summary>
        public static void Named(JObject patch, ICollection<string> ids, SortedSet<string> named)
        {
            foreach (var path in Paths) named.UnionWith(Keys(patch, path.InPatch).Where(ids.Contains));
            foreach (var v in patch.Descendants().OfType<JValue>())
                if (v.Type == JTokenType.String && ids.Contains((string)v)) named.Add((string)v);
        }

        // The ids of a table's rows in a patch or the rules, in their order.
        static IEnumerable<string> Keys(JObject doc, string[] path) => Rows(doc, path)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();

        static JObject Rows(JObject doc, string[] path)
        {
            JToken at = doc;
            foreach (var key in path) at = (at as JObject)?[key];
            return at as JObject;
        }

        internal static bool TryPatch(string json, out JObject patch)
        {
            try
            {
                patch = ModPatch.Parse(json).Json;
                return true;
            }
            catch (ModException)
            {
                patch = null; // it composed when it went on, so this isn't reached
                return false;
            }
        }
    }
}
