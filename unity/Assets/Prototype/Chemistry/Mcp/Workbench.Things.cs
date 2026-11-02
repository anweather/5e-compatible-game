// The tools for things (docs/design/world-tools.md G4 to G6): add_item and add_entity write
// a new kind with its look, and add_thing a row of the mod's "world" section's "add". What's
// asked for comes from the caller: no kind of thing is named here.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string ThingsTable = "things";
        const string CreatureFamily = "creature";

        static bool IsThingTable(string table) => table == "entities" || table == "items";

        /// <summary>A new kind of thing to carry, with the look that draws it.</summary>
        public Draft AddItem(string draftId, string id, string name, double? mass, string material, string look, string extra)
        {
            var entry = NewThing("items", id, name, material, look, extra);
            if (mass.HasValue) entry["mass"] = mass.Value;
            return PutThing(draftId, "items", id.Trim(), entry);
        }

        /// <summary>A new kind of prop, with the look that draws it.</summary>
        public Draft AddEntity(string draftId, string id, string name, int? hp, int? mass, string material, string look, string extra)
        {
            var entry = NewThing("entities", id, name, material, look, extra);
            if (hp.HasValue) entry["hp"] = hp.Value;
            if (mass.HasValue) entry["mass"] = mass.Value;
            return PutThing(draftId, "entities", id.Trim(), entry);
        }

        JObject NewThing(string table, string id, string name, string material, string look, string extra)
        {
            id = Required(id, "id");
            if (EntryIn(Raw, "entities", id) != null || EntryIn(Raw, "items", id) != null)
                throw new RulesToolException($"There is already a thing called {id}; pick another id, or set_look to change how it looks.");
            var entry = new JObject { ["name"] = Required(name, "name") };
            if (!string.IsNullOrWhiteSpace(material)) entry["material"] = material.Trim();
            if (!string.IsNullOrWhiteSpace(extra))
            {
                if (!(Value(extra) is JObject more)) throw new RulesToolException("extra is a JSON object of more fields, such as {\"use\": \"light\"}.");
                foreach (var p in more.Properties()) entry[p.Name] = p.Value;
            }
            if (!string.IsNullOrWhiteSpace(look)) entry["look"] = ThingLook(table, look);
            RuleFields.CheckThing(table, entry);
            return entry;
        }

        // The look as the rules keep it, each parameter checked as set_look checks it.
        JObject ThingLook(string table, string json)
        {
            if (!(Value(json) is JObject given)) throw new RulesToolException("look is a JSON object, such as {\"family\": \"creature\", \"species\": ..., \"color\": [1, 0.8, 0.2], \"scale\": 1}.");
            string family = Str(given["family"]) ?? CreatureFamily;
            var takes = Takes(table, family, out var familyOf);
            var look = new JObject { ["family"] = family };
            foreach (var p in given.Properties().Where(p => p.Name != "family"))
            {
                if (!takes.Contains(p.Name)) throw new RulesToolException($"A thing's look ({Described(table, family)}) takes {List(takes)}; {p.Name} isn't one of them.");
                look[p.Name] = LookValue(p.Name, p.Value, familyOf);
            }
            return look;
        }

        Draft PutThing(string draftId, string table, string id, JObject entry)
        {
            var draft = DraftOf(draftId);
            Section(draft, table)[id] = entry;
            draft.Changed.Add($"{table} {id}");
            return draft;
        }

        /// <summary>A thing of this kind on this cell, appended to its chunk's list by the mod.</summary>
        public Draft AddThing(string draftId, string kind, string at, double? chance, double? rot)
        {
            kind = Required(kind, "kind");
            var cell = Cell(Required(at, "cell"), "at");
            if (cell.x < 0 || cell.y < 0) throw new RulesToolException("at is a cell of the world, [x, y], from 0.");
            if (chance.HasValue && !(chance.Value > 0 && chance.Value <= 1)) throw new RulesToolException("chance is above 0 and at most 1; leave it out for a thing that's always there.");
            var draft = DraftOf(draftId);
            if (Current(draft, "items", kind) == null && Current(draft, "entities", kind) == null)
                throw new RulesToolException($"There is no thing called {kind}; add_item or add_entity makes a new kind, and list items or list entities shows what there is.");
            var row = new JObject { ["kind"] = kind, ["cell"] = new JArray(cell.x, cell.y) };
            if (rot.HasValue) row["rot"] = rot.Value;
            if (chance.HasValue) row["chance"] = chance.Value;
            var world = Section(draft, "world");
            if (!(world["add"] is JArray rows)) world["add"] = rows = new JArray();
            string key = CellKey(row["cell"]);
            var same = rows.FirstOrDefault(t => CellKey(t["cell"]) == key);
            if (same != null) same.Replace(row);
            else rows.Add(row);
            draft.Changed.Add($"{ThingsTable} {key}");
            return draft;
        }

        static string CellKey(JToken cell) => string.Format(CultureInfo.InvariantCulture, "{0},{1}", (int)cell[0], (int)cell[1]);

        // apply's words for one row of the draft.
        static string ThingChange(Draft draft, string key)
        {
            var row = (draft.Patch["world"]?["add"] as JArray)?.FirstOrDefault(t => CellKey(t["cell"]) == key);
            if (row == null) return $"a thing at {key}";
            string chance = row["chance"] == null ? "" : $", at a chance of {Num((double)row["chance"])}";
            return $"a {Str(row["kind"])} at [{key.Replace(",", ", ", System.StringComparison.Ordinal)}]{chance}";
        }

        static IEnumerable<JToken> ThingsIn(Dictionary<string, JToken> raw) =>
            raw.TryGetValue(Rules.WorldThingsKey, out var rows) ? rows : Enumerable.Empty<JToken>();

        // list things: what the mods on add, and which of those are in this world.
        IEnumerable<string> ModThingLines() =>
            OnWorld(w =>
            {
                var here = w.ThingsNow.All.Where(t => t.FromMod).ToList();
                return w.Rules.ModThings.Select(t =>
                    FormattableString.Invariant($"{t.Kind} at [{t.X}, {t.Y}]{(t.Chance < 1 ? $", chance {t.Chance}" : "")}") +
                    (here.Any(h => h.CellX == t.X && h.CellY == t.Y && h.Kind == t.Kind) ? "" : " (not in this world: its chance didn't hold)")).ToList();
            });
    }
}
