// Checks components (Components.cs): the components.json table, the lists
// entities and items carry, and the names entity rules match on ("if": {
// "has", "lacks" }), with the same messages as validateRules in
// sandbox/src/rules.ts.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            HashSet<string> knownComponents;

            // components.json's names plus the engine's own.
            HashSet<string> KnownComponents => knownComponents ??= new HashSet<string>(
                Keys(r["components"]).Concat(new[] { Flammable, Creature }));

            public void Components()
            {
                foreach (var p in (r["components"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (!(p.Value is JValue v && v.Type == JTokenType.String && Truthy(v))) Errors.Add($"component {p.Name}: needs a description");
                if (KnownComponents.Count > MaxComponents) Errors.Add($"components: at most {MaxComponents}, not {KnownComponents.Count}");
            }

            /// <summary>A thing's "components" list: component names, each known.</summary>
            void ComponentList(string what, JToken list)
            {
                if (list == null) return;
                if (!(list is JArray a && a.All(n => n.Type == JTokenType.String)))
                {
                    Errors.Add($"{what}: components must be a list of component names");
                    return;
                }
                foreach (var n in a.Where(n => !KnownComponents.Contains((string)n))) Errors.Add($"{what}: unknown component {Show(n)}");
            }

            /// <summary>An entity rule's "has" or "lacks": a component name or a list of them, each known.</summary>
            void ComponentCond(string id, JObject cond, string key)
            {
                var t = cond?[key];
                if (t == null) return;
                var names = t.Type == JTokenType.String ? new[] { t } : (t as JArray)?.ToArray();
                if (names == null || names.Any(n => n.Type != JTokenType.String))
                {
                    Errors.Add($"rule {id}: {key} must be a component name or a list of them");
                    return;
                }
                foreach (var n in names.Where(n => !KnownComponents.Contains((string)n))) Errors.Add($"rule {id}: unknown component {Show(n)}");
            }
        }
    }
}
