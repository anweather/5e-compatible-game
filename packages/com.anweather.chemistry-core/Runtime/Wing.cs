// A wing on a building's row (docs/design/builders.md "The edits"): new joins and
// walls on one side, a T where they meet the old wall, whose openings there go, and the
// roof's outline grown. A row with no wall on that side (a cabin, whose cells only
// "built" holds) gets a closed box against its cells instead. Pure JSON over the row.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class Wing
    {
        // The opening rows a wing asks for by name, from rules/buildings.json.
        const string Door = "door", Window = "window";

        /// <summary>The longest wall facing this side: left of its way is outside, as outer walls run clockwise. Null if none.</summary>
        public static (string id, int length)? HostOf(JObject row, Side side)
        {
            var joins = row["joins"] as JObject;
            (string id, int length)? best = null;
            var (sx, sy) = Sides.Step(side);
            foreach (var w in (row["walls"] as JObject)?.Properties().OrderBy(p => p.Name, StringComparer.Ordinal) ?? Enumerable.Empty<JProperty>())
            {
                if (((int?)w.Value["storey"] ?? 0) != 0 || !(joins?[(string)w.Value["from"]] is JArray f) || !(joins[(string)w.Value["to"]] is JArray t)) continue;
                int dx = (int)t[0] - (int)f[0], dy = (int)t[1] - (int)f[1];
                if ((dx != 0 && dy != 0) || Math.Sign(dy) != sx || -Math.Sign(dx) != sy) continue;
                int length = Math.Abs(dx + dy);
                if (best == null || length > best.Value.length) best = (w.Name, length);
            }
            return best;
        }

        /// <summary>Adds a wing of width by depth cells to the row: on the wall facing that side where it fits along it, else against the row's cells.</summary>
        public static void Add(JObject row, Side side, int width, int depth, BuildingTables tables)
        {
            var host = HostOf(row, side);
            string name = FreeName(row);
            if (!(row["joins"] is JObject)) row["joins"] = new JObject();
            if (!(row["walls"] is JObject)) row["walls"] = new JObject();
            if (host != null && host.Value.length >= 2 * width) OnWall(row, host.Value.id, width, depth, name, tables);
            else AgainstCells(row, side, width, depth, name);
        }

        static string FreeName(JObject row)
        {
            int n = 1;
            while (row["joins"]?[$"wing{n}-a"] != null) n++;
            return $"wing{n}";
        }

        // Three walls out from the host and back, clockwise, between two new joins on it.
        static void OnWall(JObject row, string hostId, int width, int depth, string name, BuildingTables tables)
        {
            var joins = (JObject)row["joins"];
            var walls = (JObject)row["walls"];
            var host = (JObject)walls[hostId];
            var from = (JArray)joins[(string)host["from"]];
            var to = (JArray)joins[(string)host["to"]];
            int dx = Math.Sign((int)to[0] - (int)from[0]), dy = Math.Sign((int)to[1] - (int)from[1]);
            int length = Math.Abs((int)to[0] - (int)from[0]) + Math.Abs((int)to[1] - (int)from[1]);
            int start = (length - 2 * width) / 2;
            start -= start % 2;
            int sx = (int)from[0] + dx * start, sy = (int)from[1] + dy * start;
            int ox = dy * depth * 2, oy = -dx * depth * 2;
            int ex = sx + dx * width * 2, ey = sy + dy * width * 2;
            joins[name + "-a"] = new JArray(sx, sy);
            joins[name + "-b"] = new JArray(sx + ox, sy + oy);
            joins[name + "-c"] = new JArray(ex + ox, ey + oy);
            joins[name + "-d"] = new JArray(ex, ey);
            walls[name + "-near"] = new JObject { ["from"] = name + "-a", ["to"] = name + "-b" };
            walls[name + "-far"] = WithMiddle(new JObject { ["from"] = name + "-b", ["to"] = name + "-c" }, width, tables);
            walls[name + "-back"] = new JObject { ["from"] = name + "-c", ["to"] = name + "-d" };
            GoThrough(row, host, start, width, length, tables);
            GrowRoof(row, (string)host["from"], (string)host["to"], name);
        }

        // The host's openings under the wing go; a hollow building gets a door through to the wing.
        static void GoThrough(JObject row, JObject host, int start, int width, int length, BuildingTables tables)
        {
            var kept = new JArray();
            foreach (var o in (host["openings"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                int at = (int?)o["at"] ?? 0;
                if (at + BuildingRow.SpanOf(o, tables) <= start || at >= start + 2 * width) kept.Add(o.DeepClone());
            }
            if ((string)row["fill"] != "solid" && tables.Row("openings", Door) != null)
            {
                int door = start + 2 * ((width - 1) / 2);
                if (door >= 1 && door + 2 <= length - 1 && !kept.OfType<JObject>().Any(o => (int?)o["at"] == door)) kept.Add(new JObject { ["kind"] = Door, ["at"] = door });
            }
            if (kept.Count > 0) host["openings"] = kept;
            else host.Remove("openings");
        }

        // A closed box flush against the row's cells on that side, when no wall there can take the wing.
        static void AgainstCells(JObject row, Side side, int width, int depth, string name)
        {
            var (x0, y0, x1, y1) = Extent(row);
            int w = 2 * ((side == Side.North || side == Side.South) ? width : depth);
            int d = 2 * ((side == Side.North || side == Side.South) ? depth : width);
            int midX = ((x0 + x1) / 4) * 2, midY = ((y0 + y1) / 4) * 2;
            int left = midX - (w / 4 * 2), top = midY - (d / 4 * 2);
            if (side == Side.West) left = x0 - w;
            if (side == Side.East) left = x1;
            if (side == Side.North) top = y0 - d;
            if (side == Side.South) top = y1;
            var joins = (JObject)row["joins"];
            var walls = (JObject)row["walls"];
            joins[name + "-a"] = new JArray(left, top);
            joins[name + "-b"] = new JArray(left + w, top);
            joins[name + "-c"] = new JArray(left + w, top + d);
            joins[name + "-d"] = new JArray(left, top + d);
            walls[name + "-n"] = new JObject { ["from"] = name + "-a", ["to"] = name + "-b" };
            walls[name + "-e"] = new JObject { ["from"] = name + "-b", ["to"] = name + "-c" };
            walls[name + "-s"] = new JObject { ["from"] = name + "-c", ["to"] = name + "-d" };
            walls[name + "-w"] = new JObject { ["from"] = name + "-d", ["to"] = name + "-a" };
            RoofOver(row, name);
        }

        // The row's extent in half cells from its at: its joins and its built cells.
        static (int x0, int y0, int x1, int y1) Extent(JObject row)
        {
            var xs = new List<int>();
            var ys = new List<int>();
            foreach (var j in (row["joins"] as JObject)?.PropertyValues() ?? Enumerable.Empty<JToken>())
            {
                xs.Add((int)j[0]);
                ys.Add((int)j[1]);
            }
            foreach (var t in (row["built"] as JObject)?.PropertyValues() ?? Enumerable.Empty<JToken>())
                foreach (var r in t)
                {
                    ys.Add(2 * (int)r[0]);
                    ys.Add(2 * ((int)r[0] + 1));
                    for (int k = 1; k + 1 < r.Count(); k += 2)
                    {
                        xs.Add(2 * (int)r[k]);
                        xs.Add(2 * ((int)r[k + 1] + 1));
                    }
                }
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        static JObject WithMiddle(JObject wall, int cells, BuildingTables tables)
        {
            if (cells >= 3 && tables.Row("openings", Window) != null) wall["openings"] = new JArray(new JObject { ["kind"] = Window, ["at"] = 2 * ((cells - 1) / 2) });
            return wall;
        }

        // The outline goes out round the wing where the host's two joins are next to each other in it; else the wing has its own roof.
        static void GrowRoof(JObject row, string from, string to, string name)
        {
            foreach (var roof in (row["roofs"] as JObject)?.PropertyValues().OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                if (!(roof["over"] is JArray over)) continue;
                var list = over.Select(j => (string)j).ToList();
                int at = list.IndexOf(from);
                if (at < 0 || list[(at + 1) % list.Count] != to) continue;
                list.InsertRange(at + 1, new[] { name + "-a", name + "-b", name + "-c", name + "-d" });
                roof["over"] = new JArray(list);
                return;
            }
            RoofOver(row, name);
        }

        static void RoofOver(JObject row, string name)
        {
            if (!(row["roofs"] is JObject roofs)) row["roofs"] = roofs = new JObject();
            var like = roofs.PropertyValues().OfType<JObject>().FirstOrDefault();
            var roof = like == null ? new JObject() : (JObject)like.DeepClone();
            roof["over"] = new JArray(name + "-a", name + "-b", name + "-c", name + "-d");
            roofs[name] = roof;
        }
    }
}
