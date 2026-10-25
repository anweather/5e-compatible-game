// Smooth deterministic noise for the view's maths (CellPaint's soft edges,
// CellContour's wobbly outlines): a hash of integer lattice points, eased
// between them. Pure maths, no Unity, no state: the same point and seed give
// the same value on every machine. Visual only; the simulation never reads it.
//
// Not a port: the sandbox has no terrain.
using System;

namespace Chem
{
    public static class ValueNoise
    {
        /// <summary>A value in [0, 1) for an integer lattice point.</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Smooth noise in [0, 1] at a point, one lattice cell per unit.</summary>
        public static float At(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = Ease(x - ix), fy = Ease(y - iy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            float bottom = a + (b - a) * fx, top = c + (d - c) * fx;
            return bottom + (top - bottom) * fy;
        }

        /// <summary>Two octaves: broad shapes with a finer wobble on top, still in [0, 1].</summary>
        public static float Twice(float x, float y, int seed) =>
            0.7f * At(x, y, seed) + 0.3f * At(x * 2.7f + 17.3f, y * 2.7f + 5.1f, seed + 101);

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Hermite step: 0 at or below <paramref name="lo"/>, 1 at or above <paramref name="hi"/>.</summary>
        public static float SmoothStep(float lo, float hi, float x)
        {
            float t = (x - lo) / (hi - lo);
            return Ease(Math.Max(0f, Math.Min(1f, t)));
        }
    }
}
