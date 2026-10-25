// How the cells paint the chunk terrains (docs/WORLD_STREAMING.md, part 1): each
// cell's terrain, cover and scorch as weights of the valley's terrain layers, for
// its block of alphamap texels (4×4 on a full-detail chunk, 1×1 on a coarse one).
// Pure maths, no Unity: the view looks the layers up by name in the terrain's
// layers and hands the blocks to TerrainData.SetAlphamaps.
//
// Not a port: the sandbox has no terrain.
//
// The block for a cell is the cell's texels counted from the chunk's corner
// (TerrainGrid.TexelRect). Arrays are Unity's alphamap order: [row = z, column = x,
// layer], layers in Layers order.
using System;
using System.Collections.Generic;

namespace Chem
{
    public static class CellPaint
    {
        // The valley's terrain layers (ValleyBuilder.LayerNames) that cells paint.
        public const string Grass = "Grass_A";
        public const string Moss = "Grass_Moss";
        public const string Rock = "Rock";
        public const string Sand = "Sand";
        public const string Soil = "Soil_Rocks";

        /// <summary>The layers painted, in the order of the weights. The valley's Snow is never painted.</summary>
        public static readonly IReadOnlyList<string> Layers = new[] { Grass, Moss, Rock, Sand, Soil };

        // Weights in Layers order: grass, moss, rock, sand, soil.
        static readonly float[] GrassMix = { 1, 0, 0, 0, 0 };
        static readonly float[] UndergrowthMix = { 0, 1, 0, 0, 0 };
        static readonly float[] StumpMix = { 0, 0.5f, 0, 0, 0.5f };
        static readonly float[] RockMix = { 0, 0, 1, 0, 0 };
        // Bare earth: soil lightened with sand, so it reads apart from scorched ground.
        static readonly float[] EarthMix = { 0, 0, 0, 0.4f, 0.6f };
        // Scorched, dug, stained or covered ground: dark soil.
        static readonly float[] DarkMix = { 0, 0, 0, 0, 1 };

        /// <summary>Terrains with their own look; anything else (dirt, bridges, machines) is bare earth.</summary>
        static readonly Dictionary<string, float[]> ByTerrain = new Dictionary<string, float[]>
        {
            ["grass"] = GrassMix,
            ["brush"] = UndergrowthMix,
            ["tree"] = UndergrowthMix,
            ["stump"] = StumpMix,
            ["rock"] = RockMix,
            ["rubble"] = RockMix,
            ["stone"] = RockMix,
            ["pillar"] = RockMix,
            ["bedrock"] = RockMix,
            ["pit"] = DarkMix,
        };

        /// <summary>
        /// A cell's layer weights (Layers order, summing to 1). Scorched ground (grass or
        /// brush burnt away, which the cell itself doesn't remember) and any cover (ash,
        /// oil, grease) paint dark soil; otherwise the terrain decides.
        /// </summary>
        public static IReadOnlyList<float> Weights(Cell c, bool scorched = false)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (scorched || c.Cover != null) return DarkMix;
            return ByTerrain.TryGetValue(c.T, out var mix) ? mix : EarthMix;
        }

        /// <summary>The texel blocks to repaint when these cells change, one rectangle per chunk.</summary>
        public static List<ChunkRect> DirtyRects(TerrainGrid grid, IEnumerable<(int x, int y)> cells)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var rects = new ChunkRects();
            foreach (var (x, y) in cells) rects.Add(grid.TexelRect(x, y));
            return rects.ToList();
        }

        /// <summary>Every chunk's texels under the grid, for the first paint.</summary>
        public static List<ChunkRect> GridRects(TerrainGrid grid) => DirtyRects(grid, grid.Cells());

        /// <summary>
        /// The alphamap block for a rectangle, [z - r.Z, x - r.X, layer]. Texels off the grid
        /// are left all zero: don't paint a rectangle reaching past the grid (DirtyRects and
        /// GridRects never do). scorched(x, y), if given, says which cells are burnt ground.
        /// </summary>
        public static float[,,] Block(TerrainGrid grid, World world, ChunkRect r, Func<int, int, bool> scorched = null)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (world == null) throw new ArgumentNullException(nameof(world));
            var block = new float[r.Height, r.Width, Layers.Count];
            for (int tz = r.Z; tz <= r.ZMax; tz++)
            {
                for (int tx = r.X; tx <= r.XMax; tx++)
                {
                    var (x, y) = grid.CellOfTexel(r.I, r.J, tx, tz);
                    var c = grid.InGrid(x, y) ? world.At(x, y) : null;
                    if (c == null) continue;
                    var w = Weights(c, scorched != null && scorched(x, y));
                    for (int k = 0; k < w.Count; k++) block[tz - r.Z, tx - r.X, k] = w[k];
                }
            }
            return block;
        }
    }
}
