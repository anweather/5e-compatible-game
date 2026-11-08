// Fixed-point maths for the biome field (BiomeField, J-079): Q16 values (One = 1.0), integer value noise on WorldHash,
// e^-x, log2 and a square root, all in integers, so every machine gets the same bits. Punchy's BiomeSampling used float
// simplex noise and math.exp/log, which differ between the PC and the Mac; this is what replaces them.
using System;

namespace Chem
{
    public static class BiomeMath
    {
        /// <summary>1.0 in Q16.</summary>
        public const long One = 1 << 16;

        /// <summary>1.0 in Q30, for the small numbers a biome's fit can reach.</summary>
        public const long One30 = 1L << 30;

        /// <summary>ln 2 in Q16.</summary>
        const long Ln2 = 45426;

        public static long Clamp01(long v) => Math.Max(0, Math.Min(One, v));

        /// <summary>Floor division, for negative coordinates too.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        /// <summary>A Q16 number from a row's decimal: exact for the numbers the rows hold (power-of-two scaling, then a round).</summary>
        public static long FromDouble(double v) => (long)Math.Round(v * One, MidpointRounding.AwayFromZero);

        /// <summary>t² (3 − 2t) for t in Q16 [0, 1].</summary>
        public static long Smoothstep(long t)
        {
            t = Clamp01(t);
            return t * t / One * (3 * One - 2 * t) / One;
        }

        /// <summary>
        /// Hashed value noise in Q16 [0, 1] at a point given in Q16 cells, control points <paramref name="lattice"/> cells apart:
        /// WorldNoise.Value's scheme, but in integers.
        /// </summary>
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

        /// <summary>Noise in Q16 [−1, 1], where Punchy reads simplex.</summary>
        public static long Centred(int seed, int salt, long xQ, long yQ, long lattice) => 2 * Noise(seed, salt, xQ, yQ, lattice) - One;

        static long Lattice(int seed, int salt, int x, int y) => WorldHash.Hash4(seed, salt, x, y) >> 16;

        /// <summary>e^-x in Q30 for x in Q16 (x ≥ 0): 2^-n times a Taylor series for the remainder under ln 2.</summary>
        public static long ExpNeg(long xQ16)
        {
            if (xQ16 <= 0) return One30;
            long n = xQ16 / Ln2;
            if (n >= 31) return 0;
            long r = (xQ16 - n * Ln2) << 14; // Q30, under ln 2
            long term = One30, sum = One30;
            for (int k = 1; k <= 12; k++)
            {
                term = term * r / One30 / k;
                sum += (k & 1) == 1 ? -term : term;
            }
            return sum >> (int)n;
        }

        /// <summary>log2(m) in Q32 for m ≥ 1, by repeated squaring.</summary>
        public static long Log2(ulong m)
        {
            int whole = 63;
            while ((m >> whole) == 0) whole--;
            ulong y = whole >= 31 ? m >> (whole - 31) : m << (31 - whole); // [2^31, 2^32): 1.0 to 2.0 in Q31
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

        /// <summary>The integer square root (floor).</summary>
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

        /// <summary>A Q16 distance from Q16 offsets.</summary>
        public static long Length(long dx, long dy) => Sqrt(dx * dx + dy * dy);

        /// <summary>FNV-1a of a biome's id: its rolls come from its name, not its place in the file.</summary>
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
