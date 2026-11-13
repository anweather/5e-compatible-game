// A world sketch's say in the bands (J-085; docs/design/world-squares.md, "Draw first", step 2), ported from the paper's
// readSketch (tools/world-paper/index.html). Each land square reads the sketch at its 81 chunk middles; one a quarter or
// more drawn asks a band: half drawn as water is sea, a quarter is low land (a lake wants low land round it), else the
// mean over its chunks of the seed's band held inside each drawn biome's bands (the bands its templates come in), and at
// least the lowest band of any biome drawn over a quarter of it. A drawn high square then lifts the squares round it to
// one band less a square away, as far as their own drawing allows, so a drawn range keeps its height through the
// envelope. The valley, the sea the world keeps and the walls are never moved.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>What a world sketch puts under one world square, and the band it asks.</summary>
    public sealed class SketchReading
    {
        /// <summary>Chunk middles drawn (of 81), and drawn as each biome.</summary>
        public int Drawn;
        public Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The land biome drawn most (null for none), and whether a quarter or more is drawn.</summary>
        public string Land;
        public bool On;
        /// <summary>The band it asks (0 for the sea), if <see cref="On"/>.</summary>
        public int Ask;

        public int Of(string biome) => Counts.TryGetValue(biome, out int n) ? n : 0;
    }

    public sealed partial class WorldPlan
    {
        const int Middles = ChunksPerSquare * ChunksPerSquare;

        /// <summary>Per square, what the world sketch puts under it (null where nothing is drawn or there's no sketch).</summary>
        public SketchReading[] Sketch;

        // A quarter of a square's chunks: drawn enough to ask, or to count as a biome's.
        static bool Quarter(int n) => n * 4 >= Middles;

        (int lo, int hi) SketchBands(string biome)
        {
            if (biome == "ocean") return (0, 0);
            var (lo, hi) = Library.BandsOf(biome);
            return lo < 1 ? (1, Library.Lands - 1) : (lo, hi);
        }

        void ReadSketch(List<(int i, int band)> vLand)
        {
            if (Inputs.Sketch == null || Library.Biomes == null) return;
            int n = Count, top = Library.Lands - 1;
            var placed = new PlacedSketch(Inputs.Sketch, Library.Biomes, Seed, Grid.W, Grid.H, Grid.WrapX);
            Span<long> m = stackalloc long[placed.Count];
            Sketch = new SketchReading[n];
            for (int i = 0; i < n; i++)
            {
                if (Wall[i] != 0) continue;
                var r = new SketchReading();
                int x0 = Grid.X(i) * SquareCells, y0 = Grid.Y(i) * SquareCells, sum = 0;
                for (int b = 0; b < ChunksPerSquare; b++)
                    for (int a = 0; a < ChunksPerSquare; a++)
                    {
                        placed.Membership(x0 + a * ChunkCells + ChunkCells / 2, y0 + b * ChunkCells + ChunkCells / 2, 0, m);
                        int k = placed.Count - 1;
                        while (k >= 0 && m[k] < BiomeMath.One / 2) k--;
                        if (k < 0)
                        {
                            sum += Raw[i];
                            continue;
                        }
                        string biome = placed.NameOf(k);
                        r.Drawn++;
                        r.Counts[biome] = r.Of(biome) + 1;
                        var (lo, hi) = SketchBands(biome);
                        sum += Math.Max(lo, Math.Min(hi, Raw[i]));
                    }
                if (r.Drawn == 0) continue;
                int most = 0, floor = 1;
                foreach (string b in WorldSketch.Biomes)
                {
                    int c = r.Of(b);
                    if (b == "ocean" || c == 0) continue;
                    if (c > most)
                    {
                        most = c;
                        r.Land = b;
                    }
                    if (Quarter(c)) floor = Math.Max(floor, SketchBands(b).lo);
                }
                r.On = Quarter(r.Drawn);
                int water = r.Of("ocean");
                if (water * 2 >= Middles) r.Ask = 0;
                else if (Quarter(water)) r.Ask = 1;
                else r.Ask = Math.Max(1, Math.Min(top, Math.Max(floor, (2 * sum + Middles) / (2 * Middles))));
                Sketch[i] = r;
                if (!r.On || i == Valley || ForcedSea[i]) continue;
                Raw[i] = vLand.Exists(l => l.i == i) ? Math.Max(1, r.Ask) : r.Ask;
            }
            // Foothills: a drawn high square lifts the squares round it, before the envelope.
            int Cap(int j)
            {
                if (Wall[j] != 0 || ForcedSea[j] || j == Valley) return -1;
                var r = Sketch[j];
                if (r == null || !r.On) return Raw[j] == 0 ? -1 : 3;
                if (r.Ask == 0 || Quarter(r.Of("ocean"))) return -1;
                return Math.Max(r.Ask, r.Land != null ? SketchBands(r.Land).hi : 3);
            }
            for (int i = 0; i < n; i++)
            {
                var r = Sketch[i];
                if (r == null || !r.On || Raw[i] < 3 || i == Valley || ForcedSea[i]) continue;
                int a = Raw[i];
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    int d = Grid.Distance(i, j);
                    if (d >= a - 1) continue;
                    int c = Cap(j);
                    if (c < 0) continue;
                    Raw[j] = Math.Max(Raw[j], Math.Min(a - d, c));
                }
            }
        }

        /// <summary>Whether square <paramref name="i"/> is drawn enough to ask its band of the sketch.</summary>
        bool SketchOn(int i) => Sketch != null && Sketch[i] != null && Sketch[i].On;
    }
}
