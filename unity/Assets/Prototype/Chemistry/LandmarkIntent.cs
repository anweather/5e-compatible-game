// A landmark intent (docs/design/world-squares.md sections 1-2, J-080 phase 1): world/landmarks/<id>.json says what a
// world square asks for ("a village with a landmark"), with a form per biome, and Make turns (intent, biome, site, seed)
// into rows the map files already read: stamps.json stamps, a villages.json village, buildings.json rows, things.json
// things per chunk and a places.json place. A pure function in integers and WorldHash: the same rows on every machine,
// so a world can make its squares on demand.
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
    /// <summary>One intent's file: its id, how far it reaches, and its forms by biome.</summary>
    public sealed class LandmarkIntent
    {
        /// <summary>Where the intents live under the game's world folder.</summary>
        public const string Folder = "landmarks";

        /// <summary>The form for any biome the file doesn't name.</summary>
        public const string AnyBiome = "*";

        const int MostUses = 8;

        readonly JObject by;

        LandmarkIntent(string id, int footprint, JObject by)
        {
            Id = id;
            Footprint = footprint;
            this.by = by;
        }

        public string Id { get; }

        /// <summary>How many cells across the landmark and its village may take.</summary>
        public int Footprint { get; }

        /// <summary>The forms the file writes out (not the ones that only use another), in its order.</summary>
        public IEnumerable<string> Forms => by.Properties().Where(p => p.Value["use"] == null).Select(p => p.Name);

        /// <summary>world/landmarks/<paramref name="id"/>.json under <paramref name="worldDir"/>.</summary>
        public static LandmarkIntent Load(string worldDir, string id) =>
            Parse(File.ReadAllText(Path.Combine(worldDir, Folder, id + ".json")));

        /// <summary>An intent's text. Throws <see cref="FormatException"/> for one that can't be made.</summary>
        public static LandmarkIntent Parse(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"landmark: {e.Message}", e); }
            string id = (string)doc["id"];
            if (string.IsNullOrEmpty(id)) throw new FormatException("landmark: an intent needs an \"id\"");
            if (!(doc["by"] is JObject by) || by[AnyBiome] == null) throw new FormatException($"landmark {id}: \"by\" needs a \"{AnyBiome}\" form");
            var intent = new LandmarkIntent(id, (int?)doc["footprint"] ?? 160, by);
            foreach (var p in by.Properties()) intent.FormFor(p.Name);
            return intent;
        }

        /// <summary>The form a biome takes: its own, the one it uses, or "*"'s.</summary>
        public string FormFor(string biome)
        {
            string at = by[biome ?? ""] != null ? biome : AnyBiome;
            for (int n = 0; n < MostUses; n++)
            {
                var form = by[at] as JObject ?? throw new FormatException($"landmark {Id}: form {at} isn't an object");
                if (form["use"] == null) return at;
                at = (string)form["use"];
                if (by[at] == null) throw new FormatException($"landmark {Id}: a form uses {at}, which it hasn't got");
            }
            throw new FormatException($"landmark {Id}: the forms' uses go round in a circle from {biome}");
        }

        /// <summary>
        /// The rows of this intent in <paramref name="biome"/>, at a site (a world cell), from a seed. A village's house laid
        /// out here (a climb's, a shore's) goes only where <paramref name="buildable"/> takes every cell of it; without one,
        /// anywhere.
        /// </summary>
        public LandmarkRows Make(string biome, int siteX, int siteY, int seed, Func<int, int, bool> buildable = null)
        {
            string form = FormFor(biome);
            return new LandmarkMaker(Id, form, (JObject)by[form], siteX, siteY, seed, buildable).Run();
        }
    }

    /// <summary>What an intent made: rows in the map files' own shapes, and the texts of those files holding only them.</summary>
    public sealed class LandmarkRows
    {
        public string Intent { get; internal set; }
        public string Form { get; internal set; }
        /// <summary>The form's words: "a forest village by a great tree".</summary>
        public string Words { get; internal set; }
        /// <summary>The landmark's place's name and the village's.</summary>
        public string Name { get; internal set; }
        public string VillageName { get; internal set; }
        /// <summary>The landmark's middle and how far it reaches, in cells.</summary>
        public (int x, int y) Centre { get; internal set; }
        public int Reach { get; internal set; }

        public JArray Stamps { get; } = new JArray();
        /// <summary>The villages.json recipe of a village laid as a square; null for one laid out here as rows (a climb, a shore).</summary>
        public JObject Village { get; internal set; }
        public JArray Buildings { get; } = new JArray();
        /// <summary>things.json's chunks: {chunk, things}, by chunk row then column.</summary>
        public JArray Things { get; } = new JArray();
        public JObject Place { get; internal set; }
        /// <summary>The village's own place when it's laid as rows (a square village's comes from villages.json); else null.</summary>
        public JObject VillagePlace { get; internal set; }
        /// <summary>Where the village starts: a square's middle, a climb's foot, the middle of a shore's arc.</summary>
        public (int x, int y) VillageAt { get; internal set; }

        public string StampsJson => File("stamps", Stamps);
        public string VillagesJson => File("villages", Village == null ? new JArray() : new JArray(Village));
        public string BuildingsJson => File("buildings", Buildings);
        public string ThingsJson => File("chunks", Things);
        public string PlacesJson => File("places", VillagePlace == null ? new JArray(Place) : new JArray(Place, VillagePlace));

        static string File(string key, JArray rows) => new JObject { [key] = rows.DeepClone() }.ToString(Formatting.None);

        /// <summary>FNV-1a over the five files' texts: the golden the PC, the Mac and u2 compare.</summary>
        public uint Hash()
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (string text in new[] { StampsJson, VillagesJson, BuildingsJson, ThingsJson, PlacesJson })
                    foreach (byte b in Encoding.UTF8.GetBytes(text + "\n"))
                    {
                        h ^= b;
                        h *= 16777619u;
                    }
                return h;
            }
        }
    }

    // One making: the form, the site, the turn, and a count of rolls (each a hash of the key, its kind and that count).
    sealed class LandmarkMaker
    {
        const int SaltTurn = 1, SaltName = 2, SaltVillage = 3, SaltRange = 4, SaltDirection = 5, SaltSeed = 6;
        const int Unit = 1024;
        const string Square = "square", Climb = "climb", Shore = "shore", Wind = "wind", Shelf = "shelf";

        readonly string intent, form;
        readonly JObject row;
        readonly int siteX, siteY, key, turn;
        readonly LandmarkRows rows;
        readonly Func<int, int, bool> buildable;
        readonly List<(int x, int y, int w, int h)> docks = new List<(int, int, int, int)>();
        readonly List<(int x, int y, JObject thing)> things = new List<(int, int, JObject)>();
        int rolls;
        string placeId;

        public LandmarkMaker(string intent, string form, JObject row, int siteX, int siteY, int seed, Func<int, int, bool> buildable)
        {
            this.buildable = buildable;
            this.intent = intent;
            this.form = form;
            this.row = row;
            this.siteX = siteX;
            this.siteY = siteY;
            key = (int)WorldHash.Hash4(seed, (int)BiomeMath.NameHash(intent), siteX, siteY);
            turn = (int)(Roll(SaltTurn) & 3);
            rows = new LandmarkRows { Intent = intent, Form = form, Words = (string)row["name"] ?? form };
        }

        uint Roll(int salt) => WorldHash.Hash4(key, salt, rolls++, 0);

        int Pick(JToken range)
        {
            if (range == null) return 0;
            if (!(range is JArray a)) return (int)range;
            int lo = (int)a[0], hi = (int)a[1];
            return lo + (int)(Roll(SaltRange) % (uint)(Math.Max(lo, hi) - lo + 1));
        }

        string PickName(string listKey, string fallback)
        {
            var names = (row[listKey] as JArray)?.Select(t => (string)t).ToList();
            if (names == null || names.Count == 0) return fallback;
            return names[(int)(Roll(SaltName) % (uint)names.Count)];
        }

        // An offset from the site turned with the form: a quarter turn clockwise per step, y south.
        (int x, int y) Turned(int dx, int dy)
        {
            switch (turn)
            {
                case 1: return (-dy, dx);
                case 2: return (-dx, -dy);
                case 3: return (dy, -dx);
                default: return (dx, dy);
            }
        }

        (int x, int y) At(JToken offset)
        {
            var a = offset as JArray;
            var (dx, dy) = Turned(a == null ? 0 : (int)a[0], a == null ? 0 : (int)a[1]);
            return (siteX + dx, siteY + dy);
        }

        // A seeded direction, Unit long, in integers.
        (int x, int y) Direction()
        {
            for (int tries = 0; tries < 16; tries++)
            {
                int dx = (int)(Roll(SaltDirection) % 2001) - 1000, dy = (int)(Roll(SaltDirection) % 2001) - 1000;
                long length = BiomeMath.Sqrt((long)dx * dx + (long)dy * dy);
                if (length >= 200) return ((int)(dx * Unit / length), (int)(dy * Unit / length));
            }
            return (Unit, 0);
        }

        public LandmarkRows Run()
        {
            rows.Name = PickName("landmarkNames", form);
            rows.VillageName = PickName("villageNames", "Village");
            placeId = Slug(rows.Name);
            Stamps();
            Village();
            if (row["greatTree"] is JObject tree) GreatTree(tree);
            if (row["docks"] is JObject dock) Docks(dock);
            foreach (var h in (row["houses"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) House(h);
            foreach (var t in (row["things"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) Things(t);
            ThingChunks();
            Place();
            VillagePlaceOf();
            return rows;
        }

        static string Slug(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        // ---------- the land ----------

        void Stamps()
        {
            int n = 0, reach = 0;
            (int x, int y) centre = (siteX, siteY);
            foreach (var s in (row["stamps"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var (x, y) = At(s["at"]);
                int radius = Pick(s["radius"]);
                rows.Stamps.Add(new JObject
                {
                    ["id"] = $"{placeId}-{++n}",
                    ["name"] = n == 1 ? rows.Name : $"{rows.Name} ({n})",
                    ["at"] = new JArray(x, y),
                    ["profile"] = (string)s["profile"],
                    ["radius"] = radius,
                    ["rise"] = Pick(s["rise"]),
                    ["rough"] = (int?)s["rough"] ?? 40,
                    ["seed"] = (int)(Roll(SaltSeed) % 100000),
                });
                if (s["bare"] != null) ((JObject)rows.Stamps[rows.Stamps.Count - 1])["bare"] = (string)s["bare"];
                if (n == 1)
                {
                    centre = (x, y);
                    reach = radius;
                }
            }
            rows.Centre = centre;
            rows.Reach = reach;
        }

        // ---------- the village ----------

        void Village()
        {
            var v = row["village"] as JObject ?? new JObject();
            var (x, y) = At(v["at"]);
            int jitter = (int?)v["jitter"] ?? 0;
            x += Pick(new JArray(-jitter, jitter));
            y += Pick(new JArray(-jitter, jitter));
            rows.VillageAt = (x, y);
            string layout = (string)v["layout"] ?? Square;
            if (layout == Climb)
            {
                Climbing(v, x, y);
                return;
            }
            if (layout == Shore)
            {
                OnTheShore(v, x, y);
                return;
            }
            if (layout == Wind)
            {
                Winding(v, x, y);
                return;
            }
            if (layout == Shelf)
            {
                OnAShelf(v, x, y);
                return;
            }
            int square = Pick(v["square"]), lane = Pick(v["laneLength"]);
            var village = new JObject
            {
                ["name"] = rows.VillageName,
                ["at"] = new JArray(x, y),
                ["seed"] = (int)(Roll(SaltVillage) % 100000),
                ["square"] = new JArray(square, square + 1),
                ["houses"] = Pick(v["houses"]),
                ["lanes"] = (int?)v["lanes"] ?? 2,
                ["laneLength"] = new JArray(lane, lane + 4),
                ["stoneInTen"] = (int?)v["stoneInTen"] ?? 3,
            };
            foreach (var flag in new[] { "inn", "shop" })
                if (v[flag] != null) village[flag] = (bool)v[flag];
            village["note"] = $"{rows.Words}: {intent} ({form}), by {rows.Name}";
            rows.Village = village;
        }

        // ---------- the great tree: a stand-in from rows we have ----------

        // A trunk of log wall (an octagon of walls, drawn low as the root flare), the view's own tree scaled up over it (the
        // "great-tree" detail, rules/buildings.json), and roots of fallen tree laid out from it as roads. Joins are half
        // cells from the site's cell, (1, 1) its middle.
        void GreatTree(JObject t)
        {
            int trunk = Pick(t["trunk"]), roots = Pick(t["roots"]);
            var joins = new JObject { ["c"] = new JArray(1, 1) };
            var roads = new JObject();
            for (int k = 0; k < roots; k++) Root(t, joins, roads, k, roots, trunk);
            int a = 2 * trunk, b = a * 29 / 70;
            var corners = new[] { (b, -a), (a, -b), (a, b), (b, a), (-b, a), (-a, b), (-a, -b), (-b, -a) };
            var walls = new JObject();
            for (int k = 0; k < corners.Length; k++)
            {
                joins[$"o{k}"] = new JArray(1 + corners[k].Item1, 1 + corners[k].Item2);
                walls[$"w{k}"] = new JObject { ["from"] = $"o{k}", ["to"] = $"o{(k + 1) % corners.Length}" };
            }
            int scale = Pick(t["scale"]);
            rows.Buildings.Add(new JObject
            {
                ["id"] = $"{placeId}#1",
                ["at"] = new JArray(siteX, siteY),
                ["fill"] = "solid",
                ["seed"] = (int)(Roll(SaltSeed) % 100000),
                ["note"] = $"{rows.Name}: the great tree's stand-in, a tree {scale} times its size over a log trunk, with roots",
                ["wall"] = new JObject { ["material"] = "logwall", ["height"] = (double?)t["flare"] ?? 1.5, ["thickness"] = 0.45 },
                ["joins"] = joins,
                ["walls"] = walls,
                ["roads"] = roads,
                ["parts"] = new JArray(GreatTreePart(t, scale)),
            });
            rows.Reach = Math.Max(rows.Reach, trunk + Pick(t["root"]) + 4);
        }

        // The detail the view draws as the tree: at the trunk's middle, its size, and the kind of tree when the form names one.
        static JObject GreatTreePart(JObject t, int scale)
        {
            var part = new JObject { ["kind"] = "great-tree", ["join"] = "c", ["scale"] = scale };
            if (t["tree"] != null) part["tree"] = (string)t["tree"];
            return part;
        }

        // A Unit vector turned by whole degrees, by tables of tens of degrees and of single ones, in integers.
        static (int x, int y) Rotated(int x, int y, int degrees)
        {
            // cos and sin of 0..350 in steps of 10, times Unit.
            int[] cos = { 1024, 1008, 962, 887, 784, 658, 512, 350, 178, 0, -178, -350, -512, -658, -784, -887, -962, -1008, -1024, -1008, -962, -887, -784, -658, -512, -350, -178, 0, 178, 350, 512, 658, 784, 887, 962, 1008 };
            // and of 0..9 degrees, for what's left over.
            int[] fineCos = { 1024, 1024, 1023, 1023, 1022, 1020, 1018, 1016, 1014, 1011 }, fineSin = { 0, 18, 36, 54, 71, 89, 107, 125, 143, 160 };
            int whole = ((degrees % 360) + 360) % 360, step = whole / 10, left = whole % 10;
            int c = cos[step], s = cos[(step + 27) % 36];
            int rx = (x * c - y * s) / Unit, ry = (x * s + y * c) / Unit;
            return ((rx * fineCos[left] - ry * fineSin[left]) / Unit, (rx * fineSin[left] + ry * fineCos[left]) / Unit);
        }

        void Root(JObject t, JObject joins, JObject roads, int k, int n, int trunk)
        {
            var (ux, uy) = Rotated(1024, 0, k * 360 / n + Pick(new JArray(0, 9)) * 2);
            var (rx, ry) = Turned(ux, uy);
            int length = Pick(t["root"]), bend = Pick(new JArray(-3, 3));
            int startX = rx * trunk * 2 / Unit, startY = ry * trunk * 2 / Unit;
            int midX = rx * (trunk + length / 2) * 2 / Unit - ry * bend * 2 / Unit, midY = ry * (trunk + length / 2) * 2 / Unit + rx * bend * 2 / Unit;
            int endX = rx * (trunk + length) * 2 / Unit, endY = ry * (trunk + length) * 2 / Unit;
            joins[$"r{k}a"] = new JArray(1 + startX, 1 + startY);
            joins[$"r{k}b"] = new JArray(1 + midX, 1 + midY);
            joins[$"r{k}c"] = new JArray(1 + endX, 1 + endY);
            roads[$"root-{k}"] = new JObject { ["through"] = new JArray($"r{k}a", $"r{k}b", $"r{k}c"), ["terrain"] = "deadfall", ["width"] = 1 };
        }

        // ---------- villages laid out as rows: a climb up the landmark, an arc round a shore ----------

        // A unit vector from one cell towards another (east when they're the same).
        static (int x, int y) Toward(int fx, int fy, int tx, int ty)
        {
            long dx = tx - fx, dy = ty - fy, length = BiomeMath.Sqrt(dx * dx + dy * dy);
            return length == 0 ? (Unit, 0) : ((int)(dx * Unit / length), (int)(dy * Unit / length));
        }

        static Side Facing(int fx, int fy, int tx, int ty)
        {
            int dx = tx - fx, dy = ty - fy;
            if (Math.Abs(dy) >= Math.Abs(dx)) return dy < 0 ? Side.North : Side.South;
            return dx < 0 ? Side.West : Side.East;
        }

        // A house of the village style centred on a cell, its door facing a point; stone or log by the village's stoneInTen.
        bool HouseAt(JObject v, (int x, int y) at, (int x, int y) facing, JObject size, string what, out JObject building)
        {
            var (cx, cy) = at;
            building = null;
            var dims = (size ?? v)["house"] as JArray;
            int w = Pick(size?["width"] ?? dims?[0] ?? 4), d = Pick(size?["depth"] ?? dims?[1] ?? 4);
            var front = Facing(cx, cy, facing.x, facing.y);
            if (front == Side.East || front == Side.West) (w, d) = (d, w);
            int ax = cx - w / 2, ay = cy - d / 2;
            if (buildable != null && !Cells(ax, ay, w, d).All(c => buildable(c.x, c.y))) return false;
            bool stone = size?["wall"] != null ? (string)size["wall"] == "stone" : Roll(SaltVillage) % 10 < (uint)((int?)v["stoneInTen"] ?? 0);
            building = new JObject
            {
                ["id"] = $"{Slug(rows.VillageName)}#{rows.Buildings.Count(b => ((string)b["id"]).StartsWith(Slug(rows.VillageName) + "#", StringComparison.Ordinal)) + 1}",
                ["at"] = new JArray(ax, ay),
                ["style"] = "village",
                ["seed"] = (int)(Roll(SaltSeed) % 100000),
                ["note"] = $"{rows.VillageName}'s {what}, by {rows.Name}",
            };
            if (stone) building["wall"] = new JObject { ["material"] = "stone" };
            // On a slope, a flat pad cut for it, its walls one height (a climb's houses).
            if (v["pad"] is JObject pad)
            {
                building["level"] = true;
                building["pad"] = pad.DeepClone();
            }
            var roof = new JObject { ["shape"] = "gable", ["pitch"] = new JArray(8, 12), ["overhang"] = 0.4, ["cover"] = stone ? "slate" : "thatch" };
            foreach (var p in BuildingRow.Box(w, d, front, "door", "window", roof).Properties()) building[p.Name] = p.Value;
            rows.Buildings.Add(building);
            return true;
        }

        static IEnumerable<(int x, int y)> Cells(int x, int y, int w, int h)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++) yield return (x + i, y + j);
        }

        // The village's way, laid as a road on its first house's row through these cells.
        static void Way(JObject first, IList<(int x, int y)> points, int width, bool graded = false)
        {
            if (first == null || points.Count < 2) return;
            int ax = (int)first["at"][0], ay = (int)first["at"][1];
            var joins = (JObject)first["joins"];
            var through = new JArray();
            for (int k = 0; k < points.Count; k++)
            {
                joins[$"way{k}"] = new JArray(2 * (points[k].x - ax) + 1, 2 * (points[k].y - ay) + 1);
                through.Add($"way{k}");
            }
            var way = new JObject { ["through"] = through, ["terrain"] = "dirt", ["width"] = width };
            // Cut into the slope it crosses (RoadGrade), a terrace of its own between the houses' pads.
            if (graded) way["graded"] = true;
            first["roads"] = new JObject { ["way"] = way };
        }

        // Houses either side of a way that climbs from the foot towards the landmark, wandering a little, with the top house
        // (a watch house) where it ends.
        void Climbing(JObject v, int x, int y)
        {
            var (ux, uy) = Toward(x, y, rows.Centre.x, rows.Centre.y);
            int px = -uy, py = ux;
            int length = Pick(v["climb"]), houses = Math.Max(2, Pick(v["houses"])), step = Math.Max(6, length / houses), wander = 0;
            var way = new List<(int x, int y)> { (x, y) };
            JObject first = null;
            for (int k = 0; k < houses; k++)
            {
                int along = step * k + step / 2, side = k % 2 == 0 ? 1 : -1, back = Pick(v["setback"]);
                wander = Math.Max(-4, Math.Min(4, wander + Pick(new JArray(-2, 2))));
                int cx = x + (ux * along + px * wander) / Unit, cy = y + (uy * along + py * wander) / Unit;
                way.Add((cx, cy));
                if (HouseAt(v, (cx + px * side * back / Unit, cy + py * side * back / Unit), (cx, cy), null, "house", out var house)) first ??= house;
            }
            int end = step * houses + step / 2;
            var (tx, ty) = (x + (ux * end + px * wander) / Unit, y + (uy * end + py * wander) / Unit);
            way.Add((tx, ty));
            if (v["top"] is JObject top)
            {
                int up = Pick(top["depth"]) / 2 + 3;
                HouseAt(v, (tx + ux * up / Unit, ty + uy * up / Unit), (tx, ty), top, (string)top["what"] ?? "house", out _);
            }
            Way(first, way, (int?)v["width"] ?? 2);
        }

        // A way that winds part-way round the landmark from the village's foot, closing in on it slowly (a gentle grade up
        // its lower slopes, never its top), with houses either side: one above the way on the up slope, the next below it.
        void Winding(JObject v, int x, int y)
        {
            var (cx, cy) = rows.Centre;
            var (bx, by) = Toward(cx, cy, x, y);
            long dx = x - cx, dy = y - cy;
            int start = (int)BiomeMath.Sqrt(dx * dx + dy * dy), end = Math.Max(4, rows.Reach + ((int?)v["to"] ?? -20));
            int span = Pick(v["span"]), sense = Roll(SaltDirection) % 2 == 0 ? 1 : -1;
            int houses = Math.Max(2, Pick(v["houses"])), steps = Math.Max(4, span / 10);
            var way = new List<(int x, int y)>();
            for (int k = 0; k <= steps; k++) way.Add(OnTheWinding((cx, cy), (bx, by), (start, end), sense * span, k, steps));
            JObject first = null;
            for (int k = 0; k < houses; k++)
            {
                // Halfway between the way's points, so a house never sits on a bend.
                var (wx, wy) = OnTheWinding((cx, cy), (bx, by), (start, end), sense * span, 2 * k + 1, 2 * houses);
                var (ux, uy) = Toward(cx, cy, wx, wy);
                int back = Pick(v["setback"]) * (k % 2 == 0 ? -1 : 1);
                if (HouseAt(v, (wx + ux * back / Unit, wy + uy * back / Unit), (wx, wy), null, "house", out var house)) first ??= house;
            }
            Way(first, way, (int?)v["width"] ?? 2, (bool?)v["graded"] ?? false);
        }

        // Houses round a shelf levelled into the landmark's lower slope (one pad, on the first house, reaching over the whole
        // shelf), their doors to its middle; and the village's way out, a trail switchbacking up to the landmark (Trail).
        void OnAShelf(JObject v, int x, int y)
        {
            int shelf = (int?)v["shelf"] ?? 14, houses = Math.Max(2, Pick(v["houses"]));
            var (ux, uy) = Direction();
            JObject first = null;
            for (int k = 0; k < houses; k++)
            {
                var (ox, oy) = Rotated(ux, uy, k * 360 / houses);
                int r = Pick(v["ring"]);
                if (HouseAt(v, (x + ox * r / Unit, y + oy * r / Unit), (x, y), null, "house", out var house)) first ??= house;
            }
            if (first != null)
            {
                int ax = (int)first["at"][0], ay = (int)first["at"][1], bank = (int?)v["bank"] ?? 6;
                first["level"] = true;
                first["pad"] = new JObject
                {
                    ["margin"] = 1,
                    ["bank"] = bank,
                    ["more"] = new JArray(new JObject
                    {
                        ["area"] = new JArray(2 * (x - shelf - ax), 2 * (y - shelf - ay), 2 * (x + shelf - ax), 2 * (y + shelf - ay)),
                        ["bank"] = bank,
                        ["clear"] = true,
                    }),
                };
            }
            if (row["trail"] is JObject trail) Trail(trail, (x, y), shelf, first);
        }

        // The village's way out: from the shelf's edge, switchbacking up towards the landmark's middle in legs that turn one
        // way and then the other round it, cut into the slope, to a lookout just short of its crater's rim.
        void Trail(JObject t, (int x, int y) from, int shelf, JObject village)
        {
            var (cx, cy) = rows.Centre;
            var (bx, by) = Toward(cx, cy, from.x, from.y);
            long dx = from.x - cx, dy = from.y - cy;
            int start = (int)BiomeMath.Sqrt(dx * dx + dy * dy) - shelf;
            int rim = rows.Stamps.Count > 1 ? (int)rows.Stamps[1]["radius"] : 6;
            int end = rim + ((int?)t["rim"] ?? 4), legs = Math.Max(1, Pick(t["legs"])), sense = Roll(SaltDirection) % 2 == 0 ? 1 : -1;
            var points = new List<(int x, int y)>();
            int angle = 0, steps = 0;
            var turns = new List<(int angle, int leg)>();
            for (int k = 0; k < legs; k++) turns.Add((sense * (k % 2 == 0 ? 1 : -1), Pick(t["leg"])));
            int total = turns.Sum(l => Math.Max(1, l.leg / 10));
            points.Add(OnTheWinding((cx, cy), (bx, by), (start, end), 0, 0, 1));
            foreach (var (way, leg) in turns)
                for (int k = 0; k < Math.Max(1, leg / 10); k++)
                {
                    angle += way * 10;
                    steps++;
                    var (ox, oy) = Rotated(bx, by, angle);
                    int r = start + (end - start) * steps / total;
                    points.Add((cx + ox * r / Unit, cy + oy * r / Unit));
                }
            var top = points[points.Count - 1];
            var (tx, ty) = Toward(cx, cy, top.x, top.y);
            JObject lookout = null;
            if (t["lookout"] is JObject l)
            {
                int up = Pick(l["depth"]) / 2 + 2;
                HouseAt(t, (top.x + tx * up / Unit, top.y + ty * up / Unit), top, l, (string)l["what"] ?? "lookout", out lookout);
            }
            Way(lookout ?? village, points, (int?)t["width"] ?? 2, (bool?)t["graded"] ?? false);
        }

        // The point k of n along the winding: turned from the start's bearing, its distance from the middle eased from start
        // to end.
        static (int x, int y) OnTheWinding((int x, int y) centre, (int x, int y) bearing, (int start, int end) reach, int degrees, int k, int n)
        {
            var (ox, oy) = Rotated(bearing.x, bearing.y, degrees * k / n);
            int r = reach.start + (reach.end - reach.start) * k / n;
            return (centre.x + ox * r / Unit, centre.y + oy * r / Unit);
        }

        // Houses on an arc round the lake, back from its shore, facing the water, with a way along the arc between them
        // and the shore.
        void OnTheShore(JObject v, int x, int y)
        {
            var (lx, ly) = At(v["around"]);
            int shore = ShoreOf(lx, ly), houses = Math.Max(2, Pick(v["houses"])), span = (int?)v["span"] ?? 140;
            var (bx, by) = Toward(lx, ly, x, y);
            int wayRadius = shore + ((int?)v["way"] ?? 7);
            var way = new List<(int x, int y)>();
            JObject first = null;
            for (int k = 0; k < houses; k++)
            {
                int angle = -span / 2 + span * k / (houses - 1) + Pick(new JArray(-4, 4));
                var (ox, oy) = Rotated(bx, by, angle);
                int r = shore + Pick(v["setback"]);
                if (HouseAt(v, (lx + ox * r / Unit, ly + oy * r / Unit), (lx, ly), null, "house", out var house)) first ??= house;
            }
            for (int angle = -span / 2 - 6; angle <= span / 2 + 6; angle += 12)
            {
                var (ox, oy) = Rotated(bx, by, angle);
                way.Add((lx + ox * wayRadius / Unit, ly + oy * wayRadius / Unit));
            }
            Way(first, way, (int?)v["width"] ?? 2);
        }

        // ---------- the docks: boathouses on the lake's shore, each with a dock out over the water ----------

        void Docks(JObject d)
        {
            var (lx, ly) = At(d["lake"]);
            int count = Pick(d["count"]), spread = (int?)d["spread"] ?? 60;
            int shore = ShoreOf(lx, ly);
            for (int k = 0; k < count; k++)
            {
                int u = -spread / 2 + spread * (2 * k + 1) / (2 * count) + Pick(new JArray(-4, 4));
                u = Math.Max(-shore + 4, Math.Min(shore - 4, u));
                int v = (int)BiomeMath.Sqrt((long)shore * shore - (long)u * u) + 1;
                var (ox, oy) = Turned(u, v);
                Boathouse(d, k, lx, ly, lx + ox, ly + oy);
            }
        }

        // How far the lake's water reaches from its middle: where the hollow is deeper than the lake's level (Places' water,
        // 4 land units).
        int ShoreOf(int lx, int ly)
        {
            var lake = rows.Stamps.OfType<JObject>().FirstOrDefault(s => (int)s["at"][0] == lx && (int)s["at"][1] == ly);
            int radius = lake == null ? 40 : (int)lake["radius"], deep = lake == null ? 40 : -(int)lake["rise"];
            return deep <= 4 ? radius : (int)BiomeMath.Sqrt((long)radius * radius * (deep - 4) / deep);
        }

        // A box whose front faces the lake, its front wall on the shore cell, the rest of it on dry land.
        void Boathouse(JObject d, int k, int lx, int ly, int sx, int sy)
        {
            var houses = d["house"] as JArray;
            int w = Pick(houses?[0]), h = Pick(houses?[1]);
            int tx = lx - sx, ty = ly - sy;
            Side front;
            if (Math.Abs(ty) >= Math.Abs(tx)) front = ty < 0 ? Side.North : Side.South;
            else front = tx < 0 ? Side.West : Side.East;
            bool across = front == Side.North || front == Side.South;
            int bw = across ? w : h, bh = across ? h : w;
            int ax = sx - bw / 2, ay = sy - bh / 2;
            if (front == Side.North) ay = sy;
            else if (front == Side.South) ay = sy - bh + 1;
            else if (front == Side.West) ax = sx;
            else ax = sx - bw + 1;
            var box = BuildingRow.Box(bw, bh, front, "door", "window", new JObject { ["shape"] = "gable", ["pitch"] = new JArray(8, 12), ["overhang"] = 0.4, ["cover"] = "thatch" });
            string wall = FrontWall(front);
            int along = across ? bw : bh;
            var building = new JObject
            {
                ["id"] = $"{placeId}#{k + 1}",
                ["at"] = new JArray(ax, ay),
                ["style"] = "plain",
                ["fill"] = "hollow",
                ["seed"] = (int)(Roll(SaltSeed) % 100000),
                ["note"] = $"{rows.VillageName}'s boathouse {k + 1}, its dock out over {rows.Name}",
                ["wall"] = new JObject { ["material"] = (string)d["wall"] ?? "logwall" },
            };
            foreach (var p in box.Properties()) building[p.Name] = p.Value;
            building["parts"] = new JArray(new JObject { ["kind"] = "dock", ["on"] = wall, ["at"] = along, ["length"] = Pick(d["length"]), ["width"] = (int?)d["width"] ?? 4 });
            rows.Buildings.Add(building);
            docks.Add((ax, ay, bw, bh));
        }

        static string FrontWall(Side s) => s switch
        {
            Side.North => "north",
            Side.East => "east",
            Side.South => "south",
            _ => "west",
        };

        // ---------- houses: a box of a style at an offset, its door towards the landmark ----------

        void House(JObject h)
        {
            var (x, y) = At(h["at"]);
            int w = Pick(h["width"]), d = Pick(h["depth"]);
            int tx = rows.Centre.x - x, ty = rows.Centre.y - y;
            Side front;
            if (Math.Abs(ty) >= Math.Abs(tx)) front = ty < 0 ? Side.North : Side.South;
            else front = tx < 0 ? Side.West : Side.East;
            if (front == Side.East || front == Side.West) (w, d) = (d, w);
            var building = new JObject
            {
                ["id"] = $"{placeId}#{rows.Buildings.Count + 1}",
                ["at"] = new JArray(x - w / 2, y - d / 2),
                ["style"] = (string)h["style"] ?? "plain",
                ["seed"] = (int)(Roll(SaltSeed) % 100000),
                ["note"] = $"{rows.VillageName}'s {(string)h["what"] ?? "house"}, by {rows.Name}",
            };
            if (h["wall"] != null) building["wall"] = new JObject { ["material"] = (string)h["wall"] };
            foreach (var p in BuildingRow.Box(w, d, front, "door", "window", new JObject { ["shape"] = "gable", ["pitch"] = new JArray(6, 12), ["overhang"] = 0.3, ["cover"] = "slate" }).Properties()) building[p.Name] = p.Value;
            rows.Buildings.Add(building);
        }

        // ---------- things ----------

        void Things(JObject t)
        {
            int count = Pick(t["count"]);
            for (int k = 0; k < count; k++)
            {
                // A few tries for a cell clear of every row, and of the things already put down; none, and it isn't put.
                for (int tries = 0; tries < 12; tries++)
                {
                    var (x, y) = (string)t["near"] == "docks" && docks.Count > 0 ? ByADock() : OnARing(t);
                    if (!Clear(x, y)) continue;
                    things.Add((x, y, new JObject { ["kind"] = (string)t["kind"], ["rot"] = (int)(Roll(SaltDirection) % 360) }));
                    break;
                }
            }
        }

        // Off every row's block (the cells its joins span) and every thing put down.
        bool Clear(int x, int y)
        {
            if (things.Any(t => t.x == x && t.y == y)) return false;
            foreach (var b in rows.Buildings.OfType<JObject>())
            {
                int ax = (int)b["at"][0], ay = (int)b["at"][1];
                var js = ((JObject)b["joins"]).Properties().Select(p => ((int)p.Value[0], (int)p.Value[1])).ToList();
                int x0 = ax + (int)BiomeMath.FloorDiv(js.Min(j => j.Item1), 2), x1 = ax + (int)BiomeMath.FloorDiv(js.Max(j => j.Item1) - 1, 2);
                int y0 = ay + (int)BiomeMath.FloorDiv(js.Min(j => j.Item2), 2), y1 = ay + (int)BiomeMath.FloorDiv(js.Max(j => j.Item2) - 1, 2);
                if (x >= x0 && x <= x1 && y >= y0 && y <= y1 && Near(b, x, y)) return false;
            }
            return true;
        }

        // Inside a row's block: for one with a way or roots (a long road), only near a house's own corners or the road's points.
        static bool Near(JObject b, int x, int y)
        {
            if (b["roads"] == null) return true;
            int ax = (int)b["at"][0], ay = (int)b["at"][1];
            return ((JObject)b["joins"]).Properties().Any(p => Math.Abs(ax + (int)BiomeMath.FloorDiv((int)p.Value[0], 2) - x) <= 5 && Math.Abs(ay + (int)BiomeMath.FloorDiv((int)p.Value[1], 2) - y) <= 5)
                || (b["joins"]["ne"] != null && x >= ax - 1 && y >= ay - 1 && x <= ax + (int)b["joins"]["ne"][0] / 2 + 1 && y <= ay + (int)b["joins"]["se"][1] / 2 + 1);
        }

        (int x, int y) OnARing(JObject t)
        {
            var (cx, cy) = At(t["at"]);
            var ring = t["ring"] as JArray;
            int r = ring == null ? 12 : Pick(ring);
            var (ux, uy) = Direction();
            // "near": on the site's side of the ring, where the village is.
            if ((string)t["half"] == "near" && (long)ux * (siteX - cx) + (long)uy * (siteY - cy) < 0) (ux, uy) = (-ux, -uy);
            return (cx + ux * r / Unit, cy + uy * r / Unit);
        }

        // Just beside a boathouse, on a side away from the water.
        (int x, int y) ByADock()
        {
            var (ax, ay, w, h) = docks[(int)(Roll(SaltRange) % (uint)docks.Count)];
            int edge = (int)(Roll(SaltRange) % 2);
            return edge == 0 ? (ax - 1 - Pick(new JArray(0, 1)), ay + Pick(new JArray(0, h - 1))) : (ax + w + Pick(new JArray(0, 1)), ay + Pick(new JArray(0, h - 1)));
        }

        void ThingChunks()
        {
            foreach (var g in things.GroupBy(t => (cx: BiomeMath.FloorDiv(t.x, Chunks.Size), cy: BiomeMath.FloorDiv(t.y, Chunks.Size))).OrderBy(g => g.Key.cy).ThenBy(g => g.Key.cx))
            {
                var list = new JArray();
                foreach (var (x, y, thing) in g)
                {
                    thing["cell"] = new JArray((int)(x - g.Key.cx * Chunks.Size), (int)(y - g.Key.cy * Chunks.Size));
                    thing["note"] = $"by {rows.Name}";
                    list.Add(thing);
                }
                rows.Things.Add(new JObject { ["chunk"] = new JArray((int)g.Key.cx, (int)g.Key.cy), ["things"] = list });
            }
        }

        // ---------- the place ----------

        // A village laid as rows names its own place: the block its houses stand in.
        void VillagePlaceOf()
        {
            if (rows.Village != null) return;
            string id = Slug(rows.VillageName);
            var mine = rows.Buildings.OfType<JObject>().Where(b => ((string)b["id"]).StartsWith(id + "#", StringComparison.Ordinal)).ToList();
            if (mine.Count == 0) return;
            // Its houses' own cells and a cell round each, row by row: a shore's arc has its lake inside its block.
            var cells = new SortedDictionary<int, SortedSet<int>>();
            foreach (var b in mine)
            {
                int ax = (int)b["at"][0], ay = (int)b["at"][1], w = (int)b["joins"]["ne"][0] / 2, h = (int)b["joins"]["se"][1] / 2;
                foreach (var (x, y) in Cells(ax - 1, ay - 1, w + 2, h + 2))
                {
                    if (!cells.TryGetValue(y, out var xs)) cells[y] = xs = new SortedSet<int>();
                    xs.Add(x);
                }
            }
            var spans = new JArray();
            foreach (var line in cells)
            {
                var span = new JArray(line.Key);
                int start = line.Value.Min, last = start;
                foreach (int x in line.Value.Skip(1))
                {
                    if (x == last + 1)
                    {
                        last = x;
                        continue;
                    }
                    span.Add(start);
                    span.Add(last);
                    start = last = x;
                }
                span.Add(start);
                span.Add(last);
                spans.Add(span);
            }
            rows.VillagePlace = new JObject
            {
                ["id"] = id,
                ["name"] = rows.VillageName,
                ["kind"] = "village",
                ["from"] = $"{LandmarkIntent.Folder}/{intent}.json",
                ["note"] = string.Format(CultureInfo.InvariantCulture, "{0}'s houses, laid by {1} ({2})", rows.VillageName, intent, form),
                ["rows"] = spans,
            };
        }

        void Place()
        {
            rows.Place = new JObject
            {
                ["id"] = placeId,
                ["name"] = rows.Name,
                ["kind"] = "landmark",
                ["from"] = $"{LandmarkIntent.Folder}/{intent}.json",
                ["note"] = string.Format(CultureInfo.InvariantCulture, "{0}, {1}, by {2}", rows.Words, form, rows.VillageName),
                ["circle"] = new JObject { ["at"] = new JArray(rows.Centre.x, rows.Centre.y), ["radius"] = rows.Reach },
            };
        }
    }
}
