// Live mods over the rule tables: composeRules from sandbox/src/rules.ts,
// done on the JSON so merging means exactly what it means in the sandbox, plus
// a fingerprint of the result for the netcode checksum. Switching rules on a
// running world is World.ApplyRules.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        string fingerprint;

        /// <summary>
        /// These rules with each mod applied in order, checked the way the sandbox checks them
        /// before switching them on. Pure: these rules are unchanged. A late joiner rebuilds a
        /// room's rules the same way, from the base rules and the room's mods in order.
        /// Throws <see cref="ModException"/> listing every problem if the result doesn't fit together.
        /// </summary>
        public Rules WithMods(IEnumerable<ModPatch> mods)
        {
            var r = (JObject)doc.DeepClone();
            foreach (var mod in mods) ApplyPatch(r, mod.Json);
            var errors = Check(r);
            if (errors.Count > 0) throw new ModException(errors);
            try
            {
                return FromDoc(r);
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
            {
                // Check covers what the tables read; this is the backstop for anything it misses.
                throw new ModException($"The rules can't be read with these mods: {e.Message}");
            }
        }

        /// <summary>
        /// A stable hash of these rules: FNV-1a over a canonical form (object keys sorted, numbers
        /// by value, so key order and 3 vs 3.0 don't matter, list order does). Every machine that
        /// applied the same mods in the same order gets the same value, so it can go into the
        /// netcode checksum next to World.CellHash. Edits made to <see cref="Sim"/> in code are not in it.
        /// </summary>
        public string Fingerprint => fingerprint ??= Fnv1a.Hash(Canonical(doc));

        /// <summary>The effective rules as JSON, in the shape of the sandbox's Rules object (for tools and tests).</summary>
        public string ToJson() => doc.ToString(Formatting.Indented);

        // ---------- merging: composeRules, one patch at a time, in the TS order ----------

        static void ApplyPatch(JObject r, JObject p)
        {
            var elements = (JArray)r["elements"];
            foreach (var e in Strings(p["elements"]).Where(e => !elements.Any(x => (string)x == e))) elements.Add(e);
            // Object.assign: a named entry replaces the old one whole.
            foreach (var table in ModPatch.Tables)
            {
                if (!(p[table] is JObject entries)) continue;
                foreach (var entry in entries.Properties()) r[table][entry.Name] = entry.Value.DeepClone();
            }
            if (p["reactions"] is JObject rx)
            {
                var drop = new HashSet<string>(Strings(rx["remove"]));
                foreach (var list in new[] { r["cellRules"], r["entityRules"] })
                    foreach (var rule in list.Where(x => drop.Contains(Text(x["id"]))).ToList()) rule.Remove();
                Upsert((JArray)r["cellRules"], rx["add"], true);
                Upsert((JArray)r["entityRules"], rx["addEntity"], true);
            }
            Upsert((JArray)r["spells"], p["spells"], false);
            Upsert((JArray)r["critters"], p["critters"], false);
            if (p["sim"] is JObject sim) MergeDeep((JObject)r["sim"], sim);
        }

        /// <summary>
        /// upsertRules / upsertById: an entry whose id is already listed replaces it in place. A new
        /// reaction rule goes to the top of its element's rules, so it wins; anything else goes last.
        /// </summary>
        static void Upsert(JArray list, JToken add, bool byElement)
        {
            if (!(add is JArray items)) return;
            foreach (var item in items)
            {
                int i = IndexOf(list, "id", item["id"]);
                if (i >= 0)
                {
                    list[i] = item.DeepClone();
                    continue;
                }
                int at = byElement ? IndexOf(list, "element", item["element"]) : -1;
                list.Insert(at >= 0 ? at : list.Count, item.DeepClone());
            }
        }

        static int IndexOf(JArray list, string key, JToken value)
        {
            for (int i = 0; i < list.Count; i++)
                if (Text(list[i][key]) == Text(value)) return i;
            return -1;
        }

        /// <summary>mergeDeep: objects merge key by key; anything else (numbers, lists, null) replaces.</summary>
        static void MergeDeep(JObject into, JObject from)
        {
            foreach (var p in from.Properties())
            {
                if (p.Value is JObject sub && into[p.Name] is JObject cur) MergeDeep(cur, sub);
                else into[p.Name] = p.Value.DeepClone();
            }
        }

        /// <summary>A string value, or null for anything else (where the TS compares with ===).</summary>
        static string Text(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        // ---------- fingerprint ----------

        static string Canonical(JToken t)
        {
            var sb = new StringBuilder();
            WriteCanonical(sb, t);
            return sb.ToString();
        }

        // Length-prefixed strings and numbers as IEEE bits: unambiguous, and the same on Mono and .NET.
        static void WriteCanonical(StringBuilder sb, JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    sb.Append('{');
                    foreach (var p in ((JObject)t).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        WriteString(sb, p.Name);
                        WriteCanonical(sb, p.Value);
                    }
                    sb.Append('}');
                    break;
                case JTokenType.Array:
                    sb.Append('[');
                    foreach (var x in t) WriteCanonical(sb, x);
                    sb.Append(']');
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    sb.Append('#').Append(BitConverter.DoubleToInt64Bits((double)t).ToString("x16", CultureInfo.InvariantCulture));
                    break;
                case JTokenType.String:
                    WriteString(sb, (string)t);
                    break;
                case JTokenType.Boolean:
                    sb.Append((bool)t ? 'T' : 'F');
                    break;
                default:
                    sb.Append('N');
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s) =>
            sb.Append('s').Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s);
    }
}
