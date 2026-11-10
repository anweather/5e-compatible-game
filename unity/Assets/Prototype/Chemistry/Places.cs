// world/places.json: the world's named landmarks and the cells each covers
// (docs/design/world-tools.md, G1), for the rules MCP's places() and here() and
// for a look's "where" (LookScopes). Read-only: mods can't change them.
//
// Most places are generated from what the world data already says
// (WorldPlaces.Generate): the villages (villages.json), the courses
// (gauntlet.json, golf.json), the puzzles (puzzles.json) and the water bodies
// (land.json's cells below the water level its "generate" section gives). The
// rest are written by hand, each saying where it came from. A place's cells are
// a shape: "area" [x0, y0, x1, y1] (corners inclusive), "line" (points and a
// width), "circle" (at and radius) or "rows" ([y, x0, x1, ...] spans per row).
//
// Not a port: the sandbox has no places.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One named landmark: its id, name, kind and cells (grid cells, y growing south).</summary>
    public sealed class Place
    {
        // Row y = Bounds.Y + i holds spans[i] as pairs x0, x1 (inclusive), west to east.
        readonly int[][] spans;

        Place(JObject json, CellRect bounds, int[][] spans)
        {
            Id = Str(json["id"]);
            Name = Str(json["name"]) ?? Id;
            Kind = Str(json["kind"]) ?? "place";
            From = Str(json["from"]);
            Generated = json["generated"]?.Type == JTokenType.Boolean && (bool)json["generated"];
            Bounds = bounds;
            this.spans = spans;
            Json = json;
            foreach (var row in spans)
                for (int k = 0; k < row.Length; k += 2) Count += row[k + 1] - row[k] + 1;
            Middle = FindMiddle();
        }

        public string Id { get; }
        public string Name { get; }
        /// <summary>village, lake, sea, river, course, puzzle... (a word, not a type: anything the file says).</summary>
        public string Kind { get; }
        /// <summary>Where it came from: the world file it was generated from, or what a hand-written one was copied from.</summary>
        public string From { get; }
        /// <summary>Made by WorldPlaces.Generate, so regenerating rewrites it.</summary>
        public bool Generated { get; }
        /// <summary>The smallest block holding every cell.</summary>
        public CellRect Bounds { get; }
        public int Count { get; }
        /// <summary>The place's own cell nearest the middle of its cells.</summary>
        public (int x, int y) Middle { get; }
        /// <summary>The entry as the file has it.</summary>
        internal JObject Json { get; }

        public bool Contains(int x, int y)
        {
            int i = y - Bounds.Y;
            if (i < 0 || i >= spans.Length || x < Bounds.X || x >= Bounds.Right) return false;
            var row = spans[i];
            for (int k = 0; k < row.Length; k += 2)
                if (x >= row[k] && x <= row[k + 1]) return true;
            return false;
        }

        /// <summary>Every cell, row by row, west to east.</summary>
        public IEnumerable<(int x, int y)> Cells()
        {
            for (int i = 0; i < spans.Length; i++)
                for (int k = 0; k < spans[i].Length; k += 2)
                    for (int x = spans[i][k]; x <= spans[i][k + 1]; x++) yield return (x, Bounds.Y + i);
        }

        /// <summary>The cells at the place's edge: those with a side on a cell outside it.</summary>
        public IEnumerable<(int x, int y)> EdgeCells() =>
            Cells().Where(c => !Contains(c.x + 1, c.y) || !Contains(c.x - 1, c.y) || !Contains(c.x, c.y + 1) || !Contains(c.x, c.y - 1));

        /// <summary>How far a cell is from the place, in cells between centres: 0 inside it.</summary>
        public double DistanceTo(int x, int y)
        {
            if (Contains(x, y)) return 0;
            long best = long.MaxValue;
            for (int i = 0; i < spans.Length; i++)
            {
                long dy = Bounds.Y + i - y;
                for (int k = 0; k < spans[i].Length; k += 2)
                {
                    long dx = Math.Max(0, Math.Max(spans[i][k] - x, x - spans[i][k + 1]));
                    best = Math.Min(best, dx * dx + dy * dy);
                }
            }
            return Math.Sqrt(best);
        }

        (int x, int y) FindMiddle()
        {
            if (Count == 0) return (Bounds.X, Bounds.Y);
            long sx = 0, sy = 0;
            foreach (var (x, y) in Cells())
            {
                sx += x;
                sy += y;
            }
            int mx = (int)Math.Round((double)sx / Count), my = (int)Math.Round((double)sy / Count);
            if (Contains(mx, my)) return (mx, my);
            return Cells().OrderBy(c => (long)(c.x - mx) * (c.x - mx) + (long)(c.y - my) * (c.y - my)).ThenBy(c => c.y).ThenBy(c => c.x).First();
        }

        /// <summary>A place from its entry in places.json. Throws <see cref="FormatException"/> if it can't be read.</summary>
        internal static Place Parse(JObject o)
        {
            string id = Str(o["id"]) ?? throw Bad("a place needs an id");
            var cells = new SortedSet<(int y, int x)>();
            if (o["area"] != null) AddArea(cells, o["area"], id);
            if (o["line"] != null) AddLine(cells, o["line"], Num(o["width"]) ?? 1, id);
            if (o["circle"] is JObject c) AddCircle(cells, c, id);
            if (o["rows"] != null) AddRows(cells, o["rows"], id);
            if (cells.Count == 0) throw Bad($"place {id} has no cells (give area, line, circle or rows)");
            return FromCells(cells, o);
        }

        /// <summary>
        /// A place of rows of spans (pairs x0, x1, west to east) from row <paramref name="top"/>, none empty at either end,
        /// its id, name, kind and source from <paramref name="json"/>.
        /// </summary>
        internal static Place FromSpans(int top, int[][] spans, JObject json)
        {
            int left = int.MaxValue, right = int.MinValue;
            foreach (var row in spans)
                if (row.Length > 0)
                {
                    left = Math.Min(left, row[0]);
                    right = Math.Max(right, row[row.Length - 1]);
                }
            return new Place(json, new CellRect(left, top, right - left + 1, spans.Length), spans);
        }

        /// <summary>A place of these cells, its id, name, kind and source from <paramref name="json"/>.</summary>
        internal static Place FromCells(SortedSet<(int y, int x)> cells, JObject json)
        {
            int top = cells.Min.y, bottom = cells.Max.y, left = cells.Min(c => c.x), right = cells.Max(c => c.x);
            var spans = new int[bottom - top + 1][];
            var row = new List<int>();
            int at = top;
            foreach (var group in cells.GroupBy(c => c.y))
            {
                for (; at < group.Key; at++) spans[at - top] = Array.Empty<int>();
                row.Clear();
                foreach (var (_, x) in group)
                {
                    if (row.Count > 0 && row[row.Count - 1] == x - 1) row[row.Count - 1] = x;
                    else
                    {
                        row.Add(x);
                        row.Add(x);
                    }
                }
                spans[at - top] = row.ToArray();
                at++;
            }
            return new Place(json, new CellRect(left, top, right - left + 1, bottom - top + 1), spans);
        }

        /// <summary>The cells as places.json's rows: [y, x0, x1, ...] per row that has any.</summary>
        internal JArray Rows()
        {
            var rows = new JArray();
            for (int i = 0; i < spans.Length; i++)
                if (spans[i].Length > 0) rows.Add(new JArray(new[] { Bounds.Y + i }.Concat(spans[i]).Select(v => (object)v).ToArray()));
            return rows;
        }

        static void AddArea(SortedSet<(int y, int x)> cells, JToken t, string id)
        {
            if (!(t is JArray a) || a.Count != 4) throw Bad($"place {id}: area needs [x0, y0, x1, y1]");
            int x0 = (int)a[0], y0 = (int)a[1], x1 = (int)a[2], y1 = (int)a[3];
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) cells.Add((y, x));
        }

        // Every cell whose centre is within half the width of the line through the points.
        static void AddLine(SortedSet<(int y, int x)> cells, JToken t, double width, string id)
        {
            if (!(t is JArray points) || points.Count < 2 || points.Any(p => !(p is JArray xy) || xy.Count != 2))
                throw Bad($"place {id}: line needs two or more points [x, y]");
            double half = Math.Max(width, 1) / 2;
            for (int i = 1; i < points.Count; i++)
            {
                double ax = (double)points[i - 1][0], ay = (double)points[i - 1][1], bx = (double)points[i][0], by = (double)points[i][1];
                int x0 = (int)Math.Floor(Math.Min(ax, bx) - half), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + half);
                int y0 = (int)Math.Floor(Math.Min(ay, by) - half), y1 = (int)Math.Ceiling(Math.Max(ay, by) + half);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (SegmentDistance(x, y, ax, ay, bx, by) <= half) cells.Add((y, x));
            }
        }

        static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
            double s = len == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len));
            double ex = ax + s * dx - px, ey = ay + s * dy - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        static void AddCircle(SortedSet<(int y, int x)> cells, JObject c, string id)
        {
            if (!(c["at"] is JArray at) || at.Count != 2 || Num(c["radius"]) is not double r || r < 0)
                throw Bad($"place {id}: circle needs at [x, y] and a radius");
            int cx = (int)at[0], cy = (int)at[1], reach = (int)Math.Ceiling(r);
            for (int y = cy - reach; y <= cy + reach; y++)
                for (int x = cx - reach; x <= cx + reach; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) cells.Add((y, x));
        }

        static void AddRows(SortedSet<(int y, int x)> cells, JToken t, string id)
        {
            if (!(t is JArray rows)) throw Bad($"place {id}: rows needs a list of [y, x0, x1, ...]");
            foreach (var r in rows)
            {
                if (!(r is JArray row) || row.Count < 3 || row.Count % 2 == 0) throw Bad($"place {id}: each row is [y, x0, x1, ...]");
                int y = (int)row[0];
                for (int k = 1; k < row.Count; k += 2)
                    for (int x = (int)row[k]; x <= (int)row[k + 1]; x++) cells.Add((y, x));
            }
        }

        internal static FormatException Bad(string message) => new FormatException($"{WorldPlaces.FileName}: {message}");
        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static double? Num(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : (double?)null;
    }

    /// <summary>The world's places (world/places.json), in file order.</summary>
    public sealed class WorldPlaces
    {
        public const string FileName = "places.json";

        public static readonly WorldPlaces Empty = new WorldPlaces(new List<Place>(), new JObject());

        readonly List<Place> places;

        WorldPlaces(List<Place> places, JObject doc)
        {
            this.places = places;
            Doc = doc;
        }

        public IReadOnlyList<Place> All => places;
        public int Count => places.Count;
        internal JObject Doc { get; }

        /// <summary>A place by its id, or by its name in any case (with or without a leading "the"); null if none.</summary>
        public Place Find(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            var key = idOrName.Trim();
            return places.FirstOrDefault(p => p.Id == key)
                ?? places.FirstOrDefault(p => Same(p.Name, key) || Same(p.Id, key));
        }

        static bool Same(string a, string b) => string.Equals(Bare(a), Bare(b), StringComparison.OrdinalIgnoreCase);

        static string Bare(string s)
        {
            s = s.Trim().Replace('-', ' ');
            return s.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? s.Substring(4) : s;
        }

        /// <summary>The ids, for a refusal's list.</summary>
        public string Ids => places.Count == 0 ? "none (world/places.json has none)" : string.Join(", ", places.Select(p => p.Id));

        /// <summary>Reads world/places.json from <paramref name="worldDir"/>; no file means no places.</summary>
        public static WorldPlaces Load(string worldDir)
        {
            var path = worldDir == null ? null : Path.Combine(worldDir, FileName);
            return path != null && File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

        /// <summary>The places in a places.json text. Throws <see cref="FormatException"/> for one that can't be read.</summary>
        public static WorldPlaces FromJson(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"{FileName}: {e.Message}", e); }
            var list = new List<Place>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in (doc["places"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var place = Place.Parse(p);
                if (!ids.Add(place.Id)) throw Place.Bad($"place {place.Id} is listed twice");
                list.Add(place);
            }
            return new WorldPlaces(list, doc);
        }

        // ---------- generating them from the world data ----------

        /// <summary>
        /// places.json as it should be: its about and generate sections, the places generated from
        /// the world files in <paramref name="worldDir"/> (villages, courses, puzzles, water bodies),
        /// then the hand-written ones the file has now, in its order.
        /// </summary>
        public static string Generate(string worldDir) => Generate(worldDir, null);

        /// <summary>
        /// As <see cref="Generate(string)"/>, the water bodies those <paramref name="water"/> found as the land was
        /// written (a bake's, which never reads its land back whole), else land.json's.
        /// </summary>
        internal static string Generate(string worldDir, WaterRuns water)
        {
            var current = Load(worldDir);
            var made = new List<Place>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Add(Place p)
            {
                if (ids.Add(p.Id)) made.Add(p);
            }
            foreach (var spec in VillageLayout.Load(worldDir))
            {
                var plan = VillageLayout.Lay(spec, (x, y) => true);
                Add(FromRect(Slug(spec.Name), spec.Name, "village", VillageLayout.FileName, plan.Bounds));
            }
            if (CourseLayout.Load(worldDir) is CourseLayout course)
                Add(FromRect(Slug(course.Name), course.Name, "course", CourseLayout.FileName, course.Area));
            if (GolfCommand.Load(worldDir) is GolfCommand golf)
                Add(Shaped(Path.GetFileNameWithoutExtension(GolfCommand.FileName), "course", GolfCommand.FileName,
                    new JObject { ["line"] = new JArray(new JArray(golf.TeeX, golf.TeeY), new JArray(golf.HoleX, golf.HoleY)), ["width"] = 3 }));
            foreach (var puzzle in WorldPuzzles.Load(worldDir).All)
                Add(FromRect(Slug(puzzle.Name), puzzle.Name, "puzzle", WorldPuzzles.FileName, new CellRect(puzzle.X, puzzle.Y, puzzle.Width, puzzle.Height)));
            water ??= WaterRuns.Read(worldDir, current.Doc);
            if (water != null) foreach (var body in water.Bodies()) Add(body);
            foreach (var hand in current.All.Where(p => !p.Generated)) Add(hand);
            return Write(current.Doc, made);
        }

        static Place FromRect(string id, string name, string kind, string from, CellRect r) =>
            Shaped(id, kind, from, new JObject { ["area"] = new JArray(r.X, r.Y, r.Right - 1, r.Bottom - 1) }, name);

        static Place Shaped(string id, string kind, string from, JObject shape, string name = null)
        {
            var o = new JObject { ["id"] = id, ["name"] = name ?? id, ["kind"] = kind, ["from"] = from, ["generated"] = true };
            foreach (var p in shape.Properties()) o[p.Name] = p.Value;
            return Place.Parse(o);
        }

        internal static string Slug(string name) =>
            string.Join("-", new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // The file with one place a block and each row of cells on a line of its own, so a diff shows what moved.
        static string Write(JObject doc, List<Place> places)
        {
            var sb = new StringBuilder("{\n");
            foreach (var p in doc.Properties().Where(p => p.Name != "places"))
                sb.Append("  ").Append(JsonConvert.SerializeObject(p.Name)).Append(": ").Append(p.Value.ToString(Formatting.None)).Append(",\n");
            sb.Append("  \"places\": [\n");
            for (int i = 0; i < places.Count; i++)
            {
                var o = places[i].Json;
                var head = new JObject(o.Properties().Where(p => p.Name != "rows").Select(p => new JProperty(p.Name, p.Value)));
                if (o["rows"] is JArray rows)
                {
                    sb.Append("    ").Append(head.ToString(Formatting.None).TrimEnd('}')).Append(",\"rows\": [\n");
                    sb.Append(string.Join(",\n", rows.Select(r => "      " + r.ToString(Formatting.None))));
                    sb.Append("\n    ]}");
                }
                else sb.Append("    ").Append(head.ToString(Formatting.None));
                sb.Append(i < places.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        /// <summary>A cell in words for here(): "(x, y)".</summary>
        public static string CellWords(int x, int y) => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", x, y);
    }

    /// <summary>
    /// The water bodies of a world's land (places.json's "generate" "water"), read a row at a time, north to south: its
    /// connected cells below the water level, each body of at least "least" cells, one touching the world's edge a sea,
    /// one closed in a lake, ids by kind, largest first. A row's runs of water are joined to the runs of the row above
    /// that share a column with them, so it holds the runs, never the land or a cell at a time (a bake's, bake-lean): the
    /// sea of the scale island is millions of cells and a few thousand runs.
    /// </summary>
    internal sealed class WaterRuns
    {
        readonly int w, h, below, least;
        // Every run in the order the rows gave them: its row, its first and last x, and the run it's joined to.
        readonly List<int> runY = new List<int>(), runX0 = new List<int>(), runX1 = new List<int>(), parent = new List<int>();
        int above, aboveEnd, y;

        WaterRuns(int w, int h, JObject settings)
        {
            this.w = w;
            this.h = h;
            below = (int)settings["below"];
            least = (int?)settings["least"] ?? 1;
        }

        /// <summary>The finder for a w × h world whose places.json (in <paramref name="worldDir"/>) asks for water bodies; null if it asks none.</summary>
        internal static WaterRuns For(string worldDir, int w, int h)
        {
            // Its settings alone: the places themselves are read (and checked) when places.json is generated.
            var path = Path.Combine(worldDir, WorldPlaces.FileName);
            if (!File.Exists(path)) return null;
            return JObject.Parse(File.ReadAllText(path))["generate"]?["water"] is JObject settings ? new WaterRuns(w, h, settings) : null;
        }

        /// <summary>land.json's water bodies, as the places.json <paramref name="doc"/> asks for them; null if it asks none or there's no land.</summary>
        internal static WaterRuns Read(string worldDir, JObject doc)
        {
            var path = Path.Combine(worldDir, "land.json");
            if (!(doc["generate"]?["water"] is JObject settings) || !File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            var o = JObject.Parse(text);
            int w = (int)o["w"], h = (int)o["h"];
            var runs = new WaterRuns(w, h, settings);
            var land = LandFile.Read(text, w, h);
            for (int y = 0; y < h && land.Length > 0; y++) runs.Row(new ReadOnlySpan<int>(land, y * w, w));
            return runs;
        }

        /// <summary>The next row's <c>w</c> cells of land.</summary>
        internal void Row(ReadOnlySpan<int> land)
        {
            int start = runY.Count, p = above, x = 0;
            while (x < w)
            {
                if (land[x] >= below)
                {
                    x++;
                    continue;
                }
                int x0 = x;
                while (x < w && land[x] < below) x++;
                int k = runY.Count, x1 = x - 1;
                runY.Add(y);
                runX0.Add(x0);
                runX1.Add(x1);
                parent.Add(k);
                // The runs above, west to east: one ending west of this run ends west of every run after it in the row.
                while (p < aboveEnd && runX1[p] < x0) p++;
                for (int q = p; q < aboveEnd && runX0[q] <= x1; q++) Join(q, k);
            }
            above = start;
            aboveEnd = runY.Count;
            y++;
        }

        int Root(int k)
        {
            while (parent[k] != k)
            {
                parent[k] = parent[parent[k]];
                k = parent[k];
            }
            return k;
        }

        void Join(int a, int b)
        {
            int ra = Root(a), rb = Root(b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        sealed class Body
        {
            public readonly List<int> Runs = new List<int>();
            public long Cells;
            public bool Edge;
        }

        /// <summary>The bodies as places: the largest first, then by their north-west-most cell.</summary>
        internal IEnumerable<Place> Bodies()
        {
            var of = new Dictionary<int, Body>();
            var bodies = new List<Body>();
            for (int k = 0; k < runY.Count; k++)
            {
                int r = Root(k);
                if (!of.TryGetValue(r, out var b))
                {
                    of[r] = b = new Body();
                    bodies.Add(b);
                }
                b.Runs.Add(k);
                b.Cells += runX1[k] - runX0[k] + 1;
                b.Edge |= runY[k] == 0 || runY[k] == h - 1 || runX0[k] == 0 || runX1[k] == w - 1;
            }
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            // A body's first run is its north-west-most cell: the rows came north to south, each west to east.
            foreach (var b in bodies.Where(b => b.Cells >= least).OrderByDescending(b => b.Cells).ThenBy(b => (runY[b.Runs[0]], runX0[b.Runs[0]])))
            {
                string kind = b.Edge ? "sea" : "lake";
                counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
                string id = counts[kind] == 1 ? kind : $"{kind}-{counts[kind]}";
                var o = new JObject { ["id"] = id, ["name"] = counts[kind] == 1 ? "the " + kind : $"{kind} {counts[kind]}", ["kind"] = kind, ["from"] = "land.json", ["generated"] = true };
                var place = Place.FromSpans(runY[b.Runs[0]], Spans(b), o);
                o["rows"] = place.Rows();
                yield return place;
            }
        }

        // A body's runs as rows of pairs x0, x1 from its first row to its last, a row without any empty.
        int[][] Spans(Body b)
        {
            int top = runY[b.Runs[0]], bottom = runY[b.Runs[b.Runs.Count - 1]];
            var spans = new int[bottom - top + 1][];
            var row = new List<int>();
            int i = 0;
            for (int at = top; at <= bottom; at++)
            {
                row.Clear();
                for (; i < b.Runs.Count && runY[b.Runs[i]] == at; i++)
                {
                    row.Add(runX0[b.Runs[i]]);
                    row.Add(runX1[b.Runs[i]]);
                }
                spans[at - top] = row.Count == 0 ? Array.Empty<int>() : row.ToArray();
            }
            return spans;
        }
    }
}
