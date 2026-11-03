// The read tools: list, describe, explain.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        public static readonly string[] Tables = { "materials", "terrain", "covers", "gases", "elements", "reactions", "spells", "groups", "sim", "critters", "entities", "items", "machines", "looks", FaunaTable, PartsTable, DragonTable, BuildingsTable, ThingsTable, LayTable, "openings", "floors", "roofs", "details", "styles" };

        // ---------- list ----------

        /// <summary>Ids and one-line summaries for a table.</summary>
        public string List(string table)
        {
            table = Table(table);
            var lines = new List<string>();
            switch (table)
            {
                case "materials":
                    foreach (var kind in MaterialKinds) lines.AddRange(Entries(kind).Select(p => MaterialLine(kind, p)));
                    break;
                case "terrain":
                case "covers":
                case "gases":
                    lines.AddRange(Entries(table).Select(p => MaterialLine(table, p)));
                    break;
                case "elements":
                    foreach (var e in Doc["elements"].Select(x => (string)x))
                    {
                        int rules = Doc["cellRules"].Count(r => Str(r["element"]) == e);
                        var spells = Doc["spells"].Where(s => Str(s["element"]) == e).Select(s => Str(s["id"])).ToList();
                        var colour = ElementEntry(e)?["look"]?["color"];
                        lines.Add($"{e}: {rules} reaction rule{(rules == 1 ? "" : "s")}; spells: {(spells.Count == 0 ? "none" : string.Join(", ", spells))}" +
                            (colour == null ? "" : $"; colour {Compact(colour)}"));
                    }
                    break;
                case "reactions":
                    lines.AddRange(RawList("reactions", "cell").Select(RuleLine));
                    lines.AddRange(RawList("reactions", "entity").Select(r => "(entity) " + RuleLine(r)));
                    break;
                case "spells":
                    lines.AddRange(((JArray)Raw["spells"]).Select(SpellLine));
                    break;
                case "looks":
                    lines.AddRange(LookLines());
                    break;
                case BuildingsTable:
                    lines.AddRange(BuildingLines());
                    break;
                case ThingsTable:
                    lines.AddRange(ModThingLines());
                    break;
                case LayTable:
                    lines.AddRange(OnWorld(w => w.Rules.ModLays.Select(l => FormattableString.Invariant($"{l.Terrain ?? l.Cover}#{l.Seed}: {l.Cells.Count} cells of {(l.Terrain != null ? "terrain" : "cover")} {l.Terrain ?? l.Cover}")).ToList()));
                    break;
                case "openings":
                case "floors":
                case "roofs":
                case "details":
                case "styles":
                    foreach (var p in (Raw[BuildingTables.Key][table] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                        lines.Add($"{p.Name}: {Compact(p.Value)}");
                    break;
                case "critters":
                    lines.AddRange(((JArray)Raw["critters"]).Select(c => $"{Str(c["id"])}: {Str(c["name"])}, {Str(c["mode"])}s {Str(c["element"])}. {Str(c["blurb"])}"));
                    break;
                case "sim":
                    Flatten((JObject)Raw["sim"], "", lines);
                    break;
                case DragonTable:
                    lines.AddRange(DragonLines());
                    break;
                case FaunaTable:
                    lines.AddRange(SpeciesLines());
                    break;
                case PartsTable:
                    lines.AddRange(PartLines());
                    break;
                default:
                    foreach (var p in ((JObject)Raw[table]).Properties())
                        lines.Add($"{p.Name}: {Str(p.Value["name"]) ?? Compact(p.Value)}{Detail(p.Value)}");
                    break;
            }
            return $"{table} ({lines.Count}):\n" + string.Join("\n", lines);
        }

        static string Detail(JToken entry)
        {
            var how = Str(entry["how"]) ?? Str(entry["srd"]);
            return how == null ? "" : ". " + how;
        }

        static string MaterialLine(string kind, JProperty p)
        {
            var tags = Tags(p.Value);
            var verbs = ((JObject)p.Value).Properties().Where(x => x.Value is JObject).Select(x => x.Name).ToList();
            return $"{p.Name} ({Single(kind)}): {Str(p.Value["name"])}" +
                (tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "") +
                (verbs.Count > 0 ? $"; verbs: {string.Join(", ", verbs)}" : "");
        }

        static string RuleLine(JToken r) =>
            $"{Str(r["id"])} ({Str(r["element"])}): if {(r["if"] is JObject c && c.Count > 0 ? Compact(c) : "always")} do {Compact(r["do"])}" +
            (Stops(r) ? ", stop" : "") +
            (Str(r["log"]) is string log ? $". \"{log}\"" : "");

        static string SpellLine(JToken s)
        {
            var size = new[] { "radius", "length", "size" }.Where(k => s[k] != null).Select(k => $"{k} {s[k]}");
            var acts = Str(s["element"]) ?? ("effect " + Str(s["effect"]));
            return $"{Str(s["id"])}: {Str(s["name"])}, level {s["level"]} {string.Join(" ", new[] { Str(s["shape"]) }.Concat(size))}, {acts}" +
                (s["damage"] is JArray d && d.Count == 2 ? $", {d[0]}d{d[1]}" : "") + $" (group {Str(s["group"]) ?? "none"})";
        }

        void Flatten(JObject o, string prefix, List<string> lines)
        {
            foreach (var p in o.Properties())
            {
                string path = prefix + p.Name;
                if (p.Value is JObject sub) Flatten(sub, path + ".", lines);
                else
                {
                    var bound = Budgets.SimBounds.Where(b => b.path == path).Select(b => $" (a mod may set {Num(b.min)} to {Num(b.max)})").FirstOrDefault();
                    lines.Add($"{path} = {Compact(p.Value)}{bound}{SimNote(path)}");
                }
            }
        }

        // ---------- describe ----------

        /// <summary>The full entry in force, plus every place the rules refer to it, which mods changed it and, for a spell, how a player reaches it.</summary>
        public string Describe(string table, string id, string draft = null)
        {
            (table, id) = DragonPath(table, id);
            table = Table(table);
            id = Required(id, "id");
            if (table == "materials") table = MaterialKinds.FirstOrDefault(k => Raw["materials"][k]?[id] != null) ?? "terrain";
            var drafted = string.IsNullOrWhiteSpace(draft) ? null : DraftOf(draft.Trim());
            var entry = drafted == null ? Find(table, id) : DraftEntry(drafted, table, id);
            if (entry == null || entry.Type == JTokenType.Null) throw new RulesToolException($"There is no {Single(table)} called {id}; list {table} to see the ids.");
            var refs = new List<string>();
            foreach (var file in Raw.Keys.Where(f => f != "budgets"))
                References(Raw[file], file, id, entry, refs);
            return $"{Single(table)} {id}:\n{entry.ToString(Formatting.Indented)}\n" +
                (refs.Count == 0 ? "Nothing in the rules refers to it." : $"Referred to by ({refs.Count}):\n" + string.Join("\n", refs.Distinct().Take(60))) +
                LookLine(table, id, entry) + ChangedBy(table, id) + DragonNote(table, id) +
                (table == FaunaTable ? SpeciesNote(id, drafted) : "") + (table == PartsTable ? PartNote(id) : "") +
                (table == "spells" ? "\n" + ReachOf(drafted, id) : "");
        }

        // An entry as a draft has it (its looks included), else as the rules in force have it.
        JToken DraftEntry(Draft draft, string table, string id)
        {
            if (LookTables.Contains(table)) return Current(draft, table, id) ?? Find(table, id);
            if (table == "reactions")
                return Over(EntryIn(Raw, table, id), (draft.Patch["reactions"]?["add"] as JArray ?? new JArray()).Concat(draft.Patch["reactions"]?["addEntity"] as JArray ?? new JArray()).FirstOrDefault(r => Str(r["id"]) == id)) ?? Find(table, id);
            if (table == "sim") return draft.Patch["sim"]?.SelectToken(id) ?? Find(table, id);
            if (FaunaTables.Holds(table)) return Current(draft, table, id) ?? Find(table, id);
            return Find(table, id);
        }

        JToken Find(string table, string id)
        {
            if (table == "elements") return Doc["elements"].Any(e => (string)e == id) ? ElementEntry(id) ?? new JObject() : JValue.CreateNull();
            if (table == BuildingsTable) return (JToken)OnWorld(w => w.BuildingRowOf(id)?.Json) ?? JValue.CreateNull();
            return EntryIn(Raw, table, id) ?? JValue.CreateNull();
        }

#pragma warning disable S1168 // a JToken is a collection to the analyser; null here means "no such entry"
        // An entry in one layer of the written rules, or null.
        static JToken EntryIn(Dictionary<string, JToken> raw, string table, string id)
        {
            switch (table)
            {
                case "terrain":
                case "covers":
                case "gases":
                    return raw["materials"][table]?[id];
                case "reactions":
                    return ((raw["reactions"]["cell"] as JArray) ?? new JArray()).Concat((raw["reactions"]["entity"] as JArray) ?? new JArray()).FirstOrDefault(r => Str(r["id"]) == id);
                case "spells":
                case "critters":
                    return raw[table].FirstOrDefault(s => Str(s["id"]) == id);
                case "sim":
                    return raw["sim"].SelectToken(id);
                case "openings":
                case "floors":
                case "roofs":
                case "details":
                case "styles":
                    return raw[BuildingTables.Key][table]?[id];
                default:
                    return raw.TryGetValue(table, out var t) ? t[id] : null;
            }
        }
#pragma warning restore S1168

        // Where a string equal to id appears (outside the entry itself), as "file: entry path".
        static void References(JToken t, string where, string id, JToken self, List<string> refs)
        {
            if (ReferenceEquals(t, self)) return;
            switch (t)
            {
                case JObject o:
                    foreach (var p in o.Properties()) References(p.Value, $"{where}.{p.Name}", id, self, refs);
                    break;
                case JArray a:
                    for (int i = 0; i < a.Count; i++)
                        References(a[i], $"{where}[{(a[i] is JObject item ? Str(item["id"]) : null) ?? i.ToString(CultureInfo.InvariantCulture)}]", id, self, refs);
                    break;
                default:
                    if (t.Type == JTokenType.String && (string)t == id) refs.Add(where);
                    break;
            }
        }

        // ---------- explain ----------

        /// <summary>
        /// What happens when an element reaches a cell holding a material: the cell rules for the element,
        /// top to bottom, that can hold for it, and the one that decides. Material may be a terrain, a
        /// cover, a gas, "water" or "ice". With a draft, under the draft's rules.
        /// </summary>
        public string Explain(string element, string material, string draft = null)
        {
            element = Required(element, "element");
            material = Required(material, "material");
            var doc = string.IsNullOrEmpty(draft) ? Doc : JObject.Parse(RulesWith(PatchJson(draft)).ToJson());
            if (!IsElement(doc, element))
            {
                // Either way round: explain(grass, fire) means fire on grass.
                var (swappedElement, swappedMaterial) = (material, element);
                if (IsElement(doc, swappedElement)) return Explain(swappedElement, swappedMaterial, draft);
                throw new RulesToolException($"{element} isn't an element; the elements are {string.Join(", ", doc["elements"].Select(e => (string)e))}.");
            }
            var kind = material == "water" || material == "ice" ? "water" : MaterialKinds.FirstOrDefault(k => doc[k]?[material] != null);
            if (kind == null) throw new RulesToolException($"{material} isn't a terrain, cover or gas (or water or ice); list materials to see them.");

            var (lines, decided) = RulesFor(doc, element, kind, material);
            var head = $"{element} on {material} ({(kind == "water" ? "water" : Single(kind))}):";
            if (lines.Count == 0) return head + $"\nNo cell rule for {element} applies, so nothing happens to the {material}.";
            var tail = decided != null ? $"\n{decided} decides it: it runs and stops the rules after it." : "\nNo rule here stops the list, so each one that holds runs, in this order.";
            return head + "\n" + string.Join("\n", lines) + tail + OwnVerbs(kind, material);
        }

        static bool IsElement(JObject doc, string id) => doc["elements"].Any(e => (string)e == id);

        /// <summary>The element's cell rules that can hold on the material, top to bottom, and the one that decides (if one does).</summary>
        static (List<string> lines, string decided) RulesFor(JObject doc, string element, string kind, string material)
        {
            var lines = new List<string>();
            foreach (var rule in doc["cellRules"].Where(r => Str(r["element"]) == element))
            {
                var (holds, needs) = Match(doc, rule["if"] as JObject, kind, material);
                if (!holds) continue;
                lines.Add($"- {Str(rule["id"])}: {Compact(rule["do"])}" + (needs.Count > 0 ? $" when {string.Join(" and ", needs)}" : "") +
                    (Str(rule["log"]) is string log ? $" (\"{log}\")" : ""));
                if (Stops(rule) && needs.Count == 0) return (lines, Str(rule["id"]));
            }
            return (lines, null);
        }

        static bool Stops(JToken rule) => rule["stop"]?.Type == JTokenType.Boolean && (bool)rule["stop"];

        string OwnVerbs(string kind, string material)
        {
            var verbs = kind == "water" ? null : (Raw["materials"][kind]?[material] as JObject)?.Properties().Where(p => p.Value is JObject).Select(p => $"{p.Name} {Compact(p.Value)}").ToList();
            return verbs != null && verbs.Count > 0 ? $"\nThe {material}'s own verbs: {string.Join("; ", verbs)}." : "";
        }

        /// <summary>Whether a rule's conditions can hold on a cell of this material, and the conditions that depend on the moment.</summary>
        static (bool holds, List<string> needs) Match(JObject doc, JObject cond, string kind, string material)
        {
            var needs = new List<string>();
            if (cond == null) return (true, needs);
            var tags = kind == "terrain" ? Tags(doc["terrain"][material]) : null;
            foreach (var p in cond.Properties())
                if (!Holds(doc, p, kind, material, tags, needs)) return (false, needs);
            return (true, needs);
        }

        /// <summary>Whether one condition can hold on the material; one that depends on the moment goes in needs.</summary>
        static bool Holds(JObject doc, JProperty p, string kind, string material, List<string> tags, List<string> needs)
        {
            switch (p.Name)
            {
                case "cover":
                    var covers = p.Value is JArray a ? a.Select(x => (string)x).ToList() : new List<string> { Str(p.Value) };
                    return kind == "covers" && covers.Contains(material);
                case "terrainTag":
                    return TagHolds(kind, tags, Str(p.Value), true, needs);
                case "notTerrainTag":
                    return TagHolds(kind, tags, Str(p.Value), false, needs);
                case "gas":
                    return kind == "gases" && (Str(p.Value) == "any" || Str(p.Value) == material);
                case "water":
                    return Truthy(p.Value) == (material == "water");
                case "frozen":
                    return Truthy(p.Value) == (material == "ice");
                case "melts":
                    return kind == "covers" && doc["covers"][material]["meltWater"] != null;
                case "chance":
                    needs.Add($"a {Num((double)p.Value)} chance comes up");
                    return true;
                default:
                    needs.Add($"{p.Name} is {Compact(p.Value)}");
                    return true;
            }
        }

        static bool TagHolds(string kind, List<string> tags, string tag, bool tagged, List<string> needs)
        {
            if (kind == "terrain") return tags.Contains(tag) == tagged;
            needs.Add(tagged ? $"the terrain is tagged {tag}" : $"the terrain isn't tagged {tag}");
            return true;
        }

        static List<string> Tags(JToken entry) => (entry["tags"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();

        // ---------- helpers ----------

        static string Table(string table)
        {
            var t = Required(table, "table").ToLowerInvariant();
            if (t == "material") t = "materials";
            if (t == "reaction" || t == "rules") t = "reactions";
            if (t == "look" || t == "families") t = "looks";
            if (t == "species" || t == "animals" || t == "creatures") t = FaunaTable;
            if (t == "part" || t == "shapes") t = PartsTable;
            if (t == "spell" || t == "cover" || t == "gas" || t == "element" || t == "group" || t == "critter" || t == "entity" || t == "item" || t == "machine")
                t = Plural(t);
            if (!Tables.Contains(t)) throw new RulesToolException($"{table} isn't a table; the tables are {string.Join(", ", Tables)}.");
            return t;
        }

        internal static string Single(string table) => table switch
        {
            "terrain" => "terrain",
            "covers" => "cover",
            "gases" => "gas",
            "entities" => "entity",
            "sim" => "sim parameter",
            "reactions" => "reaction rule",
            FaunaTable => "species",
            PartsTable => "part",
            _ => table.TrimEnd('s'),
        };

        IEnumerable<JProperty> Entries(string kind) => ((JObject)Raw["materials"][kind]).Properties();
        IEnumerable<JToken> RawList(string file, string key) => (Raw[file][key] as JArray) ?? new JArray();

        internal static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static string Plural(string t) => t switch
        {
            "gas" => "gases",
            "entity" => "entities",
            _ => t + "s",
        };

        internal static string Compact(JToken t)
        {
            if (t == null) return "nothing";
            return t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
        }
        internal static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        static bool Truthy(JToken t) => t != null && !(t.Type == JTokenType.Boolean && !(bool)t) && t.Type != JTokenType.Null;
    }
}
