// Chunk size and the maths from cell coordinates to chunks (docs/WORLD_STREAMING.md), moved out of Chunk.cs so the
// land (Chem.Land) can use them below the World.
using System;
using System.Collections.Generic;

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

        public static List<(int cx, int cy)> Within(IReadOnlyList<(int cx, int cy)> chunks, double x, double y, double radius, int wrapCells = 0, int wrapCellsY = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var near = new List<(int cx, int cy)>();
            foreach (var (cx, cy) in chunks)
            {
                double x0 = cx * Size, y0 = cy * Size;
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
