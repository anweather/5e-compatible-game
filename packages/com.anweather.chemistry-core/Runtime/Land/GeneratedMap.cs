// A map made from a world file (docs/design/world-squares.md sections 6 and 9, phase 4): world/maps/<id>/map.json says
// the seed, the size in world squares, the generator's version and the squares pinned to a map folder (the valley), and
// the rest is solved (WorldPlan) and made a chunk at a time (ChunkGen). Its identity is a hash of its inputs, never its
// output: the world file, every committed file the plan and the chunks read, each pinned map's folder hash and the
// versions. The bake (MapBake, in the core above the land: it writes Play's rows too) writes the whole map as a map
// folder the game already loads (cells.txt, land.json, things.json and the rows), with the pinned valley's own rows
// moved to where it lies in the map, and a "spawn" place for each square's spawn point (MapBake.SpawnPlaces).
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
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class GeneratedMap
    {
        /// <summary>map.json's key for a map made from a world file.</summary>
        public const string Key = "generated";
        /// <summary>A bake's own note of what it was made from, beside the files it wrote.</summary>
        public const string BakeName = "bake.json";
        /// <summary>
        /// What a bake away from the map's folder holds, as bake.json's "format": one of another format is made again.
        /// 2: its map.json says the full detail the pinned map gives (#183).
        /// </summary>
        public const int BakeFormat = 2;
        /// <summary>The files a bake writes; a generated map's folder commits only map.json.</summary>
        public static readonly string[] Baked =
        {
            MapFile.CellsName, MapFile.LandName, "things.json", WorldPlaces.FileName, MapStamps.FileName, VillageLayout.FileName,
            MapFile.BuildingsName, "golf.json", MapFile.CourseName, MapFile.PuzzlesName, "jobs.json", "rocks.json",
            "hold.json", "looks.json", BakeName,
        };

        public string Id { get; private set; }
        public string Seed { get; private set; }
        public int SquaresW { get; private set; }
        public int SquaresH { get; private set; }
        public int Generator { get; private set; }
        /// <summary>map.json's "wrap": "ew" joins east to west (the north and south rows are walls); absent, none.</summary>
        public PlanWrap Wrap { get; private set; }
        /// <summary>
        /// Whether the map may be baked whole: map.json's "bake", true when absent. A world too big to hold every cell of
        /// (the 12 × 10, about 9-10 GB) says false: its rows are written alone (MapBake.EnsureRows) and its cells made as play reads them (GeneratedSource).
        /// </summary>
        public bool Bakes { get; private set; }
        /// <summary>The pinned map's id and its square in the map (squares from the north-west).</summary>
        public string PinnedMap { get; private set; }
        public (int x, int y) PinnedAt { get; private set; }
        public WorldPlan Plan { get; private set; }
        public ChunkGen Gen { get; private set; }
        /// <summary>The map folder (world/maps/id) and the game's world folder above it.</summary>
        public string MapDir { get; private set; }
        public string WorldDir { get; private set; }
        public string PinnedDir => PinnedMap == null ? null : Path.Combine(WorldDir, "maps", PinnedMap);
        /// <summary>A hills map's land (generated "hills", docs/design/hills.md): no plan and no pinned map; else null.</summary>
        public HillsLand Hills => Gen?.Hills;
        /// <summary>The pinned map's name (its map.json's), what its spawn is called.</summary>
        public string PinnedName { get; private set; }
        /// <summary>A pinned block's own map (the nine in the scale map), when the pinned map is a generated one; else null.</summary>
        public GeneratedMap Inner { get; private set; }

        FarLandmarks far;

        /// <summary>The plan's landmarks that show from far off (world/horizon.json), for the view's far ring.</summary>
        public FarLandmarks Far => far ??= FarLandmarks.Make(Gen, HorizonTable.Load(WorldDir));

        /// <summary>A known ring on this map, sized by the rules' streaming.sight and streaming.know.</summary>
        public KnownRing KnownRing(StreamingParams streaming) => new KnownRing(Gen, streaming);

        /// <summary>
        /// The maps a world file pins, without solving its plan: each one's folder (world/maps/id) and the map cell its
        /// north-west cell lands on (its square times WorldPlan.SquareCells, as ChunkGen.PinnedX and PinnedY), each followed
        /// by the maps a pinned block pins in turn, where they lie in this one. Empty for a map that isn't generated.
        /// </summary>
        public static List<(string dir, int x, int y)> PinnedSquares(string mapDir) => MapFile.PinnedSquares(mapDir);

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
                Wrap = WrapOf((string)o["wrap"]),
                Bakes = (bool?)o["bake"] ?? true,
            };
            if (m.Generator != ChunkGen.Version) throw new FormatException($"{MapFile.FileName}: made by generator {m.Generator}, and this is {ChunkGen.Version}");
            if (o["hills"] is JObject hills) return LoadHills(m, hills);
            // An east-west world's map is its whole plan, the wall rows north and south included.
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
                // A pinned block: a generated map pinned whole, its squares held as its plan has them and its chunks its own.
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

        // A hills map: no plan solved and nothing pinned; its land is the hills' noise, its seed the world file's.
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

        /// <summary>A world file's "forced": the character that marks a pinned block's squares in its rows.</summary>
        public const string ForcedPinned = "pinned";

        // map.json's "forced" (a test map's: scale-12x10): "rows", the map's world squares from the north-west, a
        // character each, and "legend", what each character is: a template id ("filler" for the band's filler), "@" and
        // the band it asks (its first band without), or "pinned" for the pinned block's squares, which must be where
        // "pinned" puts them. Forced squares are plan squares from the map's north-west one.
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

        /// <summary>Whether the folder holds a bake of these inputs (its cells too, not only its rows: MapBake.EnsureRows).</summary>
        public static bool IsBaked(string mapDir)
        {
            string path = Path.Combine(mapDir, BakeName);
            if (!File.Exists(path)) return false;
            var note = JObject.Parse(File.ReadAllText(path));
            return (string)note["inputs"] == InputHash(mapDir) && (string)note["cells"] != CellsGenerated;
        }

        /// <summary>bake.json's "cells" in a folder of a map's rows alone (MapBake.EnsureRows): its cells are made as they're read (GeneratedSource).</summary>
        public const string CellsGenerated = "generated";

        /// <summary>The files a folder of a map's rows alone holds (MapBake.EnsureRows): a bake's but its cells, land and things, which come from GeneratedSource.</summary>
        public static IReadOnlyList<string> RowsBaked { get; } = Baked.Where(n => n != MapFile.CellsName && n != MapFile.LandName && n != "things.json").ToArray();

        /// <summary>Whether a map folder holds a map's rows alone (MapBake.EnsureRows): play makes its cells from its world file (GeneratedSource).</summary>
        public static bool IsRowsOnly(string dir)
        {
            string path = Path.Combine(dir, BakeName);
            return File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["cells"] == CellsGenerated;
        }
    }
}
