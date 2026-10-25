// The chunk terrains' layout and where the chemistry's cells sit on them
// (docs/WORLD_STREAMING.md, part 1). Pure maths, no Unity: CellHeightfield and
// CellPaint use it to shape and paint the chunk terrains from the cells, and the
// view uses it to place the terrains.
//
// Not a port: the sandbox has no terrain.
//
// Three coordinate systems:
//  - grid cell (x, y): the World's cells, y growing south;
//  - world cell (u, v): cells of the whole terrain, counted from the south-west
//    corner of chunk (0, 0), v growing north. u = x + U0, v = V0 - y;
//  - chunk (i, j) = (floor(u / 64), floor(v / 64)), with samples (sx, sz) or
//    alphamap texels (tx, tz) inside it, sz/tz growing north.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>
    /// A rectangle of heightmap samples or alphamap texels in one chunk: columns X..XMax (east)
    /// and rows Z..ZMax (north), inclusive. Unity arrays for it are [row, column], i.e.
    /// [z - Z, x - X], as SetHeights and SetAlphamaps take them.
    /// </summary>
    public readonly struct ChunkRect : IEquatable<ChunkRect>
    {
        public readonly int I, J, X, Z, XMax, ZMax;

        public ChunkRect(int i, int j, int x, int z, int xMax, int zMax)
        {
            I = i;
            J = j;
            X = x;
            Z = z;
            XMax = xMax;
            ZMax = zMax;
        }

        public int Width => XMax - X + 1;
        public int Height => ZMax - Z + 1;

        /// <summary>The smallest rectangle holding both (same chunk).</summary>
        public ChunkRect Union(ChunkRect o) =>
            new ChunkRect(I, J, Math.Min(X, o.X), Math.Min(Z, o.Z), Math.Max(XMax, o.XMax), Math.Max(ZMax, o.ZMax));

        public bool Equals(ChunkRect other) =>
            I == other.I && J == other.J && X == other.X && Z == other.Z && XMax == other.XMax && ZMax == other.ZMax;

        public override bool Equals(object obj) => obj is ChunkRect r && Equals(r);
        public static bool operator ==(ChunkRect a, ChunkRect b) => a.Equals(b);
        public static bool operator !=(ChunkRect a, ChunkRect b) => !a.Equals(b);
        public override int GetHashCode() => HashCode.Combine(I, J, X, Z, XMax, ZMax);
        public override string ToString() => $"chunk ({I}, {J}) [{X}..{XMax}] x [{Z}..{ZMax}]";
    }

    /// <summary>Collects rectangles, one bounding rectangle per chunk.</summary>
    public sealed class ChunkRects
    {
        readonly Dictionary<(int i, int j), ChunkRect> rects = new Dictionary<(int i, int j), ChunkRect>();

        public void Add(ChunkRect r) =>
            rects[(r.I, r.J)] = rects.TryGetValue((r.I, r.J), out var had) ? had.Union(r) : r;

        public int Count => rects.Count;

        /// <summary>The rectangles by chunk row, then column.</summary>
        public List<ChunkRect> ToList()
        {
            var list = new List<ChunkRect>(rects.Values);
            list.Sort((a, b) => a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I));
            return list;
        }
    }

    public sealed class TerrainGrid
    {
        /// <summary>Cells along a chunk terrain's side: the chemistry's chunk size.</summary>
        public const int ChunkCells = Chunks.Size;
        /// <summary>Heightmap samples per cell on a full-detail chunk (257 heightmap); a coarse one has 1 (65).</summary>
        public const int HighSamplesPerCell = 4;

        readonly (int i0, int j0, int i1, int j1) highRes;

        /// <param name="cellSize">A cell's side in metres.</param>
        /// <param name="origin">World x, z of the south-west corner of chunk (0, 0).</param>
        /// <param name="chunks">How many chunk terrains along x and z.</param>
        /// <param name="gridCorner">The world cell (u, v) of grid cell (0, 0).</param>
        /// <param name="gridSize">The World's size in cells (W, H).</param>
        /// <param name="highRes">The full-detail chunks, an inclusive rectangle of chunk coordinates.</param>
        public TerrainGrid(float cellSize, (float x, float z) origin, (int x, int z) chunks,
            (int u, int v) gridCorner, (int w, int h) gridSize, (int i0, int j0, int i1, int j1) highRes)
        {
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            CellSize = cellSize;
            OriginX = origin.x;
            OriginZ = origin.z;
            ChunksX = chunks.x;
            ChunksZ = chunks.z;
            U0 = gridCorner.u;
            V0 = gridCorner.v;
            GridW = gridSize.w;
            GridH = gridSize.h;
            this.highRes = highRes;
        }

        /// <summary>
        /// The valley: 9×9 chunks of 96 m, the 200×175 yard with cell (x, y) centred at world
        /// ((x - 87) * 1.5, (74 - y) * 1.5), full detail on chunks i 3..5, j 2..4.
        /// </summary>
        public static TerrainGrid Valley() =>
            new TerrainGrid(1.5f, (-419.25f, -368.25f), (9, 9), (192, 319), (200, 175), (3, 2, 5, 4));

        public float CellSize { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public int ChunksX { get; }
        public int ChunksZ { get; }
        /// <summary>World cell of grid cell (0, 0): u = x + U0, v = V0 - y.</summary>
        public int U0 { get; }
        public int V0 { get; }
        public int GridW { get; }
        public int GridH { get; }

        /// <summary>A chunk terrain's side in metres.</summary>
        public float ChunkSize => ChunkCells * CellSize;

        // ---------- chunks ----------

        public bool HasChunk(int i, int j) => i >= 0 && j >= 0 && i < ChunksX && j < ChunksZ;

        public bool IsHighRes(int i, int j) =>
            i >= highRes.i0 && i <= highRes.i1 && j >= highRes.j0 && j <= highRes.j1;

        /// <summary>Heightmap samples per cell along each axis: 4 on a full-detail chunk, else 1.</summary>
        public int SamplesPerCell(int i, int j) => IsHighRes(i, j) ? HighSamplesPerCell : 1;

        /// <summary>The chunk's heightmap resolution: 257 or 65.</summary>
        public int Resolution(int i, int j) => ChunkCells * SamplesPerCell(i, j) + 1;

        /// <summary>The chunk's alphamap resolution: 256 or 64, a block of SamplesPerCell² texels per cell.</summary>
        public int TexelResolution(int i, int j) => ChunkCells * SamplesPerCell(i, j);

        /// <summary>World x, z of the chunk's south-west corner (its Terrain's position).</summary>
        public (float x, float z) ChunkCorner(int i, int j) => (OriginX + i * ChunkSize, OriginZ + j * ChunkSize);

        /// <summary>The whole heightmap of a chunk as a rectangle.</summary>
        public ChunkRect SampleRect(int i, int j) => new ChunkRect(i, j, 0, 0, Resolution(i, j) - 1, Resolution(i, j) - 1);

        /// <summary>
        /// The chunks whose heightmaps the grid's cells reach: those holding a cell, plus those
        /// whose edge runs along the grid's west or north side.
        /// </summary>
        public List<(int i, int j)> GridChunks()
        {
            var (iLo, iHi) = ChunkSpan(U0, U0 + GridW);
            var (jLo, jHi) = ChunkSpan(V0 - GridH + 1, V0 + 1);
            var list = new List<(int i, int j)>();
            for (int j = jLo; j <= jHi; j++)
                for (int i = iLo; i <= iHi; i++)
                    if (HasChunk(i, j)) list.Add((i, j));
            return list;
        }

        /// <summary>The chunks (inclusive) whose cell-corner range [64c, 64(c+1)] meets corners c0..c1.</summary>
        internal static (int lo, int hi) ChunkSpan(int c0, int c1) =>
            (Chunks.ChunkOf(c0 + ChunkCells - 1) - 1, Chunks.ChunkOf(c1));

        // ---------- cells ----------

        public bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < GridW && y < GridH;

        public (int u, int v) WorldCell(int x, int y) => (x + U0, V0 - y);
        public (int x, int y) GridCell(int u, int v) => (u - U0, V0 - v);

        /// <summary>The chunk a grid cell lies in.</summary>
        public (int i, int j) ChunkOfCell(int x, int y) => (Chunks.ChunkOf(x + U0), Chunks.ChunkOf(V0 - y));

        public (float x, float z) CellCenter(int x, int y) =>
            (OriginX + (x + U0 + 0.5f) * CellSize, OriginZ + (V0 - y + 0.5f) * CellSize);

        /// <summary>The grid cell under a world point (it may be off the grid: check InGrid).</summary>
        public (int x, int y) CellAt(float wx, float wz)
        {
            int u = (int)Math.Floor((wx - OriginX) / CellSize), v = (int)Math.Floor((wz - OriginZ) / CellSize);
            return GridCell(u, v);
        }

        /// <summary>Every grid cell, row by row.</summary>
        public IEnumerable<(int x, int y)> Cells()
        {
            for (int y = 0; y < GridH; y++)
                for (int x = 0; x < GridW; x++) yield return (x, y);
        }

        // ---------- samples and texels ----------

        /// <summary>
        /// A sample that belongs to the cell alone: its centre on a full-detail chunk, its
        /// south-west corner on a coarse one (which has no sample inside a cell).
        /// </summary>
        public (int i, int j, int sx, int sz) CellSample(int x, int y)
        {
            var (u, v) = WorldCell(x, y);
            var (i, j) = ChunkOfCell(x, y);
            int p = SamplesPerCell(i, j), mid = p / 2;
            return (i, j, Chunks.LocalOf(u) * p + mid, Chunks.LocalOf(v) * p + mid);
        }

        /// <summary>The grid cell a sample lies in; a sample on a cell border counts as the cell north-east of it.</summary>
        public (int x, int y) CellOfSample(int i, int j, int sx, int sz)
        {
            int p = SamplesPerCell(i, j);
            return GridCell(i * ChunkCells + sx / p, j * ChunkCells + sz / p);
        }

        public (float x, float z) SamplePosition(int i, int j, int sx, int sz)
        {
            var (cx, cz) = ChunkCorner(i, j);
            float step = CellSize / SamplesPerCell(i, j);
            return (cx + sx * step, cz + sz * step);
        }

        /// <summary>The grid cell an alphamap texel paints.</summary>
        public (int x, int y) CellOfTexel(int i, int j, int tx, int tz) => CellOfSample(i, j, tx, tz);

        /// <summary>The block of alphamap texels a grid cell paints.</summary>
        public ChunkRect TexelRect(int x, int y)
        {
            var (u, v) = WorldCell(x, y);
            var (i, j) = ChunkOfCell(x, y);
            int p = SamplesPerCell(i, j), tx = Chunks.LocalOf(u) * p, tz = Chunks.LocalOf(v) * p;
            return new ChunkRect(i, j, tx, tz, tx + p - 1, tz + p - 1);
        }
    }
}
