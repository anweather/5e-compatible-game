// A hills map's generator (docs/design/hills.md): nothing but grass on gentle rolling hills, with no plan squares, pinned
// map, sea, water, roads, villages, trees or things. Its land is a few octaves of integer value noise; each octave's
// lattice period divides the map's width and height, so the land at x and x + CellsW (and y and y + CellsH) is the same
// and the map tiles round a torus with no seam in it. A cell's land is a pure function of its position and the seed, in
// integers only, so every peer makes the same chunk.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The rolling hills' land: octaves of tiling value noise over a base, in land units (0.125 m).</summary>
    public sealed class HillsLand
    {
        /// <summary>The land everywhere is at least this, in land units: above the sea level, so no cell is water.</summary>
        public readonly int Base;
        /// <summary>Each octave's lattice period in cells and its height range in land units, broadest first.</summary>
        public readonly IReadOnlyList<(int period, int amplitude)> Octaves;
        /// <summary>The biome the whole map is (biomes.json's id).</summary>
        public readonly string Biome;
        readonly int seed, w, h;

        // Fixed point for the blend between lattice points.
        const long One = 1 << 16;

        public HillsLand(int seed, int cellsW, int cellsH, int baseUnits, IReadOnlyList<(int period, int amplitude)> octaves, string biome)
        {
            if (octaves == null || octaves.Count == 0) throw new FormatException($"{MapFile.FileName}: \"hills\" needs at least one octave");
            foreach (var (period, amplitude) in octaves)
            {
                if (period < 2 || cellsW % period != 0 || cellsH % period != 0)
                    throw new FormatException($"{MapFile.FileName}: a hills octave's period ({period} cells) must divide the map's size, {cellsW} x {cellsH}, so it tiles");
                if (amplitude < 0) throw new FormatException($"{MapFile.FileName}: a hills octave's amplitude can't be negative ({amplitude})");
            }
            if (baseUnits < ChunkGen.SeaLevel) throw new FormatException($"{MapFile.FileName}: the hills' base ({baseUnits}) is under the sea level ({ChunkGen.SeaLevel})");
            this.seed = seed;
            w = cellsW;
            h = cellsH;
            Base = baseUnits;
            Octaves = octaves;
            Biome = biome;
        }

        /// <summary>The highest land the hills can reach (every octave at its top), in land units.</summary>
        public int Highest
        {
            get
            {
                int top = Base;
                foreach (var o in Octaves) top += o.amplitude;
                return top;
            }
        }

        /// <summary>map.json's generated "hills": { "base", "octaves": [[period, amplitude], ...], "biome" }.</summary>
        public static HillsLand Read(JObject o, int seed, int cellsW, int cellsH)
        {
            var list = new List<(int, int)>();
            foreach (var oct in (JArray)o["octaves"] ?? throw new FormatException($"{MapFile.FileName}: \"hills\" needs \"octaves\""))
                list.Add(((int)oct[0], (int)oct[1]));
            return new HillsLand(seed, cellsW, cellsH, (int?)o["base"] ?? 8, list, (string)o["biome"] ?? "grassland");
        }

        /// <summary>The land at any cell, x and y taken round the torus: the same at (x, y) and (x + k W, y + m H).</summary>
        public int At(int x, int y)
        {
            x = PlanGrid.Mod(x, w);
            y = PlanGrid.Mod(y, h);
            long sum = 0;
            for (int k = 0; k < Octaves.Count; k++)
            {
                var (period, amplitude) = Octaves[k];
                if (amplitude == 0) continue;
                int nx = w / period, ny = h / period;
                int i = x / period, j = y / period;
                long fx = Smooth((long)(x - i * period) * One / period), fy = Smooth((long)(y - j * period) * One / period);
                int i1 = (i + 1) % nx, j1 = (j + 1) % ny;
                long a = Lattice(k, i, j, amplitude), b = Lattice(k, i1, j, amplitude), c = Lattice(k, i, j1, amplitude), d = Lattice(k, i1, j1, amplitude);
                long top = a * One + (b - a) * fx, bottom = c * One + (d - c) * fx;
                sum += (top * One + (bottom - top) * fy) / One;
            }
            return Base + (int)(sum / One);
        }

        // A lattice point's height, 0 .. amplitude, from the seed: the octave's salt keeps octaves apart.
        long Lattice(int octave, int i, int j, int amplitude) => WorldHash.Hash4(seed, 0x4111 + octave, i, j) % (uint)(amplitude + 1);

        // 3t² − 2t³ in Q16: the land's slope is level at each lattice point, so the hills roll without creases.
        static long Smooth(long t) => t * t / One * (3 * One - 2 * t) / One;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "hills base {0}, {1} octaves, up to {2}", Base, Octaves.Count, Highest);
    }

    public sealed partial class ChunkGen
    {
        /// <summary>A hills map's land (docs/design/hills.md), or null for a plan's map.</summary>
        public readonly HillsLand Hills;

        /// <summary>Whether the map joins north to south too (a torus: a hills map's "wrap": "torus").</summary>
        public readonly bool WrapY;

        /// <summary>
        /// A hills map's generator, <paramref name="w"/> × <paramref name="h"/> world squares: no plan, no pinned map, every
        /// cell grass on the hills' land, wrapping east-west and, when <paramref name="wrapY"/>, north-south.
        /// </summary>
        public ChunkGen(HillsLand hills, BiomeTable biomes, int seed, int w, int h, bool wrapY)
        {
            Hills = hills ?? throw new ArgumentNullException(nameof(hills));
            table = biomes ?? throw new ArgumentNullException(nameof(biomes));
            if (table.IndexOf(hills.Biome) < 0) throw new FormatException($"{MapFile.FileName}: the hills' biome {hills.Biome} isn't in {BiomeTable.FileName}");
            this.seed = seed;
            SquaresW = w;
            SquaresH = h;
            WrapX = true;
            WrapY = wrapY;
            landmarkRows = Array.Empty<LandmarkRows>();
        }

        /// <summary>A map cell's y on the map: modulo the map's height where it wraps north-south, else as it is.</summary>
        public int WrapCellY(int y) => WrapY ? PlanGrid.Mod(y, CellsH) : y;

        /// <summary>A map chunk's y on the map: modulo the map's height in chunks where it wraps north-south, else as it is.</summary>
        public int WrapChunkY(int cy) => WrapY ? PlanGrid.Mod(cy, ChunksH) : cy;

        // Chunks between two chunk coordinates along one axis of n, the short way where it wraps.
        static int Along(int a, int b, int n, bool wrap)
        {
            int d = Math.Abs(a - b);
            if (!wrap) return d;
            d %= n;
            return Math.Min(d, n - d);
        }

        // Every cell grass on the hills' land; no things.
        void MakeHills(GeneratedChunk c)
        {
            int x0 = c.X * Size, y0 = c.Y * Size;
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    c.Land[j * Size + i] = Hills.At(x0 + i, y0 + j);
                    c.Cells[j * Size + i] = Grass;
                }
            c.Biome = Hills.Biome;
        }

        // The known chunk's samples: the hills' land at each, all dry, all the hills' biome.
        KnownChunk KnowHills(int cx, int cy)
        {
            var k = new KnownChunk { X = cx, Y = cy, Biome = Hills.Biome };
            int x0 = cx * Size, y0 = cy * Size;
            for (int j = 0; j < KnownChunk.Side; j++)
                for (int i = 0; i < KnownChunk.Side; i++)
                {
                    int q = j * KnownChunk.Side + i;
                    // The last samples are the next chunk's first, across the seams too: the land tiles.
                    k.Heights[q] = Hills.At(x0 + i * KnownChunk.Step, y0 + j * KnownChunk.Step);
                    k.Water[q] = KnownChunk.Dry;
                    k.Biomes[q] = Hills.Biome;
                }
            return k;
        }
    }
}
