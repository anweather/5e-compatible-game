// A chunk's land as bytes (LandChunks, the World's packed chunks); moved out of ChunkCells.cs so the land (Chem.Land)
// can use it below the World.
using System;

namespace Chem
{
    internal static class LandCodec
    {
        public const int MostBytes = Chunks.Area * 5;

        static int Predict(int[] v, int i)
        {
            if (i == 0) return 0;
            if (i < Chunks.Size) return v[i - 1];
            if ((i & Chunks.Mask) == 0) return v[i - Chunks.Size];
            return unchecked(v[i - 1] + v[i - Chunks.Size] - v[i - Chunks.Size - 1]);
        }

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
