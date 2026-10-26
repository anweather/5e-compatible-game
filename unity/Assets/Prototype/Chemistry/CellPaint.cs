// How the cells paint the chunk terrains (docs/WORLD_STREAMING.md, part 1): each
// cell's terrain, cover and scorch as weights of the valley's terrain layers,
// laid over the valley's own paint with soft, irregular edges. Pure maths, no
// Unity: the view looks the layers up by name in the terrain's layers and hands
// the blocks to TerrainData.SetAlphamaps.
//
// Not a port: the sandbox has no terrain.
//
// Soft edges: every alphamap texel (4×4 per cell on a full-detail chunk, 1 on a
// coarse one) looks at its point nudged by smooth noise (ValueNoise), then
// blends the four cells whose centres surround that point. A cell either paints
// its mix over the base or keeps the base (the caller decides which, see
// Block), so a burnt patch or a bare pad fades into the meadow along a wobbly
// line that ignores cell borders, and only the cell and its 8 neighbours'
// texels depend on it. Raised walls spread further, over their side slopes.
// Arrays are Unity's alphamap order: [row = z, column = x, layer], layers in
// Layers order.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>
    /// What cells paint over a rectangle of alphamap texels: per texel the mix (Layers order,
    /// summing to 1 where painted) and how much of it covers the base (0 keeps the base, 1 hides it).
    /// </summary>
    public sealed class PaintBlock
    {
        public PaintBlock(ChunkRect rect, int layers)
        {
            Rect = rect;
            Mix = new float[rect.Height, rect.Width, layers];
            Cover = new float[rect.Height, rect.Width];
        }

        public ChunkRect Rect { get; }
        /// <summary>[z - Rect.Z, x - Rect.X, layer].</summary>
        public float[,,] Mix { get; }
        /// <summary>[z - Rect.Z, x - Rect.X], 0..1.</summary>
        public float[,] Cover { get; }
    }

    public static class CellPaint
    {
        // The valley's terrain layers (ValleyBuilder.LayerNames) that cells paint.
        public const string Grass = "Grass_A";
        public const string Moss = "Grass_Moss";
        public const string Rock = "Rock";
        public const string Sand = "Sand";
        public const string Soil = "Soil_Rocks";
        /// <summary>Burnt ground: dark charcoal, apart from any bare earth.</summary>
        public const string Charred = "Charred";
        /// <summary>Bare, worn or dug earth.</summary>
        public const string Earth = "Earth";

        /// <summary>The layers painted, in the order of the weights. The valley's Snow is never painted.</summary>
        public static readonly IReadOnlyList<string> Layers = new[] { Grass, Moss, Rock, Sand, Soil, Charred, Earth };

        // Weights in Layers order: grass, moss, rock, sand, soil, charred, earth.
        static readonly float[] GrassMix = { 1, 0, 0, 0, 0, 0, 0 };
        static readonly float[] UndergrowthMix = { 0, 1, 0, 0, 0, 0, 0 };
        static readonly float[] StumpMix = { 0, 0, 0, 0, 0, 0.6f, 0.4f };
        // A fallen tree: torn-up earth and moss where it lay.
        static readonly float[] DeadfallMix = { 0, 0.4f, 0, 0, 0, 0, 0.6f };
        static readonly float[] RockMix = { 0, 0, 1, 0, 0, 0, 0 };
        // A raised wall: rock, spread over its side slopes (see Spread).
        static readonly float[] WallMix = { 0, 0, 1, 0, 0, 0, 0 };
        // Bare earth, a little sand in it so it reads worn rather than wet.
        static readonly float[] EarthMix = { 0, 0, 0, 0.2f, 0, 0, 0.8f };
        // Dug ground (pits, trenches): darker, stonier earth.
        static readonly float[] DugMix = { 0, 0, 0, 0, 0.3f, 0, 0.7f };
        // Scorched ground and ash.
        static readonly float[] CharredMix = { 0, 0, 0, 0, 0, 0.85f, 0.15f };

        const int CharredIndex = 5, EarthIndex = 6;

        /// <summary>Terrains with their own look; anything else (dirt, bridges, machines) is bare earth.</summary>
        static readonly Dictionary<string, float[]> ByTerrain = new Dictionary<string, float[]>
        {
            ["grass"] = GrassMix,
            ["brush"] = UndergrowthMix,
            ["tree"] = UndergrowthMix,
            ["stump"] = StumpMix,
            ["deadfall"] = DeadfallMix,
            ["rock"] = RockMix,
            ["rubble"] = RockMix,
            ["stone"] = RockMix,
            ["pillar"] = RockMix,
            ["bedrock"] = RockMix,
            ["pit"] = DugMix,
            ["river"] = DugMix,
        };

        /// <summary>
        /// A cell's layer weights (Layers order, summing to 1). Scorched ground (grass or brush
        /// burnt away, which the cell itself doesn't remember) and ash paint charred; a film of
        /// oil or grease is drawn on top (CellSlickMesh) and leaves the ground as it is.
        /// </summary>
        public static IReadOnlyList<float> Weights(Cell c, bool scorched = false)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (scorched || c.Cover == "ash") return CharredMix;
            return ByTerrain.TryGetValue(c.T, out var mix) ? mix : EarthMix;
        }

        /// <summary>
        /// Weights as the world shapes them too: a raised wall is rock (spreading over its side
        /// slopes) and grassy ground dug below its level is bare earth.
        /// </summary>
        public static IReadOnlyList<float> Mix(World world, Cell c, bool scorched = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.IsRaisedWall(c)) return WallMix;
            var mix = Weights(c, scorched);
            return c.Height < 0 && IsGrassy(mix) ? DugMix : mix;
        }

        /// <summary>Whether layer weights (Layers order) read as grass that detail grass grows on.</summary>
        public static bool IsGrassy(IReadOnlyList<float> w) => w[0] + w[1] >= 0.5f;

        /// <summary>How far a mix reaches past its cell: 1 to the cell's border, more for walls.</summary>
        static float Spread(IReadOnlyList<float> mix) => ReferenceEquals(mix, WallMix) ? 2f : 1f;

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

        /// <summary>The cells and their 8 neighbours (on the grid): every cell whose texels depend on them.</summary>
        public static HashSet<(int x, int y)> Around(TerrainGrid grid, IEnumerable<(int x, int y)> cells)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var around = new HashSet<(int x, int y)>();
            foreach (var (x, y) in cells)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (grid.InGrid(x + dx, y + dy)) around.Add((x + dx, y + dy));
            return around;
        }

        /// <summary>
        /// The cells' texels as few rectangles: cells side by side in a row of one chunk join
        /// into one strip, so a scattered fire front isn't one huge rectangle.
        /// </summary>
        public static List<ChunkRect> Runs(TerrainGrid grid, IEnumerable<(int x, int y)> cells)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var sorted = new List<(int x, int y)>(cells);
            sorted.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var runs = new List<ChunkRect>();
            for (int n = 0; n < sorted.Count; n++)
            {
                var r = grid.TexelRect(sorted[n].x, sorted[n].y);
                bool joins = n > 0 && sorted[n - 1].y == sorted[n].y && sorted[n - 1].x == sorted[n].x - 1 &&
                             runs[runs.Count - 1].I == r.I && runs[runs.Count - 1].J == r.J;
                if (joins) runs[runs.Count - 1] = runs[runs.Count - 1].Union(r);
                else runs.Add(r);
            }
            return runs;
        }

        /// <summary>
        /// What the cells paint over a rectangle of texels. <paramref name="paintOf"/>(x, y) is the
        /// mix a grid cell paints over the base (from Mix or Weights), or null where the base shows
        /// (grassy ground the valley already paints, a cell as it was baked); cells off the grid
        /// keep the base.
        /// </summary>
        public static PaintBlock Block(TerrainGrid grid, ChunkRect r, Func<int, int, IReadOnlyList<float>> paintOf)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (paintOf == null) throw new ArgumentNullException(nameof(paintOf));
            var block = new PaintBlock(r, Layers.Count);
            var cells = new CellMixes(grid, r, paintOf);
            int p = grid.SamplesPerCell(r.I, r.J);
            var mix = new float[Layers.Count];
            for (int tz = r.Z; tz <= r.ZMax; tz++)
            {
                for (int tx = r.X; tx <= r.XMax; tx++)
                {
                    float u = r.I * TerrainGrid.ChunkCells + (tx + 0.5f) / p;
                    float v = r.J * TerrainGrid.ChunkCells + (tz + 0.5f) / p;
                    float cover = Texel(cells, u, v, mix);
                    block.Cover[tz - r.Z, tx - r.X] = cover;
                    for (int k = 0; k < mix.Length; k++) block.Mix[tz - r.Z, tx - r.X, k] = mix[k];
                }
            }
            return block;
        }

        // How far a texel's point wanders (in cells, at most half, so only the 3×3 cells around
        // it matter), how wide the fade from paint to base is and how far it slides.
        const float Wander = 0.9f, WanderScale = 0.8f, FadeFrom = 0.3f, FadeTo = 0.7f, Slide = 0.36f;

        // One texel at world-cell point (u, v): fills mix (normalised) and returns its cover.
        static float Texel(CellMixes cells, float u, float v, float[] mix)
        {
            Array.Clear(mix, 0, mix.Length);
            float wu = u + Wander * (ValueNoise.Twice(u * WanderScale, v * WanderScale, 1) - 0.5f);
            float wv = v + Wander * (ValueNoise.Twice(u * WanderScale, v * WanderScale, 2) - 0.5f);
            float bu = wu - 0.5f, bv = wv - 0.5f;
            int cu = (int)Math.Floor(bu), cv = (int)Math.Floor(bv);
            float fu = bu - cu, fv = bv - cv;
            float raw = Add(cells.At(cu, cv), (1f - fu) * (1f - fv), mix) + Add(cells.At(cu + 1, cv), fu * (1f - fv), mix) +
                        Add(cells.At(cu, cv + 1), (1f - fu) * fv, mix) + Add(cells.At(cu + 1, cv + 1), fu * fv, mix);
            if (raw <= 0f) return 0f;
            for (int k = 0; k < mix.Length; k++) mix[k] /= raw;
            Mottle(mix, u, v);
            // The fade itself slides in and out over a few cells, so a long straight run of
            // painted cells (a pad's side) bulges and narrows instead of following the row.
            float slide = Slide * (ValueNoise.At(u * 0.3f, v * 0.3f, 4) - 0.5f);
            return ValueNoise.SmoothStep(FadeFrom + slide, FadeTo + slide, raw);
        }

        // Adds a cell's mix at a bilinear weight (scaled by its spread) and returns that weight.
        static float Add(IReadOnlyList<float> cell, float weight, float[] mix)
        {
            if (cell.Count == 0) return 0f;
            float w = weight * Spread(cell);
            for (int k = 0; k < mix.Length; k++) mix[k] += w * cell[k];
            return w;
        }

        // Burnt ground isn't one flat colour: some of its char shows as earth, in patches.
        static void Mottle(float[] mix, float u, float v)
        {
            float moved = mix[CharredIndex] * 0.45f * ValueNoise.Twice(u * 0.6f, v * 0.6f, 3);
            mix[CharredIndex] -= moved;
            mix[EarthIndex] += moved;
        }

        /// <summary>The mixes of the cells around a texel rectangle, looked up once, by world cell.</summary>
        sealed class CellMixes
        {
            static readonly float[] Keep = Array.Empty<float>();
            readonly IReadOnlyList<float>[] mixes;
            readonly int u0, v0, width, height;

            public CellMixes(TerrainGrid grid, ChunkRect r, Func<int, int, IReadOnlyList<float>> paintOf)
            {
                int p = grid.SamplesPerCell(r.I, r.J);
                u0 = r.I * TerrainGrid.ChunkCells + r.X / p - 1;
                v0 = r.J * TerrainGrid.ChunkCells + r.Z / p - 1;
                width = r.I * TerrainGrid.ChunkCells + r.XMax / p + 1 - u0 + 1;
                height = r.J * TerrainGrid.ChunkCells + r.ZMax / p + 1 - v0 + 1;
                mixes = new IReadOnlyList<float>[width * height];
                for (int b = 0; b < height; b++)
                {
                    for (int a = 0; a < width; a++)
                    {
                        var (x, y) = grid.GridCell(u0 + a, v0 + b);
                        mixes[b * width + a] = (grid.InGrid(x, y) ? paintOf(x, y) : null) ?? Keep;
                    }
                }
            }

            /// <summary>The mix of world cell (u, v); empty where the base shows (or outside the rectangle's cells).</summary>
            public IReadOnlyList<float> At(int u, int v)
            {
                int a = u - u0, b = v - v0;
                return a < 0 || b < 0 || a >= width || b >= height ? Keep : mixes[b * width + a];
            }
        }
    }
}
