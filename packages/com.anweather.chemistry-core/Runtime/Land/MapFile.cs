// A map's folder, world/maps/<id> (docs/design/maps-as-data.md): map.json says what the map is and what its land should
// look like, and cells.txt is its cell map, one legend character a cell, rows top to bottom.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>map.json: a map's id and name, its size, sea level and spawn, and what its land is expected to be.</summary>
    public sealed class MapFile
    {
        public const string FileName = "map.json", CellsName = "cells.txt", LandName = "land.json";
        /// <summary>
        /// The names of the rows a map folder holds that are read above the land (WorldBuildings, CourseLayout,
        /// WorldPuzzles), here so the land's GeneratedMap can name what a bake writes.
        /// </summary>
        public const string BuildingsName = "buildings.json", CourseName = "gauntlet.json", PuzzlesName = "puzzles.json";

        /// <summary>The id, which is its folder's name.</summary>
        public string Id { get; private set; }
        public string Name { get; private set; }
        public int W { get; private set; }
        public int H { get; private set; }
        /// <summary>Whether the map joins east to west (its generated "wrap": "ew" or "torus"): a World made for it wraps (World.WrapX).</summary>
        public bool WrapX { get; private set; }
        /// <summary>Whether the map joins north to south too (its generated "wrap": "torus", a hills map's).</summary>
        public bool WrapY { get; private set; }
        /// <summary>In land units: land below this is under water.</summary>
        public int SeaLevel { get; private set; }
        public (int x, int y) Spawn { get; private set; }
        /// <summary>The percent of cells at or above the sea level.</summary>
        public int LandShare { get; private set; }
        /// <summary>The land's highest and lowest cells, in land units.</summary>
        public int Highest { get; private set; }
        public int Lowest { get; private set; }
        /// <summary>How low and high land can be stamped, in land units; left out, the valley's.</summary>
        public (int low, int high) LandRange { get; private set; } = (LandLowest, LandHighest);
        /// <summary>
        /// The places that stay code which the map wants built (docs/design/maps-as-data.md, section 4: the court, the
        /// bridge, the falls...), by name; left out, none. The scene builds every place it knows into a root of its own,
        /// and a root the map doesn't name switches itself off (PlaceRoot).
        /// </summary>
        public string[] Dresses { get; private set; } = System.Array.Empty<string>();
        /// <summary>
        /// The view's systems this map turns off (map.json's "off": "fauna", ...), for this map only: a map with nothing for
        /// them (the hills) doesn't pay for them. Empty for none.
        /// </summary>
        public string[] Off { get; private set; } = System.Array.Empty<string>();

        /// <summary>Whether map.json's "off" names this system.</summary>
        public bool IsOff(string system) => System.Array.IndexOf(Off, system) >= 0;
        /// <summary>Whether map.json says its own range. The valley's doesn't hold its land: it's where stamps stop, and the sea bed lies below it.</summary>
        public bool OwnLandRange { get; private set; }

        /// <summary>A land unit in metres.</summary>
        public const double LandUnitMetres = 0.125;
        /// <summary>How far under the sea level the water's surface is drawn: below the lowest dry land, above the highest drowned.</summary>
        public const double SurfaceUnderLevel = 0.1;

        /// <summary>The height the sea and the lakes are drawn at, in metres: just under the sea level.</summary>
        public double WaterMetres => SeaLevel * LandUnitMetres - SurfaceUnderLevel;

        /// <summary>
        /// Where the swell comes in from (map.json's sea, in world metres): a point on the coast and the way out to sea. Left
        /// out, the view works it out from where the land is low.
        /// </summary>
        public (double x, double z)? SeaShore { get; private set; }
        public (double x, double z)? Seaward { get; private set; }

        /// <summary>
        /// The old chemistry yard (map.json's yard: x, y, width, height in cells), whose south-west cell stands at world
        /// (0, 0); left out, a one-cell yard at the map's middle. Where the cells stand in the world follows from it.
        /// </summary>
        public (int x, int y, int w, int h) Yard { get; private set; }

        /// <summary>The cell at world (0, 0): the yard's south-west cell.</summary>
        public (int x, int y) Origin => (Yard.x, Yard.y + Yard.h - 1);

        /// <summary>The chunks drawn in full detail (map.json's fullDetail: i0, j0, i1, j1, inclusive); left out, none.</summary>
        public (int i0, int j0, int i1, int j1) FullDetail { get; private set; }

        /// <summary>
        /// The maps a world file pins, without solving its plan: each one's folder (world/maps/id) and the map cell its
        /// north-west cell lands on (its square times WorldPlan.SquareCells, as ChunkGen.PinnedX and PinnedY), each followed
        /// by the maps a pinned block pins in turn, where they lie in this one. Empty for a map that isn't generated.
        /// </summary>
        public static List<(string dir, int x, int y)> PinnedSquares(string mapDir)
        {
            var found = new List<(string dir, int x, int y)>();
            string path = Path.Combine(mapDir, FileName);
            if (!File.Exists(path) || !(JObject.Parse(File.ReadAllText(path))["generated"] is JObject o) || !(o["pinned"] is JArray pins)) return found;
            string worldDir = Path.GetFullPath(Path.Combine(mapDir, "..", ".."));
            foreach (var pin in pins.OfType<JObject>())
            {
                string dir = Path.Combine(worldDir, "maps", (string)pin["map"]);
                int x = (int)pin["square"][0] * WorldPlan.SquareCells, y = (int)pin["square"][1] * WorldPlan.SquareCells;
                found.Add((dir, x, y));
                // A pinned block's own pinned maps, where they lie in this one (the valley in the nine in the scale map).
                foreach (var (inner, ix, iy) in PinnedSquares(dir)) found.Add((inner, x + ix, y + iy));
            }
            return found;
        }

        /// <summary>The valley's drawable range: how low and high land can be stamped, in land units, inside what a chunk's terrain can draw with room for a cell's own height (World.LandLowest, LandHighest).</summary>
        public const int LandLowest = -136, LandHighest = 2176;

        /// <summary>A cell's side in metres, on every map.</summary>
        public const float CellMetres = 1.5f;

        /// <summary>Where this map's chunk terrains and cells stand in the world.</summary>
        public TerrainGrid Layout()
        {
            var (ox, oy) = Origin;
            return new TerrainGrid(CellMetres, (-(ox + 0.5f) * CellMetres, (oy - H + 0.5f) * CellMetres), (W / Chunks.Size, H / Chunks.Size),
                (0, H - 1), (W, H), FullDetail);
        }

        /// <summary>
        /// How far a pinned map's chunk terrains move in the map that pins it, in chunks: its cell (x, y) is the map's
        /// (x + <paramref name="pinnedX"/>, y + <paramref name="pinnedY"/>), and both grids run their terrains' rows from
        /// the south (Layout: gridCorner (0, H - 1)). Throws if the square doesn't sit on whole chunks.
        /// </summary>
        public static (int di, int dj) ChunkShift(int mapH, int pinnedH, int pinnedX, int pinnedY)
        {
            int dv = mapH - pinnedH - pinnedY;
            if (pinnedX % Chunks.Size != 0 || dv % Chunks.Size != 0)
                throw new FormatException($"a pinned map at cell ({pinnedX}, {pinnedY}) doesn't sit on whole chunks");
            return (pinnedX / Chunks.Size, dv / Chunks.Size);
        }

        // Its full-detail chunks: map.json's, or on a generated map without them, its pinned map's moved to the pinned
        // square, so the pinned chunks keep their own ground (PinnedGround) at the resolution it was made at.
        static (int i0, int j0, int i1, int j1) FullDetailOf(string mapDir, JObject o, int h)
        {
            if (o["fullDetail"] is JArray full) return ((int)full[0], (int)full[1], (int)full[2], (int)full[3]);
            foreach (var (dir, x, y) in PinnedSquares(mapDir))
            {
                if (!File.Exists(Path.Combine(dir, FileName))) continue;
                var pinned = Load(dir);
                var f = pinned.FullDetail;
                if (f.i1 < f.i0) continue;
                var (di, dj) = ChunkShift(h, pinned.H, x, y);
                return (f.i0 + di, f.j0 + dj, f.i1 + di, f.j1 + dj);
            }
            return (0, 0, -1, -1);
        }

        /// <summary>The map.json in a map's folder; throws if it's missing or a field is.</summary>
        public static MapFile Load(string worldDir)
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(worldDir, FileName)));
            var expects = (JObject)o["expects"];
            var drawable = o["drawable"] as JArray;
            var sea = o["sea"] as JObject;
            return new MapFile
            {
                LandRange = drawable != null ? ((int)drawable[0], (int)drawable[1]) : (LandLowest, LandHighest),
                OwnLandRange = drawable != null,
                Yard = o["yard"] is JArray yard ? ((int)yard[0], (int)yard[1], (int)yard[2], (int)yard[3]) : ((int)o["size"][0] / 2, (int)o["size"][1] / 2, 1, 1),
                FullDetail = FullDetailOf(worldDir, o, (int)o["size"][1]),
                SeaShore = Pair(sea?["shore"]),
                Seaward = Pair(sea?["seaward"]),
                Dresses = o["dresses"] is JArray dresses ? dresses.Select(d => (string)d).ToArray() : System.Array.Empty<string>(),
                Off = o["off"] is JArray off ? off.Select(d => (string)d).ToArray() : System.Array.Empty<string>(),
                Id = (string)o["id"],
                Name = (string)o["name"],
                W = (int)o["size"][0],
                H = (int)o["size"][1],
                WrapX = (string)o["generated"]?["wrap"] is "ew" or "torus",
                WrapY = (string)o["generated"]?["wrap"] == "torus",
                SeaLevel = (int)o["seaLevel"],
                Spawn = ((int)o["spawn"][0], (int)o["spawn"][1]),
                LandShare = (int)expects["landShare"],
                Highest = (int)expects["highest"],
                Lowest = (int)expects["lowest"],
            };
        }

        static (double x, double z)? Pair(JToken t) => t is JArray a && a.Count == 2 ? ((double)a[0], (double)a[1]) : ((double, double)?)null;

        /// <summary>The map's cells as text.</summary>
        public static string ReadCells(string worldDir) => File.ReadAllText(Path.Combine(worldDir, CellsName));

        /// <summary>Writes the map's cells, leaving the file alone when it already says that.</summary>
        public static void WriteCells(string worldDir, string cells)
        {
            string path = Path.Combine(worldDir, CellsName);
            if (!File.Exists(path) || File.ReadAllText(path) != cells) File.WriteAllText(path, cells);
        }
    }
}
