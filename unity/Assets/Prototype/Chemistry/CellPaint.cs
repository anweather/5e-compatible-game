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
        /// <summary>Snow lying on the ground (a snow or snowbank cover).</summary>
        public const string Snow = "Snow";

        /// <summary>The layers painted, in the order of the weights.</summary>
        /// <summary>
        /// What the land's layers are made of, for their colours in the rules (ChemGrid.WorldLook.cs): a layer by
        /// the start of its name, first match; the terrain or cover whose colour tints it. Any other ground has no layer.
        /// </summary>
        public static readonly IReadOnlyList<(string layer, string table, string id)> LayerLooks = new[]
        {
            (Moss, "terrain", "brush"), ("Grass", "terrain", "grass"), (Rock, "terrain", "rock"), (Sand, "terrain", "sand"),
            (Soil, "terrain", "mud"), (Earth, "terrain", "dirt"), (Charred, "covers", "ash"), (Snow, "covers", "snow"),
        };

        public static readonly IReadOnlyList<string> Layers = new[] { Grass, Moss, Rock, Sand, Soil, Charred, Earth, Snow };

        // Weights in Layers order: grass, moss, rock, sand, soil, charred, earth, snow.
        static readonly float[] GrassMix = { 1, 0, 0, 0, 0, 0, 0, 0 };
        static readonly float[] UndergrowthMix = { 0, 1, 0, 0, 0, 0, 0, 0 };
        // Green shoots (Plant Growth): grass coming through bare earth, not yet grassy enough for detail grass.
        static readonly float[] ShootsMix = { 0.45f, 0, 0, 0.1f, 0, 0, 0.45f, 0 };
        static readonly float[] StumpMix = { 0, 0, 0, 0, 0, 0.6f, 0.4f, 0 };
        // A fallen tree: torn-up earth and moss where it lay.
        static readonly float[] DeadfallMix = { 0, 0.4f, 0, 0, 0, 0, 0.6f, 0 };
        static readonly float[] RockMix = { 0, 0, 1, 0, 0, 0, 0, 0 };
        // A raised wall: rock, spread over its side slopes (see CellMixes.Spread).
        static readonly float[] WallMix = { 0, 0, 1, 0, 0, 0, 0, 0 };
        // Bare earth, a little sand in it so it reads worn rather than wet.
        static readonly float[] EarthMix = { 0, 0, 0, 0.2f, 0, 0, 0.8f, 0 };
        // Dug ground (pits, trenches): darker, stonier earth.
        static readonly float[] DugMix = { 0, 0, 0, 0, 0.3f, 0, 0.7f, 0 };
        // Scorched ground and ash.
        static readonly float[] CharredMix = { 0, 0, 0, 0, 0, 0.85f, 0.15f, 0 };
        // Sand (and glass, the sand a strike fused: its glaze is a film on top, CellSlickMesh).
        static readonly float[] SandMix = { 0, 0, 0, 1, 0, 0, 0, 0 };
        // Mud: dark, wet earth, stony soil and a little char for the darkness (its wet sheen is a film, CellSlickMesh).
        static readonly float[] MudMix = { 0, 0, 0, 0, 0.35f, 0.2f, 0.45f, 0 };
        // Snow lying on the ground.
        static readonly float[] SnowMix = { 0, 0, 0, 0, 0, 0, 0, 1 };

        const int CharredIndex = 5, EarthIndex = 6;

        /// <summary>Terrains with their own look; anything else (dirt, bridges, machines) is bare earth.</summary>
        static readonly Dictionary<string, float[]> ByTerrain = new Dictionary<string, float[]>
        {
            ["grass"] = GrassMix,
            ["brush"] = UndergrowthMix,
            ["tree"] = UndergrowthMix,
            ["sprouts"] = ShootsMix,
            ["sapling"] = GrassMix,
            ["stump"] = StumpMix,
            ["deadfall"] = DeadfallMix,
            ["rock"] = RockMix,
            ["rubble"] = RockMix,
            ["stone"] = RockMix,
            ["pillar"] = RockMix,
            ["bedrock"] = RockMix,
            ["pit"] = DugMix,
            ["river"] = DugMix,
            ["sand"] = SandMix,
            ["glass"] = SandMix,
            ["mud"] = MudMix,
            ["brick"] = RockMix,
            ["lava"] = CharredMix,
            ["lava-pool"] = CharredMix,
        };

        /// <summary>
        /// A cell's layer weights (Layers order, summing to 1). Snow (and a snow bank) paints
        /// snow; scorched ground (grass or brush burnt away, which the cell itself doesn't
        /// remember) and ash paint charred; a film of oil or grease is drawn on top
        /// (CellSlickMesh) and leaves the ground as it is.
        /// </summary>
        public static IReadOnlyList<float> Weights(Cell c, bool scorched = false) => Weights(c, scorched, null);

        /// <summary>
        /// The same, with the rules in force naming the snow covers (sim weather.snow) and the ash (sim fire.ash, a
        /// material's burn.ash, or a cover whose look is a scorch): a mod's own paint as theirs. Null rules: the base ids.
        /// </summary>
        public static IReadOnlyList<float> Weights(Cell c, bool scorched, Rules rules)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (IsSnowy(c, rules)) return SnowMix;
            if (scorched || IsAsh(c.Cover, rules)) return CharredMix;
            return ByTerrain.TryGetValue(c.T, out var mix) ? mix : EarthMix;
        }

        /// <summary>
        /// Whether a cover is ash, painted charred: what fire leaves (sim.json fire.ash), what any terrain or cover
        /// burns to (its burn.ash), or a cover whose look family is scorch. Null rules: the base "ash".
        /// </summary>
        public static bool IsAsh(string cover, Rules rules)
        {
            if (cover == null) return false;
            if (rules == null) return cover == "ash";
            if (cover == rules.Sim.Fire.Ash) return true;
            if (!rules.Covers.TryGetValue(cover, out var def)) return false;
            if (def.Look?.Family == "scorch") return true;
            foreach (var t in rules.Terrain.Values)
                if (t.BurnAsh == cover) return true;
            foreach (var o in rules.Covers.Values)
                if (o.BurnAsh == cover) return true;
            return false;
        }

        /// <summary>
        /// Weights as the world shapes them too: a raised wall is rock (spreading over its side
        /// slopes) and grassy ground dug below its level is bare earth.
        /// </summary>
        public static IReadOnlyList<float> Mix(World world, Cell c, bool scorched = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.IsRaisedWall(c)) return WallMix;
            var mix = Weights(c, scorched, world.Rules);
            return c.Height < 0 && IsGrassy(mix) ? DugMix : mix;
        }

        /// <summary>Weights at a cell, as the world and the land stamps shape it: what a stamp has made steep is bare rock where its face is tall (a crag) and bare earth where it's short (a bump's bank), under anything lying on it.</summary>
        public static IReadOnlyList<float> Mix(World world, int x, int y, bool scorched = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var c = world.At(x, y);
            if (scorched || c.Cover != null) return Mix(world, c, scorched);
            if (world.LandDrop(x, y) >= World.LandSteep) return world.LandFace(x, y) >= World.LandCrag ? WallMix : EarthMix;
            return world.LandBank(x, y) ? EarthMix : Mix(world, c, scorched);
        }

        /// <summary>Whether snow lies on a cell: the base rules' snow covers (World.SnowCover or BankCover).</summary>
        public static bool IsSnowy(Cell c) => IsSnowy(c, null);

        /// <summary>Whether snow lies on a cell: the covers the rules' snow lays and drifts into (sim.json weather.snow.cover, bank); null rules, the base ones.</summary>
        public static bool IsSnowy(Cell c, Rules rules)
        {
            if (c?.Cover == null) return false;
            if (rules == null) return c.Cover == World.SnowCover || c.Cover == World.BankCover;
            var w = rules.Sim.Weather;
            return c.Cover == w.SnowCover || c.Cover == w.BankCover;
        }

        /// <summary>Whether a cover is the rules' snow bank (sim.json weather.snow.bank), which stands the ground higher; null rules, the base one.</summary>
        public static bool IsBank(string cover, Rules rules) => cover != null && cover == (rules == null ? World.BankCover : rules.Sim.Weather.BankCover);

        /// <summary>Whether layer weights (Layers order) read as grass that detail grass grows on.</summary>
        public static bool IsGrassy(IReadOnlyList<float> w) => w[0] + w[1] >= 0.5f;

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
        public static PaintBlock Block(TerrainGrid grid, ChunkRect r, Func<int, int, IReadOnlyList<float>> paintOf) =>
            Block(grid, r, paintOf, null);

        /// <summary>
        /// The same, with each texel's noise kept in <paramref name="noise"/> from one call to the next
        /// (null works it out afresh): the texels come out the same either way.
        /// </summary>
        public static PaintBlock Block(TerrainGrid grid, ChunkRect r, Func<int, int, IReadOnlyList<float>> paintOf, TexelNoise noise)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (paintOf == null) throw new ArgumentNullException(nameof(paintOf));
            var block = new PaintBlock(r, Layers.Count);
            var cells = new CellMixes(grid, r, paintOf);
            int p = grid.SamplesPerCell(r.I, r.J), res = grid.TexelResolution(r.I, r.J);
            var kept = noise?.Of(grid, r.I, r.J);
            var mix = new float[Layers.Count];
            for (int tz = r.Z; tz <= r.ZMax; tz++)
            {
                for (int tx = r.X; tx <= r.XMax; tx++)
                {
                    float u = r.I * TerrainGrid.ChunkCells + (tx + 0.5f) / p;
                    float v = r.J * TerrainGrid.ChunkCells + (tz + 0.5f) / p;
                    var noiseAt = TexelNoise.At(kept, (tz * res + tx) * TexelNoise.Values, u, v);
                    float cover = Texel(cells, u, v, noiseAt, mix);
                    block.Cover[tz - r.Z, tx - r.X] = cover;
                    for (int k = 0; k < mix.Length; k++) block.Mix[tz - r.Z, tx - r.X, k] = mix[k];
                }
            }
            return block;
        }

        // How far a texel's point wanders (in cells, at most half, so only the 3×3 cells around
        // it matter), how wide the fade from paint to base is and how far it slides.
        const float Wander = 0.9f, WanderScale = 0.8f, FadeFrom = 0.3f, FadeTo = 0.7f, Slide = 0.36f;

        /// <summary>
        /// A texel's noise, from where it is alone: what ValueNoise gives for how far its point wanders
        /// (U, V), how much of its char shows as earth (Patches) and how far its fade slides (Fade). The
        /// texel's sums use them as they used the calls, so it paints the same bits kept or afresh.
        /// </summary>
        internal readonly struct Noise
        {
            public Noise(float u, float v, float patches, float fade)
            {
                U = u;
                V = v;
                Patches = patches;
                Fade = fade;
            }

            public float U { get; }
            public float V { get; }
            public float Patches { get; }
            public float Fade { get; }
        }

        // The noise of the texel at world-cell point (u, v).
        internal static Noise NoiseAt(float u, float v) => new Noise(
            ValueNoise.Twice(u * WanderScale, v * WanderScale, 1), ValueNoise.Twice(u * WanderScale, v * WanderScale, 2),
            ValueNoise.Twice(u * 0.6f, v * 0.6f, 3), ValueNoise.At(u * 0.3f, v * 0.3f, 4));

        // One texel at world-cell point (u, v), with its noise: fills mix (normalised) and returns its cover.
        static float Texel(CellMixes cells, float u, float v, Noise noise, float[] mix)
        {
            Array.Clear(mix, 0, mix.Length);
            float wu = u + Wander * (noise.U - 0.5f);
            float wv = v + Wander * (noise.V - 0.5f);
            float bu = wu - 0.5f, bv = wv - 0.5f;
            int cu = (int)Math.Floor(bu), cv = (int)Math.Floor(bv);
            float fu = bu - cu, fv = bv - cv;
            float raw = cells.Add(cu, cv, (1f - fu) * (1f - fv), mix) + cells.Add(cu + 1, cv, fu * (1f - fv), mix) +
                        cells.Add(cu, cv + 1, (1f - fu) * fv, mix) + cells.Add(cu + 1, cv + 1, fu * fv, mix);
            if (raw <= 0f) return 0f;
            for (int k = 0; k < mix.Length; k++) mix[k] /= raw;
            Mottle(mix, noise.Patches);
            // The fade itself slides in and out over a few cells, so a long straight run of
            // painted cells (a pad's side) bulges and narrows instead of following the row.
            float slide = Slide * (noise.Fade - 0.5f);
            return ValueNoise.SmoothStep(FadeFrom + slide, FadeTo + slide, raw);
        }

        // Burnt ground isn't one flat colour: some of its char shows as earth, in patches.
        static void Mottle(float[] mix, float noise)
        {
            float moved = mix[CharredIndex] * 0.45f * noise;
            mix[CharredIndex] -= moved;
            mix[EarthIndex] += moved;
        }

        /// <summary>The mixes of the cells around a texel rectangle, looked up once, by world cell, as arrays with their spreads.</summary>
        sealed class CellMixes
        {
            static readonly float[] Keep = Array.Empty<float>();
            readonly float[][] mixes;
            readonly float[] spreads;
            readonly int u0, v0, width, height;

            public CellMixes(TerrainGrid grid, ChunkRect r, Func<int, int, IReadOnlyList<float>> paintOf)
            {
                int p = grid.SamplesPerCell(r.I, r.J);
                u0 = r.I * TerrainGrid.ChunkCells + r.X / p - 1;
                v0 = r.J * TerrainGrid.ChunkCells + r.Z / p - 1;
                width = r.I * TerrainGrid.ChunkCells + r.XMax / p + 1 - u0 + 1;
                height = r.J * TerrainGrid.ChunkCells + r.ZMax / p + 1 - v0 + 1;
                mixes = new float[width * height][];
                spreads = new float[width * height];
                for (int b = 0; b < height; b++)
                {
                    for (int a = 0; a < width; a++)
                    {
                        var (x, y) = grid.GridCell(u0 + a, v0 + b);
                        var mix = grid.InGrid(x, y) ? paintOf(x, y) : null;
                        int n = b * width + a;
                        mixes[n] = mix == null ? Keep : mix as float[] ?? new List<float>(mix).ToArray();
                        if (mix != null) spreads[n] = Spread(mix);
                    }
                }
            }

            /// <summary>How far a mix reaches past its cell: 1 to the cell's border, more for walls.</summary>
            // Snow reaches a little past its cell too, over its bank's rounded skirt (CellHeightfield.BankAt).
            static float Spread(IReadOnlyList<float> mix)
            {
                if (ReferenceEquals(mix, WallMix)) return 2f;
                return ReferenceEquals(mix, SnowMix) ? 1.3f : 1f;
            }

            /// <summary>
            /// Adds world cell (u, v)'s mix into <paramref name="into"/> at a bilinear weight (scaled by
            /// its spread) and returns that weight: 0 where the base shows (or outside the rectangle's cells).
            /// </summary>
            public float Add(int u, int v, float weight, float[] into)
            {
                int a = u - u0, b = v - v0;
                if (a < 0 || b < 0 || a >= width || b >= height) return 0f;
                int n = b * width + a;
                var cell = mixes[n];
                if (cell.Length == 0) return 0f;
                float w = weight * spreads[n];
                for (int k = 0; k < into.Length; k++) into[k] += w * cell[k];
                return w;
            }
        }
    }

    /// <summary>
    /// Each texel's noise for its paint (CellPaint.Block), by chunk: it depends only on where the
    /// texel is, so it's worked out the first time the texel paints and kept. A shower's ash repaints
    /// the same texels for minutes (#70). About 1 MB a full-detail chunk and 64 KB a coarse one, made
    /// for the chunks that paint. Visual only, like ValueNoise.
    /// </summary>
    public sealed class TexelNoise
    {
        internal const int Values = 4;
        readonly Dictionary<(int i, int j), float[]> chunks = new Dictionary<(int i, int j), float[]>();

        /// <summary>The chunk's texels' noise, Values a texel in row order; NaN where not worked out yet.</summary>
        internal float[] Of(TerrainGrid grid, int i, int j)
        {
            if (chunks.TryGetValue((i, j), out var kept)) return kept;
            int res = grid.TexelResolution(i, j);
            kept = new float[res * res * Values];
            for (int n = 0; n < kept.Length; n++) kept[n] = float.NaN;
            chunks[(i, j)] = kept;
            return kept;
        }

        /// <summary>A texel's noise as kept at <paramref name="at"/> (worked out now if it isn't yet), or afresh with nothing kept.</summary>
        internal static CellPaint.Noise At(float[] kept, int at, float u, float v)
        {
            if (kept == null) return CellPaint.NoiseAt(u, v);
            if (!float.IsNaN(kept[at])) return new CellPaint.Noise(kept[at], kept[at + 1], kept[at + 2], kept[at + 3]);
            var fresh = CellPaint.NoiseAt(u, v);
            kept[at] = fresh.U;
            kept[at + 1] = fresh.V;
            kept[at + 2] = fresh.Patches;
            kept[at + 3] = fresh.Fade;
            return fresh;
        }
    }
}
