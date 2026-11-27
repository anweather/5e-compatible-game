// The online desync check's fingerprint of the cells, a slice a tick: hashing the whole world at
// once took a frame of 120 ms, every 5 s. Each tick adds its slice of World.Cells to a running sum,
// and the sum closes at the end of a window of Every ticks, so a window sees every cell once.
//
// Not a port: the sandbox has no peers.
//
// Peers agree because a tick number and the world's size alone pick its slice: row-major, with no
// reading of the seed, the active chunks or the order anything was stepped in. A big world's window is
// longer, so a tick still hashes about two valleys' worth of cells (map-size: a 3 x 3's whole sum was
// 6 ms a tick under Mono at 30 ticks); the valley's stays Every.
//
// The online check no longer uses it (#168): LockstepSession compares the chunk digest (World.ChunkHash.cs), a hash cached
// per chunk and made again only for chunks whose cells changed. It stays for the tests and tools that hash a whole world
// in slices. A window a peer saw only part of (it joined midway) gives no fingerprint, rather than one that can't match.
using System;
using System.Globalization;

namespace Chem
{
    public sealed class RollingHash
    {
        public const int Every = 30;

        public const int CellsPerWindow = 2 * 576 * 576;

        public static int WindowFor(int cells) => Math.Max(Every, (int)((Every * (long)cells + CellsPerWindow - 1) / CellsPerWindow));

        public static int WindowFor(World world) => world.IsOpen ? Every : WindowFor(world.W * world.H);

        Fnv1aSum sum = Fnv1aSum.Start();
        int seen;

        public string Add(World world, int tick)
        {
            int window = WindowFor(world), slice = SliceOf(tick, window);
            if (slice == 0)
            {
                sum = Fnv1aSum.Start();
                seen = 0;
            }
            world.HashSlice(ref sum, slice, window);
            seen++;
            return slice == window - 1 && seen == window ? world.CloseHash(sum) : null;
        }

        public static int SliceOf(int tick) => SliceOf(tick, Every);

        public static int SliceOf(int tick, int window) => ((tick - 1) % window + window) % window;
    }

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

        public uint Value => hash;

        public override string ToString() => hash.ToString("x", CultureInfo.InvariantCulture);
    }
}
