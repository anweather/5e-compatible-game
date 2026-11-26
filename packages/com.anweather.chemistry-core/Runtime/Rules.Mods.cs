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

        public Rules WithMods(IEnumerable<ModPatch> mods)
        {
            var r = (JObject)(written ?? doc).DeepClone();
            foreach (var mod in mods) ApplyPatch(r, mod.Json);
            var asWritten = (JObject)r.DeepClone();
            var errors = MaterialGroups.Ungroup(r);
            errors.AddRange(Check(r));
            if (errors.Count > 0) throw new ModException(errors);
            try
            {
                var rules = FromDoc(r);
                rules.written = asWritten;
                return rules;
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
            {
                throw new ModException($"The rules can't be read with these mods: {e.Message}");
            }
        }

        public string Fingerprint => fingerprint ??= Fnv1a.Hash(CanonicalRules());

        public bool SameLooks(Rules other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            foreach (var p in doc.Properties())
            {
                if (FaunaTables.Holds(p.Name)) continue;
                if (!(other.doc.Property(p.Name) is JProperty q) || !SameLooks(p, q)) return false;
            }
            return other.doc.Properties().All(q => FaunaTables.Holds(q.Name) || doc.Property(q.Name) != null);
        }

        static bool SameLooks(JProperty a, JProperty b)
        {
            if (IsLookGroup(a) || IsLookGroup(b)) return JToken.DeepEquals(a.Value, b.Value);
            return SameLooks(a.Value, b.Value);
        }

        static bool SameLooks(JToken a, JToken b)
        {
            if (a is JObject oa)
            {
                if (!(b is JObject ob) || oa.Count != ob.Count) return false;
                foreach (var p in oa.Properties())
                    if (!(ob.Property(p.Name) is JProperty q) || !SameLooks(p, q)) return false;
                return true;
            }
            if (a is JArray aa)
            {
                if (!(b is JArray ab) || aa.Count != ab.Count) return false;
                for (int i = 0; i < aa.Count; i++)
                    if (!SameLooks(aa[i], ab[i])) return false;
                return true;
            }
            return true;
        }

        public string ToJson() => doc.ToString(Formatting.Indented);

        internal JObject CopyDoc() => (JObject)doc.DeepClone();

        internal JObject Written => written ?? doc;

        internal static void ApplyPatch(JObject r, JObject p)
        {
            var elements = (JArray)r["elements"];
            foreach (var e in ModPatch.ElementNames(p["elements"]).Where(e => !elements.Any(x => (string)x == e))) elements.Add(e);
            if (p["elements"] is JObject entries)
            {
                if (!(r[ElementLooksKey] is JObject table)) r[ElementLooksKey] = table = new JObject();
                foreach (var entry in entries.Properties()) table[entry.Name] = MergedEntry(table[entry.Name], entry.Value);
            }
            foreach (var table in ModPatch.Tables)
            {
                if (!(p[table] is JObject named)) continue;
                foreach (var entry in named.Properties())
                {
                    if (table == "terrain" || table == "covers") MaterialGroups.FlatOverGroup((JObject)r[table][entry.Name], entry.Value, table == "covers");
                    r[table][entry.Name] = MergedEntry(r[table][entry.Name], entry.Value);
                }
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
            if (p["combos"] is JObject combos) MergeDeep((JObject)r["combos"], combos);
            ApplyBuildings(r, p);
            ApplyThings(r, p);
            ApplyLays(r, p);
            ApplyTriggers(r, p);
        }

        internal static JToken MergedEntry(JToken under, JToken patch)
        {
            if (!(patch is JObject fields)) return patch.DeepClone();
            var merged = under is JObject old ? (JObject)old.DeepClone() : new JObject();
            MergeFields(merged, fields);
            return merged;
        }

        static void MergeFields(JObject into, JObject from)
        {
            foreach (var f in from.Properties())
            {
                if (f.Name == "look" && f.Value is JObject look) MergeLook(into, look);
                else if (f.Value.Type == JTokenType.Null) into.Remove(f.Name);
                else if (f.Value is JObject group)
                {
                    bool had = into[f.Name] is JObject;
                    var fresh = had ? (JObject)into[f.Name] : new JObject();
                    MergeFields(fresh, group);
                    if (!had && (fresh.Count > 0 || group.Count == 0)) into[f.Name] = fresh;
                }
                else into[f.Name] = f.Value.DeepClone();
            }
        }

        static void MergeLook(JObject entry, JObject patch)
        {
            var old = entry["look"] as JObject;
            var before = old?["scoped"] as JArray;
            var look = new JObject();
            bool sameFamily = patch["family"] == null || Text(patch["family"]) == Text(old?["family"]);
            if (old != null && sameFamily) look = (JObject)old.DeepClone();
            if (patch["color"] != null && patch["color"].Type != JTokenType.Null && patch["mix"] == null)
            {
                look.Remove("mix");
                look.Remove("blend");
            }
            foreach (var f in patch.Properties().Where(f => f.Name != "scoped"))
            {
                if (f.Value.Type == JTokenType.Null) look.Remove(f.Name);
                else if (f.Value is JObject group && look[f.Name] is JObject cur) MergeFields(cur, group);
                else look[f.Name] = f.Value.DeepClone();
            }
            var mine = patch["scoped"] as JArray ?? new JArray();
            var kept = new JArray((before ?? new JArray()).Where(b => !mine.Any(m => JToken.DeepEquals(m, b))).Select(b => b.DeepClone()));
            foreach (var m in mine) kept.Add(m.DeepClone());
            if (kept.Count > 0) look["scoped"] = kept;
            else look.Remove("scoped");
            entry["look"] = look;
        }

        static void Upsert(JArray list, JToken add, bool byElement)
        {
            if (!(add is JArray items)) return;
            foreach (var item in items)
            {
                int i = IndexOf(list, "id", item["id"]);
                if (i >= 0)
                {
                    list[i] = MergedEntry(list[i], item);
                    continue;
                }
                int at = byElement ? IndexOf(list, "element", item["element"]) : -1;
                list.Insert(at >= 0 ? at : list.Count, MergedEntry(null, item));
            }
        }

        static JToken KeepingLook(JToken old, JToken entry)
        {
            if (old?["look"] == null || !(entry is JObject e)) return entry;
            if (e["look"] == null) e["look"] = old["look"].DeepClone();
            else if (old["look"]["scoped"] is JArray before && before.Count > 0 && e["look"] is JObject look)
            {
                var mine = look["scoped"] as JArray ?? new JArray();
                var kept = new JArray(before.Where(b => !mine.Any(m => JToken.DeepEquals(m, b))).Select(b => b.DeepClone()));
                foreach (var m in mine) kept.Add(m.DeepClone());
                look["scoped"] = kept;
            }
            return entry;
        }

        static int IndexOf(JArray list, string key, JToken value)
        {
            for (int i = 0; i < list.Count; i++)
                if (Text(list[i][key]) == Text(value)) return i;
            return -1;
        }

        static void MergeDeep(JObject into, JObject from)
        {
            foreach (var p in from.Properties())
            {
                if (p.Value is JObject sub && into[p.Name] is JObject cur) MergeDeep(cur, sub);
                else into[p.Name] = p.Value.DeepClone();
            }
        }

        static string Text(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        string CanonicalRules()
        {
            var shared = new JObject(doc.Properties().Where(p => !FaunaTables.Holds(p.Name) && !IsEmptyTriggers(p) && !IsEmptyLiquids(p)).Select(p => new JProperty(p.Name, SharedPart(p))));
            var sb = new StringBuilder();
            WriteCanonical(sb, shared);
            return sb.ToString();
        }

        static bool IsEmptyTriggers(JProperty p) => p.Name == TriggersKey && p.Value is JObject t && t.Count == 0;

        static bool IsEmptyLiquids(JProperty p) => p.Name == LiquidsKey && p.Value is JObject t && t.Count == 0;

        static JToken SharedPart(JProperty p)
        {
            if (p.Name == BuildingTables.Key && p.Value is JObject tables) return BuildingRow.SharedTables(tables);
            if (p.Name != WorldRowsKey || !(p.Value is JObject rows)) return p.Value;
            return new JObject(rows.Properties().Select(r => new JProperty(r.Name, BuildingRow.Shared((JObject)r.Value))));
        }

        static void WriteCanonical(StringBuilder sb, JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    sb.Append('{');
                    foreach (var p in ((JObject)t).Properties().Where(p => !IsLookGroup(p)).OrderBy(p => p.Name, StringComparer.Ordinal))
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

        static bool IsLookGroup(JProperty p) => (p.Name == "look" || p.Name == ElementLooksKey) && p.Value is JObject;

        static void WriteString(StringBuilder sb, string s) =>
            sb.Append('s').Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s);
    }
}
