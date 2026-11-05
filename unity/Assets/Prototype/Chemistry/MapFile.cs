// A map's folder, world/maps/<id> (docs/design/maps-as-data.md): map.json says what the map is and what its land should
// look like, and cells.txt is its cell map, one legend character a cell, rows top to bottom.
using System.IO;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>map.json: a map's id and name, its size, sea level and spawn, and what its land is expected to be.</summary>
    public sealed class MapFile
    {
        public const string FileName = "map.json", CellsName = "cells.txt", LandName = "land.json";

        /// <summary>The id, which is its folder's name.</summary>
        public string Id { get; private set; }
        public string Name { get; private set; }
        public int W { get; private set; }
        public int H { get; private set; }
        /// <summary>In land units: land below this is under water.</summary>
        public int SeaLevel { get; private set; }
        public (int x, int y) Spawn { get; private set; }
        /// <summary>The percent of cells at or above the sea level.</summary>
        public int LandShare { get; private set; }
        /// <summary>The land's highest and lowest cells, in land units.</summary>
        public int Highest { get; private set; }
        public int Lowest { get; private set; }
        /// <summary>How low and high land can be stamped, in land units; left out, the valley's.</summary>
        public (int low, int high) LandRange { get; private set; } = (World.LandLowest, World.LandHighest);
        /// <summary>Whether map.json says its own range. The valley's doesn't hold its land: it's where stamps stop, and the sea bed lies below it.</summary>
        public bool OwnLandRange { get; private set; }

        /// <summary>The map.json in a map's folder; throws if it's missing or a field is.</summary>
        public static MapFile Load(string worldDir)
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(worldDir, FileName)));
            var expects = (JObject)o["expects"];
            var drawable = o["drawable"] as JArray;
            return new MapFile
            {
                LandRange = drawable != null ? ((int)drawable[0], (int)drawable[1]) : (World.LandLowest, World.LandHighest),
                OwnLandRange = drawable != null,
                Id = (string)o["id"],
                Name = (string)o["name"],
                W = (int)o["size"][0],
                H = (int)o["size"][1],
                SeaLevel = (int)o["seaLevel"],
                Spawn = ((int)o["spawn"][0], (int)o["spawn"][1]),
                LandShare = (int)expects["landShare"],
                Highest = (int)expects["highest"],
                Lowest = (int)expects["lowest"],
            };
        }

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
