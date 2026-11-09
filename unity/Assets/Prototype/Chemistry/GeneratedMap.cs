// A map made from a world file (docs/design/world-squares.md sections 6 and 9, phase 4): world/maps/<id>/map.json says
// the seed, the size in world squares, the generator's version and the squares pinned to a map folder (the valley), and
// the rest is solved (WorldPlan) and made a chunk at a time (ChunkGen). Its identity is a hash of its inputs, never its
// output: the world file, every committed file the plan and the chunks read, each pinned map's folder hash and the
// versions. Bake writes the whole map as a map folder the game already loads (cells.txt, land.json, things.json and the
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
    public sealed class GeneratedMap
    {
        /// <summary>map.json's key for a map made from a world file.</summary>
        public const string Key = "generated";
        /// <summary>A bake's own note of what it was made from, beside the files it wrote.</summary>
        public const string BakeName = "bake.json";
        /// <summary>The files a bake writes; a generated map's folder commits only map.json.</summary>
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
        /// <summary>The pinned map's id and its square in the map (squares from the north-west).</summary>
        public string PinnedMap { get; private set; }
        public (int x, int y) PinnedAt { get; private set; }
        public WorldPlan Plan { get; private set; }
        public ChunkGen Gen { get; private set; }
        /// <summary>The map folder (world/maps/id) and the game's world folder above it.</summary>
        public string MapDir { get; private set; }
        public string WorldDir { get; private set; }
        public string PinnedDir => Path.Combine(WorldDir, "maps", PinnedMap);
        /// <summary>The pinned map's name (its map.json's), what its spawn is called.</summary>
        public string PinnedName { get; private set; }

        FarLandmarks far;

        /// <summary>The plan's landmarks that show from far off (world/horizon.json), for the view's far ring.</summary>
        public FarLandmarks Far => far ??= FarLandmarks.Make(Gen, HorizonTable.Load(WorldDir));

        /// <summary>A known ring on this map, sized by the rules' streaming.sight and streaming.know.</summary>
        public KnownRing KnownRing(StreamingParams streaming) => new KnownRing(Gen, streaming);

        /// <summary>
        /// The maps a world file pins, without solving its plan: each one's folder (world/maps/id) and the map cell its
        /// north-west cell lands on (its square times WorldPlan.SquareCells, as ChunkGen.PinnedX and PinnedY). Empty for a
        /// map that isn't generated.
        /// </summary>
        public static List<(string dir, int x, int y)> PinnedSquares(string mapDir)
        {
            var found = new List<(string dir, int x, int y)>();
            string path = Path.Combine(mapDir, MapFile.FileName);
            if (!File.Exists(path) || !(JObject.Parse(File.ReadAllText(path))[Key] is JObject o) || !(o["pinned"] is JArray pins)) return found;
            string worldDir = Path.GetFullPath(Path.Combine(mapDir, "..", ".."));
            foreach (var pin in pins.OfType<JObject>())
                found.Add((Path.Combine(worldDir, "maps", (string)pin["map"]), (int)pin["square"][0] * WorldPlan.SquareCells, (int)pin["square"][1] * WorldPlan.SquareCells));
            return found;
        }

        /// <summary>Whether the map in this folder is made from a world file.</summary>
        public static bool IsGenerated(string mapDir)
        {
            string path = Path.Combine(mapDir, MapFile.FileName);
            return File.Exists(path) && JObject.Parse(File.ReadAllText(path))[Key] is JObject;
        }

        /// <summary>
        /// The world file in a map folder, its plan solved and its generator ready; throws if it can't be. A
        /// <paramref name="seed"/> makes the same map from another seed (for tests and pictures: its InputHash is the file's).
        /// </summary>
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
            };
            if (m.Generator != ChunkGen.Version) throw new FormatException($"{MapFile.FileName}: made by generator {m.Generator}, and this is {ChunkGen.Version}");
            if (m.SquaresW != m.SquaresH) throw new FormatException($"{MapFile.FileName}: a world plan is square for now");
            var pin = (JObject)((JArray)o["pinned"])[0];
            m.PinnedMap = (string)pin["map"];
            m.PinnedAt = ((int)pin["square"][0], (int)pin["square"][1]);
            var lib = SquareLibrary.Load(m.WorldDir);
            var pinned = ReadPinned(m.PinnedDir);
            var valleyMap = MapFile.Load(m.PinnedDir);
            var sides = ValleySides.Read(pinned.Land, WorldPlan.SquareCells, valleyMap.SeaLevel, 0.125, lib);
            var inputs = PlanInputs.For(m.Seed, m.SquaresW, PlanWrap.None, sides);
            inputs.ValleySpawn = valleyMap.Spawn;
            if (o["musts"] is JArray musts) inputs.Musts.AddRange(musts.Select(x => (string)x));
            m.Plan = WorldPlan.Solve(inputs, lib);
            m.PinnedName = valleyMap.Name;
            pinned.Square = m.Plan.Valley;
            int ox = m.Plan.Grid.X(m.Plan.Valley) - m.PinnedAt.x, oy = m.Plan.Grid.Y(m.Plan.Valley) - m.PinnedAt.y;
            var biomes = BiomeTable.Load(Path.Combine(m.WorldDir, BiomeTable.FileName));
            m.Gen = new ChunkGen(m.Plan, biomes, pinned, ox, oy, m.SquaresW, m.SquaresH);
            return m;
        }

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
            return p;
        }

        // ---------- identity ----------

        /// <summary>
        /// The map's hash from what it's made of: its world file, the committed files the plan and the chunks read (bands,
        /// biomes, square templates, landmark intents), each pinned map's folder hash and the generator's and the plan's versions.
        /// </summary>
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
            foreach (var pin in ((JArray)o["pinned"]).OfType<JObject>())
            {
                string id = (string)pin["map"];
                parts.Add(("maps/" + id, Encoding.UTF8.GetBytes(MapIdentity.Hash(Path.Combine(world, "maps", id)))));
            }
            ulong hash = 14695981039346656037;
            foreach (var (name, bytes) in parts)
                foreach (byte b in Encoding.UTF8.GetBytes(name).Concat(bytes)) hash = (hash ^ b) * 1099511628211;
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        // ---------- making it ----------

        /// <summary>Every chunk, made on <paramref name="threads"/> threads at once (any order gives the same chunks), row-major.</summary>
        public GeneratedChunk[] MakeAll(int threads = 0)
        {
            int w = Gen.ChunksW, n = w * Gen.ChunksH;
            var all = new GeneratedChunk[n];
            var options = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
            Parallel.For(0, n, options, k => all[k] = Gen.Make(k % w, k / w));
            return all;
        }

        /// <summary>Whether the folder holds a bake of these inputs.</summary>
        public static bool IsBaked(string mapDir)
        {
            string path = Path.Combine(mapDir, BakeName);
            return File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["inputs"] == InputHash(mapDir);
        }

        /// <summary>Bakes the map into its own folder unless it already holds a bake of the same inputs; whether it baked.</summary>
        public static bool EnsureBaked(string mapDir)
        {
            if (IsBaked(mapDir)) return false;
            Load(mapDir).Bake(mapDir);
            return true;
        }

        /// <summary>
        /// Writes the whole map as a map folder into <paramref name="outDir"/> (its map.json too, unless it's the map's own
        /// folder): the chunks' land and cells, the pinned valley's in its place, every chunk's things, and the valley's
        /// rows moved to the valley's place with the landmarks' rows added.
        /// </summary>
        public BakeReport Bake(string outDir, GeneratedChunk[] chunks = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            chunks ??= MakeAll();
            var report = new BakeReport { MakeMs = timer.Elapsed.TotalMilliseconds };
            Directory.CreateDirectory(outDir);
            int w = Gen.CellsW, h = Gen.CellsH, size = ChunkGen.Size;
            var land = new int[w * h];
            var cells = new char[w * h];
            var things = new JArray();
            foreach (var c in chunks)
            {
                for (int j = 0; j < size; j++)
                {
                    Array.Copy(c.Land, j * size, land, (c.Y * size + j) * w + c.X * size, size);
                    Array.Copy(c.Cells, j * size, cells, (c.Y * size + j) * w + c.X * size, size);
                }
                if (c.Things.Count > 0) things.Add(new JObject { ["chunk"] = new JArray(c.X, c.Y), ["things"] = c.Things });
            }
            var rows = new StringBuilder(w * h + h);
            for (int y = 0; y < h; y++)
            {
                if (y > 0) rows.Append('\n');
                rows.Append(cells, y * w, w);
            }
            MapFile.WriteCells(outDir, rows.ToString());
            Write(outDir, MapFile.LandName, LandFile.Write(land, w, h, 0.125));
            Write(outDir, "things.json", Json(new JObject { ["about"] = $"Made by ChunkGen {ChunkGen.Version} from {MapFile.FileName}: the valley's things in its chunks, then each chunk's landmarks' and seeded things.", ["chunks"] = things }));
            WriteRows(outDir);
            if (Path.GetFullPath(outDir).TrimEnd('/', '\\') != Path.GetFullPath(MapDir).TrimEnd('/', '\\'))
                File.Copy(Path.Combine(MapDir, MapFile.FileName), Path.Combine(outDir, MapFile.FileName), true);
            Write(outDir, WorldPlaces.FileName, WorldPlaces.Generate(outDir));
            Write(outDir, BakeName, Json(new JObject { ["inputs"] = InputHash(MapDir), ["plan"] = Plan.Hash.ToString("x8", CultureInfo.InvariantCulture), ["generator"] = ChunkGen.Version }));
            report.TotalMs = timer.Elapsed.TotalMilliseconds;
            report.Land = land;
            report.W = w;
            report.H = h;
            return report;
        }

        static void Write(string dir, string name, string text) => File.WriteAllText(Path.Combine(dir, name), text);

        static string Json(JToken o) => o.ToString(Formatting.Indented) + "\n";

        // The pinned map's file, or a new one holding an empty list under <paramref name="list"/>.
        JObject ReadPinned(string name, string list) => TryReadPinned(name, out var o) ? o : new JObject { [list] = new JArray() };

        bool TryReadPinned(string name, out JObject o)
        {
            string path = Path.Combine(PinnedDir, name);
            o = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            return File.Exists(path);
        }

        /// <summary>
        /// The map's spawn places, one per square in the plan's order: kind "spawn", a circle of the spawn's clearing round
        /// its cell, the map square it's in ("square", from the north-west) and what it stands near. Each is named for its
        /// square: the pinned map's name for the valley (whose is the map's own spawn, "start"), the village's name where
        /// one was made, else the square's template (a filler's biome), with a compass word where two names would meet.
        /// </summary>
        public List<JObject> SpawnPlaces()
        {
            var list = new List<JObject>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in Gen.Spawns)
            {
                int sx = s.X / ChunkGen.Square, sy = s.Y / ChunkGen.Square;
                string name = s.Pinned ? PinnedName : VillageIn(s.Square) ?? SquareTitle(s.Square);
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

        string VillageIn(int square)
        {
            for (int k = 0; k < Gen.LandmarkRows.Count; k++)
                if (Gen.LandmarkRows[k]?.VillageName is string v && Plan.Landmarks[k].Square == square) return v;
            return null;
        }

        string SquareTitle(int square)
        {
            var t = Plan.TemplateAt(square);
            string words = (t.Filler ? t.Biome : t.Id) ?? "land";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.Replace('-', ' '));
        }

        // Where a map square lies in the map, as a compass word ("north-west", "middle", "east").
        string Compass(int sx, int sy)
        {
            string ns = (sy * 3 / SquaresH) switch { 0 => "north", 2 => "south", _ => "" };
            string ew = (sx * 3 / SquaresW) switch { 0 => "west", 2 => "east", _ => "" };
            if (ns.Length > 0 && ew.Length > 0) return ns + "-" + ew;
            return ns.Length + ew.Length > 0 ? ns + ew : "middle";
        }

        // The valley's rows moved to its place in the map, and the landmarks' rows after them.
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

            // The hand-written places moved; the generated ones are made again from the folder (WorldPlaces.Generate).
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

    /// <summary>What a bake took, and the land it wrote (for pictures).</summary>
    public sealed class BakeReport
    {
        public double MakeMs, TotalMs;
        public int[] Land;
        public int W, H;
    }
}
