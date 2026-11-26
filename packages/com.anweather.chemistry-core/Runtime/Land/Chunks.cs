// Chunk size and the maths from cell coordinates to chunks (docs/WORLD_STREAMING.md), moved out of Chunk.cs so the
// land (Chem.Land) can use them below the World.
using System;
using System.Collections.Generic;

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

        /// <summary>
        /// The chunks any part of which lies within <paramref name="radius"/> cells of (x, y), a point
        /// in cell units (cell (0, 0) spans 0 to 1), by row then column. For what a view draws by
        /// distance from its camera, such as the terrains' grass (ChemGrid.DetailChunks, ChunkStreaming.Within): a chunk
        /// left out has no part near enough to draw any. On a world <paramref name="wrapCells"/> cells round (World.W
        /// when it wraps; 0 for none) a chunk is measured the short way round the seam; on a torus <paramref name="wrapCellsY"/>
        /// cells down (World.H) the short way round the south seam too.
        /// </summary>
        public static List<(int cx, int cy)> Within(IReadOnlyList<(int cx, int cy)> chunks, double x, double y, double radius, int wrapCells = 0, int wrapCellsY = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var near = new List<(int cx, int cy)>();
            foreach (var (cx, cy) in chunks)
            {
                double x0 = cx * Size, y0 = cy * Size;
                // The copy of the chunk nearest x: its middle within half a world of it.
                if (wrapCells > 0) x0 += wrapCells * Math.Round((x - (x0 + Size / 2.0)) / wrapCells);
                if (wrapCellsY > 0) y0 += wrapCellsY * Math.Round((y - (y0 + Size / 2.0)) / wrapCellsY);
                double dx = Math.Max(0, Math.Max(x0 - x, x - (x0 + Size)));
                double dy = Math.Max(0, Math.Max(y0 - y, y - (y0 + Size)));
                if (dx * dx + dy * dy <= radius * radius) near.Add((cx, cy));
            }
            near.Sort((a, b) => a.cy != b.cy ? a.cy.CompareTo(b.cy) : a.cx.CompareTo(b.cx));
            return near;
        }
    }
}
