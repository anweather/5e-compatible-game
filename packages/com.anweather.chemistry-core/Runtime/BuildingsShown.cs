// What a mod's buildings say that nothing ever draws (#71): rules/buildings.json "drawn" lists what the
// view reads of a row, a wall, an opening and a style, and a roof's and a detail's own look.takes what
// it reads of those; the view draws only the roof shapes and details the shipped rules have. validate
// and apply name what's left over in a line, so a model can tell the player instead of claiming a
// change nobody will see. An opening that becomes a terrain of its own is named too: on a building the
// house draws its openings from their rows, never the cells' looks. Words only: nothing here reaches
// the cells, the hash or the fingerprint. Not a port: the sandbox has no buildings.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class BuildingsShown
    {
        /// <summary>What an opening's "lamp" says for no light.</summary>
        public const string LampOff = "off";

        /// <summary>Whether <paramref name="lamp"/> is a lamp's colour, "#rrggbb", or "off".</summary>
        public static bool IsLamp(JToken lamp)
        {
            if (lamp?.Type != JTokenType.String) return false;
            string s = (string)lamp;
            return s == LampOff || (s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit));
        }

        /// <summary>
        /// What <paramref name="patch"/>'s buildings say that the view won't draw, a phrase each, in the patch's order.
        /// <paramref name="composed"/> is the tables with the patch on (its styles resolve the rows); <paramref name="shipped"/>
        /// the base rules' tables, whose "drawn" lists, roof shapes and details are what the view knows.
        /// </summary>
        public static IReadOnlyList<string> Unseen(JObject patch, BuildingTables composed, BuildingTables shipped)
        {
            var said = new List<string>();
            var drawn = shipped.Json["drawn"] as JObject ?? new JObject();
            var tables = patch?[BuildingTables.Key] as JObject;
            foreach (var p in (tables?["openings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) OpeningRow(said, drawn, p.Name, p.Value, shipped);
            foreach (var p in (tables?["styles"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                Extra(said, $"style {p.Name}", p.Value, drawn["style"], "a style");
            foreach (var p in (tables?["roofs"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                if (shipped.Row("roofs", p.Name) == null) said.Add($"roof shape {p.Name} (the view draws {shipped.Ids("roofs")}; another is drawn as a hip)");
            foreach (var p in (tables?["details"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                if (shipped.Row("details", p.Name) == null) said.Add($"detail {p.Name} (the view draws {shipped.Ids("details")})");
            foreach (var p in (patch?["world"]?["buildings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                if (p.Value is JObject row) Row(said, drawn, p.Name, BuildingRow.Resolved(row, composed), shipped);
            return said;
        }

        /// <summary>validate's and apply's line for what won't show, or "" if everything will.</summary>
        public static string Note(IReadOnlyList<string> unseen) =>
            unseen.Count == 0 ? "" :
            "\nWon't show: nothing in the game draws " + string.Join("; ", unseen) +
            ". It's kept, but the player will see no change from it: don't say it worked, and say what the game can draw instead.";

        static void OpeningRow(List<string> said, JObject drawn, string id, JToken row, BuildingTables shipped)
        {
            Extra(said, $"opening {id}'s look", row["look"], drawn["openingLook"], "an opening's look");
            string becomes = (string)row["becomes"];
            bool known = becomes == null || becomes == OpeningDef.StaysWall || becomes == OpeningDef.Open || shipped.Openings.Values.Any(o => o.Becomes == becomes);
            if (!known)
                said.Add($"the look of {becomes}, which opening {id} becomes (on a building the house draws each opening from its row, not its cells' looks; " +
                    "keep becomes pane for glass, which breaks, and give the opening a look lamp for a window lit at night)");
        }

        static void Row(List<string> said, JObject drawn, string id, JObject row, BuildingTables shipped)
        {
            string who = $"building {id}";
            Extra(said, who, row, drawn["row"], "a building");
            Extra(said, $"{who}'s floor", row["floor"], drawn["floor"], "a floor");
            Extra(said, $"{who}'s wall", row["wall"], drawn["wall"], "a wall");
            Extra(said, $"{who}'s wall finish", row["wall"]?["finish"], drawn["finish"], "a finish");
            foreach (var w in (row["walls"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                Extra(said, $"{who}'s wall {w.Name}", w.Value, drawn["wall"], "a wall");
                Extra(said, $"{who}'s wall {w.Name} finish", w.Value["finish"], drawn["finish"], "a finish");
                int n = 0;
                foreach (var o in (w.Value["openings"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    n++;
                    string opening = $"{who}'s wall {w.Name} opening {n} ({(string)o["kind"]})";
                    Extra(said, opening, o, drawn["opening"], "an opening in a wall");
                    Extra(said, opening + " finish", o["finish"], drawn["finish"], "a finish");
                }
            }
            foreach (var r in (row["roofs"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                string shape = (string)r.Value["shape"] ?? "gable";
                var takes = shipped.Row("roofs", shape)?["look"]?["takes"];
                if (takes == null) continue; // a new shape is named with its table row
                Extra(said, $"{who}'s roof {r.Name}", r.Value, new JArray(takes.Append("shape")), $"a {shape} roof");
            }
            int k = 0;
            foreach (var part in (row["parts"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                k++;
                string kind = (string)part["kind"];
                var takes = shipped.Row("details", kind)?["look"]?["takes"];
                if (takes == null)
                {
                    said.Add($"{who}'s part {k} ({kind}: the view draws {shipped.Ids("details")})");
                    continue;
                }
                Extra(said, $"{who}'s part {k} ({kind})", part, new JArray(takes.Append("kind")), $"a {kind}");
            }
        }

        // The fields of <paramref name="given"/> outside <paramref name="reads"/>, as one phrase.
        static void Extra(List<string> said, string where, JToken given, JToken reads, string what)
        {
            if (!(given is JObject fields) || !(reads is JArray list)) return;
            var known = new HashSet<string>(list.Select(t => (string)t), StringComparer.Ordinal);
            var extra = fields.Properties().Select(p => p.Name).Where(n => !known.Contains(n)).ToList();
            if (extra.Count == 0) return;
            said.Add($"{where}: {string.Join(", ", extra)} ({what} takes {string.Join(", ", known)})");
        }
    }
}
