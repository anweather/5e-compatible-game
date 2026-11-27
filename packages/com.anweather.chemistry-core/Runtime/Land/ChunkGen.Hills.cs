// A hills map's generator (docs/design/hills.md): grass on gentle rolling hills, with no plan squares, pinned map, sea,
// villages or things; roads, a massif, rivers and woods where map.json asks (HillsRoads, HillsMassif, HillsRivers,
// HillsForest). Its land is a few octaves of integer value noise; each octave's
// lattice period divides the map's width and height, so the land at x and x + CellsW (and y and y + CellsH) is the same
// and the map tiles round a torus with no seam in it. A cell's land is a pure function of its position and the seed, in
// integers only, so every peer makes the same chunk.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class HillsLand
    {
        public readonly int Base;
        public readonly IReadOnlyList<(int period, int amplitude)> Octaves;
        public readonly string Biome;
        public HillsRoads Roads { get; private set; }
        public HillsMassif Massif { get; private set; }
        public HillsRivers Rivers { get; private set; }
        public HillsForest Forest { get; private set; }
        readonly int seed, w, h;

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

        public int Highest
        {
            get
            {
                int top = Base;
                foreach (var o in Octaves) top += o.amplitude;
                return top + (Massif?.Highest ?? 0);
            }
        }

        public static HillsLand Read(JObject o, int seed, int cellsW, int cellsH)
        {
            var list = new List<(int, int)>();
            foreach (var oct in (JArray)o["octaves"] ?? throw new FormatException($"{MapFile.FileName}: \"hills\" needs \"octaves\""))
                list.Add(((int)oct[0], (int)oct[1]));
            var land = new HillsLand(seed, cellsW, cellsH, (int?)o["base"] ?? 8, list, (string)o["biome"] ?? "grassland");
            var massif = HillsMassif.Read(o["massif"] as JObject, seed, cellsW, cellsH);
            land.Roads = HillsRoads.Read(o["roads"] as JObject, seed, cellsW / WorldPlan.SquareCells, cellsH / WorldPlan.SquareCells,
                massif != null ? massif.InRing : (Func<int, int, bool>)null);
            if (massif != null) massif.SetPasses(land.Roads?.MainLines() ?? new List<(long[], long[])>());
            var rivers = o["rivers"] as JObject;
            var settings = HillsRivers.Settings.Read(rivers);
            if (massif != null && rivers != null) massif.SetGorge(land.Rolling, settings.LakeDepth, settings.GorgeWidth, settings.GorgeFall);
            land.Massif = massif;
            land.Roads?.Grade(land.At, massif != null ? massif.Touches : (Func<int, int, bool>)null);
            if (massif != null && rivers != null)
                land.Rivers = new HillsRivers(land.At, cellsW, cellsH, massif.Springs(settings.Springs, settings.SpringUp), settings,
                    (massif.CentreX, massif.CentreY, massif.Foot + massif.Warp), (land.Roads?.MainLines(), HillsRoads.Dry));
            land.Forest = HillsForest.Read(o["forest"] as JObject, seed, cellsW, cellsH, massif);
            return land;
        }

        public int At(int x, int y) => Massif == null ? Rolling(x, y) : Massif.Land(x, y, Rolling(x, y));

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

        long Lattice(int octave, int i, int j, int amplitude) => WorldHash.Hash4(seed, 0x4111 + octave, i, j) % (uint)(amplitude + 1);

        static long Smooth(long t) => t * t / One * (3 * One - 2 * t) / One;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "hills base {0}, {1} octaves, up to {2}", Base, Octaves.Count, Highest);
    }

    public sealed partial class ChunkGen
    {
        public readonly HillsLand Hills;

        public readonly bool WrapY;

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

        public int WrapCellY(int y) => WrapY ? PlanGrid.Mod(y, CellsH) : y;

        public int WrapChunkY(int cy) => WrapY ? PlanGrid.Mod(cy, ChunksH) : cy;

        static int Along(int a, int b, int n, bool wrap)
        {
            int d = Math.Abs(a - b);
            if (!wrap) return d;
            d %= n;
            return Math.Min(d, n - d);
        }

        public const int HillsRock = 7, HillsScree = 4;

        void MakeHills(GeneratedChunk c)
        {
            int x0 = c.X * Size, y0 = c.Y * Size;
            const int ap = Size + 2;
            var near = HillsNearBox(x0 - 1, y0 - 1, x0 + Size, y0 + Size);
            var land = new int[ap * ap];
            var kind = new char[ap * ap];
            var clear = new bool[ap * ap];
            for (int j = 0; j < ap; j++)
                for (int i = 0; i < ap; i++) land[j * ap + i] = HillsPoint(x0 + i - 1, y0 + j - 1, near, out kind[j * ap + i], out _, out clear[j * ap + i]);
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    int k = (j + 1) * ap + i + 1, h = land[k];
                    c.Land[j * Size + i] = h;
                    c.Cells[j * Size + i] = kind[k] != '\0' ? kind[k] : HillsGround(Slope(land, k, ap), x0 + i, y0 + j, h, clear[k]);
                }
            c.Biome = Hills.Biome;
        }

        char HillsGround(int slope, int x, int y, int land, bool clear)
        {
            char ground = HillsGround(slope);
            var forest = Hills.Forest;
            if (ground != Grass || clear || forest == null) return ground;
            return forest.Roll(x, y, forest.Density(x, y, land)) ? Tree : Grass;
        }

        public char HillsGroundAt(int x, int y)
        {
            var near = HillsNearBox(x - 1, y - 1, x + 1, y + 1);
            int h = HillsPoint(x, y, near, out char kind, out _, out bool clear);
            if (kind != '\0') return kind;
            int slope = 0;
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                slope = Math.Max(slope, Math.Abs(HillsPoint(x + dx, y + dy, near, out _, out _, out _) - h));
            return HillsGround(slope, x, y, h, clear);
        }

        static int Slope(int[] land, int k, int ap)
        {
            int h = land[k];
            return Math.Max(Math.Max(Math.Abs(land[k - 1] - h), Math.Abs(land[k + 1] - h)), Math.Max(Math.Abs(land[k - ap] - h), Math.Abs(land[k + ap] - h)));
        }

        static char HillsGround(int slope)
        {
            if (slope >= HillsRock) return Rock;
            return slope >= HillsScree ? Scree : Grass;
        }

        public struct HillsNear
        {
            public List<HillsRoads.Placed> Roads;
            public List<HillsRivers.Placed> Rivers, Fords;
        }

        const int LakeShore = 6;

        static readonly HillsRivers.Hit NoRiver = new HillsRivers.Hit { Distance = long.MaxValue };

        HillsNear HillsNearBox(int x0, int y0, int x1, int y1) => new HillsNear
        {
            Roads = Hills.Roads?.Near(x0, y0, x1, y1),
            Rivers = Hills.Rivers?.Near(x0, y0, x1, y1, Hills.Rivers.Reach),
            Fords = Hills.Rivers?.Near(x0, y0, x1, y1, Hills.Rivers.FordReach),
        };

        int HillsPoint(int x, int y, HillsNear near, out char kind, out bool ford) => HillsPoint(x, y, near, out kind, out ford, out _);

        int HillsPoint(int x, int y, HillsNear near, out char kind, out bool ford, out bool clear)
        {
            int natural = Hills.At(x, y);
            kind = '\0';
            ford = false;
            var rivers = Hills.Rivers;
            int land = RoadPoint(x, y, natural, near.Roads, out bool road, out clear);
            var river = rivers != null && near.Rivers != null && near.Rivers.Count > 0 ? HillsRivers.Nearest(near.Rivers, x, y, rivers.Reach) : NoRiver;
            if (road && river.Distance == long.MaxValue && near.Fords != null && near.Fords.Count > 0) river = HillsRivers.Nearest(near.Fords, x, y, rivers.FordReach);
            long halfQ = (long)river.Half * One, d = river.Distance;
            if (d < halfQ + (long)(HillsRivers.Bank + 1) * One) clear = true;
            int lake = rivers != null ? rivers.LakeAt(x, y, natural) : HillsRivers.NoLake;
            if (rivers != null && lake == HillsRivers.NoLake && !clear && rivers.LakeAt(x, y, natural - LakeShore) != HillsRivers.NoLake) clear = true;
            if (lake != HillsRivers.NoLake && !(d < halfQ && river.Surface < lake))
            {
                clear = true;
                if (!road)
                {
                    kind = Pond;
                    return lake;
                }
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

        int RoadPoint(int x, int y, int land, List<HillsRoads.Placed> near, out bool road, out bool bankOrRoad)
        {
            road = false;
            bankOrRoad = false;
            if (near == null || near.Count == 0) return land;
            var hit = HillsRoads.Nearest(near, x, y);
            long d = hit.Distance;
            if (d == long.MaxValue) return land;
            int half = hit.Half;
            long halfQ = (long)half * One, bank = (long)HillsRoads.Bank * One;
            if (d >= halfQ + bank) return land;
            bankOrRoad = true;
            int centre = hit.Arm.Profile != null ? hit.Arm.ProfileAt(hit.Piece, hit.Along) : Hills.At(hit.X, hit.Y);
            if (d < halfQ)
            {
                road = true;
                return centre;
            }
            long lean = BiomeMath.Smoothstep((d - halfQ) / HillsRoads.Bank);
            return centre + (int)BiomeMath.FloorDiv((land - centre) * lean + One / 2, One);
        }

        public int HillsLandAt(int x, int y, out bool road)
        {
            int land = HillsPoint(x, y, HillsNearBox(x, y, x, y), out char kind, out bool ford);
            road = kind == Dirt || ford;
            return land;
        }

        public int HillsCellAt(int x, int y, out char kind, out bool ford) => HillsPoint(x, y, HillsNearBox(x, y, x, y), out kind, out ford);

        static byte KnownWater(char kind)
        {
            if (kind == Stream) return KnownChunk.River;
            return kind == Pond ? KnownChunk.Lake : KnownChunk.Dry;
        }

        KnownChunk KnowHills(int cx, int cy)
        {
            var k = new KnownChunk { X = cx, Y = cy, Biome = Hills.Biome };
            int x0 = cx * Size, y0 = cy * Size;
            var near = HillsNearBox(x0, y0, x0 + Size, y0 + Size);
            for (int j = 0; j < KnownChunk.Side; j++)
                for (int i = 0; i < KnownChunk.Side; i++)
                {
                    int q = j * KnownChunk.Side + i;
                    int x = x0 + i * KnownChunk.Step, y = y0 + j * KnownChunk.Step;
                    k.Heights[q] = HillsPoint(x, y, near, out char kind, out _, out bool clear);
                    k.Water[q] = KnownWater(kind);
                    if (Hills.Forest != null && kind == '\0' && !clear) k.Wood[q] = Hills.Forest.Cover(Hills.Forest.Density(x, y, k.Heights[q]));
                    k.Biomes[q] = Hills.Biome;
                }
            return k;
        }
    }
}
