// The buildings every peer's world already stands with, as rows with ids
// (docs/design/world-tools.md G1; builders.md section 4). world/buildings.json has
// the ones outside a village; a village's come from its recipe. Where a plan's plots
// were refused depends on the land the Editor baked, which only the Editor can
// work out, but the baked cells say it: a plot that stands is all wall, and one
// that was refused isn't. So laying the recipe again with "is it wall?" for the land
// gives the plan the bake made, on every peer, from the world's starting cells.
//
// Not a port: the sandbox has no buildings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>world/buildings.json and world/villages.json: all a world needs to name the buildings it was made with.</summary>
    public sealed class WorldBuildings
    {
        public const string FileName = "buildings.json";

        public static readonly WorldBuildings Empty = new WorldBuildings(new List<JObject>(), new List<VillageSpec>());

        readonly List<JObject> rows;
        readonly List<VillageSpec> villages;

        WorldBuildings(List<JObject> rows, List<VillageSpec> villages)
        {
            this.rows = rows;
            this.villages = villages;
        }

        /// <summary>The rows of world/buildings.json.</summary>
        public IReadOnlyList<JObject> FileRows => rows;

        /// <summary>The villages' recipes (world/villages.json).</summary>
        public IReadOnlyList<VillageSpec> Villages => villages;

        /// <summary>Reads the two files from <paramref name="worldDir"/>; no file means none of them.</summary>
        public static WorldBuildings Load(string worldDir)
        {
            if (worldDir == null || !Directory.Exists(worldDir)) return Empty;
            var path = Path.Combine(worldDir, FileName);
            var rows = File.Exists(path) ? (JObject.Parse(File.ReadAllText(path))["buildings"] as JArray)?.OfType<JObject>().ToList() : null;
            return new WorldBuildings(rows ?? new List<JObject>(), VillageLayout.Load(worldDir));
        }

        /// <summary>
        /// The rows of the buildings standing on these cells: a file's row whose built cells are there,
        /// and each village's as the cells say it was laid. A world without them (a small test yard) has none.
        /// </summary>
        public List<JObject> RowsOn(Func<int, int, string> terrainAt)
        {
            var found = new List<JObject>();
            found.AddRange(rows.Where(r => Stands(r, terrainAt)).Select(r => (JObject)r.DeepClone()));
            foreach (var spec in villages)
                found.AddRange(VillageRows.Of(LaidPlan(spec, terrainAt)));
            return found;
        }

        /// <summary>A village's plan as it was laid on these cells.</summary>
        public static VillagePlan LaidPlan(VillageSpec spec, Func<int, int, string> terrainAt)
        {
            var walls = new HashSet<string>(spec.Materials.Walls.Values) { spec.Materials.Window };
            return VillageLayout.Lay(spec, (x, y) => terrainAt(x, y) is string t && walls.Contains(t));
        }

        static bool Stands(JObject row, Func<int, int, string> terrainAt)
        {
            int ax = (int)row["at"][0], ay = (int)row["at"][1];
            foreach (var t in (row["built"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                foreach (var r in t.Value.OfType<JArray>())
                    for (int k = 1; k + 1 < r.Count; k += 2)
                        for (int x = (int)r[k]; x <= (int)r[k + 1]; x++)
                            if (terrainAt(ax + x, ay + (int)r[0]) != t.Name) return false;
            return true;
        }
    }

    /// <summary>A village plan's buildings as rows (fill solid), so their cells stay what the plan's are.</summary>
    public static class VillageRows
    {
        // Rows on the lattice that the openings' names read from: each is a row of rules/buildings.json.
        const string Window = "window", Door = "drawn-door", Style = "village";

        /// <summary>Every building of the plan, its id the village's name and its place in the plan from 1.</summary>
        public static IEnumerable<JObject> Of(VillagePlan plan)
        {
            string village = plan.Spec.Name.Replace(' ', '-');
            for (int i = 0; i < plan.Buildings.Count; i++) yield return Row(plan.Buildings[i], $"{village}#{i + 1}");
        }

        static JObject Row(VillageBuilding b, string id)
        {
            var r = b.Footprint;
            int w = r.W * 2, h = r.H * 2;
            var openings = new Dictionary<Side, JArray>
            {
                [Side.North] = new JArray(),
                [Side.East] = new JArray(),
                [Side.South] = new JArray(),
                [Side.West] = new JArray(),
            };
            foreach (var win in b.Windows)
                openings[win.Face].Add(new JObject { ["kind"] = Window, ["at"] = AlongWall(r, win.Face, win.X, win.Y) });
            openings[b.Door].Add(new JObject { ["kind"] = Door, ["at"] = AlongWall(r, b.Door, b.DoorCell.x, b.DoorCell.y) });
            JObject Wall(string from, string to, Side side)
            {
                var wall = new JObject { ["from"] = from, ["to"] = to };
                if (openings[side].Count > 0) wall["openings"] = openings[side];
                return wall;
            }
            var row = new JObject
            {
                ["id"] = id,
                ["at"] = new JArray(r.X, r.Y),
                ["style"] = Style,
                ["kind"] = b.Kind.ToString().ToLowerInvariant(),
                ["seed"] = b.Seed,
                ["fill"] = "solid",
                ["storeys"] = b.Storeys,
                ["front"] = b.Door.ToString().ToLowerInvariant(),
                ["wall"] = new JObject { ["material"] = b.WallMaterial, ["kind"] = b.Walls.ToString().ToLowerInvariant() },
                ["joins"] = new JObject { ["nw"] = new JArray(0, 0), ["ne"] = new JArray(w, 0), ["se"] = new JArray(w, h), ["sw"] = new JArray(0, h) },
                ["walls"] = new JObject
                {
                    ["north"] = Wall("nw", "ne", Side.North),
                    ["east"] = Wall("ne", "se", Side.East),
                    ["south"] = Wall("se", "sw", Side.South),
                    ["west"] = Wall("sw", "nw", Side.West),
                },
                ["roofs"] = new JObject
                {
                    ["main"] = new JObject
                    {
                        ["over"] = new JArray("nw", "ne", "se", "sw"),
                        ["shape"] = b.Roof.ToString().ToLowerInvariant(),
                        ["cover"] = b.RoofMaterial,
                    },
                },
            };
            if (b.Chimney) row["parts"] = new JArray(new JObject { ["kind"] = "chimney", ["on"] = b.Door == Side.North ? "south" : "north" });
            return row;
        }

        // The half cell a cell starts at along a wall running clockwise: north runs east, east south, south west, west north.
        static int AlongWall(CellRect r, Side side, int x, int y) => side switch
        {
            Side.North => 2 * (x - r.X),
            Side.East => 2 * (y - r.Y),
            Side.South => 2 * (r.Right - 1 - x),
            _ => 2 * (r.Bottom - 1 - y),
        };
    }
}
