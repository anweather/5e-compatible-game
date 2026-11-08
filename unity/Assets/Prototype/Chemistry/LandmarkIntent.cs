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

        /// <summary>The rows of this intent in <paramref name="biome"/>, at a site (a world cell), from a seed.</summary>
        public LandmarkRows Make(string biome, int siteX, int siteY, int seed)
        {
            string form = FormFor(biome);
            return new LandmarkMaker(Id, form, (JObject)by[form], siteX, siteY, seed).Run();
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
        public JObject Village { get; internal set; }
        public JArray Buildings { get; } = new JArray();
        /// <summary>things.json's chunks: {chunk, things}, by chunk row then column.</summary>
        public JArray Things { get; } = new JArray();
        public JObject Place { get; internal set; }

        public string StampsJson => File("stamps", Stamps);
        public string VillagesJson => File("villages", new JArray(Village));
        public string BuildingsJson => File("buildings", Buildings);
        public string ThingsJson => File("chunks", Things);
        public string PlacesJson => File("places", new JArray(Place));

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

        readonly string intent, form;
        readonly JObject row;
        readonly int siteX, siteY, key, turn;
        readonly LandmarkRows rows;
        readonly List<(int x, int y, int w, int h)> docks = new List<(int, int, int, int)>();
        readonly List<(int x, int y, JObject thing)> things = new List<(int, int, JObject)>();
        int rolls;
        string placeId;

        public LandmarkMaker(string intent, string form, JObject row, int siteX, int siteY, int seed)
        {
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

        // A trunk of log wall (an octagon of walls under a round tower), a crown of tree towers round it, and roots of
        // fallen tree laid out from it as roads. Joins are half cells from the site's cell, (1, 1) its middle.
        void GreatTree(JObject t)
        {
            int trunk = Pick(t["trunk"]), crown = Pick(t["crown"]), lobes = Pick(t["lobes"]), roots = Pick(t["roots"]);
            var joins = new JObject { ["c"] = new JArray(1, 1) };
            var towers = new JObject();
            var start = Direction();
            for (int k = 0; k < lobes; k++)
            {
                var (dx, dy) = Around(start, k, lobes, crown * 2);
                joins[$"l{k}"] = new JArray(1 + dx, 1 + dy);
                towers[$"crown-{k}"] = new JObject { ["at"] = $"l{k}", ["radius"] = Pick(t["lobe"]), ["material"] = "tree" };
            }
            towers["trunk"] = new JObject { ["at"] = "c", ["radius"] = trunk * 2 };
            var roads = new JObject();
            for (int k = 0; k < roots; k++) Root(t, joins, roads, k, roots, trunk);
            int a = 2 * (trunk - 1), b = a * 29 / 70;
            var corners = new[] { (b, -a), (a, -b), (a, b), (b, a), (-b, a), (-a, b), (-a, -b), (-b, -a) };
            var walls = new JObject();
            for (int k = 0; k < corners.Length; k++)
            {
                joins[$"o{k}"] = new JArray(1 + corners[k].Item1, 1 + corners[k].Item2);
                walls[$"w{k}"] = new JObject { ["from"] = $"o{k}", ["to"] = $"o{(k + 1) % corners.Length}" };
            }
            rows.Buildings.Add(new JObject
            {
                ["id"] = $"{placeId}#1",
                ["at"] = new JArray(siteX, siteY),
                ["fill"] = "solid",
                ["seed"] = (int)(Roll(SaltSeed) % 100000),
                ["note"] = $"{rows.Name}: the great tree's stand-in, a trunk of log wall {(int?)t["height"] ?? 14} m high under a crown of trees, with roots",
                ["wall"] = new JObject { ["material"] = "logwall", ["height"] = (int?)t["height"] ?? 14, ["thickness"] = 0.45 },
                ["joins"] = joins,
                ["walls"] = walls,
                ["towers"] = towers,
                ["roads"] = roads,
            });
            rows.Reach = Math.Max(rows.Reach, crown + 8);
        }

        // The k-th of n points round a circle of this radius, starting from a seeded direction, with a little give.
        (int x, int y) Around((int x, int y) start, int k, int n, int radius)
        {
            var (vx, vy) = Rotated(start.x, start.y, k * 360 / n);
            int r = radius + Pick(new JArray(-radius / 6, radius / 6));
            return (vx * r / Unit, vy * r / Unit);
        }

        // A Unit vector turned by whole degrees, by a table of tenths of a turn and small steps, in integers.
        static (int x, int y) Rotated(int x, int y, int degrees)
        {
            // cos and sin of 0..350 in steps of 10, times Unit.
            int[] cos = { 1024, 1008, 962, 887, 784, 658, 512, 350, 178, 0, -178, -350, -512, -658, -784, -887, -962, -1008, -1024, -1008, -962, -887, -784, -658, -512, -350, -178, 0, 178, 350, 512, 658, 784, 887, 962, 1008 };
            int step = ((degrees % 360) + 360) % 360 / 10;
            int c = cos[step], s = cos[(step + 27) % 36];
            return ((x * c - y * s) / Unit, (x * s + y * c) / Unit);
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

        // ---------- the docks: boathouses on the lake's shore, each with a dock out over the water ----------

        void Docks(JObject d)
        {
            var (lx, ly) = At(d["lake"]);
            int count = Pick(d["count"]), spread = (int?)d["spread"] ?? 60;
            // The lake's water: where the hollow is deeper than the lake's level (Places' water, 4 land units).
            var lake = rows.Stamps.OfType<JObject>().FirstOrDefault(s => (int)s["at"][0] == lx && (int)s["at"][1] == ly);
            int radius = lake == null ? 40 : (int)lake["radius"], deep = lake == null ? 40 : -(int)lake["rise"];
            int shore = deep <= 4 ? radius : (int)BiomeMath.Sqrt((long)radius * radius * (deep - 4) / deep);
            for (int k = 0; k < count; k++)
            {
                int u = -spread / 2 + spread * (2 * k + 1) / (2 * count) + Pick(new JArray(-4, 4));
                u = Math.Max(-shore + 4, Math.Min(shore - 4, u));
                int v = (int)BiomeMath.Sqrt((long)shore * shore - (long)u * u) + 1;
                var (ox, oy) = Turned(u, v);
                Boathouse(d, k, lx, ly, lx + ox, ly + oy);
            }
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
                var (x, y) = (string)t["near"] == "docks" && docks.Count > 0 ? ByADock() : OnARing(t);
                things.Add((x, y, new JObject { ["kind"] = (string)t["kind"], ["rot"] = (int)(Roll(SaltDirection) % 360) }));
            }
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
