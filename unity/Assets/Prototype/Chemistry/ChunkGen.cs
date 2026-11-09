// ap chunk from seed and plan (docs/design/world-squares.md section 5, level 2; J-080 phase 4): its land, cells and
// things, a pure function of the world plan, the committed files it reads and the chunk's coordinates. It never reads
// another chunk's cells, so chunks can be made in any order, on any thread, and agree at their seams: every cell's land
// is worked out from position alone (ChunkGen.Land.cs), and a chunk only samples one cell past its edge for slopes.
// The pinned valley's chunks come from its map folder unchanged.
//
// Coordinates are the map's: cells and chunks from the north-west corner of the map's squares (the plan's square
// (OriginX, OriginY)), y growing south, so a map of the nine never needs a negative row (the valley sits at chunk rows
// 18..26; its own rows are shifted 1152 cells south when the nine is put together, NineWorld).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One chunk as made: 64 × 64 cells of land (land units) and Legend characters, and its things.</summary>
    public sealed class GeneratedChunk
    {
        public int X, Y;
        /// <summary>Row-major from the chunk's north-west cell, in land units (0.125 m).</summary>
        public int[] Land;
        /// <summary>ap ChemGrid Legend character per cell, row-major as Land.</summary>
        public char[] Cells;
        /// <summary>things.json rows for this chunk, in id order: the pinned square's or the landmarks', then the seeded ones.</summary>
        public JArray Things = new JArray();
        /// <summary>The biome most of its cells are, by id; the pinned map's id for a pinned chunk.</summary>
        public string Biome;
        public bool Pinned;
        /// <summary>The plan's landmarks whose footprint reaches it, by their index in the plan.</summary>
        public List<int> Landmarks = new List<int>();
        /// <summary>FNV-1a over its coordinates, land, cells and things: what the two-orders golden compares.</summary>
        public uint Hash;
    }

    /// <summary>ap world square laid from a map folder as it is (the valley): its land, its cells and its things.</summary>
    public sealed class PinnedSquare
    {
        /// <summary>The square in the plan.</summary>
        public int Square;
        public string Map;
        /// <summary>576² land units, row-major.</summary>
        public int[] Land;
        /// <summary>576 rows of 576 Legend characters.</summary>
        public string[] Rows;
        /// <summary>things.json's chunk lists by the square's own chunk (0..8, 0..8).</summary>
        public Dictionary<(int x, int y), JArray> Things = new Dictionary<(int, int), JArray>();
    }

    public sealed partial class ChunkGen
    {
        /// <summary>The generator's version, in the map's input hash: a change that alters any chunk is a new version.</summary>
        public const int Version = 6;
        public const int Size = WorldPlan.ChunkCells, Square = WorldPlan.SquareCells;

        // Legend characters (ChemGrid.Legend).
        const byte MarkRoad = 1, MarkBare = 2, MarkClear = 3;
        internal const char Grass = ',', Dirt = '.', Stone = '#', Tree = 'T', Brush = '"', Pond = 'w', Stream = 'r';

        public readonly WorldPlan Plan;
        /// <summary>The plan square at the map's north-west corner, and the map's size in squares.</summary>
        public readonly int OriginX, OriginY, SquaresW, SquaresH;
        /// <summary>
        /// Whether the map joins east to west (an east-west plan, the map its whole width): a land point at x is the one at
        /// x modulo <see cref="CellsW"/>, and every noise lattice repeats round it (generator 6).
        /// </summary>
        public readonly bool WrapX;
        public readonly PinnedSquare Pinned;
        public readonly BiomeField Field;
        readonly BiomeTable table;
        readonly int seed;
        /// <summary>The landmarks' rows, by the plan's landmark index (null for one made elsewhere: the castle is the valley's).</summary>
        public IReadOnlyList<LandmarkRows> LandmarkRows => landmarkRows;
        readonly LandmarkRows[] landmarkRows;
        readonly List<(LandStamp stamp, char bare, int landmark)> stamps = new List<(LandStamp, char, int)>();

        public int ChunksW => SquaresW * WorldPlan.ChunksPerSquare;
        public int ChunksH => SquaresH * WorldPlan.ChunksPerSquare;
        public int CellsW => SquaresW * Square;
        public int CellsH => SquaresH * Square;

        /// <summary>
        /// ap generator for the map of <paramref name="w"/> × <paramref name="h"/> plan squares from (<paramref name="originX"/>,
        /// <paramref name="originY"/>), with the biome rows of <paramref name="biomes"/> anchored by the plan's templates.
        /// </summary>
        public ChunkGen(WorldPlan plan, BiomeTable biomes, PinnedSquare pinned, int originX, int originY, int w, int h)
        {
            Plan = plan;
            Pinned = pinned;
            OriginX = originX;
            OriginY = originY;
            SquaresW = w;
            SquaresH = h;
            if (plan.Grid.WrapY) throw new ArgumentException("a generated map wraps east-west at most, not north-south");
            WrapX = plan.Grid.WrapX;
            if (WrapX && (originX != 0 || w != plan.Grid.W))
                throw new ArgumentException($"a map that wraps is its plan's whole width, {plan.Grid.W} squares from square 0, not {w} from {originX}");
            seed = plan.Seed;
            table = Anchored(biomes, plan);
            Field = new BiomeField(table, seed);
            SetUpLand();
            SetUpSpawns();
            landmarkRows = new LandmarkRows[plan.Landmarks.Count];
            for (int k = 0; k < plan.Landmarks.Count; k++)
            {
                var l = plan.Landmarks[k];
                if (l.Intent != WorldPlan.VillageIntent || plan.Library.Village == null || (pinned != null && l.Square == pinned.Square)) continue;
                var (x, y) = MapCell(l.Square, l.SiteX, l.SiteY);
                if (x < 0 || y < 0 || x >= CellsW || y >= CellsH) continue;
                var rows = plan.Library.Village.Make(l.Biome, x, y, unchecked((int)WorldPlan.H(seed, 0x1a4d, k)), Buildable);
                landmarkRows[k] = rows;
                foreach (var s in MapStamps.FromJson(rows.StampsJson).All) stamps.Add((s.Stamp, BareChar(s.Bare), k));
            }
        }

        /// <summary>ap plan site (cells within square <paramref name="i"/>) as a map cell.</summary>
        public (int x, int y) MapCell(int i, int siteX, int siteY) =>
            ((Plan.Grid.X(i) - OriginX) * Square + siteX, (Plan.Grid.Y(i) - OriginY) * Square + siteY);

        // The biome rows with the plan's anchors: each square's template biome, if it's a land biome that's on.
        static BiomeTable Anchored(BiomeTable biomes, WorldPlan plan)
        {
            var g = plan.Grid;
            var layout = new int[g.W, g.H];
            for (int i = 0; i < plan.Count; i++)
            {
                int b = biomes.IndexOf(plan.TemplateAt(i).Biome ?? "");
                bool land = b >= 0 && biomes.Rows[b].On && !biomes.Rows[b].Ocean && plan.Band[i] > 0 && plan.Wall[i] == 0;
                layout[g.X(i), g.H - 1 - g.Y(i)] = land ? b : -1;
            }
            var t = new BiomeTable
            {
                Seed = plan.Seed,
                RegionCells = biomes.RegionCells,
                WarpCells = biomes.WarpCells,
                ClimateCells = biomes.ClimateCells,
                BlendCells = biomes.BlendCells,
                ShapeBlendCells = biomes.ShapeBlendCells,
                SquareCells = biomes.SquareCells,
                ClimateInfluence = biomes.ClimateInfluence,
                AnchorBias = biomes.AnchorBias,
                WrapCells = g.WrapX ? g.W * Square : 0,
                Layout = layout,
            };
            t.Rows.AddRange(biomes.Rows);
            return t;
        }

        static char BareChar(string terrain)
        {
            if (terrain == null) return '\0';
            return terrain == "stone" || terrain == "bedrock" ? Stone : Dirt;
        }

        /// <summary>A map cell's x on the map: modulo the map's width where it wraps, else as it is.</summary>
        public int WrapCell(int x) => WrapX ? PlanGrid.Mod(x, CellsW) : x;

        /// <summary>A map chunk's x on the map: modulo the map's width in chunks where it wraps, else as it is.</summary>
        public int WrapChunk(int cx) => WrapX ? PlanGrid.Mod(cx, ChunksW) : cx;

        /// <summary>Chebyshev distance in chunks between two map chunks, the short way round where the map wraps.</summary>
        public int ChunkDistance(int ax, int ay, int bx, int by) =>
            WrapX ? Plan.Grid.ChunkDistance(ax, ay, bx, by) : Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

        // x − from in cells, the short way round where the map wraps.
        int Dx(int x, int from)
        {
            int d = x - from;
            if (!WrapX) return d;
            d = PlanGrid.Mod(d, CellsW);
            return d >= CellsW - CellsW / 2 ? d - CellsW : d;
        }

        /// <summary>Whether the pinned square holds map chunk (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
        public bool IsPinned(int cx, int cy) => Pinned != null && SquareOfChunk(cx, cy) == Pinned.Square;

        int SquareOfChunk(int cx, int cy) =>
            Plan.Grid.Index(OriginX + Chunks.ChunkOf(cx * Size) / WorldPlan.ChunksPerSquare, OriginY + Chunks.ChunkOf(cy * Size) / WorldPlan.ChunksPerSquare);

        /// <summary>Makes map chunk (<paramref name="cx"/>, <paramref name="cy"/>): safe from any thread, the same whatever was made before.</summary>
        public GeneratedChunk Make(int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= ChunksW || cy >= ChunksH) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the map");
            var c = new GeneratedChunk { X = cx, Y = cy, Land = new int[Size * Size], Cells = new char[Size * Size] };
            if (IsPinned(cx, cy)) MakePinned(c);
            else MakeGenerated(c);
            c.Hash = HashOf(c);
            return c;
        }

        void MakePinned(GeneratedChunk c)
        {
            int px = c.X * Size - PinnedX, py = c.Y * Size - PinnedY;
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    c.Land[j * Size + i] = Pinned.Land[(py + j) * Square + px + i];
                    c.Cells[j * Size + i] = Pinned.Rows[py + j][px + i];
                }
            c.Pinned = true;
            c.Biome = Pinned.Map;
            if (Pinned.Things.TryGetValue((px / Size, py / Size), out var things)) c.Things = (JArray)things.DeepClone();
        }

        /// <summary>The pinned square's north-west map cell.</summary>
        public int PinnedX => Pinned == null ? 0 : (Plan.Grid.X(Pinned.Square) - OriginX) * Square;
        public int PinnedY => Pinned == null ? 0 : (Plan.Grid.Y(Pinned.Square) - OriginY) * Square;

        void MakeGenerated(GeneratedChunk c)
        {
            int x0 = c.X * Size, y0 = c.Y * Size;
            const int ap = Size + 2; // one cell of apron round the chunk, for slopes: land is a function of position
            var land = new int[ap * ap];
            var wet = new byte[ap * ap];
            var road = new bool[ap * ap];
            var clear = new bool[Size * Size];
            var steep = new bool[Size * Size];
            var near = Nearby(x0 - 1, y0 - 1, x0 + Size, y0 + Size);
            var blend = new BiomeBlend[Size * Size];
            for (int j = 0; j < ap; j++)
                for (int i = 0; i < ap; i++)
                {
                    // The apron stays on the map: past its edge a cell's slope is read against itself. Round a world that
                    // wraps, the apron is the land across the seam (land is the same at x and x + CellsW).
                    int x = WrapX ? x0 - 1 + i : Math.Max(0, Math.Min(CellsW - 1, x0 - 1 + i)), y = Math.Max(0, Math.Min(CellsH - 1, y0 - 1 + j));
                    var p = Point(x, y, near, out var b);
                    land[j * ap + i] = p.Land;
                    wet[j * ap + i] = p.Wet;
                    road[j * ap + i] = p.Road;
                    if (i <= 0 || j <= 0 || i > Size || j > Size) continue;
                    blend[(j - 1) * Size + i - 1] = b;
                    clear[(j - 1) * Size + i - 1] = p.Clear;
                }
            // The landmarks' stamps whose footprint reaches it.
            var bare = new bool[Size * Size];
            for (int k = 0; k < stamps.Count; k++)
            {
                var (s, bareChar, landmark) = stamps[k];
                int reach = s.Bound, sx = StampX(s, x0);
                if (sx + reach < x0 || sx - reach >= x0 + Size || s.Y + reach < y0 || s.Y - reach >= y0 + Size) continue;
                if (!c.Landmarks.Contains(landmark)) c.Landmarks.Add(landmark);
                for (int j = -1; j <= Size; j++)
                    for (int i = -1; i <= Size; i++)
                    {
                        int by = s.At(x0 + i - sx, y0 + j - s.Y);
                        if (by == 0) continue;
                        land[(j + 1) * ap + i + 1] += by;
                        if (bareChar != '\0' && i >= 0 && j >= 0 && i < Size && j < Size) bare[j * Size + i] = true;
                    }
            }
            // Landmarks without stamps still count when their footprint (their rows' reach) is over the chunk.
            for (int k = 0; k < landmarkRows.Length; k++)
            {
                var r = landmarkRows[k];
                if (r == null || c.Landmarks.Contains(k)) continue;
                var (lx, ly) = r.Centre;
                lx = x0 + Size / 2 + Dx(lx, x0 + Size / 2);
                if (lx + r.Reach >= x0 && lx - r.Reach < x0 + Size && ly + r.Reach >= y0 && ly - r.Reach < y0 + Size) c.Landmarks.Add(k);
            }
            c.Landmarks.Sort();
            var counts = new Dictionary<int, long>();
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    int k = (j + 1) * ap + i + 1, h = land[k];
                    c.Land[j * Size + i] = h;
                    var b = blend[j * Size + i];
                    int dom = b.Dominant;
                    counts[dom] = (counts.TryGetValue(dom, out long n) ? n : 0) + 1;
                    int slope = Math.Max(Math.Max(Math.Abs(land[k - 1] - h), Math.Abs(land[k + 1] - h)), Math.Max(Math.Abs(land[k - ap] - h), Math.Abs(land[k + ap] - h)));
                    byte mark = bare[j * Size + i] ? MarkBare : (byte)0;
                    if (clear[j * Size + i]) mark = MarkClear;
                    if (road[k]) mark = MarkRoad;
                    steep[j * Size + i] = slope >= SteepUnits;
                    c.Cells[j * Size + i] = CellOf(x0 + i, y0 + j, h, wet[k], mark, slope, b);
                }
            int top = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
            c.Biome = table.Rows[top].Id;
            // Things: the landmarks' rows for this chunk first, then the seeded ones, so every id holds.
            foreach (int k in c.Landmarks)
                foreach (var list in (landmarkRows[k]?.Things ?? new JArray()).OfType<JObject>())
                    if ((int)list["chunk"][0] == c.X && (int)list["chunk"][1] == c.Y)
                        foreach (var t in (JArray)list["things"]) c.Things.Add(t.DeepClone());
            SeedThings(c, blend, clear, steep);
        }

        // A stamp's x seen from the chunk at x0: across the seam where that's the short way round.
        int StampX(LandStamp s, int x0) => WrapX ? x0 + Size / 2 + Dx(s.X, x0 + Size / 2) : s.X;

        // The cell's Legend character: water where the land is under the sea or a river runs, a road's dirt, a stamp's
        // bare rock, a spawn's clearing grass, bare dirt on a steep face, else the biome's ground, a tree or brush by a
        // roll per cell. Stone ('#') is a blocking wall 1.2 m high wherever it is read (ChemGrid's legend, materials.json's
        // "stone"), so only a landmark's own stamp lays it: a steep face is land like the valley's, whose steep faces are
        // grass at every pitch, and one too steep to climb stops a walker by its height, not by a wall on it.
        char CellOf(int x, int y, int h, byte wet, byte mark, int slope, BiomeBlend b)
        {
            bool road = mark == MarkRoad, bare = mark == MarkBare;
            if (wet == WetSea) return h < SeaLevel ? Pond : Dirt;
            if (wet == WetRiver) return Stream;
            if (wet == WetLake) return Pond;
            if (road) return Dirt;
            if (bare) return Stone;
            if (mark == MarkClear) return Grass;
            if (slope >= SteepUnits) return Dirt;
            if (wet == WetShore) return Dirt;
            long trees = 0, brush = 0, dirt = 0;
            for (int s = 0; s < 4; s++)
            {
                long w = b.Weight(s);
                if (w <= 0) continue;
                var look = Ground(table.Rows[b.Id(s)].Id);
                trees += w * look.trees;
                brush += w * look.brush;
                dirt += w * look.dirt;
            }
            long roll = WorldHash.Hash4(seed, SaltCell, x, y) % 1000;
            if (h > TreeLine) trees /= 4;
            if (roll < trees / BiomeMath.One) return Tree;
            if (roll < (trees + brush) / BiomeMath.One) return Brush;
            if (roll >= 1000 - dirt / BiomeMath.One) return Dirt;
            return Grass;
        }

        // Per biome, per thousand cells: trees, brush and bare ground.
        static (int trees, int brush, int dirt) Ground(string biome) => biome switch
        {
            "temperate" => (170, 20, 10),
            "grassland" => (22, 30, 10),
            "mountains" => (40, 10, 200),
            "tundra" => (6, 40, 120),
            "salt-flat" => (0, 2, 980),
            "foggy-marsh" => (60, 90, 40),
            "ashlands" => (3, 5, 900),
            _ => (30, 20, 50),
        };

        // ap spot every 8 cells, jittered within its 8 × 8 block, with one roll: a boulder or a crate where the land
        // allows, by the biome there, but never in a spawn's clearing or on a steep face. Its place in the chunk's list is
        // its spot's order, so ids hold.
        void SeedThings(GeneratedChunk c, BiomeBlend[] blend, bool[] clear, bool[] steep)
        {
            const int spot = 8;
            for (int sj = 0; sj < Size / spot; sj++)
                for (int si = 0; si < Size / spot; si++)
                {
                    int x = c.X * Size + si * spot, y = c.Y * Size + sj * spot;
                    uint h = WorldHash.Hash4(seed, SaltSpot, x, y);
                    int i = si * spot + (int)(h & 7), j = sj * spot + (int)((h >> 3) & 7);
                    char cell = c.Cells[j * Size + i];
                    if ((cell != Grass && cell != Dirt && cell != Brush) || clear[j * Size + i] || steep[j * Size + i]) continue;
                    string biome = table.Rows[blend[j * Size + i].Dominant].Id;
                    int odds = biome switch { "mountains" => 60, "tundra" => 30, "ashlands" => 25, "salt-flat" => 4, _ => 6 };
                    if ((h >> 8) % 1000 >= (uint)odds) continue;
                    c.Things.Add(new JObject { ["kind"] = "boulder", ["cell"] = new JArray(i, j), ["note"] = "seeded" });
                }
        }

        static uint HashOf(GeneratedChunk c)
        {
            unchecked
            {
                uint h = 2166136261u;
                void Int(int v)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        h ^= (byte)(v >> (8 * k));
                        h *= 16777619u;
                    }
                }
                Int(c.X);
                Int(c.Y);
                foreach (int v in c.Land) Int(v);
                foreach (char ch in c.Cells) Int(ch);
                foreach (char ch in c.Things.ToString(Newtonsoft.Json.Formatting.None)) Int(ch);
                return h;
            }
        }
    }
}
