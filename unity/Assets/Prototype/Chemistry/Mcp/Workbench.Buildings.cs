// The world tools for buildings (docs/design/builders.md section 4; world-tools.md G1,
// G3, G5): find and find_site read the world, and the drafts write a mod's "world"
// section, a whole row per building, as a mod replaces every row. No building or style
// is named here: everything comes from the rows, the places and what the caller asks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    /// <summary>The running game's world, for tools that read it. Called off the game's main thread: Read crosses to it and waits.</summary>
    public interface IWorldDesk
    {
        T Read<T>(Func<World, T> read);
    }

    public sealed partial class Workbench
    {
        const string BuildingsTable = "buildings";
        const string DefaultStyle = "plain", DoorKind = "door", WindowKind = "window";
        const int MinSide = 3, FindLines = 24, SiteLines = 5;

        /// <summary>The running game's world; null reads the stand-in in memory (StandInValley).</summary>
        public IWorldDesk WorldDesk { get; set; }

        T OnWorld<T>(Func<World, T> read) => WorldDesk != null ? WorldDesk.Read(read) : read(Live);

        // A game is attached but can't show its world yet: a site can only be judged there, at the tick.
        bool WorldBlind => Desk != null && WorldDesk == null;

        string StandInLine => WorldBlind || WorldDesk == null ? "\n(A stand-in of the valley: no game's world is attached, so there are no trees or yard here.)" : "";

        // ---------- find ----------

        /// <summary>find(): buildings and things, with their ids, nearest first.</summary>
        public string Find(string kind, string near, int? within, string side, string edge)
        {
            int reach = within ?? 64;
            var from = Targets(near, side, edge, out string name);
            var lines = OnWorld(w =>
            {
                var found = new List<(double d, string id, string line)>();
                if (Wanted(kind, "building"))
                    foreach (var row in w.BuildingsNow.Where(r => WantedRow(kind, r)))
                    {
                        var cells = w.BuildingCellsOf(row);
                        if (cells.Count > 0) found.Add((Nearest(cells.Select(c => (c.X, c.Y)), from), row.Id, BuildingLine(row, cells)));
                    }
                foreach (var t in Budgets.Things.All.Where(t => Wanted(kind, "thing") || kind == t.Kind))
                    found.Add((Nearest(new[] { (t.CellX, t.CellY) }, from), t.Id.ToString(), FormattableString.Invariant($"a {t.Kind}, a thing at [{t.CellX}, {t.CellY}]")));
                return found.Where(f => f.d <= reach).OrderBy(f => f.d).ThenBy(f => f.id, StringComparer.Ordinal).ToList();
            });
            if (lines.Count == 0) return FormattableString.Invariant($"Nothing of that kind within {reach} cells of {name}.") + StandInLine;
            var sb = new StringBuilder(FormattableString.Invariant($"{lines.Count} within {reach} cells of {name}, nearest first:"));
            foreach (var (d, id, line) in lines.Take(FindLines))
                sb.Append(FormattableString.Invariant($"\n- {id}: {line}, {Math.Round(d)} cells away"));
            if (lines.Count > FindLines) sb.Append(FormattableString.Invariant($"\n(and {lines.Count - FindLines} more)"));
            return sb.Append(StandInLine).ToString();
        }

        static bool Wanted(string kind, string what) => string.IsNullOrWhiteSpace(kind) || string.Equals(kind.Trim(), what, StringComparison.OrdinalIgnoreCase);

        static bool WantedRow(string kind, BuildingRow row)
        {
            if (Wanted(kind, "building")) return true;
            return Str(row.Json["style"]) == kind || Str(row.Json["kind"]) == kind;
        }

        static string BuildingLine(BuildingRow row, List<BuildingCell> cells)
        {
            int x0 = cells.Min(c => c.X), x1 = cells.Max(c => c.X), y0 = cells.Min(c => c.Y), y1 = cells.Max(c => c.Y);
            string what = Str(row.Json["kind"]) ?? Str(row.Json["style"]) ?? "building";
            return FormattableString.Invariant($"{Article(what)} {what}, {x1 - x0 + 1} by {y1 - y0 + 1} cells from [{x0}, {y0}], {row.Fill.ToString().ToLowerInvariant()}, walls of {row.WallMaterial}");
        }

        static double Nearest(IEnumerable<(int x, int y)> cells, List<(int x, int y)> from)
        {
            long best = long.MaxValue;
            foreach (var (x, y) in cells)
                foreach (var (fx, fy) in from) best = Math.Min(best, (long)(x - fx) * (x - fx) + (long)(y - fy) * (y - fy));
            return Math.Sqrt(best);
        }

        // What "near" names, as cells to measure from, and its name for a sentence.
        List<(int x, int y)> Targets(string near, string side, string edge, out string name)
        {
            string key = (near ?? "").Trim();
            if (key.Length == 0 || key == "me")
            {
                var me = Here?.Asking() ?? throw new RulesToolException("No player is in the game, so there's no \"near me\"; give a place, a building or a cell.");
                name = "you";
                return new List<(int, int)> { (me.X, me.Y) };
            }
            if (key.StartsWith('['))
            {
                var at = Cell(key, "near");
                name = FormattableString.Invariant($"cell [{at.x}, {at.y}]");
                return new List<(int, int)> { at };
            }
            var place = Places.Find(key);
            if (place != null)
            {
                name = place.Name;
                return PlaceCells(place, side, edge, ref name);
            }
            var cells = OnWorld(w => w.BuildingRowOf(key) is BuildingRow row ? w.BuildingCellsOf(row).Select(c => (c.X, c.Y)).ToList() : null);
            if (cells == null || cells.Count == 0)
                throw new RulesToolException($"{key} is neither a place nor a building; places lists the places ({Places.Ids}), and find lists buildings.");
            name = key;
            return cells;
        }

        // A place, or just its edge (the world's own edge isn't a shore), or just the side of it facing one way.
        List<(int x, int y)> PlaceCells(Place place, string side, string edge, ref string name)
        {
            bool shore = !string.IsNullOrWhiteSpace(edge);
            if (shore && !string.Equals(edge.Trim(), "shore", StringComparison.OrdinalIgnoreCase)) throw new RulesToolException($"{edge.Trim()} isn't an edge; say shore, or leave it out.");
            Side? facing = string.IsNullOrWhiteSpace(side) ? (Side?)null : SideOf(side);
            if (!shore && facing == null) return place.Cells().ToList();
            int w = OnWorld(x => x.W), h = OnWorld(x => x.H);
            bool InWorld(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
            var cells = place.EdgeCells().Where(c =>
            {
                if (facing != null)
                {
                    var (dx, dy) = Sides.Step(facing.Value);
                    return InWorld(c.x + dx, c.y + dy) && !place.Contains(c.x + dx, c.y + dy);
                }
                return new[] { Side.North, Side.East, Side.South, Side.West }.Any(s =>
                {
                    var (dx, dy) = Sides.Step(s);
                    return InWorld(c.x + dx, c.y + dy) && !place.Contains(c.x + dx, c.y + dy);
                });
            }).ToList();
            if (cells.Count == 0) throw new RulesToolException($"{place.Name} has no such edge in the world.");
            name = (facing != null ? $"{place.Name}'s {facing.Value.ToString().ToLowerInvariant()} shore" : $"{place.Name}'s shore");
            return cells;
        }

        static Side SideOf(string side) =>
            Enum.TryParse(side.Trim(), true, out Side s) && Enum.IsDefined(typeof(Side), s) ? s : throw new RulesToolException($"{side.Trim()} isn't a side; say north, east, south or west.");

        static (int x, int y) Cell(string json, string what)
        {
            if (Value(json) is JArray a && a.Count == 2 && a.All(v => v.Type == JTokenType.Integer)) return ((int)a[0], (int)a[1]);
            throw new RulesToolException($"{what} is a cell as JSON, [x, y].");
        }

        // ---------- find_site ----------

        /// <summary>find_site(): where a building of this size can stand, nearest first.</summary>
        public string FindSite(int width, int depth, string near, string side, string edge, int? within)
        {
            if (width < 1 || depth < 1) throw new RulesToolException("A site is at least a cell each way.");
            double max = Budgets.Number("buildings.maxCells", 24);
            if (width > max || depth > max) throw new RulesToolException(FormattableString.Invariant($"A building is at most {max} by {max} cells."));
            int reach = within ?? 32;
            var from = Targets(near, side, edge, out string name);
            var sites = OnWorld(w => Sites(w, width, depth, from, reach));
            if (sites.Count == 0) return FormattableString.Invariant($"No site of {width} by {depth} cells within {reach} cells of {name}; try a smaller one, or further off.") + StandInLine;
            var sb = new StringBuilder(FormattableString.Invariant($"Sites of {width} by {depth} cells near {name}, nearest first; each is its north-west cell, for add_building:"));
            foreach (var s in sites)
                sb.Append(FormattableString.Invariant($"\n- [{s.x}, {s.y}], on {s.ground}, {Math.Round(s.d)} cells away"));
            return sb.Append(StandInLine).ToString();
        }

        List<(int x, int y, double d, string ground)> Sites(World w, int width, int depth, List<(int x, int y)> from, int reach)
        {
            var distance = DistanceMap(w, from, reach);
            var taken = new Dictionary<(int, int), string>();
            foreach (var row in w.BuildingsNow)
                foreach (var c in w.BuildingCellsOf(row)) taken[(c.X, c.Y)] = row.Id;
            var free = new bool?[w.W * w.H];
            bool Free(int x, int y)
            {
                if (!w.InBounds(x, y)) return false;
                int i = w.Idx(x, y);
                free[i] ??= w.CellProblem(x, y, taken) == null && !Settled(x, y);
                return free[i].Value;
            }
            var candidates = new List<(int x, int y, int d)>();
            for (int y = 0; y + depth <= w.H; y++)
                for (int x = 0; x + width <= w.W; x++)
                {
                    int best = int.MaxValue;
                    for (int dy = 0; dy < depth; dy++)
                        for (int dx = 0; dx < width; dx++)
                        {
                            int d = distance[w.Idx(x + dx, y + dy)];
                            if (d >= 0 && d < best) best = d;
                        }
                    if (best == int.MaxValue) continue;
                    if (Clear(x, y, width, depth, Free)) candidates.Add((x, y, best));
                }
            var picked = new List<(int x, int y, double d, string ground)>();
            foreach (var c in candidates.OrderBy(c => c.d).ThenBy(c => c.y).ThenBy(c => c.x))
            {
                if (picked.Any(p => Math.Abs(p.x - c.x) < width && Math.Abs(p.y - c.y) < depth)) continue;
                picked.Add((c.x, c.y, c.d, MostOf(w, c.x, c.y, width, depth)));
                if (picked.Count == SiteLines) break;
            }
            return picked;
        }

        // What most of a site stands on, by name.
        static string MostOf(World w, int x, int y, int width, int depth)
        {
            var cells = Enumerable.Range(0, depth).SelectMany(dy => Enumerable.Range(0, width).Select(dx => w.TerrainOf(w.At(x + dx, y + dy)).Name));
            return cells.GroupBy(n => n).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        }

        // Every cell of the site is free.
        static bool Clear(int x, int y, int width, int depth, Func<int, int, bool> free)
        {
            for (int dy = 0; dy < depth; dy++)
                for (int dx = 0; dx < width; dx++)
                    if (!free(x + dx, y + dy)) return false;
            return true;
        }

        // The world's streets, courses and puzzles stay as they are; only open country is a site.
        bool Settled(int x, int y) => Places.All.Any(p => (p.Kind == "village" || p.Kind == "course" || p.Kind == "puzzle") && p.Contains(x, y));

        // Cells from the targets (-1 past reach), by steps across the grid.
        static int[] DistanceMap(World w, List<(int x, int y)> from, int reach)
        {
            var map = new int[w.W * w.H];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var queue = new Queue<int>();
            foreach (var (x, y) in from.Where(c => w.InBounds(c.x, c.y)))
                if (map[w.Idx(x, y)] < 0)
                {
                    map[w.Idx(x, y)] = 0;
                    queue.Enqueue(w.Idx(x, y));
                }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % w.W, y = i / w.W, d = map[i];
                if (d >= reach) continue;
                foreach (var (nx, ny, _) in w.Neighbours(x, y))
                {
                    int n = w.Idx(nx, ny);
                    if (map[n] >= 0) continue;
                    map[n] = d + 1;
                    queue.Enqueue(n);
                }
            }
            return map;
        }

        // ---------- the drafts ----------

        /// <summary>Why a patch's buildings couldn't stand on the world now; null if they can, or no world can say.</summary>
        string SiteNote(string json)
        {
            if (WorldBlind) return null;
            if (!(ModPatch.Parse(json).Json["world"] is JObject)) return null;
            try
            {
                return OnWorld(w => w.SiteProblemOf(json, "The draft"));
            }
            catch (ModException e)
            {
                return e.Errors.FirstOrDefault();
            }
        }

        /// <summary>A draft with its buildings as a mod, and a sentence if they wouldn't stand.</summary>
        public string Sited(Draft draft)
        {
            string note = SiteNote(draft.Patch.ToString(Formatting.None));
            return (note == null ? "" : $"This won't stand yet: {note}.\n") + draft.Show();
        }

        static JObject DraftRows(Draft draft)
        {
            var world = Section(draft, "world");
            if (!(world["buildings"] is JObject rows)) world["buildings"] = rows = new JObject();
            return rows;
        }

        /// <summary>A new building: a box from its size and style, or a whole row.</summary>
        public Draft AddBuilding(string draftId, string at, int? width, int? depth, string style, string front, string id, string fill, string row)
        {
            JObject made;
            if (!string.IsNullOrWhiteSpace(row))
            {
                made = Value(row) as JObject ?? throw new RulesToolException("row is a whole building as a JSON object.");
                id = string.IsNullOrWhiteSpace(id) ? Str(made["id"]) : id.Trim();
                if (string.IsNullOrEmpty(id)) throw new RulesToolException("A row needs an id.");
            }
            else made = NewBox(draftId, at, width, depth, style, front, fill, ref id);
            if (OnWorld(w => w.BuildingRowOf(id)) != null) throw new RulesToolException($"There is already a building {id}; use set_building or add_wing to change it, or give another id.");
            made["id"] = id;
            var draft = DraftOf(draftId);
            DraftRows(draft)[id] = made;
            draft.Changed.Add($"{BuildingsTable} {id}");
            return draft;
        }

        JObject NewBox(string draftId, string at, int? width, int? depth, string style, string front, string fill, ref string id)
        {
            var (x, y) = Cell(Required(at, "cell (at)"), "at");
            int w = width ?? 0, d = depth ?? 0;
            if (w < MinSide || d < MinSide) throw new RulesToolException($"A box needs a width and a depth of at least {MinSide} cells, so its door stands clear of its corners; find_site takes the same size.");
            string styleId = string.IsNullOrWhiteSpace(style) ? DefaultStyle : style.Trim();
            var row = new JObject
            {
                ["at"] = new JArray(x, y),
                ["style"] = styleId,
                ["seed"] = (int)WorldHash.Hash4(x, y, w, d),
            };
            if (!string.IsNullOrWhiteSpace(fill)) row["fill"] = fill.Trim();
            var given = string.IsNullOrWhiteSpace(draftId) ? null : DraftOf(draftId).Patch[BuildingTables.Key]?["styles"]?[styleId] as JObject;
            var known = given ?? Raw[BuildingTables.Key]["styles"]?[styleId] as JObject
                ?? throw new RulesToolException($"There's no style {styleId}; list styles shows them, and add_building_part adds one to the draft.");
            foreach (var p in BuildingRow.Box(w, d, string.IsNullOrWhiteSpace(front) ? Side.South : SideOf(front), DoorKind, WindowKind, known["roof"] as JObject).Properties()) row[p.Name] = p.Value;
            if (string.IsNullOrWhiteSpace(id)) id = string.Format(CultureInfo.InvariantCulture, "{0}#{1}-{2}", styleId, x, y);
            return row;
        }

        /// <summary>One field of a building by dotted path; null removes it. The whole row goes in the draft.</summary>
        public Draft SetBuilding(string draftId, string id, string field, string value)
        {
            id = Required(id, "building");
            var draft = DraftOf(draftId);
            var row = RowInDraft(draft, id);
            SetPath(row, Required(field, "field"), value);
            draft.Changed.Add($"{BuildingsTable} {id}");
            return draft;
        }

        // The draft's row, or the world's copied in: every edit is of the whole row.
        JObject RowInDraft(Draft draft, string id)
        {
            var rows = DraftRows(draft);
            if (rows[id] is JObject mine) return mine;
            var row = OnWorld(w => w.BuildingRowOf(id)?.Json) ?? throw new RulesToolException($"There's no building {id}; find lists them.");
            var copy = (JObject)row.DeepClone();
            copy["id"] = id;
            rows[id] = copy;
            return copy;
        }

        /// <summary>A row of a building table (a style, an opening, a floor, a roof shape, a detail).</summary>
        public Draft AddBuildingPart(string draftId, string table, string id, string row)
        {
            table = Required(table, "table").ToLowerInvariant();
            if (!BuildingTables.Tables.Contains(table)) throw new RulesToolException($"{table} isn't a building table; they are {string.Join(", ", BuildingTables.Tables)}.");
            id = Required(id, "id");
            var entry = Value(Required(row, "row")) as JObject ?? throw new RulesToolException("row is a whole row as a JSON object.");
            var draft = DraftOf(draftId);
            var tables = Section(draft, BuildingTables.Key);
            if (!(tables[table] is JObject rows)) tables[table] = rows = new JObject();
            rows[id] = entry;
            draft.Changed.Add($"{table} {id}");
            return draft;
        }

        string BuildingChange(string id) =>
            OnWorld(w => w.BuildingRowOf(id)) != null ? $"the building {id}" : $"a new building, {id}";

        IEnumerable<string> BuildingLines() =>
            OnWorld(w => w.BuildingsNow.Select(r => $"{r.Id}: {BuildingLine(r, w.BuildingCellsOf(r))}").ToList());

        // ---------- add_wing ----------

        /// <summary>A wing on one side of a building by id, or on the first side whose site stands.</summary>
        public Draft AddWing(string draftId, string building, int width, int depth, string side)
        {
            building = Required(building, "building");
            if (width < MinSide || depth < MinSide) throw new RulesToolException($"A wing is at least {MinSide} cells wide and {MinSide} deep, or it has no inside to speak of.");
            var draft = DraftOf(draftId);
            var row = RowInDraft(draft, building);
            var sides = string.IsNullOrWhiteSpace(side) ? new[] { Side.North, Side.East, Side.South, Side.West } : new[] { SideOf(side) };
            var refusals = new List<string>();
            foreach (var s in sides.OrderByDescending(s => HostLength(row, s)))
            {
                var trial = (JObject)row.DeepClone();
                Wing.Add(trial, s, width, depth, BuildingTablesIn(draft));
                var patch = (JObject)draft.Patch.DeepClone();
                patch["world"]["buildings"][building] = trial;
                string why = SiteNote(patch.ToString(Formatting.None));
                if (why == null)
                {
                    DraftRows(draft)[building] = trial;
                    draft.Changed.Add($"{BuildingsTable} {building}");
                    return draft;
                }
                refusals.Add($"{s.ToString().ToLowerInvariant()}: {why}");
            }
            draft.Changed.Add($"{BuildingsTable} {building}"); // the row was copied in to try it, so remove can take it back
            throw new RulesToolException($"No wing of {width} by {depth} cells stands on {building}. {string.Join("; ", refusals)}.");
        }

        static int HostLength(JObject row, Side side) => Wing.HostOf(row, side)?.length ?? 0;

        // The tables as the draft has them.
        BuildingTables BuildingTablesIn(Draft draft)
        {
            var rules = InForce.WithMods(new[] { ModPatch.Parse(new JObject { [BuildingTables.Key] = draft.Patch[BuildingTables.Key]?.DeepClone() ?? new JObject() }.ToString()) });
            return rules.Buildings;
        }
    }
}
