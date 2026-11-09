// The plan's spawn points in a generated map (WorldPlan.Spawns.cs): each square's spot as a map cell, and a few cells
// round it kept clear, levelled to the land at the spot and leaning back over a bank, grass with no tree, stone or
// boulder on it, so a player starts on open, flat ground. The valley's is its own map's spawn and left as it is. Laid
// once when the generator is made, so a cell's land stays a function of its position.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A spawn point as a map cell: its plan square and what it stands near (WorldPlan's PlanSpawn).</summary>
    public sealed class MapSpawn
    {
        public int Square, X, Y;
        public string Near;
        public bool Pinned;
    }

    public sealed partial class ChunkGen
    {
        /// <summary>A spawn's clearing: its radius in cells, and the bank past it over which the land leans back.</summary>
        public const int ClearCells = 4, ClearBank = 6;

        /// <summary>The plan's spawns on the map, in the plan's order.</summary>
        public IReadOnlyList<MapSpawn> Spawns => spawns;
        readonly List<MapSpawn> spawns = new List<MapSpawn>();
        // The clearings, each a map cell and the land it is levelled to (land units × Q).
        readonly List<(int x, int y, long land)> clearings = new List<(int, int, long)>();

        void SetUpSpawns()
        {
            var level = new List<(int, int, long)>();
            foreach (var s in Plan.Spawns)
            {
                var (x, y) = MapCell(s.Square, s.SiteX, s.SiteY);
                if (x < 0 || y < 0 || x >= CellsW || y >= CellsH) continue;
                bool pinned = Pinned != null && s.Square == Pinned.Square;
                spawns.Add(new MapSpawn { Square = s.Square, X = x, Y = y, Near = s.Near, Pinned = pinned });
                // The land at the spot as it would be with no clearing (none is laid yet).
                if (!pinned) level.Add((x, y, (long)LandAt(x, y) * Q));
            }
            clearings.AddRange(level);
        }

        // Levels a cell in a spawn's clearing to the spot's land, and leans its bank back; whether it is in a clearing.
        bool Clearing(int x, int y, ref long g)
        {
            const int reach = ClearCells + ClearBank;
            bool inside = false;
            foreach (var (cx, cy, land) in clearings)
            {
                long dx = Dx(x, cx), dy = y - cy;
                if (Math.Abs(dx) > reach || Math.Abs(dy) > reach) continue;
                long d = BiomeMath.Sqrt((dx * dx + dy * dy) * One * One);
                if (d >= reach * One) continue;
                if (d < ClearCells * One)
                {
                    g = land;
                    inside = true;
                }
                else g = land + (g - land) * BiomeMath.Smoothstep((d - ClearCells * One) / ClearBank) / One;
            }
            return inside;
        }
    }
}
