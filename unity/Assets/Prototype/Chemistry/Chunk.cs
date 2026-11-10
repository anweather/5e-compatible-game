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
    /// An unpacked chunk's cells and the per-cell state that only matters while it's unpacked (the sparse World,
    /// docs/design/sparse-world.md): its Cell objects, fire generations, per-tick marks and still-water snapshot. Taken from
    /// the World's pool as a chunk unpacks and given back as it packs (World.Pack.cs), so unpacking makes no garbage.
    /// </summary>
    internal sealed class ChunkBlock
    {
        public readonly Cell[] Cells = new Cell[Chunks.Area];
        /// <summary>
        /// How many spreads each burning cell is from where its fire started (see FireParams.FadeCells).
        /// Kept outside Cell so cell state, checksums and the TS parity tests are unaffected.
        /// </summary>
        public readonly int[] FireGeneration = new int[Chunks.Area];
        /// <summary>Per-tick marks machines read in the TS (World.Mark*); cleared at the end of each tick.</summary>
        public readonly byte[] Marks = new byte[Chunks.Area];
        // Still water (World.StillWater): what the water pass reads of each cell as it was at the last pass, made at a
        // chunk's first pass and kept with the block.
        public int[] SnapWater, SnapHeight;
        public bool[] SnapFrozen;
        public byte[] SnapKind;
        public string[] SnapT;
        /// <summary>The land under each cell (World.SetLand), made at the first unpack of a world with land; meaningless without.</summary>
        public int[] Land;
    }

    /// <summary>
    /// One chunk's cells (row-major, local index = ly * Size + lx) and the per-cell sim state
    /// kept outside Cell. Slots outside the world's extent are null. Unpacked, it holds them in a
    /// block (ChunkBlock); packed (an inactive chunk only, World.Pack.cs), as its changes against the
    /// world's source. How it holds them never shows: every read of a packed chunk through CellAt
    /// (or its per-cell arrays) unpacks it first.
    /// </summary>
    internal sealed class Chunk
    {
        public readonly int X, Y;
        // Its block while it's unpacked, else null (and Packed holds its changes).
        ChunkBlock block;

        /// <summary>Its changes against the world's source while it's packed, else null (World.Pack.cs).</summary>
        public PackedChunk Packed;
        /// <summary>Its land while it's packed, as LandCodec's bytes; null while it's unpacked (the block holds it) or the world has none.</summary>
        public byte[] PackedLand;
        /// <summary>Its starting state (World.KeepStart) as changes against the starting state's source; null when none is kept.</summary>
        public PackedChunk Start;
        /// <summary>The world it's a chunk of, for unpacking it on a read.</summary>
        public World Owner;
        /// <summary>
        /// The last tick it was active, beside an active chunk or in the view's drawn hint, or was unpacked: when it may pack
        /// (World.PackDue). Storage, not shared state: never hashed or saved.
        /// </summary>
        public int Needed;

        /// <summary>Its block, unpacking it first if it's packed.</summary>
        internal ChunkBlock Block
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => block ?? Owner.UnpackOnRead(this);
        }

        /// <summary>Its block as held: null while it's packed (for the reads that mustn't unpack it, World.Pack.cs).</summary>
        internal ChunkBlock Held => block;

        /// <summary>Gives it a block (unpacking) or takes it away (packing), for World.Pack.cs alone.</summary>
        internal void Hold(ChunkBlock b) => block = b;

        /// <summary>The cell at local index i, or null outside the world's extent. A packed chunk unpacks to be read (World.Pack.cs).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int i) => Block.Cells[i];

        /// <summary>
        /// Its cells as one array by local index, for the tick's hot passes over an active chunk (World.Sim, Weather,
        /// Machines, StillWater) and a view's pass over a chunk it draws (World.ChunkCells).
        /// </summary>
        public Cell[] ActiveCells => Block.Cells;
        /// <summary>
        /// How many spreads each burning cell is from where its fire started (see FireParams.FadeCells).
        /// Kept outside Cell so cell state, checksums and the TS parity tests are unaffected.
        /// </summary>
        public int[] FireGeneration => Block.FireGeneration;
        /// <summary>Per-tick marks machines read in the TS (World.Mark*); cleared at the end of each tick.</summary>
        public byte[] Marks => Block.Marks;
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
        /// <summary>Cells that differed from the starting state when it was last audited (World.Dragon).</summary>
        public int Changed;
        /// <summary>
        /// The rules its cells were last made to fit (World's fit number, World.Fit.cs): behind the world's after a switch
        /// that could unfit cells while it was inactive, until it activates. Shared state: saved and in the cell hash.
        /// </summary>
        public int Fit = 1;

        // Still water (World.StillWater): the neighbours' active state at the last water pass (the cells' snapshot is in
        // the block).
        public readonly bool[] SnapNeighbourActive = new bool[4];
        /// <summary>The snapshot is from the last water pass (false after activating, unpacking or a rule switch).</summary>
        public bool SnapValid;
        /// <summary>Nothing the water pass reads changed since the last pass.</summary>
        public bool Unchanged;
        /// <summary>How many passes in a row it has been Unchanged (a thick liquid must sit out its turn first).</summary>
        public int UnchangedFor;
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
            return next?.CellAt(ni);
        }
    }
}
