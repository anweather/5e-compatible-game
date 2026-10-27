// Chunks: the unit the world is stored, simulated and (later) streamed in.
// See docs/WORLD_STREAMING.md. The World keeps its cells here, keyed by chunk
// coordinate; World.Chunks.cs holds the store and the active-chunk set.
using System.Runtime.CompilerServices;

namespace Chem
{
    /// <summary>Chunk size and the maths from world-wide cell coordinates to chunks.</summary>
    public static class Chunks
    {
        /// <summary>log2 of Size: the one place the chunk size is set.</summary>
        public const int Shift = 6;
        /// <summary>Cells along a chunk's side (64).</summary>
        public const int Size = 1 << Shift;
        public const int Mask = Size - 1;
        /// <summary>Cells in one chunk.</summary>
        public const int Area = Size * Size;

        /// <summary>The chunk a cell coordinate falls in: floor(cell / Size), negatives included.</summary>
        public static int ChunkOf(int cell) => cell >> Shift;

        /// <summary>The cell's coordinate inside its chunk: cell mod Size, always 0..Size-1.</summary>
        public static int LocalOf(int cell) => cell & Mask;

        /// <summary>One number per chunk coordinate, for the chunk store's dictionary.</summary>
        public static long Key(int cx, int cy) => ((long)cy << 32) | (uint)cx;

        /// <summary>
        /// The chunk edges a local cell index lies on, as bits 1 &lt;&lt; d for d = 0 to 3 (N, E, S, W,
        /// the order of Chunk.Next): 0 inside, two bits in a corner.
        /// </summary>
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

    /// <summary>
    /// One chunk's cells (row-major, local index = ly * Size + lx) and the per-cell sim state
    /// kept outside Cell. Slots outside the world's extent are null.
    /// </summary>
    internal sealed class Chunk
    {
        public readonly int X, Y;
        public readonly Cell[] Cells = new Cell[Chunks.Area];
        /// <summary>
        /// How many spreads each burning cell is from where its fire started (see FireParams.FadeCells).
        /// Kept outside Cell so cell state, checksums and the TS parity tests are unaffected.
        /// </summary>
        public readonly int[] FireGeneration = new int[Chunks.Area];
        /// <summary>Per-tick marks machines read in the TS (World.Mark*); cleared at the end of each tick.</summary>
        public readonly byte[] Marks = new byte[Chunks.Area];
        /// <summary>Loaded neighbours, or null at the edge of what is loaded.</summary>
        public Chunk North, East, South, West;
        public bool Active = true;
        /// <summary>
        /// The last tick anything in it was live: fire, gas, charge, a timer counting down (Life), or
        /// water moving (docs/WORLD_STREAMING.md, "Part 2, second slice"). World.NeverLive for a
        /// chunk nothing has happened in. Kept outside Cell, like FireGeneration, so cell hashes are
        /// unaffected.
        /// </summary>
        public int LastLive = World.NeverLive;
        /// <summary>LastLive for the cells along each edge, N, E, S, W (the order of Next).</summary>
        public readonly int[] EdgeLive = { World.NeverLive, World.NeverLive, World.NeverLive, World.NeverLive };
        /// <summary>The tick it deactivated while still live, to catch up from (World.CatchUp); World.NeverLive if it settled first.</summary>
        public int StoppedAt = World.NeverLive;
        /// <summary>The ticks it caught up when it last activated (0 if it had none to catch up).</summary>
        public int CaughtUp;

        // Still water (World.StillWater): what the water pass reads of each cell (water, height,
        // frozen, terrain) as it was at the last pass, and the neighbours' active state then.
        public int[] SnapWater, SnapHeight;
        public bool[] SnapFrozen;
        public string[] SnapT;
        public readonly bool[] SnapNeighbourActive = new bool[4];
        /// <summary>The snapshot is from the last water pass (false after activating or a rule switch).</summary>
        public bool SnapValid;
        /// <summary>Nothing the water pass reads changed since the last pass.</summary>
        public bool Unchanged;
        /// <summary>The last tick water moved into, out of or within it.</summary>
        public int LastFlowTick = World.NeverLive;
        /// <summary>This tick's water pass may skip it (until water moves in it or next to it: Disturbed).</summary>
        public bool WaterStill, WaterDisturbed;

        public Chunk(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// The cell next to local index i in direction d (0=N 1=E 2=S 3=W), with its chunk and
        /// local index; null off the loaded world or outside its extent.
        /// </summary>
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
            return next?.Cells[ni];
        }
    }
}
