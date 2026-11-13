// Hashed value noise over global coordinates for the world generator (D11):
// each lattice value is a hash of its point, so the field is defined over the
// whole plane and any window of it is the same whoever samples it. Port of
// PocketTactics' src/core/world/noise.ts, bit for bit (WorldGenTests), using
// only exactly rounded arithmetic.
using System;

namespace Chem
{
    public static class WorldNoise
    {
        static double Smoothstep(double t) => t * t * (3 - 2 * t);

        /// <summary>One octave, in [0, 1]. <paramref name="lattice"/> is the control-point spacing in the caller's units.</summary>
        public static double Value(int seed, int salt, double x, double y, double lattice)
        {
            double fx = x / lattice, fy = y / lattice;
            double x0 = Math.Floor(fx), y0 = Math.Floor(fy);
            double tx = Smoothstep(fx - x0), ty = Smoothstep(fy - y0);
            int ix = (int)x0, iy = (int)y0;
            double a = WorldHash.HashUnit(seed, salt, ix, iy);
            double b = WorldHash.HashUnit(seed, salt, ix + 1, iy);
            double c = WorldHash.HashUnit(seed, salt, ix, iy + 1);
            double d = WorldHash.HashUnit(seed, salt, ix + 1, iy + 1);
            double top = a + (b - a) * tx;
            double bottom = c + (d - c) * tx;
            return top + (bottom - top) * ty;
        }

        /// <summary><paramref name="octaves"/> octaves, each at half the spacing and half the amplitude of the last. In [0, 1].</summary>
        public static double Fractal(int seed, int salt, double x, double y, double lattice, int octaves)
        {
            double sum = 0, norm = 0, amp = 1, lat = lattice;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(seed, unchecked(salt + o * 0x01000193), x, y, lat);
                norm += amp;
                amp *= 0.5;
                lat *= 0.5;
            }
            return sum / norm;
        }

        // The measured distribution of Fractal per octave count: value noise piles up round 0.5, so
        // "cover a share of the land" can't be a raw threshold. Measured once from a fixed seed, so it
        // is part of the generator's code, not its input.
        const int CdfSamples = 160, MaxOctaves = 8;
        static readonly Lazy<double[]>[] Cdfs = MakeCdfs();

        static Lazy<double[]>[] MakeCdfs()
        {
            var cdfs = new Lazy<double[]>[MaxOctaves + 1];
            for (int o = 0; o <= MaxOctaves; o++)
            {
                int octaves = o;
                cdfs[o] = new Lazy<double[]>(() => MeasureCdf(octaves));
            }
            return cdfs;
        }

        static double[] MeasureCdf(int octaves)
        {
            var cdf = new double[CdfSamples * CdfSamples];
            int k = 0;
            for (int j = 0; j < CdfSamples; j++)
                for (int i = 0; i < CdfSamples; i++)
                    cdf[k++] = Fractal(0x5eed1234, 0x0cdf, i * 0.371 + 0.13, j * 0.413 + 0.07, 4, octaves);
            Array.Sort(cdf);
            return cdf;
        }

        static double[] CdfFor(int octaves) => Cdfs[Math.Max(1, Math.Min(MaxOctaves, octaves))].Value;

        /// <summary>The value v such that a share <paramref name="q"/> of Fractal(octaves) samples fall below it.</summary>
        public static double Quantile(int octaves, double q)
        {
            var cdf = CdfFor(octaves);
            int i = Math.Max(0, Math.Min(cdf.Length - 1, (int)Math.Floor(q * cdf.Length)));
            return cdf[i];
        }

        /// <summary>A Fractal(octaves) sample's rank in [0, 1]: a roughly uniform value.</summary>
        public static double ToUniform(int octaves, double v)
        {
            var cdf = CdfFor(octaves);
            int lo = 0, hi = cdf.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cdf[mid] < v) lo = mid + 1;
                else hi = mid;
            }
            return lo / (double)cdf.Length;
        }
    }
}
