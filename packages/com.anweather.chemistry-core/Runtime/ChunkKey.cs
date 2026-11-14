// The open world's address for a chunk (D20, docs/design/core-package/open-world.md): a layer and a place, plain ints,
// negative allowed, with no bounds and no origin to agree. A layer is a sheet of terrain with its own cells (the surface
// 0, a cave, a sky island), numbered by the host game.
//
// An open World holds every layer in one sheet of rows: a layer is a run of LayerRows rows, so row = y + layer *
// LayerRows, and a chunk's row is its y plus layer * LayerChunkRows. Layers' rows never touch (a fill more than half a
// layer's rows from 0 is refused), so cells in different layers never meet; and since the layers' rows lie in the
// layers' order, the World's row-major walks are the keys' order: layer, then y, then x. What crosses a World's API (a
// cast, At, ChunkCells, a ChunkSwitch) is in rows; Row and KeyOfChunk turn one into the other.
using System;
using System.Globalization;

namespace Chem
{
    /// <summary>A chunk of the open world: its layer and its chunk coordinates in that layer (cell >> Chunks.Shift).</summary>
    public readonly struct ChunkKey : IEquatable<ChunkKey>, IComparable<ChunkKey>
    {
        /// <summary>The chunk rows a layer holds in the open World's sheet: 2^20, about 67,000 km of 1 m cells.</summary>
        public const int LayerChunkRows = 1 << 20;

        /// <summary>The cell rows a layer holds (LayerChunkRows chunks of Chunks.Size).</summary>
        public const int LayerRows = LayerChunkRows << Chunks.Shift;

        /// <summary>The layers there may be, MinLayer to MaxLayer: as many as an int's rows hold.</summary>
        public const int MinLayer = -15, MaxLayer = 15;

        /// <summary>How far from 0 a chunk may be, either way, in a layer (half its rows, so layers never touch; the same across).</summary>
        public const int MostChunks = LayerChunkRows / 2 - 1;

        public ChunkKey(int layer, int x, int y)
        {
            Layer = layer;
            X = x;
            Y = y;
        }

        public int Layer { get; }
        public int X { get; }
        public int Y { get; }

        /// <summary>The chunk a cell of a layer is in: floor division, so cell -1 is in chunk -1.</summary>
        public static ChunkKey OfCell(int layer, int x, int y) => new ChunkKey(layer, Chunks.ChunkOf(x), Chunks.ChunkOf(y));

        /// <summary>
        /// The cell a host game's position falls in, along one axis: floor(position / cellSize), so negatives round down.
        /// On the host only (floats never decide shared state: what it makes of them is stamped as commands).
        /// </summary>
        public static int CellOf(double position, double cellSize) => (int)Math.Floor(position / cellSize);

        /// <summary>Whether a chunk may be in the open world: its layer one there may be, and it within MostChunks of 0 both ways.</summary>
        public bool Fits => Layer >= MinLayer && Layer <= MaxLayer && Math.Abs(X) <= MostChunks && Math.Abs(Y) <= MostChunks;

        /// <summary>A layer's cell row in the open World's sheet, for a cast or a read (World.At(x, Row(layer, y))).</summary>
        public static int Row(int layer, int y) => y + layer * LayerRows;

        /// <summary>A row of the open World's sheet back to its layer and the cell's y in that layer.</summary>
        public static (int layer, int y) OfRow(int row)
        {
            int layer = FloorDiv(row + LayerRows / 2, LayerRows);
            return (layer, row - layer * LayerRows);
        }

        /// <summary>The chunk's coordinates in the open World's sheet (World.HasChunk, IsActive, a ChunkSwitch).</summary>
        public (int cx, int cy) Chunk => (X, Y + Layer * LayerChunkRows);

        /// <summary>The key of a chunk of the open World's sheet.</summary>
        public static ChunkKey KeyOfChunk(int cx, int cy)
        {
            int layer = FloorDiv(cy + LayerChunkRows / 2, LayerChunkRows);
            return new ChunkKey(layer, cx, cy - layer * LayerChunkRows);
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
        }

        /// <summary>Chunks from this one to another of its layer, the larger of across and down; int.MaxValue in another layer.</summary>
        public int DistanceTo(ChunkKey other) =>
            other.Layer != Layer ? int.MaxValue : (int)Math.Min(int.MaxValue, Math.Max(Math.Abs((long)other.X - X), Math.Abs((long)other.Y - Y)));

        /// <summary>The open world's order: layer, then row, then column (the order every walk over its chunks takes).</summary>
        public int CompareTo(ChunkKey other)
        {
            if (Layer != other.Layer) return Layer.CompareTo(other.Layer);
            return Y != other.Y ? Y.CompareTo(other.Y) : X.CompareTo(other.X);
        }

        public bool Equals(ChunkKey other) => Layer == other.Layer && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ChunkKey k && Equals(k);
        public override int GetHashCode() => unchecked((Layer * 486187739) ^ (Y * 16777619) ^ X);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}:{1},{2}", Layer, X, Y);
        public static bool operator ==(ChunkKey a, ChunkKey b) => a.Equals(b);
        public static bool operator !=(ChunkKey a, ChunkKey b) => !a.Equals(b);
        public static bool operator <(ChunkKey a, ChunkKey b) => a.CompareTo(b) < 0;
        public static bool operator >(ChunkKey a, ChunkKey b) => a.CompareTo(b) > 0;
        public static bool operator <=(ChunkKey a, ChunkKey b) => a.CompareTo(b) <= 0;
        public static bool operator >=(ChunkKey a, ChunkKey b) => a.CompareTo(b) >= 0;
    }
}
