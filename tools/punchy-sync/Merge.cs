// A three-way merge of rows, field by field: what Punchy had when we last synced (base), our
// row (ours) and Punchy's now (theirs). A field upstream changed and we didn't takes theirs; one
// we changed keeps ours; one both changed differently is a conflict, kept ours and reported.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PunchySync
{
    public sealed class Change
    {
        public string Path;
        public JsonNode Base, Ours, Theirs;
    }

    public static class Merge
    {
        /// <summary>The merged node; <paramref name="changes"/> gets what came from upstream, <paramref name="conflicts"/> what both sides changed.</summary>
        public static JsonNode Three(JsonNode b, JsonNode o, JsonNode t, string path, List<Change> changes, List<Change> conflicts)
        {
            if (Same(t, b)) return o?.DeepClone();
            if (Same(o, b))
            {
                changes.Add(new Change { Path = path, Base = b, Ours = o, Theirs = t });
                return t?.DeepClone();
            }
            if (Same(o, t)) return o?.DeepClone();
            if (o is JsonObject oo && t is JsonObject tt && (b == null || b is JsonObject))
            {
                var bb = b as JsonObject;
                var oKeys = oo.Select(p => p.Key).ToList();
                var tKeys = tt.Select(p => p.Key).ToList();
                var keys = oKeys.ToHashSet().SetEquals(tKeys) ? oKeys : tKeys.Concat(oKeys.Where(k => !tt.ContainsKey(k))).ToList();
                var merged = new JsonObject();
                foreach (var k in keys)
                {
                    var r = Three(bb?[k], oo[k], tt[k], $"{path}.{k}", changes, conflicts);
                    if (r != null) merged[k] = r;
                }
                return merged;
            }
            if (o is JsonArray oa && t is JsonArray ta && oa.Count == ta.Count && (b == null || (b is JsonArray ba0 && ba0.Count == oa.Count)) && oa.All(x => x is JsonObject))
            {
                var ba = b as JsonArray;
                var merged = new JsonArray();
                for (int i = 0; i < oa.Count; i++) merged.Add(Three(ba?[i], oa[i], ta[i], $"{path}[{i}]", changes, conflicts));
                return merged;
            }
            conflicts.Add(new Change { Path = path, Base = b, Ours = o, Theirs = t });
            return o?.DeepClone();
        }

        /// <summary>A change broken into the fields it touched, for the report.</summary>
        public static IEnumerable<Change> Fields(Change c)
        {
            if (c.Ours is JsonObject o && c.Theirs is JsonObject t)
            {
                foreach (var k in t.Select(p => p.Key).Concat(o.Select(p => p.Key).Where(k => !t.ContainsKey(k))))
                    if (!Same(o[k], t[k]))
                        foreach (var f in Fields(new Change { Path = $"{c.Path}.{k}", Ours = o[k], Theirs = t[k] })) yield return f;
                yield break;
            }
            if (c.Ours is JsonArray oa && c.Theirs is JsonArray ta && oa.Count == ta.Count && oa.All(x => x is JsonObject))
            {
                for (int i = 0; i < oa.Count; i++)
                    if (!Same(oa[i], ta[i]))
                        foreach (var f in Fields(new Change { Path = $"{c.Path}[{i}]", Ours = oa[i], Theirs = ta[i] })) yield return f;
                yield break;
            }
            yield return c;
        }

        /// <summary>Equal as rows: numbers by value, objects by keys (any order), arrays in order.</summary>
        public static bool Same(JsonNode a, JsonNode b)
        {
            if (a == null || b == null) return a == null && b == null;
            switch (a)
            {
                case JsonObject ao when b is JsonObject bo:
                    return ao.Count == bo.Count && ao.All(p => bo.ContainsKey(p.Key) && Same(p.Value, bo[p.Key]));
                case JsonArray aa when b is JsonArray ba:
                    return aa.Count == ba.Count && aa.Zip(ba).All(z => Same(z.First, z.Second));
                case JsonValue av when b is JsonValue bv:
                    if (av.TryGetValue<double>(out var ad) && bv.TryGetValue<double>(out var bd)) return ad == bd;
                    if (av.TryGetValue<bool>(out var ab) && bv.TryGetValue<bool>(out var bb)) return ab == bb;
                    if (av.TryGetValue<string>(out var aS) && bv.TryGetValue<string>(out var bS)) return string.Equals(aS, bS, StringComparison.Ordinal);
                    return av.ToJsonString() == bv.ToJsonString();
                default:
                    return false;
            }
        }
    }
}
