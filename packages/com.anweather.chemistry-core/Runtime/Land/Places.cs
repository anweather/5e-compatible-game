// world/places.json: the world's named landmarks and the cells each covers
// (docs/design/world-tools.md, G1), for the rules MCP's places() and here() and
// for a look's "where" (LookScopes). Read-only: mods can't change them.
//
// Most places are generated from what the world data already says
// (GeneratedPlaces.Generate, in the core above the land: it reads Play's files): the villages (villages.json), the courses
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
    public sealed class Place
    {
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
        public string Kind { get; }
        public string From { get; }
        public bool Generated { get; }
        public CellRect Bounds { get; }
        public int Count { get; }
        public (int x, int y) Middle { get; }
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

        public IEnumerable<(int x, int y)> Cells()
        {
            for (int i = 0; i < spans.Length; i++)
                for (int k = 0; k < spans[i].Length; k += 2)
                    for (int x = spans[i][k]; x <= spans[i][k + 1]; x++) yield return (x, Bounds.Y + i);
        }

        public IEnumerable<(int x, int y)> EdgeCells() =>
            Cells().Where(c => !Contains(c.x + 1, c.y) || !Contains(c.x - 1, c.y) || !Contains(c.x, c.y + 1) || !Contains(c.x, c.y - 1));

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

        internal static Place Parse(JObject o)
        {
            string id = Str(o["id"]) ?? throw Bad("a place needs an id");
            if (o["rows"] != null && o["area"] == null && o["line"] == null && o["circle"] == null) return FromRows(o["rows"], o, id);
            var cells = new SortedSet<(int y, int x)>();
            if (o["area"] != null) AddArea(cells, o["area"], id);
            if (o["line"] != null) AddLine(cells, o["line"], Num(o["width"]) ?? 1, id);
            if (o["circle"] is JObject c) AddCircle(cells, c, id);
            if (o["rows"] != null) AddRows(cells, o["rows"], id);
            if (cells.Count == 0) throw Bad($"place {id} has no cells (give area, line, circle or rows)");
            return FromCells(cells, o);
        }

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
            foreach (var (y, x0, x1) in RowSpans(t, id))
                for (int x = x0; x <= x1; x++) cells.Add((y, x));
        }

        static IEnumerable<(int y, int x0, int x1)> RowSpans(JToken t, string id)
        {
            if (!(t is JArray rows)) throw Bad($"place {id}: rows needs a list of [y, x0, x1, ...]");
            foreach (var r in rows)
            {
                if (!(r is JArray row) || row.Count < 3 || row.Count % 2 == 0) throw Bad($"place {id}: each row is [y, x0, x1, ...]");
                int y = (int)row[0];
                for (int k = 1; k < row.Count; k += 2) yield return (y, (int)row[k], (int)row[k + 1]);
            }
        }

        static Place FromRows(JToken t, JObject o, string id)
        {
            var byRow = new SortedDictionary<int, List<(int x0, int x1)>>();
            foreach (var (y, x0, x1) in RowSpans(t, id))
            {
                if (x0 > x1) continue;
                if (!byRow.TryGetValue(y, out var list))
                {
                    list = new List<(int, int)>();
                    byRow.Add(y, list);
                }
                list.Add((x0, x1));
            }
            if (byRow.Count == 0) throw Bad($"place {id} has no cells (give area, line, circle or rows)");
            int top = byRow.Keys.First();
            var spans = new int[byRow.Keys.Last() - top + 1][];
            for (int i = 0; i < spans.Length; i++) spans[i] = Array.Empty<int>();
            var row = new List<int>();
            foreach (var (y, list) in byRow)
            {
                list.Sort();
                row.Clear();
                foreach (var (x0, x1) in list)
                {
                    if (row.Count > 0 && x0 <= row[row.Count - 1] + 1) row[row.Count - 1] = Math.Max(row[row.Count - 1], x1);
                    else
                    {
                        row.Add(x0);
                        row.Add(x1);
                    }
                }
                spans[y - top] = row.ToArray();
            }
            return FromSpans(top, spans, o);
        }

        internal static FormatException Bad(string message) => new FormatException($"{WorldPlaces.FileName}: {message}");
        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static double? Num(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : (double?)null;
    }

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

        public string Ids => places.Count == 0 ? "none (world/places.json has none)" : string.Join(", ", places.Select(p => p.Id));

        public static WorldPlaces Load(string worldDir)
        {
            var path = worldDir == null ? null : Path.Combine(worldDir, FileName);
            return path != null && File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

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

        internal static string Slug(string name) =>
            string.Join("-", new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        internal static string Write(JObject doc, List<Place> places)
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

        public static string CellWords(int x, int y) => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", x, y);
    }
}
