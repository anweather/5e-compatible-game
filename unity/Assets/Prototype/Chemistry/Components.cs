// Components (rules/components.json, backlog 10.1, D9): what a thing is made of
// or does, as data. Entities (entities.json) and items (items.json) list theirs
// ("components": ["flammable", "breakable"]), and entity rules match on them
// ("if": { "has": "flammable" } or "lacks"), so one rule covers everything with
// a component rather than naming each thing: "fire lights anything flammable".
// The names are interned once per rule set to bits (sorted, so the same rules
// give the same bits everywhere), and a thing's components are one 64-bit set.
// The old "flammable": true and "creature": true keys still load, as shorthand
// for those components. Mirrors components in sandbox/src/rules.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A set of components, as bits (Rules.Component gives each name its bit).</summary>
    public readonly struct ComponentSet : IEquatable<ComponentSet>
    {
        public ComponentSet(ulong bits) => Bits = bits;

        public ulong Bits { get; }
        public static ComponentSet Empty => default;
        public bool IsEmpty => Bits == 0;

        /// <summary>Whether it has the component with this id (false for -1, an unknown name).</summary>
        public bool Has(int id) => id >= 0 && (Bits & (1UL << id)) != 0;
        public bool HasAll(ComponentSet other) => (Bits & other.Bits) == other.Bits;
        public bool HasAny(ComponentSet other) => (Bits & other.Bits) != 0;
        public ComponentSet With(int id) => id < 0 ? this : new ComponentSet(Bits | (1UL << id));

        public bool Equals(ComponentSet other) => Bits == other.Bits;
        public override bool Equals(object obj) => obj is ComponentSet c && Equals(c);
        public override int GetHashCode() => Bits.GetHashCode();
        public static bool operator ==(ComponentSet a, ComponentSet b) => a.Equals(b);
        public static bool operator !=(ComponentSet a, ComponentSet b) => !a.Equals(b);
    }

    public sealed partial class Rules
    {
        /// <summary>The components the engine itself reads, there whatever components.json says.</summary>
        public const string Flammable = "flammable", Creature = "creature";

        /// <summary>At most this many components (a thing's are one 64-bit set).</summary>
        public const int MaxComponents = 64;

        readonly List<string> componentNames = new List<string>();
        readonly Dictionary<string, int> componentIds = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The component names, in id order (sorted).</summary>
        public IReadOnlyList<string> Components => componentNames;

        /// <summary>A component's id (its bit), or -1 for a name the rules don't know.</summary>
        public int Component(string name) => name != null && componentIds.TryGetValue(name, out int id) ? id : -1;

        /// <summary>Whether the entity with this id has the component (false for an unknown entity or name).</summary>
        public bool EntityHas(string entityId, string component) => Entity(entityId)?.Components.Has(Component(component)) ?? false;

        /// <summary>Whether the item with this id has the component (false for an unknown item or name).</summary>
        public bool ItemHas(string itemId, string component) =>
            itemId != null && items.TryGetValue(itemId, out var item) && item.Components.Has(Component(component));

        /// <summary>The ids of the entities with a component, sorted.</summary>
        public IReadOnlyList<string> EntitiesWith(string component)
        {
            int id = Component(component);
            return entities.Values.Where(e => e.Components.Has(id)).Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

        // components.json's names plus the engine's own, sorted, numbered from 0.
        static void ParseComponents(Rules r, JObject table)
        {
            var names = table.Properties().Select(p => p.Name).Concat(new[] { Flammable, Creature })
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(MaxComponents);
            foreach (string n in names)
            {
                r.componentIds[n] = r.componentNames.Count;
                r.componentNames.Add(n);
            }
        }

        /// <summary>A thing's "components" list, plus the old shorthand keys ("flammable": true, "creature": true).</summary>
        ComponentSet ThingComponents(JObject o)
        {
            var set = Names(o["components"]);
            if (Truthy(o["flammable"])) set = set.With(Component(Flammable));
            if (Truthy(o["creature"])) set = set.With(Component(Creature));
            return set;
        }

        /// <summary>A component name or a list of them, as a set (unknown names left out; the checks report them).</summary>
        ComponentSet Names(JToken t)
        {
            var set = ComponentSet.Empty;
            if (t is JValue v && v.Type == JTokenType.String) return set.With(Component((string)v));
            if (t is JArray list)
                foreach (var n in list.OfType<JValue>().Where(n => n.Type == JTokenType.String)) set = set.With(Component((string)n));
            return set;
        }

        /// <summary>An entity rule's "if": a status it has, components it has and lacks ("flammable": true / false are has / lacks flammable).</summary>
        EntityCond ParseEntityCond(JObject c)
        {
            var cond = new EntityCond { Status = Str(c["status"]), Has = Names(c["has"]), Lacks = Names(c["lacks"]) };
            var flammable = Bool(c["flammable"]);
            if (flammable == true) cond.Has = cond.Has.With(Component(Flammable));
            if (flammable == false) cond.Lacks = cond.Lacks.With(Component(Flammable));
            return cond;
        }
    }
}
