// Fixed-point maths for the biome field (BiomeField, J-079): Q16 values (One = 1.0), integer value noise on WorldHash,
// e^-x, log2 and a square root, all in integers, so every machine gets the same bits. Punchy's BiomeSampling used float
// simplex noise and math.exp/log, which differ between the PC and the Mac; this is what replaces them.
using System;

namespace Chem
{
    public static class BiomeMath
    {
        public const long One = 1 << 16;

        public const long One30 = 1L << 30;

        const long Ln2 = 45426;

        public static long Clamp01(long v) => Math.Max(0, Math.Min(One, v));

        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        public static long FromDouble(double v) => (long)Math.Round(v * One, MidpointRounding.AwayFromZero);

        public static long Smoothstep(long t)
        {
            t = Clamp01(t);
            return t * t / One * (3 * One - 2 * t) / One;
        }

        public static long Noise(int seed, int salt, long xQ, long yQ, long lattice)
        {
            long span = lattice * One;
            long ix = FloorDiv(xQ, span), iy = FloorDiv(yQ, span);
            long tx = Smoothstep((xQ - ix * span) / lattice), ty = Smoothstep((yQ - iy * span) / lattice);
            int x0 = unchecked((int)ix), y0 = unchecked((int)iy);
            long a = Lattice(seed, salt, x0, y0), b = Lattice(seed, salt, x0 + 1, y0);
            long c = Lattice(seed, salt, x0, y0 + 1), d = Lattice(seed, salt, x0 + 1, y0 + 1);
            long top = a + (b - a) * tx / One;
            long bottom = c + (d - c) * tx / One;
            return top + (bottom - top) * ty / One;
        }

        public static long Noise(int seed, int salt, long xQ, long yQ, long lattice, long periodX, long periodY)
        {
            if (periodX <= 0 && periodY <= 0) return Noise(seed, salt, xQ, yQ, lattice);
            var (x0, x1, tx) = Axis(xQ, lattice, periodX);
            var (y0, y1, ty) = Axis(yQ, lattice, periodY);
            long a = Lattice(seed, salt, x0, y0), b = Lattice(seed, salt, x1, y0);
            long c = Lattice(seed, salt, x0, y1), d = Lattice(seed, salt, x1, y1);
            long top = a + (b - a) * tx / One;
            long bottom = c + (d - c) * tx / One;
            return top + (bottom - top) * ty / One;
        }

        static (int i0, int i1, long t) Axis(long q, long lattice, long period)
        {
            if (period <= 0)
            {
                long span = lattice * One;
                long i = FloorDiv(q, span);
                return (unchecked((int)i), unchecked((int)i + 1), Smoothstep((q - i * span) / lattice));
            }
            long n = Math.Max(1, (period + lattice / 2) / lattice);
            long u = FloorDiv(q * n, period), k = FloorDiv(u, One);
            return ((int)Mod(k, n), (int)Mod(k + 1, n), Smoothstep(u - k * One));
        }

        public static long Mod(long a, long n) => ((a % n) + n) % n;

        public static long Centred(int seed, int salt, long xQ, long yQ, long lattice) => 2 * Noise(seed, salt, xQ, yQ, lattice) - One;

        public static long Centred(int seed, int salt, long xQ, long yQ, long lattice, long periodX, long periodY) =>
            2 * Noise(seed, salt, xQ, yQ, lattice, periodX, periodY) - One;

        static long Lattice(int seed, int salt, int x, int y) => WorldHash.Hash4(seed, salt, x, y) >> 16;

        public static long ExpNeg(long xQ16)
        {
            if (xQ16 <= 0) return One30;
            long n = xQ16 / Ln2;
            if (n >= 31) return 0;
            long r = (xQ16 - n * Ln2) << 14;
            long term = One30, sum = One30;
            for (int k = 1; k <= 12; k++)
            {
                term = term * r / One30 / k;
                sum += (k & 1) == 1 ? -term : term;
            }
            return sum >> (int)n;
        }

        public static long Log2(ulong m)
        {
            int whole = 63;
            while ((m >> whole) == 0) whole--;
            ulong y = whole >= 31 ? m >> (whole - 31) : m << (31 - whole);
            long frac = 0;
            for (int bit = 31; bit >= 0; bit--)
            {
                y = y * y >> 31;
                if (y >= 1UL << 32)
                {
                    y >>= 1;
                    frac |= 1L << bit;
                }
            }
            return ((long)whole << 32) + frac;
        }

        public static long Sqrt(long v)
        {
            if (v <= 0) return 0;
            ulong n = (ulong)v, root = 0, bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= root + bit)
                {
                    n -= root + bit;
                    root = (root >> 1) + bit;
                }
                else root >>= 1;
                bit >>= 2;
            }
            return (long)root;
        }

        public static long Length(long dx, long dy) => Sqrt(dx * dx + dy * dy);

        public static uint NameHash(string id)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char ch in id)
                {
                    h ^= ch;
                    h *= 16777619u;
                }
                return h;
            }
        }
    }
}
