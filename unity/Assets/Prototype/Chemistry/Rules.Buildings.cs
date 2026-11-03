// The rules' side of buildings (docs/design/builders.md section 4): the parts
// tables in rules/buildings.json, and the rows mods bring in their "world" section.
// A row replaces the one under its id, as every table's does (ModPatch), so the rows
// in force are always a pure function of the base rules and the mods on, and a
// revert is only composing again. World.Buildings.cs lays what changed.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        /// <summary>Where the composed document holds the mods' building rows, by id.</summary>
        internal const string WorldRowsKey = "worldBuildings";

        /// <summary>rules/buildings.json with the mods' rows.</summary>
        public BuildingTables Buildings { get; private set; } = new BuildingTables();

        /// <summary>The building rows the mods on have brought, whether edits or new, in id order.</summary>
        public IReadOnlyList<BuildingRow> ModBuildings { get; private set; } = Array.Empty<BuildingRow>();

        void ParseBuildings(JObject d)
        {
            Buildings = BuildingTables.Parse(d);
            if (!(d[WorldRowsKey] is JObject rows)) return;
            ModBuildings = rows.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => BuildingRow.Read(p.Name, (JObject)p.Value, Buildings)).ToList();
        }

        static void ApplyBuildings(JObject r, JObject p)
        {
            if (p[BuildingTables.Key] is JObject tables)
            {
                if (!(r[BuildingTables.Key] is JObject mine)) r[BuildingTables.Key] = mine = new JObject();
                foreach (var table in BuildingTables.Tables)
                {
                    if (!(tables[table] is JObject named)) continue;
                    if (!(mine[table] is JObject rows)) mine[table] = rows = new JObject();
                    foreach (var entry in named.Properties()) rows[entry.Name] = KeepingLook(rows[entry.Name], entry.Value.DeepClone());
                }
            }
            if (!(p["world"]?["buildings"] is JObject given)) return;
            if (!(r[WorldRowsKey] is JObject world)) r[WorldRowsKey] = world = new JObject();
            foreach (var entry in given.Properties()) world[entry.Name] = entry.Value.DeepClone();
        }

        sealed partial class Checker
        {
            public void Buildings()
            {
                var tables = BuildingTables.Parse(r);
                OpeningRows(tables);
                foreach (var p in (tables.Json["floors"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (terrain[Show(p.Value["terrain"])] == null) Errors.Add($"floor {p.Name}: unknown terrain {Show(p.Value["terrain"])}");
                foreach (var p in (tables.Json["styles"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    StyleRow(p.Name, (JObject)p.Value, tables);
                foreach (var p in (r[WorldRowsKey] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    BuildingRowChecks(p.Name, (JObject)p.Value, tables);
            }

            void OpeningRows(BuildingTables tables)
            {
                foreach (var p in (tables.Json["openings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (p.Value["cells"] != null && !IsWholeAtLeast(p.Value["cells"], 0)) Errors.Add($"opening {p.Name}: cells must be a whole number, 0 or more");
                    string becomes = Show(p.Value["becomes"]);
                    if (becomes != OpeningDef.StaysWall && becomes != OpeningDef.Open && terrain[becomes] == null)
                        Errors.Add($"opening {p.Name}: becomes names unknown terrain {becomes} (a terrain, \"{OpeningDef.StaysWall}\" or \"{OpeningDef.Open}\")");
                    Lamp($"opening {p.Name}", p.Value["look"]?["lamp"]);
                }
            }

            void StyleRow(string id, JObject style, BuildingTables tables)
            {
                WallMaterial($"style {id}", style["wall"]?["material"]);
                if (style["fill"] != null) Fill($"style {id}", style["fill"]);
                if (style["floor"]?["kind"] != null) Named($"style {id}", "floor", "floors", style["floor"]["kind"], tables);
                if (style["roof"] != null) Roof($"style {id}", style["roof"], tables, null);
                if (!(style["recipe"] is JObject recipe)) return;
                foreach (var key in new[] { "width", "depth" })
                    if (!(recipe[key] is JArray a) || a.Count != 2 || !a.All(v => IsWholeAtLeast(v, 2)))
                        Errors.Add($"style {id}: recipe {key} must be [smallest, largest], whole cells, 2 or more");
                foreach (var key in new[] { "door", "window" })
                    if (recipe[key] != null) Named($"style {id}", "opening", "openings", recipe[key], tables);
            }

            void BuildingRowChecks(string id, JObject given, BuildingTables tables)
            {
                string who = $"building {id}";
                if (given["style"] != null) Named(who, "style", "styles", given["style"], tables);
                var row = BuildingRow.Resolved(given, tables);
                if (!(row["at"] is JArray at) || at.Count != 2 || !at.All(v => IsWholeAtLeast(v, 0)))
                {
                    Errors.Add($"{who} needs \"at\": [x, y], the cell it counts from");
                    return;
                }
                if (row["fill"] != null) Fill(who, row["fill"]);
                if (row["floor"]?["kind"] != null) Named(who, "floor", "floors", row["floor"]["kind"], tables);
                if (row["storeys"] != null && !IsWholeAtLeast(row["storeys"], 1)) Errors.Add($"{who}: storeys must be a whole number, 1 or more");
                foreach (var t in (row["built"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (terrain[t.Name] == null) Errors.Add($"{who}: built names unknown terrain {t.Name}");
                    if (!(t.Value is JArray spans) || !spans.All(s => s is JArray a && a.Count % 2 == 1 && a.Count >= 3)) Errors.Add($"{who}: built cells are spans, [y, x0, x1, ...] from at");
                }
                var joins = row["joins"] as JObject ?? new JObject();
                foreach (var j in joins.Properties())
                    if (!(j.Value is JArray xy) || xy.Count != 2 || !xy.All(IsWhole)) Errors.Add($"{who}: join {j.Name} must be [x, y], whole half cells from at");
                var walls = row["walls"] as JObject;
                if (walls == null || walls.Count == 0)
                {
                    if (row["built"] == null) Errors.Add($"{who} has no walls, and its style has no recipe to lay them");
                }
                else
                {
                    if (row["wall"]?["material"] == null && walls.PropertyValues().Any(w => w["material"] == null)) Errors.Add($"{who} needs a wall material, in \"wall\", in its style or on each wall");
                    else WallMaterial(who, row["wall"]?["material"]);
                    foreach (var w in walls.Properties()) Wall(who, w.Name, w.Value, joins, tables, (int?)row["storeys"] ?? 1);
                }
                foreach (var f in (row["roofs"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) Roof($"{who}: roof {f.Name}", f.Value, tables, joins);
                foreach (var part in (row["parts"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    Named(who, "detail", "details", part["kind"], tables);
                    if (part["on"] != null && walls?[Show(part["on"])] == null) Errors.Add($"{who}: a {Show(part["kind"])} is on wall {Show(part["on"])}, which it hasn't got");
                }
            }

            void Wall(string who, string id, JToken wall, JObject joins, BuildingTables tables, int storeys)
            {
                string me = $"{who}: wall {id}";
                var from = joins[Show(wall["from"])] as JArray;
                var to = joins[Show(wall["to"])] as JArray;
                if (from == null || to == null)
                {
                    Errors.Add($"{me} needs from and to, each one of its joins");
                    return;
                }
                if (!from.All(IsWhole) || !to.All(IsWhole)) return; // the join's own sentence says so
                int dx = Math.Abs((int)to[0] - (int)from[0]), dy = Math.Abs((int)to[1] - (int)from[1]);
                if (dx == 0 && dy == 0) Errors.Add($"{me} runs nowhere: from and to are the same place");
                else if (dx != 0 && dy != 0 && dx != dy) Errors.Add($"{me} must run along an axis or at 45°");
                if (wall["material"] != null) WallMaterial(me, wall["material"]);
                if (wall["storey"] != null && (!IsWholeAtLeast(wall["storey"], 0) || (int)wall["storey"] >= storeys)) Errors.Add($"{me}: storey must be a whole number below the building's storeys ({storeys})");
                int length = Math.Max(dx, dy);
                foreach (var o in (wall["openings"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) Opening(me, o, length, tables);
            }

            void Opening(string wall, JObject o, int length, BuildingTables tables)
            {
                Named(wall, "opening", "openings", o["kind"], tables);
                Lamp($"{wall}: a {Show(o["kind"])}", o["lamp"]);
                var kind = tables.Row("openings", Show(o["kind"]));
                if (kind == null) return;
                if (o["at"] != null && !IsWholeAtLeast(o["at"], 0))
                {
                    Errors.Add($"{wall}: an opening's at must be a whole number of half cells from the wall's from");
                    return;
                }
                if (o["width"] != null && !(o["width"].Type == JTokenType.Integer || o["width"].Type == JTokenType.Float))
                {
                    Errors.Add($"{wall}: an opening's width is in metres");
                    return;
                }
                int at = (int?)o["at"] ?? 0, span = BuildingRow.SpanOf(o, tables);
                if (at + span > length) Errors.Add($"{wall}: a {Show(o["kind"])} at {at} is {span} half cells long and runs past the wall's {length}");
                // One that leaves a cell keeps half a cell clear of a join; a gap may run to the corners.
                else if (Show(kind["becomes"]) != OpeningDef.Open && (at < 1 || at + span > length - 1))
                    Errors.Add($"{wall}: a {Show(o["kind"])} at {at} must stand half a cell clear of a join");
            }

            void Roof(string who, JToken roof, BuildingTables tables, JObject joins)
            {
                if (roof["shape"] != null) Named(who, "roof shape", "roofs", roof["shape"], tables);
                if (roof["cover"] != null)
                {
                    var cover = terrain[Show(roof["cover"])];
                    if (cover == null) Errors.Add($"{who}: cover names unknown terrain {Show(roof["cover"])}");
                    else if (!Strings(cover["tags"]).Contains("roof")) Errors.Add($"{who}: cover {Show(roof["cover"])} isn't a roof terrain (it needs the tag roof)");
                }
                if (joins == null) return;
                var over = (roof["over"] as JArray)?.Select(Show).ToList();
                if (over == null || over.Count < 3) Errors.Add($"{who} needs \"over\": the joins of its outline, three or more");
                else foreach (var j in over.Where(j => joins[j] == null)) Errors.Add($"{who} is over join {j}, which the building hasn't got");
            }

            // A window's light at night (the view's): a colour, or off.
            void Lamp(string who, JToken lamp)
            {
                if (lamp != null && !BuildingsShown.IsLamp(lamp)) Errors.Add($"{who}: lamp is the colour its window glows at night, \"#rrggbb\", or \"{BuildingsShown.LampOff}\", not {Show(lamp)}");
            }

            void WallMaterial(string who, JToken material)
            {
                if (material == null) return;
                var t = terrain[Show(material)];
                if (t == null) Errors.Add($"{who}: unknown wall material {Show(material)} (a terrain)");
                else if (!Strings(t["tags"]).Contains("blocking")) Errors.Add($"{who}: wall material {Show(material)} doesn't block, so it can't be a wall (it needs the tag blocking)");
            }

            void Fill(string who, JToken fill)
            {
                if (Show(fill) != "solid" && Show(fill) != "hollow") Errors.Add($"{who}: fill is solid or hollow, not {Show(fill)}");
            }

            void Named(string who, string what, string table, JToken id, BuildingTables tables)
            {
                if (tables.Row(table, id?.Type == JTokenType.String ? (string)id : null) == null)
                    Errors.Add($"{who}: unknown {what} {Show(id)}; rules/buildings.json has {tables.Ids(table)}");
            }

            static bool IsWhole(JToken t) => t != null && (t.Type == JTokenType.Integer || (t.Type == JTokenType.Float && Math.Abs((double)t - Math.Floor((double)t)) < 1e-9));
            static bool IsWholeAtLeast(JToken t, int least) => IsWhole(t) && (double)t >= least;
        }
    }
}
