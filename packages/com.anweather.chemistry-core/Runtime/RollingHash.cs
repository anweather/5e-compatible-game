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
// After (chunk-hash, 2026-11-08): a hash cached per chunk and recomputed only for chunks that changed would cost almost
// nothing a tick and keep a big world's window at 5 s. It needs every cell write to mark its chunk, and cells are written
// in place all over the core, so it waits for D9's accessors (cells behind World.At and arrays) rather than a partial
// dirty flag that could miss a change (a missed desync) or invent one. A window a peer saw
// only part of (it joined midway) gives no fingerprint, rather than one that can't match.
using System;
using System.Globalization;

namespace Chem
{
    public sealed class RollingHash
    {
        /// <summary>Ticks to a window, and how many slices the cells are cut into, at the least: the valley's.</summary>
        public const int Every = 30;

        /// <summary>The cells a window of Every ticks covers at most: two valleys (576 x 576 each), about 1.5 ms a tick.</summary>
        public const int CellsPerWindow = 2 * 576 * 576;

        /// <summary>A world's window in ticks: Every, or longer for a world of more than CellsPerWindow cells, by its size alone.</summary>
        public static int WindowFor(int cells) => Math.Max(Every, (int)((Every * (long)cells + CellsPerWindow - 1) / CellsPerWindow));

        /// <summary>An open world's is Every: its size grows as it's filled, and a window must be the same length all through.</summary>
        public static int WindowFor(World world) => world.IsOpen ? Every : WindowFor(world.W * world.H);

        Fnv1aSum sum = Fnv1aSum.Start();
        int seen;

        /// <summary>
        /// Adds this finished tick's slice. At the end of a window, returns the cells' fingerprint
        /// and starts the next; null otherwise.
        /// </summary>
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

        /// <summary>The slice a tick hashes in the valley's window.</summary>
        public static int SliceOf(int tick) => SliceOf(tick, Every);

        /// <summary>The slice a tick hashes: a window runs from a tick after a multiple of its length up to the next multiple.</summary>
        public static int SliceOf(int tick, int window) => ((tick - 1) % window + window) % window;
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
