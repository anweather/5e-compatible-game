// The online desync check's fingerprint of the cells, a slice a tick: hashing the whole world at
// once took a frame of 120 ms, every 5 s. Each tick adds its slice of World.Cells to a running sum,
// and the sum closes at the end of a window of Every ticks, so a window sees every cell once.
//
// Not a port: the sandbox has no peers.
//
// Peers agree because a tick number alone picks its slice: row-major from the world's size, with no
// reading of the seed, the active chunks or the order anything was stepped in. A window a peer saw
// only part of (it joined midway) gives no fingerprint, rather than one that can't match.
using System.Globalization;

namespace Chem
{
    public sealed class RollingHash
    {
        /// <summary>Ticks to a window, and how many slices the cells are cut into.</summary>
        public const int Every = 30;

        Fnv1aSum sum = Fnv1aSum.Start();
        int seen;

        /// <summary>
        /// Adds this finished tick's slice. At the end of a window, returns the cells' fingerprint
        /// and starts the next; null otherwise.
        /// </summary>
        public string Add(World world, int tick)
        {
            int slice = SliceOf(tick);
            if (slice == 0)
            {
                sum = Fnv1aSum.Start();
                seen = 0;
            }
            world.HashSlice(ref sum, slice, Every);
            seen++;
            return slice == Every - 1 && seen == Every ? world.CloseHash(sum) : null;
        }

        /// <summary>The slice a tick hashes: a window runs from a tick after a multiple of Every up to the next multiple.</summary>
        public static int SliceOf(int tick) => ((tick - 1) % Every + Every) % Every;
    }

    /// <summary>32-bit FNV-1a a character at a time: Fnv1a.Hash of what was written, with no string to build.</summary>
    public struct Fnv1aSum
    {
        uint hash;

        public static Fnv1aSum Start() => new Fnv1aSum { hash = 0x811c9dc5 };

        public void Add(char c)
        {
            unchecked { hash = (hash ^ c) * 0x01000193u; }
        }

        public void Add(string s)
        {
            if (s == null) return;
            for (int i = 0; i < s.Length; i++) Add(s[i]);
        }

        /// <summary>Writes a number as its decimal digits.</summary>
        public void Add(long n)
        {
            if (n < 0)
            {
                Add('-');
                AddDigits(unchecked((ulong)(-n)));
            }
            else AddDigits((ulong)n);
        }

        public void AddDigits(ulong n)
        {
            if (n >= 10) AddDigits(n / 10);
            Add((char)('0' + (int)(n % 10)));
        }

        public override string ToString() => hash.ToString("x", CultureInfo.InvariantCulture);
    }
}
