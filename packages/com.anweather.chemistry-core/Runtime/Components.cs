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
    public readonly struct ComponentSet : IEquatable<ComponentSet>
    {
        public ComponentSet(ulong bits) => Bits = bits;

        public ulong Bits { get; }
        public static ComponentSet Empty => default;
        public bool IsEmpty => Bits == 0;

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
        public const string Flammable = "flammable", Creature = "creature";

        public const int MaxComponents = 64;

        readonly List<string> componentNames = new List<string>();
        readonly Dictionary<string, int> componentIds = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<string> Components => componentNames;

        public int Component(string name) => name != null && componentIds.TryGetValue(name, out int id) ? id : -1;

        public bool EntityHas(string entityId, string component) => Entity(entityId)?.Components.Has(Component(component)) ?? false;

        public bool ItemCracks(string itemId) =>
            Item(itemId) is ItemDef item && item.Breaks?.Spill == null && item.Material != null && terrain.TryGetValue(item.Material, out var t) && t.Cracks;

        public bool ItemHas(string itemId, string component) =>
            itemId != null && items.TryGetValue(itemId, out var item) && item.Components.Has(Component(component));

        public IReadOnlyList<string> EntitiesWith(string component)
        {
            int id = Component(component);
            return entities.Values.Where(e => e.Components.Has(id)).Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

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

        ComponentSet ThingComponents(JObject o)
        {
            var set = Names(o["components"]);
            if (Truthy(o["flammable"])) set = set.With(Component(Flammable));
            if (Truthy(o["creature"])) set = set.With(Component(Creature));
            return new ComponentSet(set.Bits | FromMaterial(Str(o["material"])).Bits);
        }

        ComponentSet FromMaterial(string material)
        {
            var set = ComponentSet.Empty;
            if (material == null || !terrain.TryGetValue(material, out var m)) return set;
            foreach (var (name, verb) in MaterialComponents)
                if (m.Has(verb)) set = set.With(Component(name));
            return set;
        }

        public static readonly IReadOnlyList<(string component, string tag)> MaterialComponents = new[]
        {
            ("flammable", "flammable"), ("breakable", "breakable"), ("conducts", "conductive"), ("brittle", "brittle"),
        };

        ComponentSet Names(JToken t)
        {
            var set = ComponentSet.Empty;
            if (t is JValue v && v.Type == JTokenType.String) return set.With(Component((string)v));
            if (t is JArray list)
                foreach (var n in list.OfType<JValue>().Where(n => n.Type == JTokenType.String)) set = set.With(Component((string)n));
            return set;
        }

        EntityCond ParseEntityCond(JObject c)
        {
            var cond = new EntityCond { Status = Str(c["status"]), Has = Names(c["has"]), Lacks = Names(c["lacks"]), Verb = Str(c["verb"]) };
            var flammable = Bool(c["flammable"]);
            if (flammable == true) cond.Has = cond.Has.With(Component(Flammable));
            if (flammable == false) cond.Lacks = cond.Lacks.With(Component(Flammable));
            return cond;
        }
    }
}
