// The plan's spawn points in a generated map (WorldPlan.Spawns.cs): each square's spot as a map cell, and a few cells
// round it kept clear, levelled to the land at the spot and leaning back over a bank, grass with no tree, stone or
// boulder on it, so a player starts on open, flat ground. The valley's is its own map's spawn and left as it is. Laid
// once when the generator is made, so a cell's land stays a function of its position.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class MapSpawn
    {
        public int Square, X, Y;
        public string Near;
        public bool Pinned, Held;
    }

    public sealed partial class ChunkGen
    {
        public const int ClearCells = 4, ClearBank = 6;

        public IReadOnlyList<MapSpawn> Spawns => spawns;
        readonly List<MapSpawn> spawns = new List<MapSpawn>();
        readonly List<(int x, int y, long land)> clearings = new List<(int, int, long)>();

        void SetUpSpawns()
        {
            var level = new List<(int, int, long)>();
            foreach (var s in Plan.Spawns)
            {
                var (x, y) = MapCell(s.Square, s.SiteX, s.SiteY);
                if (x < 0 || y < 0 || x >= CellsW || y >= CellsH) continue;
                bool pinned = InPinnedSquare(s.Square);
                spawns.Add(new MapSpawn { Square = s.Square, X = x, Y = y, Near = s.Near, Pinned = pinned });
                if (!pinned) level.Add((x, y, (long)LandAt(x, y) * Q));
            }
            clearings.AddRange(level);
            if (Pinned?.Inner == null) return;
            foreach (var s in Pinned.Inner.Spawns)
            {
                int x = s.X + PinnedX, y = s.Y + PinnedY;
                spawns.Add(new MapSpawn { Square = Plan.Grid.Index(OriginX + x / Square, OriginY + y / Square), X = x, Y = y, Near = s.Near, Pinned = s.Pinned, Held = true });
            }
            spawns.Sort((a, b) => a.Square.CompareTo(b.Square));
        }

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
