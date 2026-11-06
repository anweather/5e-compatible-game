// scatter (docs/design/events-and-placing.md 2b): picks cells over an area of the real world by
// Placement.Pick, once, now, and writes them into a draft: a terrain or a cover as a "lay" row of
// the mod's world section, a thing as "add" rows. So the mod is the cells, readable and editable
// ("move them back from the water" is another scatter with another seed), and a revert is exact.
// "50 trees near the lake" is this, not a building of tree.
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
        const string LayTable = "lay";

        /// <summary>scatter(): count of a terrain, cover or thing over open cells near something, written into a draft.</summary>
        public string Scatter(string draftId, string what, int count, string near, int? within, int? spacing, string side, string edge, int? seed, string[] onto)
        {
            what = Required(what, "what");
            var draft = DraftOf(draftId);
            string table = ScatterTable(draft, what);
            if (table == null) throw new RulesToolException($"There is no terrain, cover or thing called {what}; list terrain, list covers and list items show what there is.");
            double maxCount = table == "things" ? Budgets.Number("things.maxPerMod", 16) : Budgets.Number("lay.maxCells", 256);
            if (count < 1 || count > maxCount) throw new RulesToolException(FormattableString.Invariant($"count is 1 to {maxCount} for a {(table == "things" ? "thing" : "terrain or cover")}."));
            int reach = within ?? 12, apart = spacing ?? 0, salt = seed ?? 1;
            var picked = PickNear(count, near, reach, apart, side, edge, salt, onto, out string name);
            if (picked.Count == 0)
                return FormattableString.Invariant($"No open cell within {reach} cells of {name}{(onto?.Length > 0 ? " with " + string.Join(" or ", onto) : "")}; try further off, or other ground.") + StandInLine;
            if (table == "things")
                foreach (var c in picked) AddThing(draft.Id, what, FormattableString.Invariant($"[{c.x}, {c.y}]"), null, null);
            else LayRowInto(draft, table == "terrain" ? "terrain" : "cover", what, salt, picked.Select(c => (c.x, c.y)));
            string ground = picked.GroupBy(c => c.ground).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
            string line = FormattableString.Invariant($"{picked.Count} {what} on {ground.ToLowerInvariant()} near {name}, {apart} cells apart at least, the nearest {picked.Min(c => c.d)} cells from it and the furthest {picked.Max(c => c.d)} (seed {salt})");
            if (picked.Count < count) line += FormattableString.Invariant($"; only {picked.Count} of {count} had room");
            return line + ".\n" + Sited(draft) + StandInLine;
        }

        // Open cells near something, by the world's seed, off villages and courses: what scatter and act both place on.
        List<(int x, int y, int d, string ground)> PickNear(int count, string near, int reach, int apart, string side, string edge, int salt, string[] onto, out string name)
        {
            double maxWithin = Budgets.Number("lay.maxWithin", 32);
            if (reach < 0 || reach > maxWithin) throw new RulesToolException(FormattableString.Invariant($"within is 0 to {maxWithin} cells."));
            if (apart < 0) throw new RulesToolException("spacing is the fewest cells between two, from 0.");
            var from = Targets(near, side, edge, out name);
            return OnWorld(w =>
            {
                var distance = DistanceMap(w, from, reach);
                var cells = OpenNear(w, distance, count, apart, salt, onto);
                return cells.Select(c => (c.x, c.y, d: distance[w.Idx(c.x, c.y)], ground: w.TerrainOf(w.At(c.x, c.y)).Name)).ToList();
            });
        }

        // The cells a distance map reaches, picked by the world's seed, off villages and courses.
        List<(int x, int y)> OpenNear(World w, int[] distance, int count, int apart, int salt, string[] onto)
        {
            var area = Enumerable.Range(0, w.W * w.H).Where(i => distance[i] >= 0).Select(i => (x: i % w.W, y: i / w.W));
            var open = w.PlaceOpen(onto, false);
            return Placement.Pick(area, count, apart, (x, y) => open(x, y) && !Settled(x, y), unchecked((int)w.Seed), salt);
        }

        // Whether a scatter's what is a terrain, a cover or a thing, with the draft on; null for none.
        string ScatterTable(Draft draft, string what)
        {
            if (Current(draft, "terrain", what) != null) return "terrain";
            if (Current(draft, "covers", what) != null) return "covers";
            return Current(draft, "items", what) != null || Current(draft, "entities", what) != null ? "things" : null;
        }

        // One lay row per what and seed: a second scatter with the same replaces it, another seed adds one.
        static void LayRowInto(Draft draft, string key, string what, int seed, IEnumerable<(int x, int y)> cells)
        {
            var world = Section(draft, "world");
            if (!(world["lay"] is JArray rows)) world["lay"] = rows = new JArray();
            var row = new JObject { [key] = what, ["cells"] = new JArray(cells.Select(c => new JArray(c.x, c.y))), ["seed"] = seed };
            string id = LayId(row);
            var same = rows.FirstOrDefault(r => LayId(r) == id);
            if (same != null) same.Replace(row);
            else rows.Add(row);
            draft.Changed.Add($"{LayTable} {id}");
        }

        static string LayId(JToken row) => string.Format(CultureInfo.InvariantCulture, "{0}#{1}", Str(row["terrain"]) ?? Str(row["cover"]), (int?)row["seed"] ?? 0);

        // apply's words for one lay row of the draft.
        static string LayChange(Draft draft, string id)
        {
            var row = (draft.Patch["world"]?["lay"] as JArray)?.FirstOrDefault(r => LayId(r) == id);
            return row == null ? $"cells laid as {id}" : $"{(row["cells"] as JArray)?.Count ?? 0} cells of {Str(row["terrain"]) ?? Str(row["cover"])} laid (seed {(int?)row["seed"] ?? 0})";
        }

        static void RemoveLay(Draft draft, string id) =>
            ((JArray)draft.Patch["world"]["lay"]).Where(r => LayId(r) == id).ToList().ForEach(r => r.Remove());
    }
}
