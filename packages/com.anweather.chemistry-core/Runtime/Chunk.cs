// Chunks: the unit the world is stored, simulated and (later) streamed in.
// See docs/WORLD_STREAMING.md. The World keeps its cells here, keyed by chunk
// coordinate; World.Chunks.cs holds the store and the active-chunk set.
using System.Runtime.CompilerServices;

namespace Chem
{
    public static class Chunks
    {
        public const int Shift = 6;
        public const int Size = 1 << Shift;
        public const int Mask = Size - 1;
        public const int Area = Size * Size;

        public static int ChunkOf(int cell) => cell >> Shift;

        public static int LocalOf(int cell) => cell & Mask;

        public static long Key(int cx, int cy) => ((long)cy << 32) | (uint)cx;

        public static int EdgesOf(int i)
        {
            int lx = i & Mask, edges = 0;
            if (i < Size) edges |= 1;
            if (lx == Mask) edges |= 2;
            if (i >= Area - Size) edges |= 4;
            if (lx == 0) edges |= 8;
            return edges;
        }
    }

    internal sealed class ChunkBlock
    {
        public readonly Cell[] Cells = new Cell[Chunks.Area];
        public readonly int[] FireGeneration = new int[Chunks.Area];
        public readonly byte[] Marks = new byte[Chunks.Area];
        public int[] SnapWater, SnapHeight;
        public bool[] SnapFrozen;
        public byte[] SnapKind;
        public string[] SnapT;
        public int[] Land;
    }

    internal sealed class Chunk
    {
        public readonly int X, Y;
        ChunkBlock block;

        public PackedChunk Packed;
        public byte[] PackedLand;
        public PackedChunk Start;
        public World Owner;
        public int Needed;

        internal ChunkBlock Block
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => block ?? Owner.UnpackOnRead(this);
        }

        internal ChunkBlock Held => block;

        internal void Hold(ChunkBlock b) => block = b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int i) => Block.Cells[i];

        public Cell[] ActiveCells => Block.Cells;
        public int[] FireGeneration => Block.FireGeneration;
        public byte[] Marks => Block.Marks;
        public Chunk North, East, South, West;
        public bool Active = true;
        public int LastLive = World.NeverLive;
        public readonly int[] EdgeLive = { World.NeverLive, World.NeverLive, World.NeverLive, World.NeverLive };
        public int StoppedAt = World.NeverLive;
        public int CaughtUp;
        public int Changed;
        public int Fit = 1;

        public readonly bool[] SnapNeighbourActive = new bool[4];
        public bool SnapValid;
        public bool LandLater;
        public bool Unchanged;
        public int UnchangedFor;
        public int LastFlowTick = World.NeverLive;
        public bool WaterStill, WaterDisturbed;

        public Chunk(int x, int y)
        {
            X = x;
            Y = y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Cell Next(Chunk ch, int i, int d, out Chunk next, out int ni)
        {
            const int lastRow = Chunks.Area - Chunks.Size;
            switch (d)
            {
                case 0:
                    if (i >= Chunks.Size) { next = ch; ni = i - Chunks.Size; }
                    else { next = ch.North; ni = i + lastRow; }
                    break;
                case 1:
                    if ((i & Chunks.Mask) != Chunks.Mask) { next = ch; ni = i + 1; }
                    else { next = ch.East; ni = i - Chunks.Mask; }
                    break;
                case 2:
                    if (i < lastRow) { next = ch; ni = i + Chunks.Size; }
                    else { next = ch.South; ni = i - lastRow; }
                    break;
                default:
                    if ((i & Chunks.Mask) != 0) { next = ch; ni = i - 1; }
                    else { next = ch.West; ni = i + Chunks.Mask; }
                    break;
            }
            return next?.CellAt(ni);
        }
    }
}
