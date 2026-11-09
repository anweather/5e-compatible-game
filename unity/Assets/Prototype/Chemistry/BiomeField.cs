// Which biomes lie where, over the whole plane (J-079, docs/design/world-nine-options.md phase 2). Port of Punchy's
// BiomeSampling (Assets/Game/Scripts/Terrain/Biomes/BiomeData.cs, https://github.com/Skeleman/Punchy at 4601262, used
// with Zachary Franklin's permission; ThirdParty/Punchy/README.md): domain-warped Voronoi regions, four climate
// channels read at each region's centre, a biome picked per region by a seeded race weighted by
// Frequency × e^-(misfit²), and the top four biomes blended at a point by how much farther each region is than the
// nearest. Changed from Punchy:
//   - integers only (BiomeMath: Q16, value noise on WorldHash), so peers agree bit for bit; no float state;
//   - sizes in cells, a region about one valley square (576 cells, 864 m) where Punchy's are 1400 m;
//   - anchors in place of the spawn cell: a 3 × 3 layout of valley-sized squares names a biome per square (the valley's
//     Temperate south-west), the region nearest each square's centre takes it outright, and the other regions with
//     their centre in the square favour it, so the climate draws the borders and the variety inside;
//   - Punchy's realms and baked cell tables are left out (anchors do the curating; regions are cheap enough to work
//     out, see BiomeFieldTests' timing).
// Pure functions of the seed and the cell: a chunk comes out the same whichever peer asks, in any order.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A climate in Q16, each 0..1: altitude, temperature (falling with altitude), humidity, volcanism.</summary>
    public readonly struct BiomeClimate
    {
        public readonly long Altitude, Temperature, Humidity, Volcanism;

        public BiomeClimate(long altitude, long temperature, long humidity, long volcanism)
        {
            Altitude = altitude;
            Temperature = temperature;
            Humidity = humidity;
            Volcanism = volcanism;
        }

        public long this[int channel] => channel switch { 0 => Altitude, 1 => Temperature, 2 => Humidity, _ => Volcanism };
    }

    /// <summary>Up to four biomes (row indices into BiomeTable.Rows) and their Q16 weights at a cell, summing to One.</summary>
    public struct BiomeBlend
    {
        public int Id0, Id1, Id2, Id3;
        public long W0, W1, W2, W3;

        public int Id(int i) => i switch { 0 => Id0, 1 => Id1, 2 => Id2, _ => Id3 };
        public long Weight(int i) => i switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };

        void Set(int i, int id, long w)
        {
            switch (i)
            {
                case 0: Id0 = id; W0 = w; break;
                case 1: Id1 = id; W1 = w; break;
                case 2: Id2 = id; W2 = w; break;
                default: Id3 = id; W3 = w; break;
            }
        }

        /// <summary>Adds weight to a biome: to its slot, or to an empty or weaker slot (Punchy's BiomeWeights.Add).</summary>
        public void Add(int id, long w)
        {
            if (w <= 0) return;
            for (int i = 0; i < 4; i++)
            {
                if (Weight(i) > 0 && Id(i) == id)
                {
                    Set(i, id, Weight(i) + w);
                    return;
                }
            }
            int slot = 0;
            for (int i = 1; i < 4; i++)
                if (Weight(i) < Weight(slot)) slot = i;
            if (w > Weight(slot)) Set(slot, id, w);
        }

        /// <summary>Scales the weights to sum to One exactly; what rounding leaves goes to the strongest.</summary>
        public void Normalize()
        {
            long sum = W0 + W1 + W2 + W3;
            if (sum <= 0)
            {
                Set(0, Id0, BiomeMath.One);
                return;
            }
            long left = BiomeMath.One;
            for (int i = 0; i < 4; i++)
            {
                long w = Weight(i) * BiomeMath.One / sum;
                Set(i, Id(i), w);
                left -= w;
            }
            int top = DominantSlot();
            Set(top, Id(top), Weight(top) + left);
        }

        int DominantSlot()
        {
            int best = 0;
            for (int i = 1; i < 4; i++)
                if (Weight(i) > Weight(best)) best = i;
            return best;
        }

        /// <summary>The strongest biome's row index.</summary>
        public int Dominant => Id(DominantSlot());
    }

    /// <summary>One biome as a row of world/biomes.json: climate ranges in Q16, and the shape numbers phase 3 will read.</summary>
    public sealed class BiomeRow
    {
        public string Id, Name;
        public bool On, Ocean;
        public long Frequency;            // Q16
        public long[] ClimateMin, ClimateMax; // Q16, altitude, temperature, humidity, volcanism
        public uint NameHash;
        public JObject Shape;              // base elevation, relief kind and sizes, kept for land from biomes
        public double Rainfall;
    }

    /// <summary>The biome rows and the field's settings, from world/biomes.json.</summary>
    public sealed class BiomeTable
    {
        public const string FileName = "biomes.json";
        public static readonly string[] Channels = { "altitude", "temperature", "humidity", "volcanism" };

        public List<BiomeRow> Rows { get; } = new List<BiomeRow>();
        public int Seed, RegionCells, WarpCells, ClimateCells, BlendCells, ShapeBlendCells, SquareCells;
        public long ClimateInfluence, AnchorBias; // Q16
        /// <summary>
        /// Cells after which the field repeats east-west, a world that wraps (0 for none): regions, warp, climate and
        /// hotspots all repeat, so the land runs on unbroken across the seam. A whole number of regions.
        /// </summary>
        public int WrapCells;

        /// <summary>The anchor biome's row index per square, [x, y] with y north; −1 for none.</summary>
        public int[,] Layout;

        public int IndexOf(string id) => Rows.FindIndex(r => r.Id == id);

        public static BiomeTable Load(string path) => Parse(File.ReadAllText(path));

        public static BiomeTable Parse(string json)
        {
            var o = JObject.Parse(json);
            var t = new BiomeTable();
            foreach (var b in (JArray)o["biomes"])
            {
                var row = new BiomeRow
                {
                    Id = (string)b["id"],
                    Name = (string)b["name"],
                    On = (bool?)b["on"] ?? false,
                    Ocean = (bool?)b["ocean"] ?? false,
                    Frequency = BiomeMath.FromDouble((double)b["frequency"]),
                    ClimateMin = new long[4],
                    ClimateMax = new long[4],
                    Shape = (JObject)b["shape"],
                    Rainfall = (double?)b["rainfall"] ?? 0,
                };
                for (int c = 0; c < 4; c++)
                {
                    var range = (JArray)b["climate"][Channels[c]];
                    row.ClimateMin[c] = BiomeMath.FromDouble((double)range[0]);
                    row.ClimateMax[c] = BiomeMath.FromDouble((double)range[1]);
                }
                row.NameHash = BiomeMath.NameHash(row.Id);
                if (t.IndexOf(row.Id) >= 0) throw new InvalidDataException($"biomes.json: {row.Id} twice");
                t.Rows.Add(row);
            }
            var f = (JObject)o["field"];
            t.Seed = (int)f["seed"];
            t.RegionCells = (int)f["regionCells"];
            t.WarpCells = (int)f["warpCells"];
            t.ClimateCells = (int)f["climateCells"];
            t.BlendCells = (int)f["blendCells"];
            t.ShapeBlendCells = (int)f["shapeBlendCells"];
            t.SquareCells = (int)f["squareCells"];
            t.ClimateInfluence = BiomeMath.FromDouble((double)f["climateInfluence"]);
            t.AnchorBias = BiomeMath.FromDouble((double)f["anchorBias"]);
            var layout = (JArray)o["layout"]; // north row first, as on a map
            int rows = layout.Count, cols = ((JArray)layout[0]).Count;
            t.Layout = new int[cols, rows];
            for (int r = 0; r < rows; r++)
            {
                var line = (JArray)layout[r];
                for (int x = 0; x < cols; x++)
                {
                    var id = (string)line[x];
                    int i = id == null ? -1 : t.IndexOf(id);
                    if (id != null && (i < 0 || !t.Rows[i].On || t.Rows[i].Ocean))
                        throw new InvalidDataException($"biomes.json: the layout names {id}, which isn't a land biome that's on");
                    t.Layout[x, rows - 1 - r] = i;
                }
            }
            return t;
        }
    }

    public sealed class BiomeField
    {
        const long One = BiomeMath.One;
        const int SaltWarpX = 0x5101, SaltWarpY = 0x5102, SaltJitter = 0x5103, SaltAlt = 0x5104, SaltAlt2 = 0x5105,
            SaltTemp = 0x5106, SaltHum = 0x5107, SaltHum2 = 0x5108, SaltVolc = 0x5109, SaltRace = 0x510a;

        const long JitterLow = 6554, JitterSpan = 52428; // a feature point at 0.1 + 0.8 × u, as Punchy's
        const int ChunkSize = 64;

        readonly BiomeTable table;
        readonly int seed;
        readonly int[] land; // row indices of the land biomes that are on
        readonly Dictionary<(int, int), int> anchored = new Dictionary<(int, int), int>();
        // Regions east-west before the field repeats; 0 for a field that doesn't.
        readonly int regionsX;

        public BiomeTable Table => table;

        public BiomeField(BiomeTable table, int? seed = null)
        {
            this.table = table;
            this.seed = seed ?? table.Seed;
            var on = new List<int>();
            for (int i = 0; i < table.Rows.Count; i++)
                if (table.Rows[i].On && !table.Rows[i].Ocean) on.Add(i);
            land = on.ToArray();
            if (table.WrapCells > 0)
            {
                if (table.WrapCells % table.RegionCells != 0)
                    throw new InvalidDataException($"biomes.json: a world {table.WrapCells} cells round needs a whole number of {table.RegionCells}-cell regions");
                regionsX = table.WrapCells / table.RegionCells;
            }
            // The region nearest each square's centre takes the square's anchor.
            for (int sx = 0; sx < table.Layout.GetLength(0); sx++)
                for (int sy = 0; sy < table.Layout.GetLength(1); sy++)
                {
                    int a = table.Layout[sx, sy];
                    if (a < 0) continue;
                    long cx = ((long)sx * table.SquareCells * 2 + table.SquareCells) * One / 2 / table.RegionCells;
                    long cy = ((long)sy * table.SquareCells * 2 + table.SquareCells) * One / 2 / table.RegionCells;
                    var (rx, ry) = NearestRegion(cx, cy);
                    anchored[(WrapRegion(rx), ry)] = a;
                }
        }

        // ---- regions ----

        /// <summary>A region's feature point in Q16 region units.</summary>
        (long x, long y) Feature(int rx, int ry)
        {
            uint h = WorldHash.Hash4(seed, SaltJitter, WrapRegion(rx), ry);
            return ((long)rx * One + JitterLow + (h & 0xFFFF) * JitterSpan / 0xFFFF, (long)ry * One + JitterLow + (h >> 16) * JitterSpan / 0xFFFF);
        }

        // The region east-west as the field counts it: the same region either side of a wrapped world's seam.
        int WrapRegion(int rx) => regionsX > 0 ? (int)BiomeMath.Mod(rx, regionsX) : rx;

        (int, int) NearestRegion(long px, long py)
        {
            int cx = (int)BiomeMath.FloorDiv(px, One), cy = (int)BiomeMath.FloorDiv(py, One);
            (int, int) best = (cx, cy);
            long bestD = long.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                int rx = cx + i % 3 - 1, ry = cy + i / 3 - 1;
                var f = Feature(rx, ry);
                long d = (px - f.x) * (px - f.x) + (py - f.y) * (py - f.y);
                if (d < bestD)
                {
                    bestD = d;
                    best = (rx, ry);
                }
            }
            return best;
        }

        /// <summary>Where a cell lies in warped region space (Q16 region units).</summary>
        (long x, long y) Warp(int x, int y)
        {
            long xq = (long)x * One + One / 2, yq = (long)y * One + One / 2;
            long lattice = Math.Max(1, table.RegionCells / 2);
            long wx = BiomeMath.Centred(seed, SaltWarpX, xq, yq, lattice, table.WrapCells, 0) * table.WarpCells;
            long wy = BiomeMath.Centred(seed, SaltWarpY, xq, yq, lattice, table.WrapCells, 0) * table.WarpCells;
            return (BiomeMath.FloorDiv(xq + wx, table.RegionCells), BiomeMath.FloorDiv(yq + wy, table.RegionCells));
        }

        /// <summary>The region a cell is in.</summary>
        public (int rx, int ry) RegionAt(int x, int y)
        {
            var p = Warp(x, y);
            return NearestRegion(p.x, p.y);
        }

        /// <summary>A region's centre in Q16 cells.</summary>
        public (long x, long y) RegionCentre(int rx, int ry)
        {
            var f = Feature(rx, ry);
            return (f.x * table.RegionCells, f.y * table.RegionCells);
        }

        /// <summary>The anchor biome of the square a region's centre is in, or −1.</summary>
        public int AnchorOf(int rx, int ry)
        {
            var c = RegionCentre(rx, ry);
            long span = (long)table.SquareCells * One;
            long sx = BiomeMath.FloorDiv(c.x, span), sy = BiomeMath.FloorDiv(c.y, span);
            if (sx < 0 || sy < 0 || sx >= table.Layout.GetLength(0) || sy >= table.Layout.GetLength(1)) return -1;
            return table.Layout[sx, sy];
        }

        /// <summary>The biome (row index) of a region.</summary>
        public int RegionBiome(int rx, int ry)
        {
            rx = WrapRegion(rx);
            if (anchored.TryGetValue((rx, ry), out int forced)) return forced;
            if (land.Length <= 1) return land.Length == 1 ? land[0] : 0;
            var c = RegionCentre(rx, ry);
            var climate = Climate(c.x, c.y);
            int anchor = AnchorOf(rx, ry);
            Span<long> suit = stackalloc long[land.Length];
            long total = 0;
            for (int i = 0; i < land.Length; i++)
            {
                suit[i] = Suitability(table.Rows[land[i]], climate);
                if (land[i] == anchor) suit[i] = suit[i] * table.AnchorBias / One;
                total += suit[i];
            }
            if (total == 0)
            {
                // A climate no biome covers takes the nearest fit.
                int best = land[0];
                long bestMisfit = long.MaxValue;
                foreach (int i in land)
                {
                    long m = table.Rows[i].Frequency > 0 ? Misfit(table.Rows[i], climate) : long.MaxValue;
                    if (m < bestMisfit)
                    {
                        bestMisfit = m;
                        best = i;
                    }
                }
                return best;
            }
            // Each biome races: −ln(u) / weight, earliest wins, which picks in proportion to weight; a biome's draw
            // comes from its name, so adding one only takes the regions it wins. (ln 2 is common to all, so log2.)
            int pick = land[0];
            long first = long.MaxValue;
            for (int i = 0; i < land.Length; i++)
            {
                if (suit[i] <= 0) continue;
                uint h = WorldHash.Hash4(seed ^ unchecked((int)table.Rows[land[i]].NameHash), SaltRace, rx, ry);
                ulong m = ((ulong)(h >> 8) << 1) + 1; // u = m / 2^25, never 0
                long l = ((25L << 32) - BiomeMath.Log2(m)) >> 8; // −log2 u in Q24
                long t = (l << 30) / suit[i];
                if (t < first)
                {
                    first = t;
                    pick = land[i];
                }
            }
            return pick;
        }

        // ---- climate ----

        static long Spread(long n) => BiomeMath.Clamp01(One / 2 + n * 42598 / One); // 0.5 + 0.65 n

        long Altitude(long xq, long yq)
        {
            long c = table.ClimateCells;
            long p = table.WrapCells;
            long n = BiomeMath.Centred(seed, SaltAlt, xq, yq, c, p, 0) * 7 / 10 + BiomeMath.Centred(seed, SaltAlt2, xq, yq, c * 10 / 23, p, 0) * 3 / 10;
            return Spread(n);
        }

        /// <summary>The climate at a point in Q16 cells: smooth fields a few regions across, and volcanic hotspots.</summary>
        public BiomeClimate Climate(long xq, long yq)
        {
            long c = table.ClimateCells, p = table.WrapCells;
            long altitude = Altitude(xq, yq);
            long temperature = BiomeMath.Clamp01(Spread(BiomeMath.Centred(seed, SaltTemp, xq, yq, c * 10 / 8, p, 0)) - (altitude - One / 2) * 6 / 10);
            long humidity = Spread(BiomeMath.Centred(seed, SaltHum, xq, yq, c * 10 / 11, p, 0) * 3 / 4 + BiomeMath.Centred(seed, SaltHum2, xq, yq, c * 10 / 26, p, 0) / 4);
            return new BiomeClimate(altitude, temperature, humidity, Volcanism(xq, yq));
        }

        // About a quarter of the cells of a coarse grid hold a hotspot (if it's in high country); volcanism falls off
        // from it, so every volcanic area has its heart. Round a wrapped world the grid's columns stretch a little so a
        // whole number of them go round, and a hotspot repeats with them.
        long Volcanism(long xq, long yq)
        {
            long g = (long)table.ClimateCells * 8 / 5;
            long cols = table.WrapCells > 0 ? Math.Max(1, (table.WrapCells + g / 2) / g) : 0;
            long gx = cols > 0 ? BiomeMath.FloorDiv(xq * cols, table.WrapCells) : xq / g, gy = yq / g; // Q16 grid units
            long hx = BiomeMath.FloorDiv(gx, One), hy = BiomeMath.FloorDiv(gy, One);
            long v = 0;
            for (int i = 0; i < 9; i++)
            {
                int cx = (int)hx + i % 3 - 1, cy = (int)hy + i / 3 - 1;
                uint h = WorldHash.Hash4(seed, SaltVolc, cols > 0 ? (int)BiomeMath.Mod(cx, cols) : cx, cy);
                if ((h & 0xFF) >= 70) continue;
                long sx = (long)cx * One + 13107 + ((h >> 8) & 0xFFF) * 39321 / 4095;
                long sy = (long)cy * One + 13107 + (h >> 20) * 39321 / 4095;
                long atX = cols > 0 ? BiomeMath.FloorDiv(sx * table.WrapCells, cols) : sx * g;
                if (Altitude(atX, sy * g) < 29491) continue; // 0.45: only in high country
                v = Math.Max(v, BiomeMath.Clamp01(One - BiomeMath.Length(gx - sx, gy - sy) * One / 23593)); // over 0.36
            }
            return v;
        }

        /// <summary>How far a climate lies outside a biome's ranges, squared, in units of the tolerance (Q16).</summary>
        public static long Misfit(BiomeRow b, in BiomeClimate climate)
        {
            long m = 0;
            for (int c = 0; c < 4; c++)
            {
                long v = climate[c];
                long outside = Math.Max(Math.Max(b.ClimateMin[c] - v, v - b.ClimateMax[c]), 0);
                long k = outside * One / (c == 3 ? 2621 : 9830); // 0.04 for volcanism (strict), 0.15 for the rest
                m += k * k / One;
            }
            return m;
        }

        /// <summary>Frequency × lerp(1, e^-misfit, influence), in Q30.</summary>
        long Suitability(BiomeRow b, in BiomeClimate climate)
        {
            long fit = BiomeMath.ExpNeg(Misfit(b, climate));
            long mix = BiomeMath.One30 + (fit - BiomeMath.One30) * table.ClimateInfluence / One;
            return mix * b.Frequency / One;
        }

        // ---- weights ----

        /// <summary>The top four biomes at a cell for a transition <paramref name="blendCells"/> wide (the table's surface blend by default).</summary>
        public BiomeBlend At(int x, int y, int blendCells = 0) => At(x, y, blendCells, null);

        BiomeBlend At(int x, int y, int blendCells, RegionCache cache)
        {
            var p = Warp(x, y);
            int cx = (int)BiomeMath.FloorDiv(p.x, One), cy = (int)BiomeMath.FloorDiv(p.y, One);
            Span<long> d = stackalloc long[9];
            long nearest = long.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                var f = Feature(cx + i % 3 - 1, cy + i / 3 - 1);
                d[i] = BiomeMath.Length(p.x - f.x, p.y - f.y);
                nearest = Math.Min(nearest, d[i]);
            }
            long blend = Math.Max(1, (long)(blendCells > 0 ? blendCells : table.BlendCells) * One / table.RegionCells);
            var w = default(BiomeBlend);
            for (int i = 0; i < 9; i++)
            {
                long s = BiomeMath.Smoothstep(One - (d[i] - nearest) * One / blend);
                if (s <= 0) continue;
                int rx = cx + i % 3 - 1, ry = cy + i / 3 - 1;
                w.Add(cache != null ? cache.Biome(this, rx, ry) : RegionBiome(rx, ry), s);
            }
            w.Normalize();
            return w;
        }

        /// <summary>A 64 × 64 chunk's weights, row by row from its south-west cell, into <paramref name="into"/> (4096 long).</summary>
        public void Chunk(int chunkX, int chunkY, BiomeBlend[] into, int blendCells = 0)
        {
            var cache = new RegionCache();
            for (int j = 0; j < ChunkSize; j++)
                for (int i = 0; i < ChunkSize; i++)
                    into[j * ChunkSize + i] = At(chunkX * ChunkSize + i, chunkY * ChunkSize + j, blendCells, cache);
        }

        // The regions one chunk touches: a handful, so a short list beats a dictionary.
        sealed class RegionCache
        {
            readonly List<(int x, int y, int biome)> seen = new List<(int, int, int)>();

            public int Biome(BiomeField field, int rx, int ry)
            {
                foreach (var s in seen)
                    if (s.x == rx && s.y == ry) return s.biome;
                int b = field.RegionBiome(rx, ry);
                seen.Add((rx, ry, b));
                return b;
            }
        }
    }
}
