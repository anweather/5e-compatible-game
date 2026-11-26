// A chunk's land as bytes (LandChunks, the World's packed chunks); moved out of ChunkCells.cs so the land (Chem.Land)
// can use it below the World.
using System;

namespace Chem
{
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
