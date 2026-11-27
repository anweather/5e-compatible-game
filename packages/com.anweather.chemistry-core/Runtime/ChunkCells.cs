// The sparse World, step 3 (D17, docs/design/sparse-world.md): the core's per-cell state beside the cells, held a chunk at
// a time rather than as arrays the size of the map. What few chunks hold (the land the stamps and the map moved, the bare
// cells) is a ChunkCells, an array a chunk only for the chunks a write reached; the land under every cell is in an
// unpacked chunk's block and, for a packed chunk, in LandCodec's few bytes (World.cs, SetLand).
using System;
using System.Collections.Generic;

namespace Chem
{
    internal sealed class ChunkCells<T> where T : struct
    {
        readonly int w, h, across, down;
        readonly bool wrapX, wrapY;
        readonly T[][] byChunk;

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

        public T this[int x, int y]
        {
            get
            {
                var a = byChunk[ChunkIndex(x, y)];
                return a == null ? default : a[Local(x, y)];
            }
        }

        public ref T Cell(int x, int y)
        {
            ref var a = ref byChunk[ChunkIndex(x, y)];
            a ??= new T[Chunks.Area];
            return ref a[Local(x, y)];
        }

        public int ChunkCount
        {
            get
            {
                int n = 0;
                foreach (var a in byChunk) if (a != null) n++;
                return n;
            }
        }

        public long Bytes(int size) => 24 + 8L * byChunk.Length + ChunkCount * (24 + (long)size * Chunks.Area);

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
