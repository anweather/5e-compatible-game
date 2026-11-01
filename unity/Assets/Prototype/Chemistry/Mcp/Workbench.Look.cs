// The look tools: set_look, and what describe and list say of looks. A look is
// the view's only (rules/looks.json, docs/RULES_MCP.md "Looks"): a spell's or
// material's names its family, whose parameters it may change; an element's,
// and a cover's or gas's until a family of theirs is drawn, is bare, a colour.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S1168 // a JToken is a collection to the analyser; null here means "no such entry"

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        /// <summary>The tables whose entries have a look, by the name set_look takes.</summary>
        static readonly string[] LookTables = { "spells", "terrain", "covers", "gases", "elements" };

        /// <summary>
        /// One parameter of the look of a spell, terrain, cover, gas or element, by id; null removes
        /// it. Only the parameters its family takes (a bare look's: color), and "form" where the
        /// family has forms. <paramref name="said"/> is what it takes, and anything to know.
        /// </summary>
        public Draft SetLook(string draftId, string id, string param, string value, string table, out string said)
        {
            id = Required(id, "id");
            param = Required(param, "parameter").ToLowerInvariant();
            if (param == "colour") param = "color";
            var draft = DraftOf(draftId);
            table = LookTableOf(draft, id, table);
            var look = LookIn(table, id, Current(draft, table, id));
            string family = Str(look["family"]);
            var takes = Takes(table, family, out var familyOf);
            if (!takes.Contains(param))
                throw new RulesToolException($"The {Single(table)} {id}'s look ({Described(table, family)}) takes {List(takes)}; {param} isn't one of them.");
            var v = LookValue(param, Value(value), familyOf);
            var entry = Entry(draft, table, id);
            if (!(entry["look"] is JObject own)) entry["look"] = own = (JObject)look.DeepClone();
            if (v.Type == JTokenType.Null) own.Remove(param);
            else own[param] = v;
            draft.Changed.Add($"{table} {id}");
            string wont = param == "color" && v.Type != JTokenType.Null ? WontShow(table, id, familyOf, own, v) : null;
            said = $"The {Single(table)} {id}'s look ({Described(table, family)}) takes {List(takes)}.{LookNote(table, id, entry, familyOf)}{(wont == null ? "" : " " + wont + " Tell the player.")}\n";
            return draft;
        }

        // Why a colour won't show, or null: what it's drawn from now is the family's own art, else the base rules' colour.
        string WontShow(string table, string id, LookFamily family, JObject look, JToken colour)
        {
            float[] now = colour.Select(c => (float)c).ToArray();
            float[] was = family?.Own ?? (table == "elements" ? BaseElement(id)?["look"] : Raw["materials"][table]?[id]?["look"])?["color"]?.Select(c => (float)c).ToArray();
            return LookFamilies.WontShow(Budgets.Looks, table, id, family, look["baked"]?.Value<bool>() == true, was, now);
        }

        // Which table an id's look is in: the one named, else the only one holding it.
        string LookTableOf(Draft draft, string id, string table)
        {
            var holding = LookTables.Where(t => Current(draft, t, id) != null).ToList();
            if (!string.IsNullOrWhiteSpace(table))
            {
                var t = Table(table);
                if (t == "materials") holding = holding.Where(MaterialKinds.Contains).ToList();
                else if (!LookTables.Contains(t)) throw new RulesToolException($"A {Single(t)} has no look; set_look takes a spell, terrain, cover, gas or element.");
                else holding = holding.Where(h => h == t).ToList();
                if (holding.Count == 0) throw new RulesToolException($"There is no {Single(t == "materials" ? "terrain" : t)} called {id}; list {t} to see the ids.");
            }
            if (holding.Count == 0) throw new RulesToolException($"Nothing called {id} has a look; set_look takes a spell, terrain, cover, gas or element by id.");
            if (holding.Count > 1)
                throw new RulesToolException($"{id} is both {string.Join(" and ", holding.Select(h => ("aeiou".Contains(Single(h)[0]) ? "an " : "a ") + Single(h)))}; say which with table. " +
                    "An element's look colours what it makes anywhere, whichever spell or reaction made it; a spell's or material's colours only its own.");
            return holding[0];
        }

        // The entry as the draft has it, else as the base has it, else null.
        JToken Current(Draft draft, string table, string id)
        {
            switch (table)
            {
                case "spells":
                    return (draft.Patch["spells"] as JArray)?.FirstOrDefault(s => Str(s["id"]) == id) ?? ((JArray)Raw["spells"]).FirstOrDefault(s => Str(s["id"]) == id);
                case "elements":
                    if (draft.Patch["elements"] is JObject named && named[id] is JObject entry) return entry;
                    bool known = BaseDoc["elements"].Any(e => Str(e) == id) || ModPatch.ElementNames(draft.Patch["elements"]).Contains(id);
                    return known ? BaseElement(id) ?? new JObject() : null;
                default:
                    return draft.Patch[table]?[id] ?? Raw["materials"][table]?[id];
            }
        }

        JToken BaseElement(string id) => Raw.TryGetValue("elements", out var table) ? table[id] : null;

        // The entry in the draft, copied from the base the first time.
        JObject Entry(Draft draft, string table, string id)
        {
            switch (table)
            {
                case "spells":
                    var spells = Spells(draft);
                    int at = IndexOfId(spells, id);
                    if (at >= 0) return (JObject)spells[at];
                    var spell = (JObject)Current(draft, table, id).DeepClone();
                    spells.Add(spell);
                    return spell;
                case "elements":
                    var named = Elements(draft);
                    if (!(named[id] is JObject entry)) named[id] = entry = (JObject)(BaseElement(id)?.DeepClone() ?? new JObject());
                    return entry;
                default:
                    var section = Section(draft, table);
                    if (!(section[id] is JObject material)) section[id] = material = (JObject)Raw["materials"][table][id].DeepClone();
                    return material;
            }
        }

        /// <summary>The draft's elements as entries by id: a list of new names becomes entries with nothing in them.</summary>
        static JObject Elements(Draft draft)
        {
            if (draft.Patch["elements"] is JObject named) return named;
            named = new JObject();
            foreach (var e in ModPatch.ElementNames(draft.Patch["elements"])) named[e] = new JObject();
            draft.Patch["elements"] = named;
            return named;
        }

        static JObject LookIn(string table, string id, JToken entry)
        {
            if (entry?["look"] is JObject look) return look;
            if (table != "terrain" && table != "spells") return new JObject();
            string tool = table == "spells" ? "set_spell" : "set_material";
            throw new RulesToolException($"The {Single(table)} {id} has no look to change; give it one with {tool} (field look, naming a family from rules/looks.json).");
        }

        // The parameters a look may change: its family's, and "form" where the family has forms.
        List<string> Takes(string table, string family, out LookFamily familyOf)
        {
            familyOf = family == null ? null : Budgets.Looks.Find(table, family);
            var takes = Budgets.Looks.ParamsOf(table, family)?.ToList()
                ?? throw new RulesToolException($"The look family \"{family}\" isn't one rules/looks.json has for a {Single(table)}; validate says which are.");
            if (familyOf?.Forms.Count > 0) takes.Add("form");
            return takes;
        }

        static string Described(string table, string family)
        {
            if (family != null) return $"family {family}";
            return table == "elements" ? "an element's: only a colour" : $"no family, only a colour (no {Single(table)} family is drawn yet)";
        }

        static string List(IReadOnlyCollection<string> takes) => takes.Count == 0 ? "nothing yet" : string.Join(", ", takes);

        // A parameter's value as the look keeps it, or the sentence saying what it must be.
        static JToken LookValue(string param, JToken v, LookFamily family)
        {
            if (v.Type == JTokenType.Null) return v;
            switch (param)
            {
                case "color":
                    return Colour(v) ?? throw new RulesToolException("color is three numbers from 0 to 1, red, green and blue, such as [0.25, 0.45, 1] for a blue (or \"#4073ff\").");
                case "form":
                    return Str(v) is string form && family.Forms.Contains(form) ? v : throw new RulesToolException($"The look family \"{family.Name}\" takes a form, one of {string.Join(", ", family.Forms)}.");
                case "baked":
                    return v.Type == JTokenType.Boolean ? v : throw new RulesToolException("baked is true or false.");
                case "roughness":
                    return IsNumber(v) && (double)v >= 0 && (double)v <= 1 ? v : throw new RulesToolException("roughness is a number from 0 (glossy) to 1 (matt).");
                default:
                    return IsNumber(v) && (double)v >= 0 ? v : throw new RulesToolException($"{param} is a number from 0.");
            }
        }

        static bool IsNumber(JToken v) => v.Type == JTokenType.Integer || v.Type == JTokenType.Float;

        /// <summary>[r, g, b] from 0 to 1 (or 0 to 255), or "#rrggbb", as [r, g, b] from 0 to 1; null if it's neither.</summary>
        static JArray Colour(JToken v)
        {
            if (Str(v) is string hex && hex.Trim().TrimStart('#') is string h && h.Length == 6
                && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return new JArray(Channel((rgb >> 16) & 255, 255), Channel((rgb >> 8) & 255, 255), Channel(rgb & 255, 255));
            if (!(v is JArray c) || c.Count != 3 || !c.All(x => IsNumber(x) && (double)x >= 0)) return null;
            double scale = c.Any(x => (double)x > 1) ? 255 : 1;
            if (c.Any(x => (double)x > scale)) return null;
            return new JArray(c.Select(x => Channel((double)x, scale)));
        }

        static double Channel(double x, double scale) => Math.Round(x / scale, 3);

        // What else the caller should know: where else the colour shows, or where it doesn't.
        static string LookNote(string table, string id, JObject entry, LookFamily family)
        {
            var notes = new List<string>();
            if (family?.Notes != null) notes.Add(family.Notes);
            if (table == "spells" && Str(entry["element"]) is string element)
                notes.Add($"This colours {Str(entry["name"]) ?? id}'s own show; what its {element} leaves on cells is the {element} element's look (set_look on {element}, table element: it changes every {element}).");
            if (table == "elements")
                notes.Add($"Every {id} in the world takes it, whichever spell or reaction made it; a spell's own show keeps the spell's look.");
            return notes.Count == 0 ? "" : " " + string.Join(" ", notes);
        }

        // ---------- what describe and list say ----------

        /// <summary>describe's last line for an entry with a look: what draws it and what set_look may change.</summary>
        string LookLine(string table, string id, JToken entry)
        {
            if (!LookTables.Contains(table)) return "";
            if (!(entry?["look"] is JObject) && (table == "terrain" || table == "spells"))
                return $"\nNo look: the view can't draw it from data. Give it one naming a family ({Budgets.Looks.DrawnNames(table)}).";
            string family = Str(entry?["look"]?["family"]);
            var takes = Budgets.Looks.ParamsOf(table, family)?.ToList() ?? new List<string>();
            var familyOf = family == null ? null : Budgets.Looks.Find(table, family);
            if (familyOf?.Forms.Count > 0) takes.Add($"form ({string.Join(", ", familyOf.Forms)})");
            string about = familyOf?.About ?? (table == "elements" ? $"the colour of what {id} makes in the world, whichever spell or reaction made it" : null);
            return $"\nLook: {Described(table, family)}{(about == null ? "" : $", {about.TrimEnd('.')}")}. set_look changes {List(takes)}." +
                (familyOf?.Notes != null ? " " + familyOf.Notes : "");
        }

        /// <summary>list looks: the families by table with the parameters each takes, the bare look, and what takes no colour.</summary>
        List<string> LookLines()
        {
            var looks = Budgets.Looks;
            var lines = new List<string> { $"bare ({string.Join(", ", looks.BareTables)}): no family; takes {List(looks.Bare.Params)}. {looks.Bare.About}" };
            var noColour = new List<string>();
            foreach (var table in looks.TableNames)
                foreach (var f in looks.In(table))
                {
                    if (!f.Params.Contains("color")) noColour.Add($"{table} {f.Name}");
                    var forms = f.Forms.Count > 0 ? $"; forms {string.Join(", ", f.Forms)}" : "";
                    var what = f.Drawn ? $"takes {List(f.Params)}{forms}" : $"not drawn from data yet (needs {f.Needs})";
                    lines.Add($"{table} {f.Name}: {what}.{(f.About == null ? "" : " " + f.About)}{(f.Notes == null ? "" : " " + f.Notes)}");
                }
            lines.Add($"Takes no colour: {string.Join(", ", noColour)}. A fire's flames are the fire element's colour; a family not drawn yet waits on the view (its \"needs\"), and a cover or gas may give a bare colour meanwhile.");
            return lines;
        }
    }
}
