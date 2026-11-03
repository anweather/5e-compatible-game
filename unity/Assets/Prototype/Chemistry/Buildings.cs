// A building as parts (docs/design/builders.md section 4, J-059): join points on
// the half-cell lattice, wall segments between them, openings in the walls, and
// what a style gives a row that doesn't say. BuildingRow reads one row and
// BuildingRaster turns it into cells: a pure integer function, so every peer lays
// the same house. Option (a), outside only: a wall takes the cells on its right
// (one through cell centres takes those), the outside is flooded from the border
// through open openings, and what's left is the row's fill. Heights, finishes,
// roofs and details are the view's: they stay in Json and decide no cell.
//
// Row ids come from data; nothing here names a building, a style or a material.
// Not a port: the sandbox has no buildings.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>What's left inside a building's walls: its wall's terrain (today's block), or a floor.</summary>
    public enum BuildingFill { Hollow, Solid }

    /// <summary>What part of a building a cell is.</summary>
    public enum BuildingPart { Wall, Opening, Fill, Built }

    /// <summary>One opening's row (rules/buildings.json "openings"): its cells, and what they become.</summary>
    public sealed class OpeningDef
    {
        /// <summary>What "becomes" says for the wall's own terrain, a door the view only draws.</summary>
        public const string StaysWall = "wall";
        /// <summary>What "becomes" says for no cell at all: a gap, which the outside reaches through.</summary>
        public const string Open = "open";
        /// <summary>The most cells an opening's row may say: far past any wall, and its span in half cells (twice it) stays an int (#85).</summary>
        public const int MaxCells = 1 << 20;

        public string Id;
        /// <summary>How many cells it covers; 0 for one whose width the wall says.</summary>
        public int Cells;
        public string Becomes;
    }

    /// <summary>rules/buildings.json as the rasteriser reads it, with the rows a mod's "buildings" section brought.</summary>
    public sealed class BuildingTables
    {
        public const string Key = "buildings";
        /// <summary>The tables a mod's "buildings" section adds rows to.</summary>
        internal static readonly string[] Tables = { "openings", "floors", "roofs", "details", "styles" };

        internal JObject Json = new JObject();
        public string Ground { get; private set; } = "dirt";
        public IReadOnlyDictionary<string, OpeningDef> Openings { get; private set; } = new Dictionary<string, OpeningDef>();

        internal static BuildingTables Parse(JObject doc)
        {
            var t = new BuildingTables { Json = doc?[Key] as JObject ?? new JObject() };
            t.Ground = (string)t.Json["ground"] ?? t.Ground;
            var openings = new Dictionary<string, OpeningDef>(StringComparer.Ordinal);
            foreach (var p in (t.Json["openings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                openings[p.Name] = new OpeningDef { Id = p.Name, Cells = (int?)p.Value["cells"] ?? 0, Becomes = (string)p.Value["becomes"] ?? OpeningDef.StaysWall };
            t.Openings = openings;
            return t;
        }

        /// <summary>A row of a table (openings, floors, roofs, details, styles), or null.</summary>
        public JObject Row(string table, string id) => Json[table]?[id ?? ""] as JObject;

        /// <summary>The ids a table has, for a refusal's list.</summary>
        public string Ids(string table) => string.Join(", ", (Json[table] as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
    }

    /// <summary>An opening in a wall: its row, and where it starts in half cells from the wall's from.</summary>
    public sealed class WallOpening
    {
        public string Kind;
        public int At;
        /// <summary>How long it is in half cells.</summary>
        public int Span;
    }

    /// <summary>A wall segment between two joins, in half cells from the row's at.</summary>
    public sealed class BuildingWall
    {
        public string Id, Material;
        public int FromX, FromY, ToX, ToY, Storey;
        public List<WallOpening> Openings { get; } = new List<WallOpening>();
    }

    /// <summary>One building: the fields that decide cells, and the whole row (Json) for the view.</summary>
    public sealed class BuildingRow
    {
        public string Id { get; private set; }
        /// <summary>The cell its joins count from: (0, 0) in half cells is this cell's north-west corner.</summary>
        public int X { get; private set; }
        public int Y { get; private set; }
        public BuildingFill Fill { get; private set; }
        /// <summary>The terrain a hollow building's floor is.</summary>
        public string Floor { get; private set; }
        public string WallMaterial { get; private set; }
        /// <summary>The walls, by id, in id order.</summary>
        public IReadOnlyList<BuildingWall> Walls { get; private set; } = Array.Empty<BuildingWall>();
        /// <summary>The row with its style's defaults and recipe filled in: everything the view draws from.</summary>
        public JObject Json { get; private set; }

        /// <summary>Cells as they were baked, by terrain, as (x, y) from at: a cabin's, which no lattice describes.</summary>
        internal List<(int x, int y, string terrain)> Built { get; } = new List<(int, int, string)>();

        /// <summary>A row read over these tables. Throws FormatException for one Rules.Check would refuse.</summary>
        public static BuildingRow Read(string id, JObject row, BuildingTables tables)
        {
            var json = Resolved(row, tables);
            if (!(json["at"] is JArray at) || at.Count != 2) throw new FormatException($"building {id} needs \"at\": [x, y]");
            var b = new BuildingRow { Id = id, X = (int)at[0], Y = (int)at[1], Json = json };
            b.Fill = (string)json["fill"] == "solid" ? BuildingFill.Solid : BuildingFill.Hollow;
            b.WallMaterial = (string)json["wall"]?["material"];
            b.Floor = (string)tables.Row("floors", (string)json["floor"]?["kind"])?["terrain"] ?? tables.Ground;
            b.ReadWalls(json, tables);
            b.ReadBuilt(json["built"] as JObject);
            return b;
        }

        void ReadWalls(JObject json, BuildingTables tables)
        {
            var joins = json["joins"] as JObject;
            var walls = new List<BuildingWall>();
            foreach (var p in (json["walls"] as JObject)?.Properties().OrderBy(p => p.Name, StringComparer.Ordinal) ?? Enumerable.Empty<JProperty>())
            {
                var from = joins?[(string)p.Value["from"]] as JArray ?? throw new FormatException($"building {Id}: wall {p.Name} has no from");
                var to = joins[(string)p.Value["to"]] as JArray ?? throw new FormatException($"building {Id}: wall {p.Name} has no to");
                var w = new BuildingWall
                {
                    Id = p.Name,
                    Material = (string)p.Value["material"] ?? WallMaterial,
                    FromX = (int)from[0],
                    FromY = (int)from[1],
                    ToX = (int)to[0],
                    ToY = (int)to[1],
                    Storey = (int?)p.Value["storey"] ?? 0,
                };
                foreach (var o in p.Value["openings"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    w.Openings.Add(new WallOpening { Kind = (string)o["kind"], At = (int?)o["at"] ?? 0, Span = SpanOf(o, tables) });
                walls.Add(w);
            }
            Walls = walls;
        }

        void ReadBuilt(JObject built)
        {
            foreach (var t in built?.Properties() ?? Enumerable.Empty<JProperty>())
                foreach (var r in t.Value.OfType<JArray>())
                    for (int k = 1; k + 1 < r.Count; k += 2)
                        for (int x = (int)r[k]; x <= (int)r[k + 1]; x++) Built.Add((x, (int)r[0], t.Name));
        }

        // An opening's length in half cells: its row's cells, or for one whose width the wall says, that
        // width in metres to the nearest half cell (0.75 m). The numbers are the same bits on every peer.
        internal static int SpanOf(JToken opening, BuildingTables tables)
        {
            var kind = tables.Row("openings", (string)opening["kind"]);
            int cells = (int?)kind?["cells"] ?? 0;
            if (cells > 0) return cells * 2;
            double metres = (double?)opening["width"] ?? 0.75;
            return Math.Max(1, (int)Math.Round(metres * 4 / 3, MidpointRounding.AwayFromZero));
        }

        // ---------- a style's defaults, and its recipe ----------

        const int SaltWidth = 1, SaltDepth = 2;

        /// <summary>The row with what its style gives it where it says nothing, and a recipe's walls if it has none.</summary>
        internal static JObject Resolved(JObject row, BuildingTables tables)
        {
            var json = (JObject)row.DeepClone();
            var style = tables.Row("styles", (string)row["style"]);
            if (style == null) return json;
            foreach (var key in new[] { "fill", "floor", "wall", "storeys" })
                if (json[key] == null && style[key] != null) json[key] = style[key].DeepClone();
            if (json["wall"] is JObject wall && style["wall"] is JObject given)
                foreach (var p in given.Properties().Where(p => wall[p.Name] == null)) wall[p.Name] = p.Value.DeepClone();
            if (json["parts"] == null && style["parts"] != null) json["parts"] = style["parts"].DeepClone();
            if (json["walls"] == null && json["built"] == null && style["recipe"] is JObject recipe) LayRecipe(json, recipe, style);
            return json;
        }

        // A box of cells whose size comes from the row's seed, with the recipe's door on its front and window on its back.
        static void LayRecipe(JObject json, JObject recipe, JObject style)
        {
            int seed = (int?)json["seed"] ?? 0;
            int Pick(JToken range, int salt)
            {
                int lo = (int)range[0], hi = (int)range[1];
                return lo + (int)(WorldHash.Hash4(seed, salt, 0, 0) % (uint)(Math.Max(lo, hi) - lo + 1));
            }
            var box = Box(Pick(recipe["width"], SaltWidth), Pick(recipe["depth"], SaltDepth), Side.South, (string)recipe["door"], (string)recipe["window"], style["roof"] as JObject);
            foreach (var p in box.Properties()) json[p.Name] = p.Value;
        }

        /// <summary>
        /// A closed box of walls, width by depth cells from (0, 0), running clockwise: joins, walls and a
        /// roof over them. A door in the middle of the front, a window in the middle of each other wall
        /// long enough to keep it clear of its corners. Either kind may be null for none.
        /// </summary>
        public static JObject Box(int width, int depth, Side front, string door, string window, JObject roof = null)
        {
            int w = width * 2, d = depth * 2;
            var walls = new JObject
            {
                ["north"] = BoxWall("nw", "ne", Side.North, front, w, door, window),
                ["east"] = BoxWall("ne", "se", Side.East, front, d, door, window),
                ["south"] = BoxWall("se", "sw", Side.South, front, w, door, window),
                ["west"] = BoxWall("sw", "nw", Side.West, front, d, door, window),
            };
            var box = new JObject
            {
                ["joins"] = new JObject { ["nw"] = new JArray(0, 0), ["ne"] = new JArray(w, 0), ["se"] = new JArray(w, d), ["sw"] = new JArray(0, d) },
                ["walls"] = walls,
            };
            if (roof != null)
            {
                var main = (JObject)roof.DeepClone();
                main["over"] = new JArray("nw", "ne", "se", "sw");
                box["roofs"] = new JObject { ["main"] = main };
            }
            return box;
        }

        static JObject BoxWall(string from, string to, Side side, Side front, int length, string door, string window)
        {
            var wall = new JObject { ["from"] = from, ["to"] = to };
            string kind = side == front ? door : window;
            if (kind != null && length >= 6) wall["openings"] = new JArray(new JObject { ["kind"] = kind, ["at"] = 2 * ((length / 2 - 1) / 2) });
            return wall;
        }

        // ---------- what the fingerprint sees ----------

        static readonly string[] SharedKeys = { "at", "style", "seed", "fill", "joins", "built" };

        /// <summary>A row with only what decides cells: joins, walls and openings, fill and floor kind. The rest is the view's (Rules.Fingerprint).</summary>
        internal static JObject Shared(JObject row)
        {
            var o = new JObject();
            foreach (var key in SharedKeys)
                if (row[key] != null) o[key] = row[key].DeepClone();
            if (row["floor"]?["kind"] != null) o["floor"] = row["floor"]["kind"].DeepClone();
            if (row["wall"]?["material"] != null) o["wall"] = row["wall"]["material"].DeepClone();
            if (row["recipe"] != null) o["recipe"] = row["recipe"].DeepClone();
            if (row["walls"] is JObject walls)
            {
                var kept = new JObject();
                foreach (var w in walls.Properties().OfType<JProperty>())
                {
                    var one = new JObject { ["from"] = w.Value["from"]?.DeepClone(), ["to"] = w.Value["to"]?.DeepClone() };
                    if (w.Value["material"] != null) one["material"] = w.Value["material"].DeepClone();
                    if (w.Value["storey"] != null) one["storey"] = w.Value["storey"].DeepClone();
                    if (w.Value["openings"] is JArray openings)
                        one["openings"] = new JArray(openings.OfType<JObject>().Select(x => new JObject { ["kind"] = x["kind"]?.DeepClone(), ["at"] = x["at"]?.DeepClone(), ["width"] = x["width"]?.DeepClone() }));
                    kept[w.Name] = one;
                }
                o["walls"] = kept;
            }
            return o;
        }

        /// <summary>The tables with a style's rows cut to what decides cells, for the fingerprint.</summary>
        internal static JObject SharedTables(JObject tables)
        {
            var o = (JObject)tables.DeepClone();
            if (o["styles"] is JObject styles)
                foreach (var p in styles.Properties().ToList()) p.Value = Shared((JObject)p.Value);
            o.Remove("about");
            o.Remove("drawn");
            return o;
        }
    }

    /// <summary>One cell of a building, in world cells.</summary>
    public readonly struct BuildingCell
    {
        public BuildingCell(int x, int y, string terrain, BuildingPart part)
        {
            X = x;
            Y = y;
            Terrain = terrain;
            Part = part;
        }

        public int X { get; }
        public int Y { get; }
        public string Terrain { get; }
        public BuildingPart Part { get; }
    }

    /// <summary>A building's parts to cells, outside only (option (a)). Pure and whole-number.</summary>
    public static class BuildingRaster
    {
        /// <summary>The row's cells, north to south then west to east, in world cells.</summary>
        public static List<BuildingCell> Cells(BuildingRow row, BuildingTables tables)
        {
            var taken = new SortedDictionary<(int y, int x), (string terrain, BuildingPart part)>();
            var open = new HashSet<(int y, int x)>();
            foreach (var (x, y, terrain) in row.Built) taken[(y, x)] = (terrain, BuildingPart.Built);
            LayWalls(row, tables, taken, open);
            Fill(row, taken, open);
            return taken.Select(c => new BuildingCell(row.X + c.Key.x, row.Y + c.Key.y, c.Value.terrain, c.Value.part)).ToList();
        }

        /// <summary>FNV-1a over a building's cells: its golden.</summary>
        public static uint Digest(IEnumerable<BuildingCell> cells)
        {
            uint h = 2166136261;
            void Mix(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) h = (h ^ (byte)(v >> (8 * i))) * 16777619;
                }
            }
            foreach (var c in cells)
            {
                Mix(c.X);
                Mix(c.Y);
                foreach (byte b in Encoding.UTF8.GetBytes(c.Terrain)) Mix(b);
            }
            return h;
        }

        // The walls, in id order, the first to take a cell keeping it: the upper storeys take none. An
        // opening holds only on a cell one wall takes: where another wall meets it, it goes.
        static void LayWalls(BuildingRow row, BuildingTables tables, SortedDictionary<(int y, int x), (string terrain, BuildingPart part)> taken, HashSet<(int y, int x)> open)
        {
            var walls = row.Walls.Where(w => w.Storey == 0).ToList();
            var takers = new Dictionary<(int y, int x), int>();
            var runs = walls.Select(w => Run(w).ToList()).ToList();
            foreach (var run in runs)
                foreach (var c in run) takers[c] = takers.TryGetValue(c, out int n) ? n + 1 : 1;
            for (int i = 0; i < walls.Count; i++)
                foreach (var c in runs[i])
                {
                    var opening = OpeningOn(walls[i], c, tables);
                    if (opening != null && takers[c] == 1) LayOpening(opening, walls[i], c, taken, open);
                    else if (!taken.ContainsKey(c)) taken[c] = (walls[i].Material, BuildingPart.Wall);
                }
        }

        static void LayOpening(OpeningDef opening, BuildingWall wall, (int y, int x) c, SortedDictionary<(int y, int x), (string terrain, BuildingPart part)> taken, HashSet<(int y, int x)> open)
        {
            if (opening.Becomes == OpeningDef.Open) open.Add(c);
            else taken[c] = (opening.Becomes == OpeningDef.StaysWall ? wall.Material : opening.Becomes, BuildingPart.Opening);
        }

        // The distinct cells a wall takes, from its from to its to: for each half cell along it, the cell
        // just to the right of that half cell's middle. Counted in eighths of a cell so an edge line, a line
        // through centres and a 45° line are one rule. y grows south, so right of (dx, dy) is (-dy, dx).
        static IEnumerable<(int y, int x)> Run(BuildingWall w)
        {
            int dx = Math.Sign(w.ToX - w.FromX), dy = Math.Sign(w.ToY - w.FromY);
            int length = Math.Max(Math.Abs(w.ToX - w.FromX), Math.Abs(w.ToY - w.FromY));
            var seen = new HashSet<(int y, int x)>();
            for (int i = 0; i < length; i++)
            {
                int px = (w.FromX * 4) + ((2 * i + 1) * 2 * dx) - dy;
                int py = (w.FromY * 4) + ((2 * i + 1) * 2 * dy) + dx;
                var cell = (FloorDiv(py, 8), FloorDiv(px, 8));
                if (seen.Add(cell)) yield return cell;
            }
        }

        // The opening whose span holds the middle of this cell, measured along the wall from its from.
        static OpeningDef OpeningOn(BuildingWall w, (int y, int x) c, BuildingTables tables)
        {
            int dx = Math.Sign(w.ToX - w.FromX), dy = Math.Sign(w.ToY - w.FromY);
            int along = (((2 * c.x) + 1 - w.FromX) * dx + ((2 * c.y) + 1 - w.FromY) * dy) / (dx * dx + dy * dy);
            foreach (var o in w.Openings)
                if (along >= o.At && along < o.At + o.Span && tables.Openings.TryGetValue(o.Kind, out var def)) return def;
            return null;
        }

        // Flood the outside from a border one cell past everything, through whatever no wall holds; what
        // the flood doesn't reach is inside.
        static void Fill(BuildingRow row, SortedDictionary<(int y, int x), (string terrain, BuildingPart part)> taken, HashSet<(int y, int x)> open)
        {
            if (taken.Count == 0 && open.Count == 0) return;
            var all = taken.Keys.Concat(open).ToList();
            int x0 = all.Min(c => c.x) - 1, x1 = all.Max(c => c.x) + 1, y0 = all.Min(c => c.y) - 1, y1 = all.Max(c => c.y) + 1;
            var outside = new HashSet<(int y, int x)>();
            var stack = new Stack<(int y, int x)>();
            stack.Push((y0, x0));
            outside.Add((y0, x0));
            while (stack.Count > 0)
            {
                var (y, x) = stack.Pop();
                foreach (var n in new[] { (y - 1, x), (y, x + 1), (y + 1, x), (y, x - 1) })
                {
                    if (n.Item1 < y0 || n.Item1 > y1 || n.Item2 < x0 || n.Item2 > x1 || taken.ContainsKey(n) || !outside.Add(n)) continue;
                    stack.Push(n);
                }
            }
            string fill = row.Fill == BuildingFill.Solid ? row.WallMaterial : row.Floor;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (!outside.Contains((y, x)) && !taken.ContainsKey((y, x))) taken[(y, x)] = (fill, BuildingPart.Fill);
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
