// A generated map's bake (docs/design/world-squares.md section 9): the whole map written as a map folder a row of chunks
// at a time, each row dropped once it's written, so a bake holds two rows of chunks whatever the map's size (J-086: costs
// follow what's near players, never the map's size; bake-lean). A row's chunks are made on every thread at once while the
// row before is written on one, in order, so the files are the same bytes on any number of threads, and the same bytes as
// when the bake held the whole map and wrote it at the end (BakeTests). Also the folder of a map's rows alone
// (EnsureRows), for a map too big to bake whole.
//
// It was GeneratedMap's until the land became its own assembly (docs/design/chem-land-split.md, way 1): a bake writes
// places.json from Play's files (CourseLayout, GolfCommand, WorldPuzzles: GeneratedPlaces), so it lives above the land.
// Bake, WriteRowsOnly and SpawnPlaces are extensions, so map.Bake(dir) reads as it did.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>Writes a generated map (GeneratedMap) as a map folder: whole (Bake), or its rows alone (EnsureRows).</summary>
    public static class MapBake
    {
        /// <summary>
        /// Writes the whole map as a map folder into <paramref name="outDir"/> (its map.json too, unless it's the map's own
        /// folder): the chunks' land and cells, the pinned valley's in its place, every chunk's things, and the valley's
        /// rows moved to the valley's place with the landmarks' rows added. Its chunks are made as it writes, on
        /// <paramref name="threads"/> threads (all of them for 0), or taken from <paramref name="chunks"/> (MakeAll's).
        /// </summary>
        public static BakeReport Bake(this GeneratedMap m, string outDir, GeneratedChunk[] chunks = null, int threads = 0)
        {
            if (!m.Bakes) throw new InvalidOperationException(NoBake(m));
            var timer = Stopwatch.StartNew();
            // A pinned block's rows are read from its own bake, as a pinned map's are from its folder.
            if (m.Inner != null && !GeneratedMap.IsBaked(m.PinnedDir)) m.Inner.Bake(m.PinnedDir);
            int w = m.Gen.ChunksW, h = m.Gen.ChunksH;
            if (chunks != null && chunks.Length != w * h) throw new ArgumentException($"a bake of {w} x {h} chunks, given {chunks.Length}", nameof(chunks));
            Directory.CreateDirectory(outDir);
            var report = new BakeReport { W = m.Gen.CellsW, H = m.Gen.CellsH };
            // The rows first: the places.json they write says which water bodies the land is read for as it goes.
            WriteRows(m, outDir);
            var water = WaterRuns.For(outDir, m.Gen.CellsW, m.Gen.CellsH);
            var options = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
            using (var files = new ChunkRows(outDir, m.Gen.CellsW, m.Gen.CellsH, water))
            {
                var writing = Task.CompletedTask;
                try
                {
                    for (int cy = 0; cy < h; cy++)
                    {
                        var row = new GeneratedChunk[w];
                        var making = Stopwatch.StartNew();
                        if (chunks != null) Array.Copy(chunks, cy * w, row, 0, w);
                        else
                        {
                            int y = cy;
                            Parallel.For(0, w, options, cx => row[cx] = m.Gen.Make(cx, y));
                        }
                        report.MakeMs += making.Elapsed.TotalMilliseconds;
                        writing.GetAwaiter().GetResult();
                        writing = Task.Run(() => files.Add(row));
                    }
                    writing.GetAwaiter().GetResult();
                }
                finally
                {
                    // A row still being written when making one threw: let it finish before its files close.
                    if (!writing.IsCompleted) writing.ContinueWith(_ => { }, TaskScheduler.Default).Wait();
                }
                files.Finish();
            }
            if (Path.GetFullPath(outDir).TrimEnd('/', '\\') != Path.GetFullPath(m.MapDir).TrimEnd('/', '\\'))
                Write(outDir, MapFile.FileName, OwnMapFile(m));
            Write(outDir, WorldPlaces.FileName, GeneratedPlaces.Generate(outDir, water));
            Write(outDir, GeneratedMap.BakeName, Json(new JObject { ["inputs"] = GeneratedMap.InputHash(m.MapDir), ["plan"] = m.Plan.Hash.ToString("x8", CultureInfo.InvariantCulture), ["generator"] = ChunkGen.Version, ["format"] = GeneratedMap.BakeFormat }));
            report.TotalMs = timer.Elapsed.TotalMilliseconds;
            return report;
        }

        // cells.txt, land.json and things.json, written a row of chunks at a time from the north: what the bake once
        // gathered whole (a char and an int a cell of the map, and every chunk's things) and wrote at the end, the same
        // bytes. Its two row buffers are a row of chunks' cells and land.
        sealed class ChunkRows : IDisposable
        {
            const int Size = ChunkGen.Size;
            readonly int w, h;
            readonly char[] cells;
            readonly int[] land;
            readonly string cellsPath, cellsTemp;
            readonly StreamWriter cellsOut, thingsOut;
            readonly FileStream landOut;
            readonly LandFile.Writer landRows;
            readonly JsonTextWriter things;
            readonly WaterRuns water;
            int y;
            bool finished;

            public ChunkRows(string dir, int w, int h, WaterRuns water)
            {
                this.w = w;
                this.h = h;
                this.water = water;
                cells = new char[w * Size];
                land = new int[w * Size];
                var utf8 = new UTF8Encoding(false);
                // cells.txt is put in place only if it changed (MapFile.WriteCells), so it's written beside it first.
                cellsPath = Path.Combine(dir, MapFile.CellsName);
                cellsTemp = cellsPath + ".baking";
                cellsOut = new StreamWriter(cellsTemp, false, utf8, 1 << 16);
                landOut = new FileStream(Path.Combine(dir, MapFile.LandName), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                landRows = new LandFile.Writer(landOut, w, h, 0.125);
                // As JObject.ToString(Formatting.Indented) wrote the whole file: the same writer, a chunk at a time.
                thingsOut = new StreamWriter(Path.Combine(dir, "things.json"), false, utf8, 1 << 16);
                things = new JsonTextWriter(thingsOut) { Formatting = Formatting.Indented };
                things.WriteStartObject();
                things.WritePropertyName("about");
                things.WriteValue($"Made by ChunkGen {ChunkGen.Version} from {MapFile.FileName}: the valley's things in its chunks, then each chunk's landmarks' and seeded things.");
                things.WritePropertyName("chunks");
                things.WriteStartArray();
            }

            /// <summary>The next row of chunks, west to east.</summary>
            public void Add(GeneratedChunk[] row)
            {
                foreach (var c in row)
                    for (int j = 0; j < Size; j++)
                    {
                        Array.Copy(c.Land, j * Size, land, j * w + c.X * Size, Size);
                        Array.Copy(c.Cells, j * Size, cells, j * w + c.X * Size, Size);
                    }
                for (int j = 0; j < Size; j++, y++)
                {
                    if (y > 0) cellsOut.Write('\n');
                    cellsOut.Write(cells, j * w, w);
                    var landRow = new ReadOnlySpan<int>(land, j * w, w);
                    landRows.Row(landRow);
                    water?.Row(landRow);
                }
                foreach (var c in row)
                    if (c.Things.Count > 0) new JObject { ["chunk"] = new JArray(c.X, c.Y), ["things"] = c.Things }.WriteTo(things);
            }

            /// <summary>Ends the three files, every row written.</summary>
            public void Finish()
            {
                if (y != h) throw new InvalidOperationException($"a bake wrote {y} of its {h} rows");
                things.WriteEndArray();
                things.WriteEndObject();
                things.Flush();
                thingsOut.Write('\n');
                landRows.Finish();
                finished = true;
                Dispose();
                // As MapFile.WriteCells: the file is left as it is when it holds these cells already.
                if (File.Exists(cellsPath) && Same(cellsPath, cellsTemp)) File.Delete(cellsTemp);
                else
                {
                    if (File.Exists(cellsPath)) File.Delete(cellsPath);
                    File.Move(cellsTemp, cellsPath);
                }
            }

            public void Dispose()
            {
                cellsOut.Dispose();
                ((IDisposable)things).Dispose();
                landRows.Dispose();
                landOut.Dispose();
                if (!finished && File.Exists(cellsTemp)) File.Delete(cellsTemp);
            }

            static bool Same(string a, string b)
            {
                using var fa = File.OpenRead(a);
                using var fb = File.OpenRead(b);
                if (fa.Length != fb.Length) return false;
                var ba = new byte[1 << 16];
                var bb = new byte[1 << 16];
                int n;
                while ((n = fa.Read(ba, 0, ba.Length)) > 0)
                {
                    int got = 0;
                    while (got < n)
                    {
                        int r = fb.Read(bb, got, n - got);
                        if (r <= 0) return false;
                        got += r;
                    }
                    if (!ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n))) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Writes a map's rows into <paramref name="outDir"/> unless it holds this map's already: everything a bake writes
        /// but its cells, land and things (cells.txt, land.json, things.json), which play makes a chunk at a time from the
        /// world file (GeneratedSource), so a map too big to bake whole ("bake": false) can be played. Its map.json too,
        /// unless it's the map's own folder; places.json without the water bodies, which only the whole map's land finds.
        /// Whether it wrote them.
        /// </summary>
        public static bool EnsureRows(string mapDir, string outDir)
        {
            string path = Path.Combine(outDir, GeneratedMap.BakeName);
            if (File.Exists(path))
            {
                var note = JObject.Parse(File.ReadAllText(path));
                if ((string)note["inputs"] == GeneratedMap.InputHash(mapDir) && (int?)note["format"] == GeneratedMap.BakeFormat && (string)note["cells"] == GeneratedMap.CellsGenerated) return false;
            }
            GeneratedMap.Load(mapDir).WriteRowsOnly(outDir);
            return true;
        }

        /// <summary>EnsureRows's writing, made already: a pinned block's own map is baked first, as Bake does.</summary>
        public static void WriteRowsOnly(this GeneratedMap m, string outDir)
        {
            if (m.Inner != null && !GeneratedMap.IsBaked(m.PinnedDir)) m.Inner.Bake(m.PinnedDir);
            Directory.CreateDirectory(outDir);
            foreach (string name in new[] { MapFile.CellsName, MapFile.LandName, "things.json" })
                if (File.Exists(Path.Combine(outDir, name))) File.Delete(Path.Combine(outDir, name));
            WriteRows(m, outDir);
            if (Path.GetFullPath(outDir).TrimEnd('/', '\\') != Path.GetFullPath(m.MapDir).TrimEnd('/', '\\'))
                Write(outDir, MapFile.FileName, OwnMapFile(m));
            Write(outDir, WorldPlaces.FileName, GeneratedPlaces.Generate(outDir, null));
            Write(outDir, GeneratedMap.BakeName, Json(new JObject
            {
                ["inputs"] = GeneratedMap.InputHash(m.MapDir),
                ["plan"] = (m.Plan?.Hash ?? 0u).ToString("x8", CultureInfo.InvariantCulture),
                ["generator"] = ChunkGen.Version,
                ["format"] = GeneratedMap.BakeFormat,
                ["cells"] = GeneratedMap.CellsGenerated,
            }));
        }

        /// <summary>Bakes the map into its own folder unless it already holds a bake of the same inputs; whether it baked.</summary>
        public static bool EnsureBaked(string mapDir)
        {
            if (GeneratedMap.IsBaked(mapDir)) return false;
            var m = GeneratedMap.Load(mapDir);
            if (!m.Bakes) throw new InvalidOperationException(NoBake(m));
            m.Bake(mapDir);
            return true;
        }

        /// <summary>
        /// Bakes the map into <paramref name="outDir"/>, a folder away from the map's own (a build bakes into the player's
        /// data), unless it already holds a bake of the same inputs in this format; whether it baked.
        /// </summary>
        public static bool EnsureBakedInto(string mapDir, string outDir)
        {
            string path = Path.Combine(outDir, GeneratedMap.BakeName);
            if (File.Exists(path))
            {
                var note = JObject.Parse(File.ReadAllText(path));
                if ((string)note["inputs"] == GeneratedMap.InputHash(mapDir) && (int?)note["format"] == GeneratedMap.BakeFormat) return false;
            }
            GeneratedMap.Load(mapDir).Bake(outDir);
            return true;
        }

        // The map's map.json for a bake away from its folder. The pinned maps are found from the map's own folder
        // (PinnedSquares: ../../maps/<id>) and can't be from there, so what map.json takes from them is written in: the
        // chunks drawn in full detail (MapFile.FullDetailOf). Without it the bake's layout had none, the pinned ground laid
        // into it (PinnedGround) kept its own 257 samples a chunk, and a build's world was never made (#183).
        static string OwnMapFile(GeneratedMap m)
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(m.MapDir, MapFile.FileName)));
            var (i0, j0, i1, j1) = MapFile.Load(m.MapDir).FullDetail;
            if (o["fullDetail"] == null && i1 >= i0) o["fullDetail"] = new JArray(i0, j0, i1, j1);
            return Json(o);
        }

        static string NoBake(GeneratedMap m) => $"{m.Id} is {m.SquaresW} x {m.SquaresH} world squares, too big to bake whole (its map.json says \"bake\": false): its rows are laid out (EnsureRows) and its cells made as they're read (GeneratedSource)";

        static void Write(string dir, string name, string text) => File.WriteAllText(Path.Combine(dir, name), text);

        static string Json(JToken o) => o.ToString(Formatting.Indented) + "\n";

        // The pinned map's file, or a new one holding an empty list under <paramref name="list"/>.
        static JObject ReadPinned(GeneratedMap m, string name, string list) => TryReadPinned(m, name, out var o) ? o : new JObject { [list] = new JArray() };

        static bool TryReadPinned(GeneratedMap m, string name, out JObject o)
        {
            string path = m.PinnedDir == null ? null : Path.Combine(m.PinnedDir, name);
            o = path != null && File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            return path != null && File.Exists(path);
        }

        /// <summary>
        /// The map's spawn places, one per square in the plan's order: kind "spawn", a circle of the spawn's clearing round
        /// its cell, the map square it's in ("square", from the north-west) and what it stands near. Each is named for its
        /// square: the pinned map's name for the valley (whose is the map's own spawn, "start"), the village's name where
        /// one was made, else the square's template (a filler's biome), with a compass word where two names would meet.
        /// </summary>
        public static List<JObject> SpawnPlaces(this GeneratedMap m)
        {
            var list = new List<JObject>();
            if (m.Hills != null) return new List<JObject> { HillsSpawn(m) };
            var names = new HashSet<string>(StringComparer.Ordinal);
            // A pinned block's spawn places come with its own rows (WriteRows), named already: none here takes their names.
            if (m.Inner != null && TryReadPinned(m, WorldPlaces.FileName, out var own))
                foreach (var p in ((JArray)own["places"]).OfType<JObject>().Where(p => (string)p["kind"] == "spawn")) names.Add((string)p["name"]);
            foreach (var s in m.Gen.Spawns)
            {
                if (s.Held) continue;
                int sx = s.X / ChunkGen.Square, sy = s.Y / ChunkGen.Square;
                string name = s.Pinned ? m.PinnedName : VillageIn(m, s.Square) ?? m.Gen.SquareTitle(s.Square);
                if (!names.Add(name))
                {
                    string first = name + " (" + Compass(m, sx, sy) + ")";
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

        // A hills map's one spawn place: its map.json's spawn, named for the map.
        static JObject HillsSpawn(GeneratedMap m)
        {
            var spawn = MapFile.Load(m.MapDir).Spawn;
            return new JObject
            {
                ["id"] = "spawn-" + WorldPlaces.Slug(m.PinnedName),
                ["name"] = m.PinnedName,
                ["kind"] = "spawn",
                ["from"] = MapFile.FileName,
                ["square"] = new JArray(spawn.x / ChunkGen.Square, spawn.y / ChunkGen.Square),
                ["near"] = "hills",
                ["circle"] = new JObject { ["at"] = new JArray(spawn.x, spawn.y), ["radius"] = ChunkGen.ClearCells },
                ["start"] = true,
            };
        }

        static string VillageIn(GeneratedMap m, int square)
        {
            for (int k = 0; k < m.Gen.LandmarkRows.Count; k++)
                if (m.Gen.LandmarkRows[k]?.VillageName is string v && m.Plan.Landmarks[k].Square == square) return v;
            return null;
        }

        // Where a map square lies in the map, as a compass word ("north-west", "middle", "east").
        static string Compass(GeneratedMap m, int sx, int sy)
        {
            string ns = (sy * 3 / m.SquaresH) switch { 0 => "north", 2 => "south", _ => "" };
            string ew = (sx * 3 / m.SquaresW) switch { 0 => "west", 2 => "east", _ => "" };
            if (ns.Length > 0 && ew.Length > 0) return ns + "-" + ew;
            return ns.Length + ew.Length > 0 ? ns + ew : "middle";
        }

        // The valley's rows moved to its place in the map, and the landmarks' rows after them.
        static void WriteRows(GeneratedMap m, string outDir)
        {
            int dx = m.Gen.PinnedX, dy = m.Gen.PinnedY;
            var made = m.Gen.LandmarkRows.Where(r => r != null).ToList();
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

            var stamps = ReadPinned(m, MapStamps.FileName, "stamps");
            Each(stamps["stamps"], s => Pair(s["at"]));
            Write(outDir, MapStamps.FileName, Json(stamps));

            var villages = ReadPinned(m, VillageLayout.FileName, "villages");
            Each(villages["villages"], v => Pair(v["at"]));
            foreach (var r in made.Where(r => r.Village != null)) ((JArray)villages["villages"]).Add(r.Village.DeepClone());
            Write(outDir, VillageLayout.FileName, Json(villages));

            var buildings = ReadPinned(m, WorldBuildings.FileName, "buildings");
            Each(buildings["buildings"], b => Pair(b["at"]));
            foreach (var r in made) foreach (var b in r.Buildings) ((JArray)buildings["buildings"]).Add(b.DeepClone());
            Write(outDir, WorldBuildings.FileName, Json(buildings));

            if (TryReadPinned(m, "golf.json", out var golf))
            {
                Pair(golf["tee"]);
                Pair(golf["hole"]);
                Write(outDir, "golf.json", Json(golf));
            }
            if (TryReadPinned(m, CourseLayout.FileName, out var course))
            {
                foreach (string k in new[] { "area", "bell", "gate" }) Box(course[k]);
                Each(course["booths"], b =>
                {
                    Box(b["area"]);
                    Pair(b["checkpoint"]);
                });
                Write(outDir, CourseLayout.FileName, Json(course));
            }
            if (TryReadPinned(m, WorldPuzzles.FileName, out var puzzles))
            {
                Each(puzzles["puzzles"], p => Pair(p["at"]));
                Write(outDir, WorldPuzzles.FileName, Json(puzzles));
            }
            if (TryReadPinned(m, "jobs.json", out var jobs))
            {
                if (jobs["spots"] is JObject spots)
                    foreach (var s in spots.Properties().Select(p => p.Value).OfType<JObject>())
                    {
                        Pair(s["at"]);
                        Pair(s["out"]);
                    }
                Write(outDir, "jobs.json", Json(jobs));
            }
            if (TryReadPinned(m, "rocks.json", out var rocks))
            {
                if (rocks["rocks"] is JArray list) foreach (var r in list) Pair(r);
                Write(outDir, "rocks.json", Json(rocks));
            }
            foreach (string same in new[] { "hold.json", "looks.json" })
                if (TryReadPinned(m, same, out var o)) Write(outDir, same, Json(o));

            // The hand-written places moved; the generated ones are made again from the folder (GeneratedPlaces.Generate).
            var places = ReadPinned(m, WorldPlaces.FileName, "places");
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
            foreach (var s in m.SpawnPlaces()) kept.Add(s);
            places["places"] = kept;
            Write(outDir, WorldPlaces.FileName, Json(places));
        }
    }

    /// <summary>What a bake took, and the size of the map it wrote.</summary>
    public sealed class BakeReport
    {
        /// <summary>The time making its chunks (written as the next row is made) and the whole bake's.</summary>
        public double MakeMs, TotalMs;
        public int W, H;
    }
}
