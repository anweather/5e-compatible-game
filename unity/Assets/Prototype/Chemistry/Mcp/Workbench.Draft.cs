// The draft tools: add_material, set_material, add_reaction, set_reaction,
// add_spell, set_spell, tune, remove. Each composes onto a draft (a mod patch in
// memory) and returns it. A mod patches only the fields it names (Rules.MergedEntry), so
// a set_ writes just that field, with null for one taken out, and mods stack.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        // ---------- materials ----------

        /// <summary>A new terrain, cover or gas: its name, tags and verb groups ({"burn": {"catch": 0.4, "fuel": 10}}).</summary>
        public Draft AddMaterial(string draftId, string kind, string id, string name, IEnumerable<string> tags = null, string verbs = null)
        {
            kind = Kind(kind);
            id = Required(id, "id");
            if (string.IsNullOrWhiteSpace(name)) throw new RulesToolException($"The {Single(kind)} {id} needs a name.");
            var owner = MaterialKinds.FirstOrDefault(k => Doc[k]?[id] != null);
            if (owner != null) throw new RulesToolException($"There is already a {Single(owner)} called {id}; use set_material to change it.");
            var entry = new JObject { ["name"] = name.Trim() };
            // A cover and a terrain must list tags, even none; a gas has no use for them.
            var tagList = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
            if (tagList.Count > 0 || kind != "gases") entry["tags"] = new JArray(tagList);
            if (!string.IsNullOrWhiteSpace(verbs))
            {
                if (!(Value(verbs) is JObject groups)) throw new RulesToolException("verbs is a JSON object of verb groups, such as {\"burn\": {\"catch\": 0.4, \"fuel\": 10}}.");
                foreach (var p in groups.Properties()) entry[p.Name] = p.Value;
            }
            RuleFields.CheckMaterial(kind, entry);
            var draft = DraftOf(draftId);
            Section(draft, kind)[id] = entry;
            draft.Changed.Add($"{kind} {id}");
            return draft;
        }

        /// <summary>One field of a terrain, cover or gas, by dotted path ("burn.catch"); null removes it.</summary>
        public Draft SetMaterial(string draftId, string id, string field, string value)
        {
            id = Required(id, "id");
            var draft = DraftOf(draftId);
            var kind = MaterialKinds.FirstOrDefault(k => draft.Patch[k]?[id] != null) ?? MaterialKinds.FirstOrDefault(k => Raw["materials"][k]?[id] != null)
                ?? throw new RulesToolException($"There is no terrain, cover or gas called {id}; use add_material for a new one.");
            RuleFields.CheckMaterial(kind, Required(field, "field"), Value(value));
            var section = Section(draft, kind);
            if (!(section[id] is JObject entry)) section[id] = entry = new JObject();
            SetField(entry, field, value);
            draft.Changed.Add($"{kind} {id}");
            return draft;
        }

        // ---------- reactions ----------

        /// <summary>
        /// A new cell rule (or entity rule): when element reaches a cell where every condition holds, the
        /// effects run in order. It goes to the top of its element's rules, so it wins.
        /// </summary>
        public Draft AddReaction(string draftId, string id, string element, string effects, string conditions = null, string log = null,
            bool stop = false, bool notable = false, double? chance = null, bool entity = false)
        {
            id = Required(id, "id");
            element = Required(element, "element");
            if (Doc["cellRules"].Concat(Doc["entityRules"]).Any(r => Str(r["id"]) == id))
                throw new RulesToolException($"There is already a reaction rule called {id}; use set_reaction to change it.");
            var rule = new JObject { ["id"] = id, ["element"] = element };
            var cond = string.IsNullOrWhiteSpace(conditions) ? new JObject() : Value(conditions) as JObject
                ?? throw new RulesToolException("conditions is a JSON object, such as {\"cover\": [\"oil\"]} or {\"terrainTag\": \"flammable\"}.");
            RuleFields.CheckConditions("if", cond);
            if (chance.HasValue) cond["chance"] = chance.Value;
            if (cond.Count > 0) rule["if"] = cond;
            var effect = Value(Required(effects, "effects"));
            if (effect is JObject single) effect = new JArray(single);
            if (!(effect is JArray list) || list.Count == 0) throw new RulesToolException("effects is a JSON list of effects, one key each, such as [{\"ignite\": 2}].");
            RuleFields.CheckEffects(list);
            rule["do"] = list;
            if (!string.IsNullOrWhiteSpace(log)) rule["log"] = log.Trim();
            if (notable) rule["notable"] = true;
            if (stop) rule["stop"] = true;
            var draft = DraftOf(draftId);
            NewElement(draft, element);
            var adds = RuleAdds(draft, entity);
            int at = IndexOfId(adds, id);
            if (at >= 0) adds[at] = rule;
            else adds.Add(rule);
            draft.Changed.Add($"reactions {id}");
            return draft;
        }

        /// <summary>One field of a reaction rule ("if.chance", "do", "log", "stop"); it keeps its place in its element's list.</summary>
        public Draft SetReaction(string draftId, string id, string field, string value)
        {
            id = Required(id, "id");
            RuleFields.CheckRule(Required(field, "field"), Value(value));
            var draft = DraftOf(draftId);
            JObject rule = null;
            foreach (var entity in new[] { false, true })
            {
                var adds = RuleAdds(draft, entity);
                int at = IndexOfId(adds, id);
                if (at >= 0)
                {
                    rule = (JObject)adds[at];
                    break;
                }
                if (!RawList("reactions", entity ? "entity" : "cell").Any(r => Str(r["id"]) == id)) continue;
                rule = new JObject { ["id"] = id };
                adds.Add(rule);
                break;
            }
            if (rule == null) throw new RulesToolException($"There is no reaction rule called {id}; use add_reaction for a new one.");
            if (field == "id") throw new RulesToolException("A rule's id can't change; add a new rule instead.");
            SetField(rule, field, value);
            draft.Changed.Add($"reactions {id}");
            return draft;
        }

        // ---------- spells ----------

        /// <summary>A new spell, in the 5E-compatible frame: it acts through an element (or an effect) and sits in a group.</summary>
        public Draft AddSpell(string draftId, SpellArgs a)
        {
            var id = Required(a.Id, "id");
            if (Doc["spells"].Any(s => Str(s["id"]) == id)) throw new RulesToolException($"There is already a spell called {id}; use set_spell to change it.");
            var spell = new JObject
            {
                ["id"] = id,
                ["group"] = Required(a.Group, "group"),
                ["name"] = Required(a.Name, "name"),
                ["level"] = a.Level,
                ["shape"] = Required(a.Shape, "shape"),
            };
            if (!string.IsNullOrWhiteSpace(a.Origin)) spell["origin"] = a.Origin.Trim();
            if (!string.IsNullOrWhiteSpace(a.Element)) spell["element"] = a.Element.Trim();
            if (!string.IsNullOrWhiteSpace(a.Effect)) spell["effect"] = a.Effect.Trim();
            foreach (var (key, v) in new[] { ("power", a.Power), ("radius", a.Radius), ("length", a.Length), ("size", a.Size), ("push", a.Push) })
                if (v.HasValue) spell[key] = v.Value;
            if (!string.IsNullOrWhiteSpace(a.Damage)) spell["damage"] = Dice(a.Damage);
            if (!string.IsNullOrWhiteSpace(a.Extra))
            {
                if (!(Value(a.Extra) is JObject extra)) throw new RulesToolException("extra is a JSON object of more spell fields, such as {\"crater\": {\"radius\": 1, \"depth\": 1}}.");
                foreach (var p in extra.Properties()) spell[p.Name] = p.Value;
            }
            if (!string.IsNullOrWhiteSpace(a.Blurb)) spell["blurb"] = a.Blurb.Trim();
            foreach (var p in spell.Properties()) RuleFields.CheckSpell(p.Name, p.Value);
            RefuseBadPlace(id, spell);
            var draft = DraftOf(draftId);
            if (Str(spell["element"]) is string element) NewElement(draft, element);
            Upsert(Spells(draft), spell);
            draft.Changed.Add($"spells {id}");
            return draft;
        }

        /// <summary>One field of a spell ("radius", "damage", "crater.depth"); the rest of it is kept.</summary>
        public Draft SetSpell(string draftId, string id, string field, string value)
        {
            id = Required(id, "id");
            if (Required(field, "field") == "id") throw new RulesToolException("A spell's id can't change; add a new spell instead.");
            RuleFields.CheckSpell(field, Value(value));
            var draft = DraftOf(draftId);
            var held = (draft.Patch["spells"] as JArray)?.FirstOrDefault(s => Str(s["id"]) == id);
            if (held == null && !((JArray)Raw["spells"]).Any(s => Str(s["id"]) == id))
                throw new RulesToolException($"There is no spell called {id}; use add_spell for a new one.");
            var spell = (JObject)held?.DeepClone() ?? new JObject { ["id"] = id };
            if (field == "damage" && Str(Value(value)) is string dice) value = Dice(dice).ToString(Formatting.None);
            SetField(spell, field, value);
            RefuseBadPlace(id, spell);
            Upsert(Spells(draft), spell);
            draft.Changed.Add($"spells {id}");
            return draft;
        }

        // What validate would say later, at once: a number past the caps, a group the radial doesn't have.
        void RefuseBadPlace(string id, JObject spell)
        {
            var over = ModValidator.SpellCaps(Budgets, id, spell).FirstOrDefault();
            if (over != null) throw new RulesToolException(over);
            if (Str(spell["group"]) is string group && Raw["groups"][group] == null)
                throw new RulesToolException($"There is no group {group}; the radial's groups are {string.Join(", ", ((JObject)Raw["groups"]).Properties().Select(p => p.Name))}.");
        }

        /// <summary>How a player reaches a spell: on the radial, by a combo, or not yet and what to do about it. For add_spell and describe.</summary>
        public string ReachOf(Draft draft, string id)
        {
            var spell = draft == null ? EntryIn(Raw, "spells", id) : Current(draft, "spells", id);
            string name = Str(spell?["name"]) ?? id;
            Rules rules;
            try
            {
                rules = draft == null ? InForce : RulesWith(draft.Patch.ToString(Formatting.None));
            }
            catch (RulesToolException)
            {
                bool listed = !(spell?["radial"]?.Type == JTokenType.Boolean && !(bool)spell["radial"]);
                return listed
                    ? $"Once this draft fits together (validate says what's left), a player reaches {name} on the spell radial: hold Tab (LB on a pad), the {Str(Raw["groups"][Str(spell?["group"]) ?? ""]?["name"]) ?? "last"} wedge, then {name}."
                    : $"{name} has radial false, so no player can reach it from the radial; set_spell {id} radial true puts it there.";
            }
            var ways = new List<string>();
            var radial = RadialLayout.From(rules);
            int wedge = radial.GroupOfSpell(id);
            if (wedge >= 0)
            {
                string note = Str(spell?["group"]) != radial.Groups[wedge].Id ? $" (it names no group the radial has, so it goes in the last wedge)" : "";
                ways.Add($"on the spell radial: hold Tab (LB on a pad), the {radial.Groups[wedge].Name} wedge{note}, then {name}");
            }
            foreach (var c in rules.Combos.All.Where(c => c.Spell == id || c.Held == id))
                ways.Add($"by the combo {c.Name} ({c.Id.Replace("+", " then ", System.StringComparison.Ordinal)}){(c.Spell == id ? "" : ", held to full charge")}");
            foreach (var d in rules.Combos.Dpad.Where(d => d.Spell == id))
                ways.Add($"on the d-pad's list as {d.Name}");
            return ways.Count == 0
                ? $"No player can reach {name} yet: it isn't on the radial (radial false) and no combo casts it. set_spell {id} radial true puts it on the radial."
                : $"A player reaches {name} {string.Join("; or ", ways)}.";
        }

        // ---------- sim ----------

        /// <summary>A sim parameter by dotted path ("fire.spreadChance"), within the bounds rules/budgets.json declares.</summary>
        public Draft Tune(string draftId, string param, string value)
        {
            param = Required(param, "parameter");
            var bound = Budgets.SimBounds.Where(b => b.path == param).Select(b => ((double, double)?)(b.min, b.max)).FirstOrDefault();
            var current = Raw["sim"].SelectToken(param);
            if (current == null && bound == null) throw new RulesToolException($"sim has no parameter {param}; list sim to see them.");
            if (current is JObject group) throw new RulesToolException($"sim {param} is a group of parameters; tune one of them, such as {param}.{group.Properties().First().Name}.");
            var v = Value(Required(value, "value"));
            bool numeric = v.Type == JTokenType.Integer || v.Type == JTokenType.Float;
            if (bound.HasValue && !(numeric && (double)v >= bound.Value.Item1 && (double)v <= bound.Value.Item2))
                throw new RulesToolException($"sim {param} may be set from {Num(bound.Value.Item1)} to {Num(bound.Value.Item2)}, not {Compact(v)}.");
            var draft = DraftOf(draftId);
            SetPath(Section(draft, "sim"), param, v.ToString(Formatting.None));
            draft.Changed.Add($"sim {param}");
            return draft;
        }

        // ---------- remove ----------

        /// <summary>Takes back something the draft added or changed. Base entries can't be removed this way.</summary>
        public Draft Remove(string draftId, string table, string id)
        {
            var draft = DraftOf(Required(draftId, "draft"));
            table = Table(table);
            id = Required(id, "id");
            if (table == "materials") table = MaterialKinds.FirstOrDefault(k => draft.Patch[k]?[id] != null) ?? "terrain";
            if (!draft.Changed.Contains($"{table} {id}"))
                throw new RulesToolException($"Draft {draft.Id} didn't add or change the {Single(table)} {id}; remove only takes back what the draft holds.");
            switch (table)
            {
                case "reactions":
                    foreach (var entity in new[] { false, true }) Drop(RuleAdds(draft, entity), id);
                    break;
                case "spells":
                    Drop(Spells(draft), id);
                    break;
                case "sim":
                    RemovePath((JObject)draft.Patch["sim"], id);
                    break;
                case BuildingsTable:
                    ((JObject)draft.Patch["world"]["buildings"]).Remove(id);
                    break;
                case ThingsTable:
                    ((JArray)draft.Patch["world"]["add"]).Where(t => CellKey(t["cell"]) == id).ToList().ForEach(t => t.Remove());
                    break;
                case LayTable:
                    RemoveLay(draft, id);
                    break;
                case "openings":
                case "floors":
                case "roofs":
                case "details":
                case "styles":
                    ((JObject)draft.Patch[BuildingTables.Key][table]).Remove(id);
                    break;
                case "elements":
                    if (draft.Patch["elements"] is JObject named) named.Remove(id);
                    else foreach (var e in ((JArray)draft.Patch["elements"]).Where(e => Str(e) == id).ToList()) e.Remove();
                    break;
                default:
                    ((JObject)draft.Patch[table]).Remove(id);
                    break;
            }
            draft.Changed.Remove($"{table} {id}");
            Tidy(draft.Patch);
            return draft;
        }

        // ---------- helpers ----------

        /// <summary>An element the base rules don't have joins the draft's elements (validate then asks that more than one spell use it).</summary>
        void NewElement(Draft draft, string element)
        {
            if (Doc["elements"].Any(e => Str(e) == element)) return;
            if (draft.Patch["elements"] is JObject named)
            {
                if (named[element] == null) named[element] = new JObject();
            }
            else
            {
                if (!(draft.Patch["elements"] is JArray list)) draft.Patch["elements"] = list = new JArray();
                if (!list.Any(e => Str(e) == element)) list.Add(element);
            }
            draft.Changed.Add($"elements {element}");
        }

        public sealed class SpellArgs
        {
            public string Id, Name, Group, Shape, Origin, Element, Effect, Damage, Blurb, Extra;
            public int Level;
            public int? Power, Radius, Length, Size, Push;
        }

        static string Kind(string kind)
        {
            var k = Required(kind, "kind (terrain, cover or gas)").ToLowerInvariant();
            if (k == "cover") k = "covers";
            if (k == "gas") k = "gases";
            if (!MaterialKinds.Contains(k)) throw new RulesToolException($"{kind} isn't a kind of material; say terrain, cover or gas.");
            return k;
        }

        /// <summary>"8d6" or [8, 6] as [dice, sides].</summary>
        static JArray Dice(string text)
        {
            var t = text.Trim();
            if (t.StartsWith('[')) return (JArray)JToken.Parse(t);
            var parts = t.ToLowerInvariant().Split('d');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int sides))
                return new JArray(n, sides);
            throw new RulesToolException($"{text} isn't damage dice; write it like 8d6.");
        }

        /// <summary>A value as JSON when it reads as JSON (numbers, true, lists, objects), else as text.</summary>
        static JToken Value(string text)
        {
            if (text == null) return JValue.CreateNull();
            try
            {
                return JToken.Parse(text);
            }
            catch (JsonReaderException)
            {
                return new JValue(text);
            }
        }

        // One field of a patch's entry, by dotted path. Null stays, as what takes the field out of the entry under it.
        static void SetField(JObject o, string path, string value)
        {
            var keys = path.Split('.');
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (!(o[keys[i]] is JObject sub)) o[keys[i]] = sub = new JObject();
                o = sub;
            }
            o[keys[keys.Length - 1]] = Value(value);
        }

        static void SetPath(JObject o, string path, string value)
        {
            var keys = path.Split('.');
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (!(o[keys[i]] is JObject sub)) o[keys[i]] = sub = new JObject();
                o = sub;
            }
            var v = Value(value);
            if (v.Type == JTokenType.Null) o.Remove(keys[keys.Length - 1]);
            else o[keys[keys.Length - 1]] = v;
        }

        static void RemovePath(JObject o, string path)
        {
            var keys = path.Split('.');
            for (int i = 0; i < keys.Length - 1 && o != null; i++) o = o[keys[i]] as JObject;
            o?.Remove(keys[keys.Length - 1]);
        }

        /// <summary>Drops empty objects and lists, so taking everything back leaves {}.</summary>
        static void Tidy(JObject o)
        {
            foreach (var p in o.Properties().ToList())
            {
                if (p.Value is JObject sub) Tidy(sub);
                if ((p.Value is JObject so && so.Count == 0) || (p.Value is JArray a && a.Count == 0)) p.Remove();
            }
        }

        static JObject Section(Draft d, string key)
        {
            if (!(d.Patch[key] is JObject o)) d.Patch[key] = o = new JObject();
            return o;
        }

        static JArray RuleAdds(Draft d, bool entity)
        {
            var rx = Section(d, "reactions");
            var key = entity ? "addEntity" : "add";
            if (!(rx[key] is JArray a)) rx[key] = a = new JArray();
            return a;
        }

        static JArray Spells(Draft d)
        {
            if (!(d.Patch["spells"] is JArray a)) d.Patch["spells"] = a = new JArray();
            return a;
        }

        static int IndexOfId(JArray list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (Str(list[i]["id"]) == id) return i;
            return -1;
        }

        static void Upsert(JArray list, JObject entry)
        {
            int at = IndexOfId(list, Str(entry["id"]));
            if (at >= 0) list[at] = entry;
            else list.Add(entry);
        }

        static void Drop(JArray list, string id)
        {
            int at = IndexOfId(list, id);
            if (at >= 0) list.RemoveAt(at);
        }
    }
}
