// A hills map's generator (docs/design/hills.md): nothing but grass on gentle rolling hills, with no plan squares, pinned
// map, sea, water, villages, trees or things; roads where map.json asks (HillsRoads). Its land is a few octaves of integer value noise; each octave's
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
        /// <summary>Its roads (map.json's "hills" "roads", HillsRoads), or null for hills with none.</summary>
        public HillsRoads Roads { get; private set; }
        /// <summary>Its alpine massif (map.json's "hills" "massif", HillsMassif), or null for hills with none.</summary>
        public HillsMassif Massif { get; private set; }
        /// <summary>Its rivers (map.json's "hills" "rivers", HillsRivers: from springs on the massif), or null for none.</summary>
        public HillsRivers Rivers { get; private set; }
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
                return top + (Massif?.Highest ?? 0);
            }
        }

        /// <summary>map.json's generated "hills": { "base", "octaves": [[period, amplitude], ...], "biome" }.</summary>
        public static HillsLand Read(JObject o, int seed, int cellsW, int cellsH)
        {
            var list = new List<(int, int)>();
            foreach (var oct in (JArray)o["octaves"] ?? throw new FormatException($"{MapFile.FileName}: \"hills\" needs \"octaves\""))
                list.Add(((int)oct[0], (int)oct[1]));
            var land = new HillsLand(seed, cellsW, cellsH, (int?)o["base"] ?? 8, list, (string)o["biome"] ?? "grassland");
            var massif = HillsMassif.Read(o["massif"] as JObject, seed, cellsW, cellsH);
            // The massif's shape first, then the roads (no lesser one into its ring), then its passes along the main roads,
            // then the roads graded over the whole land.
            land.Roads = HillsRoads.Read(o["roads"] as JObject, seed, cellsW / WorldPlan.SquareCells, cellsH / WorldPlan.SquareCells,
                massif != null ? massif.InRing : (Func<int, int, bool>)null);
            if (massif != null) massif.SetPasses(land.Roads?.MainLines() ?? new List<(long[], long[])>());
            // The rivers' gorge is cut through the ring before the roads are graded over the land.
            var rivers = o["rivers"] as JObject;
            var settings = HillsRivers.Settings.Read(rivers);
            if (massif != null && rivers != null) massif.SetGorge(land.Rolling, settings.LakeDepth, settings.GorgeWidth, settings.GorgeFall);
            land.Massif = massif;
            land.Roads?.Grade(land.At, massif != null ? massif.Touches : (Func<int, int, bool>)null);
            if (massif != null && rivers != null)
                land.Rivers = new HillsRivers(land.At, cellsW, cellsH, massif.Springs(settings.Springs, settings.SpringUp), settings,
                    (massif.CentreX, massif.CentreY, massif.Foot + massif.Warp), (land.Roads?.MainLines(), HillsRoads.Dry));
            return land;
        }

        /// <summary>The land at any cell, x and y taken round the torus: the same at (x, y) and (x + k W, y + m H).</summary>
        public int At(int x, int y) => Massif == null ? Rolling(x, y) : Massif.Land(x, y, Rolling(x, y));

        /// <summary>The rolling hills' own land at a cell, without the massif.</summary>
        public int Rolling(int x, int y)
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

        /// <summary>Land this steep across a cell (land units to a neighbour) is bare rock on a hills map, and this steep scree.</summary>
        public const int HillsRock = 7, HillsScree = 4;

        // Every cell grass on the hills' land, but its roads' dirt, and rock or scree where it's steep; no things. The land is
        // worked out a cell past the chunk's edge for the slopes, so a cell's ground is the same in either chunk.
        void MakeHills(GeneratedChunk c)
        {
            int x0 = c.X * Size, y0 = c.Y * Size;
            const int ap = Size + 2;
            var near = HillsNearBox(x0 - 1, y0 - 1, x0 + Size, y0 + Size);
            var land = new int[ap * ap];
            var kind = new char[ap * ap];
            for (int j = 0; j < ap; j++)
                for (int i = 0; i < ap; i++) land[j * ap + i] = HillsPoint(x0 + i - 1, y0 + j - 1, near, out kind[j * ap + i], out _);
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    int k = (j + 1) * ap + i + 1, h = land[k];
                    c.Land[j * Size + i] = h;
                    c.Cells[j * Size + i] = kind[k] != '\0' ? kind[k] : HillsGround(Slope(land, k, ap));
                }
            c.Biome = Hills.Biome;
        }

        static int Slope(int[] land, int k, int ap)
        {
            int h = land[k];
            return Math.Max(Math.Max(Math.Abs(land[k - 1] - h), Math.Abs(land[k + 1] - h)), Math.Max(Math.Abs(land[k - ap] - h), Math.Abs(land[k + ap] - h)));
        }

        // A hills cell off the road by its slope: grass, scree or bare rock.
        static char HillsGround(int slope)
        {
            if (slope >= HillsRock) return Rock;
            return slope >= HillsScree ? Scree : Grass;
        }

        /// <summary>The roads and rivers that may reach a box of map cells (unwrapped).</summary>
        public struct HillsNear
        {
            public List<HillsRoads.Placed> Roads;
            /// <summary>The rivers' pieces that may touch the box (their water and banks), and those a road's ford ramp may reach.</summary>
            public List<HillsRivers.Placed> Rivers, Fords;
        }

        static readonly HillsRivers.Hit NoRiver = new HillsRivers.Hit { Distance = long.MaxValue };

        HillsNear HillsNearBox(int x0, int y0, int x1, int y1) => new HillsNear
        {
            Roads = Hills.Roads?.Near(x0, y0, x1, y1),
            Rivers = Hills.Rivers?.Near(x0, y0, x1, y1, Hills.Rivers.Reach),
            Fords = Hills.Rivers?.Near(x0, y0, x1, y1, Hills.Rivers.FordReach),
        };

        // A hills cell's land and what's laid on it ('\0' for nothing: the slope decides its ground), a pure function of the
        // cell, the same either side of a chunk's edge and a seam:
        // - under a lake on a river (its land under the lake's level): the lake's level, a pond ('w'); but its outlet's
        //   river where that runs lower, and a road over it on a causeway a land unit over the water;
        // - within a river's half width: its surface there, river ('r'); where a road crosses too, a ford;
        // - on a road: the land at the road's line (level across it), coming down to the river's surface at the road's grade
        //   by a ford; on a road's bank, leaning back from that to the land over HillsRoads.Bank cells;
        // - on a river's bank: leaning back from its surface to the land over HillsRivers.Bank cells, never under it.
        int HillsPoint(int x, int y, HillsNear near, out char kind, out bool ford)
        {
            int natural = Hills.At(x, y);
            kind = '\0';
            ford = false;
            var rivers = Hills.Rivers;
            int land = RoadPoint(x, y, natural, near.Roads, out bool road);
            var river = rivers != null && near.Rivers != null && near.Rivers.Count > 0 ? HillsRivers.Nearest(near.Rivers, x, y, rivers.Reach) : NoRiver;
            // A road looks farther for a ford it comes down to.
            if (road && river.Distance == long.MaxValue && near.Fords != null && near.Fords.Count > 0) river = HillsRivers.Nearest(near.Fords, x, y, rivers.FordReach);
            long halfQ = (long)river.Half * One, d = river.Distance;
            int lake = rivers != null ? rivers.LakeAt(x, y, natural) : HillsRivers.NoLake;
            // A lake but where its outlet's river runs on lower, or a road crosses it on a causeway a unit over the water.
            if (lake != HillsRivers.NoLake && !(d < halfQ && river.Surface < lake))
            {
                if (!road)
                {
                    kind = Pond;
                    return lake;
                }
                // Where the river's line crosses the causeway the water runs over it: a ford at the lake's level.
                if (d < halfQ)
                {
                    kind = Stream;
                    ford = true;
                    return lake;
                }
                kind = Dirt;
                return Math.Max(land, lake + 1);
            }
            if (river.Distance == long.MaxValue)
            {
                if (road) kind = Dirt;
                return land;
            }
            if (d < halfQ)
            {
                kind = Stream;
                ford = road;
                return river.Surface;
            }
            if (road)
            {
                // Down to the ford at the road's grade: a road over the river's surface comes down to it, the farther the
                // higher it is (FordRamp cells at least, HillsRivers.FordRampMost at most).
                kind = Dirt;
                int over = land - river.Surface;
                if (over <= 0) return land;
                long grade = (long)(Hills.Roads?.GradePercent ?? 12) * 12 * One / 100;
                long ramp = Math.Min((long)HillsRivers.FordRampMost * One, Math.Max((long)HillsRivers.FordRamp * One, (long)over * One * One / grade));
                if (d >= halfQ + ramp) return land;
                return river.Surface + (int)((long)over * (d - halfQ) / ramp);
            }
            long bank = (long)HillsRivers.Bank * One;
            if (d >= halfQ + bank) return land;
            long lean = BiomeMath.Smoothstep((d - halfQ) / HillsRivers.Bank);
            return Math.Max(river.Surface, river.Surface + (int)BiomeMath.FloorDiv((long)(land - river.Surface) * lean + One / 2, One));
        }

        // A hills cell's land with its roads: on a road, the land at the road's line (level across it); on its bank, leaning
        // back from that to the land over HillsRoads.Bank cells.
        int RoadPoint(int x, int y, int land, List<HillsRoads.Placed> near, out bool road)
        {
            road = false;
            if (near == null || near.Count == 0) return land;
            var hit = HillsRoads.Nearest(near, x, y);
            long d = hit.Distance;
            if (d == long.MaxValue) return land;
            int half = hit.Half;
            int centre = hit.Arm.Profile != null ? hit.Arm.ProfileAt(hit.Piece, hit.Along) : Hills.At(hit.X, hit.Y);
            long halfQ = (long)half * One;
            if (d < halfQ)
            {
                road = true;
                return centre;
            }
            long bank = (long)HillsRoads.Bank * One;
            if (d >= halfQ + bank) return land;
            long lean = BiomeMath.Smoothstep((d - halfQ) / HillsRoads.Bank);
            return centre + (int)BiomeMath.FloorDiv((land - centre) * lean + One / 2, One);
        }

        /// <summary>A hills cell's land with its roads and rivers, and whether it is road (a ford's too): what its chunk makes there.</summary>
        public int HillsLandAt(int x, int y, out bool road)
        {
            int land = HillsPoint(x, y, HillsNearBox(x, y, x, y), out char kind, out bool ford);
            road = kind == Dirt || ford;
            return land;
        }

        /// <summary>
        /// A hills cell's land and what's laid on it: '.' road, 'r' river, 'w' lake, or '\0' for its own ground (by slope);
        /// and whether it's a ford (a river cell a road crosses).
        /// </summary>
        public int HillsCellAt(int x, int y, out char kind, out bool ford) => HillsPoint(x, y, HillsNearBox(x, y, x, y), out kind, out ford);

        static byte KnownWater(char kind)
        {
            if (kind == Stream) return KnownChunk.River;
            return kind == Pond ? KnownChunk.Lake : KnownChunk.Dry;
        }

        // The known chunk's samples: the hills' land at each, its rivers and lakes, all the hills' biome.
        KnownChunk KnowHills(int cx, int cy)
        {
            var k = new KnownChunk { X = cx, Y = cy, Biome = Hills.Biome };
            int x0 = cx * Size, y0 = cy * Size;
            var near = HillsNearBox(x0, y0, x0 + Size, y0 + Size);
            for (int j = 0; j < KnownChunk.Side; j++)
                for (int i = 0; i < KnownChunk.Side; i++)
                {
                    int q = j * KnownChunk.Side + i;
                    // The last samples are the next chunk's first, across the seams too: the land tiles.
                    k.Heights[q] = HillsPoint(x0 + i * KnownChunk.Step, y0 + j * KnownChunk.Step, near, out char kind, out _);
                    k.Water[q] = KnownWater(kind);
                    k.Biomes[q] = Hills.Biome;
                }
            return k;
        }
    }
}
