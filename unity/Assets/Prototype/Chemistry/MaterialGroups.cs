// A material's settings grouped by verb (docs/MATERIALS.md, step 1): what
// happens to it when an effect reaches it. materials.json may spell a terrain
// or a cover either way, and the loaders turn the groups into the flat keys
// the rest of the rules read, before checking:
//
//   burn    { catch, fuel, heat, leaves, log, look, intensity }  flammable tag, flammability, fuel, heat, burnsTo, burnLog, fireLook, burnIntensity
//   break   { leaves }                                            breakable tag, breaksTo
//   dig     { hardness, leaves }                                  hardness, dugTo
//   soak    { dry, wet, dampens, leaves, dries }                  soakEvery, soakEveryWet, dampens, soaksTo, driesTo
//   rust    { every, leaves }                                     rustEvery, rustsTo
//   hot     { for, quench }                                       hotFor, hotQuench
//   bake    { into }                                              bakes tag, bakesTo (heat: sand to glass, mud to brick)
//   melt    { into, water }                                       meltsTo, meltWater (covers: what's left, and the water it gives)
//   float   { every, reach }                                      floatEvery, floatReach (covers: oil floats and spreads on open water)
//   freeze  {}  /  boil {}                                        waterTo "ice" / "steam"
//   burn / freeze / soak { status }                               burnStatus, freezeStatus, soakStatus: the ticks of burning,
//                                                                 frozen, wet a creature of it takes (a group holding only
//                                                                 its status makes it neither flammable nor freezing water)
//   conduct {}                                                    conductive tag
//   conduct { chainsWet }                                         chainsWet: a wet creature of it is shocked by charged water
//                                                                 next to it (a group holding only chainsWet doesn't conduct)
//   shatter { leaves }                                            brittle tag (a thing of it breaks when it lands hard), shattersTo (a cell of it: World.Shatter)
//   scald   {}                                                    scalds tag (a creature of it is scalded by steam)
//   grow    { into, log }                                         growsTo, growLog
//   uproot  { leaves, log }                                       uprootsTo, uprootLog
//   dissolve { by, leaves }                                       dissolvedBy, dissolvesTo (covers too; a cover's leaves is a cover, or none)
//
// Group is the other way, for the parity test (and the move to the grouped
// shape): a flat entry as groups, keys moved only where the verb's tag says
// it applies, so the two spellings read the same. Mirrored by
// sandbox/src/materials.ts with the same messages.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class MaterialGroups
    {
        // Each verb: the tag having it implies (or null), and its keys as (group key, flat key).
        static readonly (string verb, string tag, (string group, string flat)[] keys)[] TerrainVerbs =
        {
            ("burn", "flammable", new[] { ("catch", "flammability"), ("fuel", "fuel"), ("heat", "heat"), ("leaves", "burnsTo"), ("log", "burnLog"), ("look", "fireLook") }),
            ("break", "breakable", new[] { ("leaves", "breaksTo") }),
            ("dig", null, new[] { ("hardness", "hardness"), ("leaves", "dugTo") }),
            ("soak", null, new[] { ("dry", "soakEvery"), ("wet", "soakEveryWet"), ("dampens", "dampens"), ("leaves", "soaksTo"), ("dries", "driesTo") }),
            ("rust", null, new[] { ("every", "rustEvery"), ("leaves", "rustsTo") }),
            ("hot", null, new[] { ("for", "hotFor"), ("quench", "hotQuench") }),
            ("bake", "bakes", new[] { ("into", "bakesTo") }),
            ("conduct", "conductive", System.Array.Empty<(string, string)>()),
            ("shatter", "brittle", new[] { ("leaves", "shattersTo") }),
            ("scald", "scalds", System.Array.Empty<(string, string)>()),
            ("grow", null, new[] { ("into", "growsTo"), ("log", "growLog") }),
            ("uproot", null, new[] { ("leaves", "uprootsTo"), ("log", "uprootLog") }),
            ("dissolve", null, new[] { ("by", "dissolvedBy"), ("leaves", "dissolvesTo") }),
        };

        static readonly (string verb, string tag, (string group, string flat)[] keys)[] CoverVerbs =
        {
            ("burn", "flammable", new[] { ("catch", "flammability"), ("fuel", "fuel"), ("intensity", "burnIntensity"), ("look", "fireLook") }),
            ("float", null, new[] { ("every", "floatEvery"), ("reach", "floatReach") }),
            ("melt", null, new[] { ("into", "meltsTo"), ("water", "meltWater") }),
            ("dissolve", null, new[] { ("by", "dissolvedBy"), ("leaves", "dissolvesTo") }),
        };

        // A creature's statuses from its material: each verb's status key and the flat key it becomes.
        static readonly (string verb, string flat)[] StatusVerbs = { ("burn", "burnStatus"), ("freeze", "freezeStatus"), ("soak", "soakStatus") };

        // What water on it turns into: freeze {} for "ice", boil {} for "steam".
        static readonly (string verb, string water)[] WaterVerbs = { ("freeze", "ice"), ("boil", "steam") };

        /// <summary>Turns the grouped spelling of every terrain and cover in a rule document into the flat keys, in place; the problems found (unknown keys in a group).</summary>
        public static List<string> Ungroup(JObject doc)
        {
            var errors = new List<string>();
            if (doc["terrain"] is JObject terrain)
                foreach (var p in terrain.Properties())
                    if (p.Value is JObject t) Ungroup($"terrain {p.Name}", t, TerrainVerbs, true, errors);
            if (doc["covers"] is JObject covers)
                foreach (var p in covers.Properties())
                    if (p.Value is JObject c) Ungroup($"cover {p.Name}", c, CoverVerbs, false, errors);
            return errors;
        }

        static void Ungroup(string what, JObject entry, (string verb, string tag, (string group, string flat)[] keys)[] verbs, bool water, List<string> errors)
        {
            if (water) UngroupStatuses(entry);
            if (water) UngroupChainsWet(entry);
            foreach (var (verb, tag, keys) in verbs)
            {
                if (!(entry[verb] is JObject g)) continue;
                entry.Remove(verb);
                foreach (var p in g.Properties())
                {
                    var key = keys.FirstOrDefault(k => k.group == p.Name);
                    if (key.flat == null) errors.Add($"{what}: {verb}.{p.Name} unknown");
                    else entry[key.flat] = p.Value.DeepClone();
                }
                if (tag != null) AddTag(entry, tag);
            }
            if (!water) return;
            foreach (var (verb, to) in WaterVerbs)
            {
                if (entry[verb] == null) continue;
                entry.Remove(verb);
                entry["waterTo"] = to;
            }
        }

        // Takes each verb's status out of its group; a group left empty goes too, so burn { status }
        // doesn't make the material flammable, nor freeze { status } freeze the water on it.
        static void UngroupStatuses(JObject entry)
        {
            foreach (var (verb, flat) in StatusVerbs)
            {
                if (!(entry[verb] is JObject g) || g["status"] == null) continue;
                entry[flat] = g["status"].DeepClone();
                g.Remove("status");
                if (g.Count == 0) entry.Remove(verb);
            }
        }

        // Takes conduct.chainsWet out of its group; a group left empty goes too, so flesh's
        // conduct { chainsWet } doesn't make it conduct like metal.
        static void UngroupChainsWet(JObject entry)
        {
            if (!(entry["conduct"] is JObject g) || g["chainsWet"] == null) return;
            entry["chainsWet"] = g["chainsWet"].DeepClone();
            g.Remove("chainsWet");
            if (g.Count == 0) entry.Remove("conduct");
        }

        static void AddTag(JObject entry, string tag)
        {
            if (!(entry["tags"] is JArray tags)) entry["tags"] = tags = new JArray();
            if (!tags.Any(t => t.Type == JTokenType.String && (string)t == tag)) tags.Add(tag);
        }

        static void GroupStatuses(JObject entry)
        {
            foreach (var (verb, flat) in StatusVerbs)
            {
                if (entry[flat] == null) continue;
                if (!(entry[verb] is JObject g)) entry[verb] = g = new JObject();
                g["status"] = entry[flat];
                entry.Remove(flat);
            }
        }

        static void GroupChainsWet(JObject entry)
        {
            if (entry["chainsWet"] == null) return;
            if (!(entry["conduct"] is JObject g)) entry["conduct"] = g = new JObject();
            g["chainsWet"] = entry["chainsWet"];
            entry.Remove("chainsWet");
        }

        /// <summary>A flat terrain (or cover) entry spelled as groups: the inverse of Ungroup, for the parity test.</summary>
        public static JObject Group(JObject flat, bool cover)
        {
            var entry = (JObject)flat.DeepClone();
            var tags = entry["tags"] as JArray;
            foreach (var (verb, tag, keys) in cover ? CoverVerbs : TerrainVerbs)
            {
                bool tagged = tag != null && tags != null && tags.Any(t => (string)t == tag);
                if (tag != null && !tagged) continue;
                var g = new JObject();
                foreach (var (group, f) in keys)
                {
                    if (entry[f] == null) continue;
                    g[group] = entry[f];
                    entry.Remove(f);
                }
                if (tag == null && g.Count == 0) continue;
                entry[verb] = g;
                if (tagged) tags.Remove(tags.First(t => (string)t == tag));
            }
            if (!cover && entry["waterTo"] != null)
            {
                string to = (string)entry["waterTo"];
                var (verb, _) = WaterVerbs.FirstOrDefault(w => w.water == to);
                if (verb != null)
                {
                    entry.Remove("waterTo");
                    entry[verb] = new JObject();
                }
            }
            if (!cover) GroupStatuses(entry);
            if (!cover) GroupChainsWet(entry);
            return entry;
        }
    }
}
