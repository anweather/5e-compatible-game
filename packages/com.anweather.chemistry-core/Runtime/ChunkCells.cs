// The sparse World, step 3 (D17, docs/design/sparse-world.md): the core's per-cell state beside the cells, held a chunk at
// a time rather than as arrays the size of the map. What few chunks hold (the land the stamps and the map moved, the bare
// cells) is a ChunkCells, an array a chunk only for the chunks a write reached; the land under every cell is in an
// unpacked chunk's block and, for a packed chunk, in LandCodec's few bytes (World.cs, SetLand).
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>
    /// A value a cell over a w × h map, held a chunk at a time and only for the chunks a write reached: the rest read as
    /// default. Reads by world cell, as the map-sized array it stands for was read.
    /// </summary>
    internal sealed class ChunkCells<T> where T : struct
    {
        readonly int w, h, across, down;
        readonly bool wrapX, wrapY;
        readonly T[][] byChunk;

        // wrapX: the map wraps east-west (World.WrapX, a whole number of chunks wide), so an x past the seam folds; wrapY,
        // north-south too (a torus, World.WrapY), so a y past the south seam folds.
        public ChunkCells(int w, int h, bool wrapX = false, bool wrapY = false)
        {
            this.w = w;
            this.h = h;
            this.wrapX = wrapX;
            this.wrapY = wrapY;
            across = Chunks.ChunkOf(w - 1) + 1;
            down = Chunks.ChunkOf(h - 1) + 1;
            byChunk = new T[across * down][];
        }

        static int Local(int x, int y) => (Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x);

        int ChunkIndex(int x, int y)
        {
            int cx = Chunks.ChunkOf(x);
            if (wrapX && (uint)cx >= (uint)across) cx = ((cx % across) + across) % across;
            int cy = Chunks.ChunkOf(y);
            if (wrapY && (uint)cy >= (uint)down) cy = ((cy % down) + down) % down;
            return cy * across + cx;
        }

        /// <summary>The value at a cell on the map: default where nothing was written.</summary>
        public T this[int x, int y]
        {
            get
            {
                var a = byChunk[ChunkIndex(x, y)];
                return a == null ? default : a[Local(x, y)];
            }
        }

        /// <summary>The value at a cell on the map, to write: its chunk's array is made at the first write.</summary>
        public ref T Cell(int x, int y)
        {
            ref var a = ref byChunk[ChunkIndex(x, y)];
            a ??= new T[Chunks.Area];
            return ref a[Local(x, y)];
        }

        /// <summary>The chunks a write reached.</summary>
        public int ChunkCount
        {
            get
            {
                int n = 0;
                foreach (var a in byChunk) if (a != null) n++;
                return n;
            }
        }

        /// <summary>What it holds, for a value of <paramref name="size"/> bytes: its chunk arrays and the index of them.</summary>
        public long Bytes(int size) => 24 + 8L * byChunk.Length + ChunkCount * (24 + (long)size * Chunks.Area);

        /// <summary>The cells whose value isn't default, row by row.</summary>
        public IEnumerable<(int x, int y)> Cells()
        {
            var cmp = EqualityComparer<T>.Default;
            for (int cy = 0; cy * Chunks.Size < h; cy++)
                for (int y = cy * Chunks.Size; y < Math.Min(h, (cy + 1) * Chunks.Size); y++)
                    for (int cx = 0; cx < across; cx++)
                    {
                        var a = byChunk[cy * across + cx];
                        if (a == null) continue;
                        for (int x = cx * Chunks.Size; x < Math.Min(w, (cx + 1) * Chunks.Size); x++)
                            if (!cmp.Equals(a[Local(x, y)], default)) yield return (x, y);
                    }
        }
    }

}
