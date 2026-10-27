// The playtest bot's dice (playtest/DESIGN.md): every choice the bot makes
// comes from one of these, seeded from the run's seed, so the same seed on
// the same build makes the same run. Its own generator (xorshift64*), not
// System.Random or UnityEngine.Random, so nothing else can move it on.
namespace Playtest
{
    public sealed class PlaytestRandom
    {
        ulong state;

        public PlaytestRandom(ulong seed) => state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>A number in [0, 1).</summary>
        public double Value() => (Next() >> 11) * (1.0 / (1UL << 53));

        /// <summary>An integer in [min, max).</summary>
        public int Range(int min, int max) => max <= min ? min : min + (int)(Next() % (ulong)(max - min));

        /// <summary>A number in [min, max).</summary>
        public float Range(float min, float max) => min + (float)Value() * (max - min);

        /// <summary>True with this chance.</summary>
        public bool Chance(double p) => Value() < p;
    }
}
