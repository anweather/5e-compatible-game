// Coordinate hashing for the world generator (D11): every value is a pure
// function of the world's seed and global coordinates, never a draw from a
// stream, so a chunk comes out the same whichever chunk asks, in whatever
// order. Port of PocketTactics' src/core/world/hash.ts (Andrew's own game),
// bit for bit: WorldGenTests pins it against values from the TypeScript.
namespace Chem
{
    public static class WorldHash
    {
        /// <summary>murmur3 fmix32: full avalanche, so neighbouring inputs land far apart.</summary>
        public static int Fmix32(int value)
        {
            unchecked
            {
                uint h = (uint)value;
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return (int)h;
            }
        }

        /// <summary>
        /// 32-bit hash of (seed, salt, a, b). Each input is folded in and avalanched separately,
        /// so (a, b) and (b, a) differ and negative coordinates are fine.
        /// </summary>
        public static uint Hash4(int seed, int salt, int a, int b)
        {
            unchecked
            {
                int h = Fmix32(seed ^ (int)((uint)salt * 0x9e3779b1u));
                h = Fmix32(h ^ (int)((uint)a * 0xcc9e2d51u));
                h = Fmix32(h ^ (int)((uint)b * 0x1b873593u));
                return (uint)h;
            }
        }

        /// <summary>Hash4 mapped to [0, 1).</summary>
        public static double HashUnit(int seed, int salt, int a, int b) => Hash4(seed, salt, a, b) / 4294967296.0;
    }
}
