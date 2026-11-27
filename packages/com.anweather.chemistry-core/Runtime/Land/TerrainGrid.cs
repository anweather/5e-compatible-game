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

    public sealed class ChunkRects
    {
        readonly Dictionary<(int i, int j), ChunkRect> rects = new Dictionary<(int i, int j), ChunkRect>();

        public void Add(ChunkRect r) =>
            rects[(r.I, r.J)] = rects.TryGetValue((r.I, r.J), out var had) ? had.Union(r) : r;

        public int Count => rects.Count;

        public List<ChunkRect> ToList()
        {
            var list = new List<ChunkRect>(rects.Values);
            list.Sort((a, b) => a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I));
            return list;
        }
    }

    public sealed class TerrainGrid
    {
        public const int ChunkCells = Chunks.Size;
        public const int HighSamplesPerCell = 4;

        readonly (int i0, int j0, int i1, int j1) highRes;

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

        public static TerrainGrid Valley() =>
            new TerrainGrid(1.5f, (-419.25f, -368.25f), (9, 9), (0, 575), (576, 576), (3, 2, 5, 4));

        public float CellSize { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public int ChunksX { get; }
        public int ChunksZ { get; }
        public int U0 { get; }
        public int V0 { get; }
        public int GridW { get; }
        public int GridH { get; }

        public float ChunkSize => ChunkCells * CellSize;

        public bool HasChunk(int i, int j) => i >= 0 && j >= 0 && i < ChunksX && j < ChunksZ;

        public bool IsHighRes(int i, int j) =>
            i >= highRes.i0 && i <= highRes.i1 && j >= highRes.j0 && j <= highRes.j1;

        public int SamplesPerCell(int i, int j) => IsHighRes(i, j) ? HighSamplesPerCell : 1;

        public int Resolution(int i, int j) => ChunkCells * SamplesPerCell(i, j) + 1;

        public int TexelResolution(int i, int j) => ChunkCells * SamplesPerCell(i, j);

        public (float x, float z) ChunkCorner(int i, int j) => (OriginX + i * ChunkSize, OriginZ + j * ChunkSize);

        public ChunkRect SampleRect(int i, int j) => new ChunkRect(i, j, 0, 0, Resolution(i, j) - 1, Resolution(i, j) - 1);

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

        internal static (int lo, int hi) ChunkSpan(int c0, int c1) =>
            (Chunks.ChunkOf(c0 + ChunkCells - 1) - 1, Chunks.ChunkOf(c1));

        public bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < GridW && y < GridH;

        public (int u, int v) WorldCell(int x, int y) => (x + U0, V0 - y);
        public (int x, int y) GridCell(int u, int v) => (u - U0, V0 - v);

        public (int i, int j) ChunkOfCell(int x, int y) => (Chunks.ChunkOf(x + U0), Chunks.ChunkOf(V0 - y));

        public List<(int i, int j)> ChunksWithin(int x, int y, double radius)
        {
            var all = new List<(int cx, int cy)>(ChunksX * ChunksZ);
            for (int j = 0; j < ChunksZ; j++)
                for (int i = 0; i < ChunksX; i++) all.Add((i, j));
            return Chunks.Within(all, x + U0 + 0.5, V0 - y + 0.5, radius);
        }

        public (float x, float z) CellCenter(int x, int y) =>
            (OriginX + (x + U0 + 0.5f) * CellSize, OriginZ + (V0 - y + 0.5f) * CellSize);

        public (int x, int y) CellAt(float wx, float wz)
        {
            int u = (int)Math.Floor((wx - OriginX) / CellSize), v = (int)Math.Floor((wz - OriginZ) / CellSize);
            return GridCell(u, v);
        }

        public IEnumerable<(int x, int y)> CellsIn(int i, int j)
        {
            int u0 = i * ChunkCells, v0 = j * ChunkCells;
            var (xLo, yHi) = GridCell(u0, v0);
            var (xHi, yLo) = GridCell(u0 + ChunkCells - 1, v0 + ChunkCells - 1);
            for (int y = Math.Max(0, yLo); y <= Math.Min(GridH - 1, yHi); y++)
                for (int x = Math.Max(0, xLo); x <= Math.Min(GridW - 1, xHi); x++) yield return (x, y);
        }

        public IEnumerable<(int x, int y)> Cells()
        {
            for (int y = 0; y < GridH; y++)
                for (int x = 0; x < GridW; x++) yield return (x, y);
        }

        public (int i, int j, int sx, int sz) CellSample(int x, int y)
        {
            var (u, v) = WorldCell(x, y);
            var (i, j) = ChunkOfCell(x, y);
            int p = SamplesPerCell(i, j), mid = p / 2;
            return (i, j, Chunks.LocalOf(u) * p + mid, Chunks.LocalOf(v) * p + mid);
        }

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

        public (int x, int y) CellOfTexel(int i, int j, int tx, int tz) => CellOfSample(i, j, tx, tz);

        public ChunkRect TexelRect(int x, int y)
        {
            var (u, v) = WorldCell(x, y);
            var (i, j) = ChunkOfCell(x, y);
            int p = SamplesPerCell(i, j), tx = Chunks.LocalOf(u) * p, tz = Chunks.LocalOf(v) * p;
            return new ChunkRect(i, j, tx, tz, tx + p - 1, tz + p - 1);
        }
    }
}
