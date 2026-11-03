// Which materials in a room's rules are temporary (docs/design/primitives.md §4, J-062):
// every terrain, cover or gas row the rules in force have and the base rules don't,
// owned by the first mod on that adds it. Pure: a RuleSet in, a list out, in mod
// order, then table order (Rules.MaterialTables), then the patch's own order, so every
// peer lists them alike. World.Lapse.cs sweeps and lapses them. Another mod may name
// one while its owner is on, and goes with it (#86): Named says which one names.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A temporary material: its table and id, the mod that adds it and its lapse.</summary>
    public readonly struct TempMaterial
    {
        public TempMaterial(string table, string id, string mod, MaterialLapse lapse)
        {
            Table = table;
            Id = id;
            Mod = mod;
            Lapse = lapse;
        }

        public string Table { get; }
        public string Id { get; }
        public string Mod { get; }
        public MaterialLapse Lapse { get; }
    }

    public static class TempMaterials
    {
        /// <summary>The temporary materials of these rules, in mod, table and patch order; empty with no mods on.</summary>
        public static IReadOnlyList<TempMaterial> Of(RuleSet set)
        {
            var list = new List<TempMaterial>();
            if (set == null) return list;
            var seen = new HashSet<string>();
            foreach (var m in set.Mods)
                if (TryPatch(m.Json, out var patch))
                    foreach (var table in Rules.MaterialTables) AddOwn(set, m.Id, table, patch[table] as JObject, seen, list);
            return list;
        }

        // A mod's rows in one table that are new over the base and still in force, unless an earlier mod added them.
        static void AddOwn(RuleSet set, string mod, string table, JObject rows, HashSet<string> seen, List<TempMaterial> list)
        {
            if (rows == null) return;
            foreach (var p in rows.Properties())
                if (!set.Base.HasMaterial(table, p.Name) && set.Effective.HasMaterial(table, p.Name) && seen.Add(table + ":" + p.Name))
                    list.Add(new TempMaterial(table, p.Name, mod, set.Effective.LapseOf(table, p.Name)));
        }

        /// <summary>
        /// The ids among these that a patch (bare or {"title", "patch"}) names, in ordinal order: a row of one
        /// of its tables by that id, or a string anywhere in it. Empty for a patch that can't be read.
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
            foreach (var table in ModPatch.Tables)
                if (patch[table] is JObject rows)
                    named.UnionWith(rows.Properties().Select(p => p.Name).Where(ids.Contains));
            foreach (var v in patch.Descendants().OfType<JValue>())
                if (v.Type == JTokenType.String && ids.Contains((string)v)) named.Add((string)v);
        }

        /// <summary>The temporary materials by id, each with its mod: a mod naming one goes when that mod does (World.Lapse.cs).</summary>
        public static IReadOnlyDictionary<string, string> Owners(IEnumerable<TempMaterial> temps)
        {
            var owners = new Dictionary<string, string>();
            foreach (var t in temps) owners[t.Id] = t.Mod;
            return owners;
        }

        static bool TryPatch(string json, out JObject patch)
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
