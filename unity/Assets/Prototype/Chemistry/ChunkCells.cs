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
        readonly int w, h, across;
        readonly T[][] byChunk;

        public ChunkCells(int w, int h)
        {
            this.w = w;
            this.h = h;
            across = Chunks.ChunkOf(w - 1) + 1;
            byChunk = new T[across * (Chunks.ChunkOf(h - 1) + 1)][];
        }

        static int Local(int x, int y) => (Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x);

        int ChunkIndex(int x, int y) => Chunks.ChunkOf(y) * across + Chunks.ChunkOf(x);

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

    /// <summary>
    /// A chunk's land (World.SetLand: an int a cell, World.NoLand on water) in a few bytes, for a packed chunk: each cell
    /// as its difference from what the cells before it predict (left + up − up-left, the left alone on the top row, the up
    /// alone down the left side), zigzagged into a varint, and a run of cells the prediction gets exactly as one varint.
    /// Smooth land is a byte or so a cell; a chunk of sea or of flat land, a few bytes. Exact for any ints.
    /// </summary>
    internal static class LandCodec
    {
        /// <summary>The most a chunk's land encodes to: five bytes a cell.</summary>
        public const int MostBytes = Chunks.Area * 5;

        static int Predict(int[] v, int i)
        {
            if (i == 0) return 0;
            if (i < Chunks.Size) return v[i - 1];
            if ((i & Chunks.Mask) == 0) return v[i - Chunks.Size];
            return unchecked(v[i - 1] + v[i - Chunks.Size] - v[i - Chunks.Size - 1]);
        }

        /// <summary>A chunk's land, Area ints, as its bytes; <paramref name="scratch"/> (MostBytes) is written through.</summary>
        public static byte[] Encode(int[] v, byte[] scratch)
        {
            int n = 0;
            for (int i = 0; i < Chunks.Area;)
            {
                int d = unchecked(v[i] - Predict(v, i));
                if (d == 0)
                {
                    int run = 1;
                    while (i + run < Chunks.Area && v[i + run] == Predict(v, i + run)) run++;
                    n = Put(scratch, n, ((ulong)run << 1) | 1);
                    i += run;
                    continue;
                }
                uint zz = unchecked((uint)((d << 1) ^ (d >> 31)));
                n = Put(scratch, n, (ulong)zz << 1);
                i++;
            }
            var bytes = new byte[n];
            Buffer.BlockCopy(scratch, 0, bytes, 0, n);
            return bytes;
        }

        /// <summary>A chunk's land back from its bytes into <paramref name="v"/> (Area ints).</summary>
        public static void Decode(byte[] bytes, int[] v)
        {
            int at = 0;
            for (int i = 0; i < Chunks.Area;)
            {
                ulong t = Get(bytes, ref at);
                if ((t & 1) != 0)
                {
                    for (int end = i + (int)(t >> 1); i < end; i++) v[i] = Predict(v, i);
                    continue;
                }
                uint zz = (uint)(t >> 1);
                int d = (int)(zz >> 1) ^ -(int)(zz & 1);
                v[i] = unchecked(Predict(v, i) + d);
                i++;
            }
        }

        static int Put(byte[] b, int n, ulong t)
        {
            while (t >= 0x80)
            {
                b[n++] = (byte)(t | 0x80);
                t >>= 7;
            }
            b[n++] = (byte)t;
            return n;
        }

        static ulong Get(byte[] b, ref int at)
        {
            ulong t = 0;
            int shift = 0;
            byte x;
            do
            {
                x = b[at++];
                t |= (ulong)(x & 0x7f) << shift;
                shift += 7;
            }
            while (x >= 0x80);
            return t;
        }
    }
}
