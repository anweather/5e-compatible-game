// Whether a map's folder is what it says it is (docs/design/maps-as-data.md, section 5): run by a test over every folder
// in world/maps/ and when a world starts, as Rules.Check is for the rules, so nothing starts on the wrong land in silence.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The checks on a map's folder.</summary>
    public static class MapCheck
    {
        /// <summary>The one size the chunks and terrains are laid out for.</summary>
        public const int Size = 576;

        // How far the land's share may stray from what map.json says, in percentage points.
        const int ShareSlack = 1;
        // The cells of a map's text that hold water.
        const string Wet = "wr*~";

        /// <summary>Everything wrong with the map in this folder; empty when it's fit to play.</summary>
        public static List<string> Errors(string worldDir)
        {
            var errors = new List<string>();
            MapFile map;
            try
            {
                map = MapFile.Load(worldDir);
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is NullReferenceException || e is InvalidCastException || e is ArgumentException)
            {
                errors.Add($"{MapFile.FileName} can't be read: {e.Message}");
                return errors;
            }
            string folder = Path.GetFileName(Path.GetFullPath(worldDir).TrimEnd('/', '\\'));
            if (map.Id != folder) errors.Add($"{MapFile.FileName} says its id is \"{map.Id}\", in the folder \"{folder}\"");
            if (map.W != Size || map.H != Size) errors.Add($"the map is {map.W} x {map.H}, and only {Size} x {Size} is laid out");
            CheckCells(worldDir, map, errors);
            int[] land = ReadLand(worldDir, map, errors);
            CheckRange(map, land, errors);
            if (land.Length > 0)
            {
                CheckLand(map, land, errors);
                CheckPlaces(worldDir, map, land, errors);
            }
            CheckThings(worldDir, map, errors);
            CheckRows(worldDir, map, errors);
            CheckHold(worldDir, errors);
            CheckStamps(worldDir, errors);
            return errors;
        }

        // A map's own land stamps read as stamps (where they land is the world's to say, World.LayMapStamps).
        static void CheckStamps(string worldDir, List<string> errors)
        {
            try
            {
                MapStamps.Load(worldDir);
            }
            catch (Exception e) when (e is FormatException || e is IOException)
            {
                errors.Add(e.Message);
            }
        }

        // A map's Hold names a row, its joins and places that are there.
        static void CheckHold(string worldDir, List<string> errors)
        {
            try
            {
                WorldHold.Load(worldDir);
            }
            catch (Exception e) when (e is FormatException || e is JsonException || e is IOException)
            {
                errors.Add(e.Message);
            }
        }

        // A row laid over the cells stands on the map, dry and clear of its things: the box of its walls and towers.
        static void CheckRows(string worldDir, MapFile map, List<string> errors)
        {
            if (!File.Exists(Path.Combine(worldDir, MapFile.CellsName))) return; // said once, by CheckCells
            var rows = MapFile.ReadCells(worldDir).Replace("\r", "").Trim('\n').Split('\n');
            var things = WorldThings.Load(worldDir).All;
            foreach (var row in WorldBuildings.Load(worldDir).LaidRows)
            {
                var (x0, y0, x1, y1) = RowBox(row);
                string id = (string)row["id"];
                if (x0 < 0 || y0 < 0 || x1 >= map.W || y1 >= map.H || y1 >= rows.Length)
                {
                    errors.Add($"the row {id} runs off the map");
                    continue;
                }
                var wet = Enumerable.Range(y0, y1 - y0 + 1).SelectMany(y => Enumerable.Range(x0, Math.Min(x1 + 1, rows[y].Length) - x0).Select(x => (x, y))).Where(c => Wet.Contains(rows[c.y][c.x])).ToList();
                if (wet.Count > 0) errors.Add($"the row {id} stands on water at {wet[0]}");
                foreach (var thing in things.Where(t => t.CellX >= x0 && t.CellX <= x1 && t.CellY >= y0 && t.CellY <= y1))
                    errors.Add($"the row {id} stands on the thing {thing.Id}");
            }
        }

        // The cells a row's walls and towers lie in; a road isn't in it, since it runs out over the land.
        static (int x0, int y0, int x1, int y1) RowBox(JObject row)
        {
            var joins = row["joins"] as JObject ?? new JObject();
            var used = (row["walls"] as JObject)?.PropertyValues().SelectMany(w => new[] { (string)w["from"], (string)w["to"] }).Select(j => (j, r: 0)).ToList() ?? new List<(string j, int r)>();
            used.AddRange((row["towers"] as JObject)?.PropertyValues().Select(t => ((string)t["at"], (int?)t["radius"] ?? 0)) ?? Enumerable.Empty<(string, int)>());
            var points = used.Where(u => joins[u.j ?? ""] is JArray).Select(u => (x: (int)joins[u.j][0], y: (int)joins[u.j][1], u.r)).ToList();
            int ax = (int)row["at"][0], ay = (int)row["at"][1];
            if (points.Count == 0) return (ax, ay, ax, ay);
            return (ax + points.Min(p => p.x - p.r) / 2, ay + points.Min(p => p.y - p.r) / 2, ax + (points.Max(p => p.x + p.r) - 1) / 2, ay + (points.Max(p => p.y + p.r) - 1) / 2);
        }

        static void CheckCells(string worldDir, MapFile map, List<string> errors)
        {
            string path = Path.Combine(worldDir, MapFile.CellsName);
            if (!File.Exists(path))
            {
                errors.Add($"no {MapFile.CellsName}");
                return;
            }
            var rows = MapFile.ReadCells(worldDir).Replace("\r", "").Trim('\n').Split('\n');
            if (rows.Length != map.H || rows.Any(r => r.Length != map.W))
                errors.Add($"{MapFile.CellsName} isn't {map.W} x {map.H}: {rows.Length} rows, the longest {rows.Max(r => r.Length)}");
            var starts = rows.SelectMany((row, y) => row.Select((ch, x) => (ch, x, y))).Where(c => c.ch == '@').ToList();
            if (starts.Count != 1) errors.Add($"{MapFile.CellsName} has {starts.Count} starts (@), not one");
            else if ((starts[0].x, starts[0].y) != map.Spawn)
                errors.Add($"{MapFile.FileName}'s spawn is {map.Spawn}, and the @ of {MapFile.CellsName} is at ({starts[0].x}, {starts[0].y})");
        }

        static int[] ReadLand(string worldDir, MapFile map, List<string> errors)
        {
            string path = Path.Combine(worldDir, MapFile.LandName);
            if (!File.Exists(path))
            {
                errors.Add($"no {MapFile.LandName}");
                return Array.Empty<int>();
            }
            var land = LandFile.Read(File.ReadAllText(path), map.W, map.H);
            if (land.Length == 0) errors.Add($"{MapFile.LandName} isn't for a {map.W} x {map.H} map");
            return land;
        }

        // A range a map says is held to its land; one left out is the valley's, whose sea bed lies below where stamps stop.
        static void CheckRange(MapFile map, int[] land, List<string> errors)
        {
            var (low, high) = map.LandRange;
            if (low >= high) errors.Add($"{MapFile.FileName}'s drawable range runs from {low} up to {high}");
            if (!map.OwnLandRange || land.Length == 0) return;
            if (land.Min() < low) errors.Add($"the land's lowest cell is {land.Min()}, below {MapFile.FileName}'s drawable {low}");
            if (land.Max() > high) errors.Add($"the land's highest cell is {land.Max()}, above {MapFile.FileName}'s drawable {high}");
        }

        static void CheckLand(MapFile map, int[] land, List<string> errors)
        {
            int high = land.Max(), low = land.Min();
            if (high != map.Highest) errors.Add($"the land's highest cell is {high}, and {MapFile.FileName} expects {map.Highest}");
            if (low != map.Lowest) errors.Add($"the land's lowest cell is {low}, and {MapFile.FileName} expects {map.Lowest}");
            int share = (int)Math.Round(100.0 * land.Count(u => u >= map.SeaLevel) / land.Length);
            if (Math.Abs(share - map.LandShare) > ShareSlack)
                errors.Add($"{share}% of the land is above the sea, and {MapFile.FileName} expects {map.LandShare}%");
            var (x, y) = map.Spawn;
            if (x < 0 || y < 0 || x >= map.W || y >= map.H) errors.Add($"the spawn {map.Spawn} is off the map");
            else if (land[y * map.W + x] < map.SeaLevel) errors.Add($"the spawn {map.Spawn} is under the sea");
        }

        static void CheckPlaces(string worldDir, MapFile map, int[] land, List<string> errors)
        {
            var places = WorldPlaces.Load(worldDir);
            if (places.Doc?["generate"]?["water"]?["below"] is JValue below && (int)below != map.SeaLevel)
                errors.Add($"{WorldPlaces.FileName} finds water below {(int)below}, and the sea level is {map.SeaLevel}");
            bool Dry(int x, int y) => land[y * map.W + x] >= map.SeaLevel;
            foreach (var place in places.All)
            {
                var cells = place.Cells().ToList();
                if (cells.Any(c => c.x < 0 || c.y < 0 || c.x >= map.W || c.y >= map.H)) errors.Add($"the place {place.Id} runs off the map");
                else if ((place.Kind == "lake" || place.Kind == "sea") && cells.Any(c => Dry(c.x, c.y))) errors.Add($"the {place.Kind} {place.Id} has dry land in it");
                else if (place.Kind == "village" && !Dry(place.Middle.x, place.Middle.y)) errors.Add($"the village {place.Id} is under the sea");
            }
        }

        static void CheckThings(string worldDir, MapFile map, List<string> errors)
        {
            foreach (var thing in WorldThings.Load(worldDir).All.Where(t => t.CellX < 0 || t.CellY < 0 || t.CellX >= map.W || t.CellY >= map.H))
                errors.Add($"the thing {thing.Id} at ({thing.CellX}, {thing.CellY}) is off the map");
        }
    }
}
