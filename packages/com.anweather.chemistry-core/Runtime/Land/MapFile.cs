// A map's folder, world/maps/<id> (docs/design/maps-as-data.md): map.json says what the map is and what its land should
// look like, and cells.txt is its cell map, one legend character a cell, rows top to bottom.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class MapFile
    {
        public const string FileName = "map.json", CellsName = "cells.txt", LandName = "land.json";
        public const string BuildingsName = "buildings.json", CourseName = "gauntlet.json", PuzzlesName = "puzzles.json";

        public string Id { get; private set; }
        public string Name { get; private set; }
        public int W { get; private set; }
        public int H { get; private set; }
        public bool WrapX { get; private set; }
        public bool WrapY { get; private set; }
        public int SeaLevel { get; private set; }
        public (int x, int y) Spawn { get; private set; }
        public int LandShare { get; private set; }
        public int Highest { get; private set; }
        public int Lowest { get; private set; }
        public (int low, int high) LandRange { get; private set; } = (LandLowest, LandHighest);
        public string[] Dresses { get; private set; } = System.Array.Empty<string>();
        public string[] Off { get; private set; } = System.Array.Empty<string>();

        public bool IsOff(string system) => System.Array.IndexOf(Off, system) >= 0;
        public bool OwnLandRange { get; private set; }

        public const double LandUnitMetres = 0.125;
        public const double SurfaceUnderLevel = 0.1;

        public double WaterMetres => SeaLevel * LandUnitMetres - SurfaceUnderLevel;

        public (double x, double z)? SeaShore { get; private set; }
        public (double x, double z)? Seaward { get; private set; }

        public (int x, int y, int w, int h) Yard { get; private set; }

        public (int x, int y) Origin => (Yard.x, Yard.y + Yard.h - 1);

        public (int i0, int j0, int i1, int j1) FullDetail { get; private set; }

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
                foreach (var (inner, ix, iy) in PinnedSquares(dir)) found.Add((inner, x + ix, y + iy));
            }
            return found;
        }

        public const int LandLowest = -136, LandHighest = 2176;

        public const float CellMetres = 1.5f;

        public TerrainGrid Layout()
        {
            var (ox, oy) = Origin;
            return new TerrainGrid(CellMetres, (-(ox + 0.5f) * CellMetres, (oy - H + 0.5f) * CellMetres), (W / Chunks.Size, H / Chunks.Size),
                (0, H - 1), (W, H), FullDetail);
        }

        public static (int di, int dj) ChunkShift(int mapH, int pinnedH, int pinnedX, int pinnedY)
        {
            int dv = mapH - pinnedH - pinnedY;
            if (pinnedX % Chunks.Size != 0 || dv % Chunks.Size != 0)
                throw new FormatException($"a pinned map at cell ({pinnedX}, {pinnedY}) doesn't sit on whole chunks");
            return (pinnedX / Chunks.Size, dv / Chunks.Size);
        }

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

        public static string ReadCells(string worldDir) => File.ReadAllText(Path.Combine(worldDir, CellsName));

        public static void WriteCells(string worldDir, string cells)
        {
            string path = Path.Combine(worldDir, CellsName);
            if (!File.Exists(path) || File.ReadAllText(path) != cells) File.WriteAllText(path, cells);
        }
    }
}
