// The fields the engine reads on a material, spell or reaction rule (RuleTables.cs), so a
// set_ or add_ tool refuses an unknown one when it's set. Left alone, a misnamed field
// is ignored and the change quietly does nothing. A new field the engine reads goes here too;
// EveryFieldInTheRulesIsKnown holds the shipped rules to it.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public static class RuleFields
    {
        static readonly string[] TerrainFields = { "name", "tags", "look", "elevation", "spring", "speed" };
        static readonly string[] CoverFields = { "name", "tags", "look", "lasts", "witherLog", "damage", "speed" };

        static readonly Dictionary<string, string[]> GasFields = new Dictionary<string, string[]>
        {
            ["name"] = null,
            ["tags"] = null,
            ["look"] = null,
            ["scald"] = null,
            ["decay"] = null,
            ["blocksSightAt"] = null,
            ["hidesAfter"] = null,
            ["condenseEvery"] = null,
            ["condenseAway"] = null,
            ["rainEvery"] = null,
            ["rusts"] = null,
            ["burn"] = new[] { "flash" },
            ["sheds"] = new[] { "element", "every", "chance" },
        };

        static readonly Dictionary<string, string[]> SpellFields = new Dictionary<string, string[]>
        {
            ["id"] = null,
            ["group"] = null,
            ["name"] = null,
            ["look"] = null,
            ["level"] = null,
            ["shape"] = null,
            ["origin"] = null,
            ["element"] = null,
            ["radial"] = null,
            ["blurb"] = null,
            ["power"] = null,
            ["length"] = null,
            ["size"] = null,
            ["radius"] = null,
            ["radiusPerCharge"] = null,
            ["effect"] = null,
            ["push"] = null,
            ["wind"] = null,
            ["range"] = null,
            ["duration"] = null,
            ["maxMass"] = null,
            ["rate"] = null,
            ["damage"] = null,
            ["raise"] = null,
            ["dig"] = null,
            ["strength"] = null,
            ["height"] = null,
            ["terrain"] = null,
            ["taper"] = null,
            ["inward"] = null,
            ["pullEvery"] = null,
            ["delay"] = null,
            ["core"] = null,
            ["travelEvery"] = null,
            ["dropPower"] = null,
            ["needs"] = null,
            ["fizzle"] = null,
            ["leaves"] = null,
            ["rolls"] = null,
            ["trail"] = null,
            ["stepping"] = null,
            ["ramp"] = null,
            ["carries"] = null,
            ["scatter"] = new[] { "cover", "count", "reach" },
            ["force"] = new[] { "throw", "push" },
            ["seeks"] = new[] { "range", "if" },
            ["light"] = new[] { "bright", "dim" },
            ["crater"] = new[] { "radius", "depth", "strength", "leaves" },
        };

        static readonly string[] RuleKeys = { "id", "element", "if", "do", "log", "notable", "stop" };

        /// <summary>A material's field set to this value: throws saying what's wrong with the field, or returns.</summary>
        public static void CheckMaterial(string kind, string path, JToken value)
        {
            if (kind == "gases")
            {
                CheckNamed("A gas", GasFields, path, value);
                return;
            }
            bool cover = kind == "covers";
            string what = cover ? "A cover" : "A terrain";
            var keys = path.Split('.');
            string top = keys[0];
            if (top == "look") return; // ModCheck.Looks and set_look know a look's parameters
            if (!MaterialGroups.Verbs(cover).Contains(top))
            {
                if (MaterialGroups.GroupedName(cover, top) is string grouped) throw new RulesToolException($"{what} writes {top} as {grouped}.");
                if ((cover ? CoverFields : TerrainFields).Contains(top) && keys.Length == 1) return;
                throw new RulesToolException($"{what} has no field {path}; its fields are {string.Join(", ", (cover ? CoverFields : TerrainFields).Where(f => f != "look"))} and the verbs {string.Join(", ", MaterialGroups.Verbs(cover))}. A colour or glow is a look: use set_look.");
            }
            var verb = MaterialGroups.KeysOf(cover, top);
            if (keys.Length == 1)
            {
                if (value is JObject group)
                    foreach (var p in group.Properties()) CheckMaterial(kind, top + "." + p.Name, p.Value);
                return;
            }
            if (keys.Length > 2 || !verb.Contains(keys[1]))
                throw new RulesToolException($"{what} has no field {path}; {top} takes {(verb.Count == 0 ? "nothing, only {}" : string.Join(", ", verb))}. A colour or glow is a look: use set_look.");
        }

        /// <summary>A whole new material's fields.</summary>
        public static void CheckMaterial(string kind, JObject entry)
        {
            foreach (var p in entry.Properties()) CheckMaterial(kind, p.Name, p.Value);
        }

        static readonly string[] ItemFields = { "name", "material", "components", "use", "message", "srd", "mass", "light", "zoom", "reach", "strength", "breaks", "look" };
        static readonly string[] EntityFields = { "name", "material", "components", "hp", "mass", "str", "ac", "explodes", "raider", "look" };

        /// <summary>A new entity's or item's fields: throws saying which the engine wouldn't read, or returns.</summary>
        public static void CheckThing(string table, JObject entry)
        {
            var known = table == "items" ? ItemFields : EntityFields;
            var unknown = entry.Properties().FirstOrDefault(p => !known.Contains(p.Name));
            if (unknown != null) throw new RulesToolException($"A{(table == "items" ? "n item" : "n entity")} has no field {unknown.Name}; its fields are {string.Join(", ", known)}.");
        }

        /// <summary>A spell's field set to this value: throws saying what's wrong with the field, or returns.</summary>
        public static void CheckSpell(string path, JToken value)
        {
            var top = path.Split('.')[0];
            if (top == "look") return;
            if (top == "needs")
            {
                CheckConditions(path, value);
                return;
            }
            CheckNamed("A spell", SpellFields, path, value);
        }

        /// <summary>A reaction rule's field set to this value.</summary>
        public static void CheckRule(string path, JToken value)
        {
            var top = path.Split('.')[0];
            if (!RuleKeys.Contains(top)) throw new RulesToolException($"A reaction rule has no field {path}; its fields are {string.Join(", ", RuleKeys)}.");
            if (top == "if") CheckConditions(path, value);
            if (top == "do" && value != null) CheckEffects(value);
        }

        /// <summary>A condition, or a whole object of them, under "if" or "needs".</summary>
        public static void CheckConditions(string path, JToken value)
        {
            var keys = path.Split('.');
            var named = keys.Length >= 2 ? new[] { keys[1] } : (value as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
            var unknown = named.FirstOrDefault(k => !Rules.IsCondition(k));
            if (unknown != null) throw new RulesToolException($"There's no condition {unknown}; the conditions are {string.Join(", ", Rules.Conditions)}.");
        }

        /// <summary>A rule's effects: one object or a list of them.</summary>
        public static void CheckEffects(JToken effects)
        {
            IEnumerable<JToken> list = effects is JArray a ? a : new[] { effects };
            var unknown = list.OfType<JObject>().SelectMany(e => e.Properties()).Select(p => p.Name).FirstOrDefault(k => !Rules.IsEffect(k));
            if (unknown != null) throw new RulesToolException($"There's no effect {unknown} (a new verb needs code); the effects are {string.Join(", ", Rules.Effects)}.");
        }

        static void CheckNamed(string what, Dictionary<string, string[]> fields, string path, JToken value)
        {
            var keys = path.Split('.');
            if (!fields.TryGetValue(keys[0], out var inner))
                throw new RulesToolException($"{what} has no field {path}; its fields are {string.Join(", ", fields.Keys.Where(f => f != "look"))}. A colour or glow is a look: use set_look.");
            if (keys.Length > 1 && inner == null) throw new RulesToolException($"{what}'s {keys[0]} has no field {keys[1]}: it's one value.");
            if (inner == null) return;
            var below = keys.Length > 1 ? new[] { keys[1] } : (value as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
            var unknown = below.FirstOrDefault(k => !inner.Contains(k));
            if (unknown != null) throw new RulesToolException($"{what}'s {keys[0]} has no field {unknown}; it takes {string.Join(", ", inner)}.");
        }
    }
}
