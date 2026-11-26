// A map made from a world file (docs/design/world-squares.md sections 6 and 9, phase 4): world/maps/<id>/map.json says
// the seed, the size in world squares, the generator's version and the squares pinned to a map folder (the valley), and
// the rest is solved (WorldPlan) and made a chunk at a time (ChunkGen). Its identity is a hash of its inputs, never its
// output: the world file, every committed file the plan and the chunks read, each pinned map's folder hash and the
// versions. Bake (GeneratedMap.Bake.cs) writes the whole map as a map folder the game already loads (cells.txt, land.json, things.json and the
// rows), with the pinned valley's own rows moved to where it lies in the map, and a "spawn" place for each square's
// spawn point (SpawnPlaces).
//
// Coordinates: the map's cells start at the north-west corner of its squares, so none is negative; the valley at the
// south-west of the nine is chunks (0..8, 18..26) and its rows move 1152 cells south.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class GeneratedMap
    {
        public const string Key = "generated";
        public const string BakeName = "bake.json";
        public const int BakeFormat = 2;
        public static readonly string[] Baked =
        {
            MapFile.CellsName, MapFile.LandName, "things.json", WorldPlaces.FileName, MapStamps.FileName, VillageLayout.FileName,
            WorldBuildings.FileName, "golf.json", CourseLayout.FileName, WorldPuzzles.FileName, "jobs.json", "rocks.json",
            "hold.json", "looks.json", BakeName,
        };

        public string Id { get; private set; }
        public string Seed { get; private set; }
        public int SquaresW { get; private set; }
        public int SquaresH { get; private set; }
        public int Generator { get; private set; }
        public PlanWrap Wrap { get; private set; }
        public bool Bakes { get; private set; }
        public string PinnedMap { get; private set; }
        public (int x, int y) PinnedAt { get; private set; }
        public WorldPlan Plan { get; private set; }
        public ChunkGen Gen { get; private set; }
        public string MapDir { get; private set; }
        public string WorldDir { get; private set; }
        public string PinnedDir => PinnedMap == null ? null : Path.Combine(WorldDir, "maps", PinnedMap);
        public HillsLand Hills => Gen?.Hills;
        public string PinnedName { get; private set; }
        public GeneratedMap Inner { get; private set; }

        FarLandmarks far;

        public FarLandmarks Far => far ??= FarLandmarks.Make(Gen, HorizonTable.Load(WorldDir));

        public KnownRing KnownRing(StreamingParams streaming) => new KnownRing(Gen, streaming);

        public static List<(string dir, int x, int y)> PinnedSquares(string mapDir)
        {
            var found = new List<(string dir, int x, int y)>();
            string path = Path.Combine(mapDir, MapFile.FileName);
            if (!File.Exists(path) || !(JObject.Parse(File.ReadAllText(path))[Key] is JObject o) || !(o["pinned"] is JArray pins)) return found;
            string worldDir = Path.GetFullPath(Path.Combine(mapDir, "..", ".."));
            foreach (var pin in pins.OfType<JObject>())
            {
                string dir = Path.Combine(worldDir, "maps", (string)pin["map"]);
                int x = (int)pin["square"][0] * WorldPlan.SquareCells, y = (int)pin["square"][1] * WorldPlan.SquareCells;
                found.Add((dir, x, y));
                foreach (var (inner, ix, iy) in PinnedSquares(dir)) found.Add((inner, x + ix, y + iy));
            }
            return found;
        }

        public static bool IsGenerated(string mapDir)
        {
            string path = Path.Combine(mapDir, MapFile.FileName);
            return File.Exists(path) && JObject.Parse(File.ReadAllText(path))[Key] is JObject;
        }

        public static GeneratedMap Load(string mapDir, string seed = null)
        {
            var o = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(mapDir, MapFile.FileName)))[Key]
                ?? throw new FormatException($"{MapFile.FileName}: no \"{Key}\"");
            var m = new GeneratedMap
            {
                Id = Path.GetFileName(Path.GetFullPath(mapDir).TrimEnd('/', '\\')),
                MapDir = mapDir,
                WorldDir = Path.GetFullPath(Path.Combine(mapDir, "..", "..")),
                Seed = seed ?? (string)o["seed"],
                SquaresW = (int)o["squares"][0],
                SquaresH = (int)o["squares"][1],
                Generator = (int)o["generator"],
                Wrap = WrapOf((string)o["wrap"]),
                Bakes = (bool?)o["bake"] ?? true,
            };
            if (m.Generator != ChunkGen.Version) throw new FormatException($"{MapFile.FileName}: made by generator {m.Generator}, and this is {ChunkGen.Version}");
            if (o["hills"] is JObject hills) return LoadHills(m, hills);
            int landRows = m.Wrap == PlanWrap.EastWest ? m.SquaresH - 2 : m.SquaresH;
            if (m.SquaresW < 1 || landRows < 1) throw new FormatException($"{MapFile.FileName}: squares {m.SquaresW} x {m.SquaresH} is too small{(m.Wrap == PlanWrap.EastWest ? " for an east-west world (its two wall rows)" : "")}");
            var pin = (JObject)((JArray)o["pinned"])[0];
            m.PinnedMap = (string)pin["map"];
            m.PinnedAt = ((int)pin["square"][0], (int)pin["square"][1]);
            var lib = SquareLibrary.Load(m.WorldDir);
            var valleyMap = MapFile.Load(m.PinnedDir);
            PinnedSquare pinned;
            PlanInputs inputs;
            if (IsGenerated(m.PinnedDir))
            {
                if (m.Wrap != PlanWrap.None) throw new FormatException($"{MapFile.FileName}: a world that wraps can't pin a generated map");
                m.Inner = Load(m.PinnedDir);
                var held = m.Inner.Plan;
                pinned = new PinnedSquare { Map = m.PinnedMap, W = m.Inner.SquaresW, H = m.Inner.SquaresH, Inner = m.Inner.Gen };
                inputs = PlanInputs.For(m.Seed, m.SquaresW, landRows, m.Wrap, held.Inputs.Valley);
                inputs.ValleySpawn = held.Inputs.ValleySpawn;
                inputs.Held = held;
                inputs.HeldAt = (m.PinnedAt.x + 1, m.PinnedAt.y);
            }
            else
            {
                pinned = ReadPinned(m.PinnedDir);
                var sides = ValleySides.Read(pinned.Land, WorldPlan.SquareCells, valleyMap.SeaLevel, 0.125, lib);
                inputs = PlanInputs.For(m.Seed, m.SquaresW, landRows, m.Wrap, sides);
                inputs.ValleySpawn = valleyMap.Spawn;
            }
            if (o["musts"] is JArray musts) inputs.Musts.AddRange(musts.Select(x => (string)x));
            if (o["towns"] != null) inputs.Towns = (int)o["towns"];
            inputs.Sketch = WorldSketch.Read(o["sketch"]);
            ReadForced(o["forced"] as JObject, inputs, lib, m);
            m.Plan = WorldPlan.Solve(inputs, lib);
            m.PinnedName = valleyMap.Name;
            pinned.Square = m.Inner != null ? m.Plan.Grid.Index(inputs.HeldAt.x, inputs.HeldAt.y) : m.Plan.Valley;
            int ox = m.Plan.Grid.X(pinned.Square) - m.PinnedAt.x, oy = m.Plan.Grid.Y(pinned.Square) - m.PinnedAt.y;
            if (m.Wrap != PlanWrap.None && (ox != 0 || oy != 0 || m.SquaresW != m.Plan.Grid.W || m.SquaresH != m.Plan.Grid.H))
                throw new FormatException($"{MapFile.FileName}: a world that wraps is its whole plan, so the valley's square is " +
                    $"[{m.Plan.Grid.X(m.Plan.Valley)}, {m.Plan.Grid.Y(m.Plan.Valley)}], not [{m.PinnedAt.x}, {m.PinnedAt.y}]");
            var biomes = BiomeTable.Load(Path.Combine(m.WorldDir, BiomeTable.FileName));
            m.Gen = new ChunkGen(m.Plan, biomes, pinned, ox, oy, m.SquaresW, m.SquaresH);
            return m;
        }

        static GeneratedMap LoadHills(GeneratedMap m, JObject hills)
        {
            if (m.Wrap == PlanWrap.EastWest) throw new FormatException($"{MapFile.FileName}: a hills map wraps both ways (\"torus\") or not at all, not \"ew\" (it has no wall rows)");
            if (m.SquaresW < 1 || m.SquaresH < 1) throw new FormatException($"{MapFile.FileName}: squares {m.SquaresW} x {m.SquaresH} is too small");
            int seed = (int)WorldPlan.Fnv("hills|" + m.Seed);
            int w = m.SquaresW * WorldPlan.SquareCells, h = m.SquaresH * WorldPlan.SquareCells;
            var biomes = BiomeTable.Load(Path.Combine(m.WorldDir, BiomeTable.FileName));
            m.Gen = new ChunkGen(HillsLand.Read(hills, seed, w, h), biomes, seed, m.SquaresW, m.SquaresH, m.Wrap == PlanWrap.Torus);
            m.PinnedName = MapFile.Load(m.MapDir).Name;
            return m;
        }

        public const string ForcedPinned = "pinned";

        static void ReadForced(JObject forced, PlanInputs inputs, SquareLibrary lib, GeneratedMap m)
        {
            if (forced == null) return;
            var legend = (JObject)forced["legend"] ?? throw new FormatException($"{MapFile.FileName}: \"forced\" needs a \"legend\"");
            var rows = ((JArray)forced["rows"] ?? throw new FormatException($"{MapFile.FileName}: \"forced\" needs \"rows\"")).Select(r => (string)r).ToList();
            if (rows.Count != m.SquaresH || rows.Any(r => r.Length != m.SquaresW))
                throw new FormatException($"{MapFile.FileName}: \"forced\" is {m.SquaresW} x {m.SquaresH} world squares, a character each");
            int ox = m.Wrap == PlanWrap.None ? 1 : 0;
            for (int y = 0; y < rows.Count; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    string key = rows[y][x].ToString();
                    string what = (string)legend[key] ?? throw new FormatException($"{MapFile.FileName}: \"forced\" has no legend for '{key}'");
                    bool inBlock = m.Inner != null && x >= m.PinnedAt.x && y >= m.PinnedAt.y && x < m.PinnedAt.x + m.Inner.SquaresW && y < m.PinnedAt.y + m.Inner.SquaresH;
                    if (what == ForcedPinned)
                    {
                        if (!inBlock) throw new FormatException($"{MapFile.FileName}: \"forced\" marks [{x}, {y}] pinned, outside the pinned map's squares");
                        continue;
                    }
                    if (inBlock) throw new FormatException($"{MapFile.FileName}: \"forced\" puts {what} at [{x}, {y}], one of the pinned map's squares");
                    string[] parts = what.Split('@');
                    string id = parts[0];
                    var t = lib.Templates.FirstOrDefault(q => q.Id == id);
                    if (id != ForcedSquare.Filler && t == null) throw new FormatException($"{MapFile.FileName}: \"forced\" names {id}, no square template");
                    int band = parts.Length > 1 ? Array.IndexOf(lib.BandIds, parts[1]) : t?.Bands[0] ?? -1;
                    if (band < 0 || band >= lib.Lands || (t != null && !t.Filler && Array.IndexOf(t.Bands, band) < 0))
                        throw new FormatException($"{MapFile.FileName}: \"forced\" asks {what}, a band {id} doesn't come in");
                    inputs.Forced[(x + ox, y)] = new ForcedSquare { Template = id, Band = band };
                }
        }

        static PlanWrap WrapOf(string wrap) => wrap switch
        {
            null => PlanWrap.None,
            "ew" => PlanWrap.EastWest,
            "torus" => PlanWrap.Torus,
            _ => throw new FormatException($"{MapFile.FileName}: wrap \"{wrap}\" isn't one of \"ew\" or \"torus\" (or none)"),
        };

        static PinnedSquare ReadPinned(string dir)
        {
            int n = WorldPlan.SquareCells;
            var p = new PinnedSquare
            {
                Map = Path.GetFileName(dir),
                Land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), n, n),
                Rows = MapFile.ReadCells(dir).Replace("\r", "").Trim('\n').Split('\n'),
            };
            if (p.Land.Length != n * n || p.Rows.Length != n || p.Rows.Any(r => r.Length != n))
                throw new FormatException($"{p.Map}: a pinned map is one world square, {n} x {n}");
            string things = Path.Combine(dir, "things.json");
            if (File.Exists(things))
                foreach (var list in ((JArray)JObject.Parse(File.ReadAllText(things))["chunks"]).OfType<JObject>())
                    p.Things[((int)list["chunk"][0], (int)list["chunk"][1])] = (JArray)list["things"];
            p.PlaceIds.AddRange(WorldPlaces.Load(dir).All.Select(q => q.Id));
            return p;
        }

        public static string InputHash(string mapDir)
        {
            string world = Path.GetFullPath(Path.Combine(mapDir, "..", ".."));
            var parts = new List<(string name, byte[] bytes)>
            {
                ("versions", Encoding.UTF8.GetBytes($"chunk-gen {ChunkGen.Version} world-plan {WorldPlan.Version}")),
                (MapFile.FileName, File.ReadAllBytes(Path.Combine(mapDir, MapFile.FileName))),
                ("bands.json", File.ReadAllBytes(Path.Combine(world, "bands.json"))),
                (BiomeTable.FileName, File.ReadAllBytes(Path.Combine(world, BiomeTable.FileName))),
            };
            foreach (string folder in new[] { SquareLibrary.Folder, LandmarkIntent.Folder })
                foreach (string f in Directory.GetFiles(Path.Combine(world, folder), "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
                    parts.Add((folder + "/" + Path.GetFileName(f), File.ReadAllBytes(f)));
            var o = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(mapDir, MapFile.FileName)))[Key];
            foreach (var pin in (o["pinned"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string id = (string)pin["map"];
                parts.Add(("maps/" + id, Encoding.UTF8.GetBytes(MapIdentity.Hash(Path.Combine(world, "maps", id)))));
            }
            ulong hash = 14695981039346656037;
            foreach (var (name, bytes) in parts)
                foreach (byte b in Encoding.UTF8.GetBytes(name).Concat(bytes)) hash = (hash ^ b) * 1099511628211;
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public GeneratedChunk[] MakeAll(int threads = 0)
        {
            int w = Gen.ChunksW, n = w * Gen.ChunksH;
            var all = new GeneratedChunk[n];
            var options = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
            Parallel.For(0, n, options, k => all[k] = Gen.Make(k % w, k / w));
            return all;
        }

        public static bool IsBaked(string mapDir)
        {
            string path = Path.Combine(mapDir, BakeName);
            if (!File.Exists(path)) return false;
            var note = JObject.Parse(File.ReadAllText(path));
            return (string)note["inputs"] == InputHash(mapDir) && (string)note["cells"] != CellsGenerated;
        }

        public const string CellsGenerated = "generated";

        public static IReadOnlyList<string> RowsBaked { get; } = Baked.Where(n => n != MapFile.CellsName && n != MapFile.LandName && n != "things.json").ToArray();

        public static bool IsRowsOnly(string dir)
        {
            string path = Path.Combine(dir, BakeName);
            return File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["cells"] == CellsGenerated;
        }

        public static bool EnsureRows(string mapDir, string outDir)
        {
            string path = Path.Combine(outDir, BakeName);
            if (File.Exists(path))
            {
                var note = JObject.Parse(File.ReadAllText(path));
                if ((string)note["inputs"] == InputHash(mapDir) && (int?)note["format"] == BakeFormat && (string)note["cells"] == CellsGenerated) return false;
            }
            Load(mapDir).WriteRowsOnly(outDir);
            return true;
        }

        public void WriteRowsOnly(string outDir)
        {
            if (Inner != null && !IsBaked(PinnedDir)) Inner.Bake(PinnedDir);
            Directory.CreateDirectory(outDir);
            foreach (string name in new[] { MapFile.CellsName, MapFile.LandName, "things.json" })
                if (File.Exists(Path.Combine(outDir, name))) File.Delete(Path.Combine(outDir, name));
            WriteRows(outDir);
            if (Path.GetFullPath(outDir).TrimEnd('/', '\\') != Path.GetFullPath(MapDir).TrimEnd('/', '\\'))
                Write(outDir, MapFile.FileName, OwnMapFile());
            Write(outDir, WorldPlaces.FileName, WorldPlaces.Generate(outDir, null));
            Write(outDir, BakeName, Json(new JObject
            {
                ["inputs"] = InputHash(MapDir),
                ["plan"] = (Plan?.Hash ?? 0u).ToString("x8", CultureInfo.InvariantCulture),
                ["generator"] = ChunkGen.Version,
                ["format"] = BakeFormat,
                ["cells"] = CellsGenerated,
            }));
        }

        public static bool EnsureBaked(string mapDir)
        {
            if (IsBaked(mapDir)) return false;
            var m = Load(mapDir);
            if (!m.Bakes) throw new InvalidOperationException(m.NoBake);
            m.Bake(mapDir);
            return true;
        }

        public static bool EnsureBakedInto(string mapDir, string outDir)
        {
            string path = Path.Combine(outDir, BakeName);
            if (File.Exists(path))
            {
                var note = JObject.Parse(File.ReadAllText(path));
                if ((string)note["inputs"] == InputHash(mapDir) && (int?)note["format"] == BakeFormat) return false;
            }
            Load(mapDir).Bake(outDir);
            return true;
        }

        string OwnMapFile()
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(MapDir, MapFile.FileName)));
            var (i0, j0, i1, j1) = MapFile.Load(MapDir).FullDetail;
            if (o["fullDetail"] == null && i1 >= i0) o["fullDetail"] = new JArray(i0, j0, i1, j1);
            return Json(o);
        }

        string NoBake => $"{Id} is {SquaresW} x {SquaresH} world squares, too big to bake whole (its map.json says \"bake\": false): its rows are laid out (EnsureRows) and its cells made as they're read (GeneratedSource)";

        static void Write(string dir, string name, string text) => File.WriteAllText(Path.Combine(dir, name), text);

        static string Json(JToken o) => o.ToString(Formatting.Indented) + "\n";

        JObject ReadPinned(string name, string list) => TryReadPinned(name, out var o) ? o : new JObject { [list] = new JArray() };

        bool TryReadPinned(string name, out JObject o)
        {
            string path = PinnedDir == null ? null : Path.Combine(PinnedDir, name);
            o = path != null && File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            return path != null && File.Exists(path);
        }

        public List<JObject> SpawnPlaces()
        {
            var list = new List<JObject>();
            if (Hills != null) return new List<JObject> { HillsSpawn() };
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (Inner != null && TryReadPinned(WorldPlaces.FileName, out var own))
                foreach (var p in ((JArray)own["places"]).OfType<JObject>().Where(p => (string)p["kind"] == "spawn")) names.Add((string)p["name"]);
            foreach (var s in Gen.Spawns)
            {
                if (s.Held) continue;
                int sx = s.X / ChunkGen.Square, sy = s.Y / ChunkGen.Square;
                string name = s.Pinned ? PinnedName : VillageIn(s.Square) ?? Gen.SquareTitle(s.Square);
                if (!names.Add(name))
                {
                    string first = name + " (" + Compass(sx, sy) + ")";
                    name = first;
                    int k = 1;
                    while (!names.Add(name)) name = first + " " + ++k;
                }
                var o = new JObject
                {
                    ["id"] = "spawn-" + WorldPlaces.Slug(name),
                    ["name"] = name,
                    ["kind"] = "spawn",
                    ["from"] = MapFile.FileName,
                    ["square"] = new JArray(sx, sy),
                    ["near"] = s.Near,
                    ["circle"] = new JObject { ["at"] = new JArray(s.X, s.Y), ["radius"] = ChunkGen.ClearCells },
                };
                if (s.Pinned) o["start"] = true;
                list.Add(o);
            }
            return list;
        }

        JObject HillsSpawn()
        {
            var spawn = MapFile.Load(MapDir).Spawn;
            return new JObject
            {
                ["id"] = "spawn-" + WorldPlaces.Slug(PinnedName),
                ["name"] = PinnedName,
                ["kind"] = "spawn",
                ["from"] = MapFile.FileName,
                ["square"] = new JArray(spawn.x / ChunkGen.Square, spawn.y / ChunkGen.Square),
                ["near"] = "hills",
                ["circle"] = new JObject { ["at"] = new JArray(spawn.x, spawn.y), ["radius"] = ChunkGen.ClearCells },
                ["start"] = true,
            };
        }

        string VillageIn(int square)
        {
            for (int k = 0; k < Gen.LandmarkRows.Count; k++)
                if (Gen.LandmarkRows[k]?.VillageName is string v && Plan.Landmarks[k].Square == square) return v;
            return null;
        }

        string Compass(int sx, int sy)
        {
            string ns = (sy * 3 / SquaresH) switch { 0 => "north", 2 => "south", _ => "" };
            string ew = (sx * 3 / SquaresW) switch { 0 => "west", 2 => "east", _ => "" };
            if (ns.Length > 0 && ew.Length > 0) return ns + "-" + ew;
            return ns.Length + ew.Length > 0 ? ns + ew : "middle";
        }

        void WriteRows(string outDir)
        {
            int dx = Gen.PinnedX, dy = Gen.PinnedY;
            var made = Gen.LandmarkRows.Where(r => r != null).ToList();
            void Pair(JToken t)
            {
                if (t is JArray a && a.Count >= 2)
                {
                    a[0] = (int)a[0] + dx;
                    a[1] = (int)a[1] + dy;
                }
            }
            void Box(JToken t)
            {
                if (t is JArray a && a.Count == 4)
                {
                    a[0] = (int)a[0] + dx;
                    a[1] = (int)a[1] + dy;
                    a[2] = (int)a[2] + dx;
                    a[3] = (int)a[3] + dy;
                }
            }
            void Each(JToken list, Action<JObject> f)
            {
                if (list is JArray a) foreach (var o in a.OfType<JObject>()) f(o);
            }

            var stamps = ReadPinned(MapStamps.FileName, "stamps");
            Each(stamps["stamps"], s => Pair(s["at"]));
            Write(outDir, MapStamps.FileName, Json(stamps));

            var villages = ReadPinned(VillageLayout.FileName, "villages");
            Each(villages["villages"], v => Pair(v["at"]));
            foreach (var r in made.Where(r => r.Village != null)) ((JArray)villages["villages"]).Add(r.Village.DeepClone());
            Write(outDir, VillageLayout.FileName, Json(villages));

            var buildings = ReadPinned(WorldBuildings.FileName, "buildings");
            Each(buildings["buildings"], b => Pair(b["at"]));
            foreach (var r in made) foreach (var b in r.Buildings) ((JArray)buildings["buildings"]).Add(b.DeepClone());
            Write(outDir, WorldBuildings.FileName, Json(buildings));

            if (TryReadPinned("golf.json", out var golf))
            {
                Pair(golf["tee"]);
                Pair(golf["hole"]);
                Write(outDir, "golf.json", Json(golf));
            }
            if (TryReadPinned(CourseLayout.FileName, out var course))
            {
                foreach (string k in new[] { "area", "bell", "gate" }) Box(course[k]);
                Each(course["booths"], b =>
                {
                    Box(b["area"]);
                    Pair(b["checkpoint"]);
                });
                Write(outDir, CourseLayout.FileName, Json(course));
            }
            if (TryReadPinned(WorldPuzzles.FileName, out var puzzles))
            {
                Each(puzzles["puzzles"], p => Pair(p["at"]));
                Write(outDir, WorldPuzzles.FileName, Json(puzzles));
            }
            if (TryReadPinned("jobs.json", out var jobs))
            {
                if (jobs["spots"] is JObject spots)
                    foreach (var s in spots.Properties().Select(p => p.Value).OfType<JObject>())
                    {
                        Pair(s["at"]);
                        Pair(s["out"]);
                    }
                Write(outDir, "jobs.json", Json(jobs));
            }
            if (TryReadPinned("rocks.json", out var rocks))
            {
                if (rocks["rocks"] is JArray list) foreach (var r in list) Pair(r);
                Write(outDir, "rocks.json", Json(rocks));
            }
            foreach (string same in new[] { "hold.json", "looks.json" })
                if (TryReadPinned(same, out var o)) Write(outDir, same, Json(o));

            var places = ReadPinned(WorldPlaces.FileName, "places");
            var kept = new JArray();
            foreach (var p in ((JArray)places["places"]).OfType<JObject>())
            {
                if ((bool?)p["generated"] == true) continue;
                Box(p["area"]);
                if (p["circle"] is JObject circle) Pair(circle["at"]);
                if (p["line"] is JArray line) foreach (var pt in line) Pair(pt);
                if (p["rows"] is JArray spans)
                    foreach (var span in spans.OfType<JArray>())
                    {
                        span[0] = (int)span[0] + dy;
                        for (int k = 1; k < span.Count; k++) span[k] = (int)span[k] + dx;
                    }
                kept.Add(p);
            }
            foreach (var r in made)
            {
                if (r.Place != null) kept.Add(r.Place.DeepClone());
                if (r.VillagePlace != null) kept.Add(r.VillagePlace.DeepClone());
            }
            foreach (var s in SpawnPlaces()) kept.Add(s);
            places["places"] = kept;
            Write(outDir, WorldPlaces.FileName, Json(places));
        }
    }

    public sealed class BakeReport
    {
        public double MakeMs, TotalMs;
        public int W, H;
    }
}
