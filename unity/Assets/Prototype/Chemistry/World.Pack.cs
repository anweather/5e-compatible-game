// Packing one chunk: a prototype for the sparse World (docs/design/sparse-world.md, "Measured"; D17, proposed). An
// inactive chunk is packed to its changes against its source and gives its Cell objects back to a pool; it is read cell
// by cell from the two for the hash and the save, and unpacked into pooled objects again. Not wired in and behind no
// switch: nothing in the game packs a chunk. It is here so the benches (SparseWorldBench) and the tests (PackTests)
// measure the real thing; step 2 builds on it or replaces it.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The ids a packed cell holds for its strings (terrain, cover, gas): each string once, numbered as first met.</summary>
    public sealed class PackStrings
    {
        readonly Dictionary<string, ushort> ids = new Dictionary<string, ushort>(StringComparer.Ordinal) { [""] = 0 };
        readonly List<string> strings = new List<string> { null };

        /// <summary>A string's id; 0 for null.</summary>
        internal ushort Id(string s)
        {
            if (s == null) return 0;
            if (ids.TryGetValue(s, out ushort id)) return id;
            if (strings.Count > ushort.MaxValue) throw new InvalidOperationException("More than 65535 strings in packed cells");
            id = (ushort)strings.Count;
            ids.Add(s, id);
            strings.Add(s);
            return id;
        }

        /// <summary>The string an id stands for.</summary>
        internal string Of(ushort id) => strings[id];
    }

    /// <summary>
    /// A changed cell's state in a packed chunk (20 bytes): what most changed cells hold, a cover (ash, snow) and the
    /// tick it's damp until (a flood, rain) among them, with the rest in a PackedRare when it has any.
    /// </summary>
    internal struct PackedCell
    {
        public ushort T, Cover;
        public byte Liquid;
        public bool Frozen;
        public short Water, Fire, Height;
        public int DampUntil;
        /// <summary>Its PackedRare's index, or -1: none, and every field above holds its whole value.</summary>
        public int Rare;
    }

    /// <summary>The fields of a changed cell few cells hold (the rest of Cell's own Rare), and its wide ones when they don't fit a short (60 bytes).</summary>
    internal struct PackedRare
    {
        public ushort Gas;
        public int Water, Fire, Height, Sets, Fuel, GasAmt, Charge, Life, Rust, Heat, Lit, Slick, Star, Cracked;
    }

    /// <summary>
    /// One chunk's cells as their changes against its source (sparse-world.md, "A chunk, unpacked and packed"): the local
    /// indices of the cells that differ, ascending, and each one's state; its fire generations that aren't 0. Its header
    /// (Active, LastLive, Fit...) stays on the Chunk.
    /// </summary>
    public sealed class PackedChunk
    {
        public int X { get; internal set; }
        public int Y { get; internal set; }
        internal int[] At;
        internal PackedCell[] Cells;
        internal PackedRare[] Rares;
        internal int[] FireAt, FireGen;

        /// <summary>The cells that differ from the source.</summary>
        public int Count => At.Length;
        /// <summary>Of those, the ones with a field few cells hold (cover, gas, a timer...) or a value too wide for a short.</summary>
        public int RareCount => Rares.Length;
        /// <summary>Its fire generations that aren't 0.</summary>
        public int FireCount => FireAt.Length;

        /// <summary>What it holds in memory, arrays' headers included (64-bit .NET: 24 bytes an array, 16 an object).</summary>
        public long Bytes => 16 + 4 * 8 + Arr(At.Length * 4L) + Arr(Cells.Length * 20L) + Arr(Rares.Length * 60L) + Arr(FireAt.Length * 4L) + Arr(FireGen.Length * 4L);

        static long Arr(long payload) => 24 + payload;

        /// <summary>The changed cell at local index i, or -1 (a binary search: the hash reads any cell of a packed chunk).</summary>
        internal int Find(int i) => Array.BinarySearch(At, i) is int k && k >= 0 ? k : -1;
    }

    public sealed partial class World
    {
        // Cell[] blocks of Chunks.Area that packed chunks gave back, for the next unpack (the design's block pool).
        readonly Stack<Cell[]> packPool = new Stack<Cell[]>();
        // What the packed chunks are against and their strings' ids, from the last Pack: one of each a world.
        IChunkSource packSource;
        PackStrings packStrings;

        /// <summary>Blocks the pool holds now.</summary>
        public int PackPoolCount => packPool.Count;

        /// <summary>Lets the pool's blocks go, but for <paramref name="keep"/> of them (the design's pool holds the most ever in use).</summary>
        public void TrimPackPool(int keep)
        {
            while (packPool.Count > keep) packPool.Pop();
        }

        /// <summary>Packed chunks a read unpacked (a tick's or anyone's, through Chunk.CellAt), since the world was made.</summary>
        public int ReadUnpacks { get; private set; }

        /// <summary>Whether a chunk is packed now.</summary>
        public bool IsPacked(int cx, int cy) => ChunkOrThrow(cx, cy).Packed != null;

        /// <summary>
        /// Packs an inactive chunk: its changes against the source kept, its Cell objects given back to the pool and its slots
        /// left empty. Between ticks only. Any read of its cells through Chunk.CellAt (World.At, a tick's neighbour) unpacks it.
        /// </summary>
        public PackedChunk Pack(int cx, int cy, IChunkSource source, PackStrings strings)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Active) throw new InvalidOperationException($"Chunk ({cx}, {cy}) is active: only an inactive chunk packs");
            if (ch.Packed != null) throw new InvalidOperationException($"Chunk ({cx}, {cy}) is packed already");
            if (packSource != null && (packSource != source || packStrings != strings)) throw new InvalidOperationException("One source and one string table a world");
            packSource = source;
            packStrings = strings;
            var p = Changes(ch, source, strings);
            var block = new Cell[Chunks.Area];
            for (int i = 0; i < Chunks.Area; i++)
            {
                block[i] = ch.HeldAt(i);
                ch.Put(i, null);
                // World.Cells (the view's, until step 1b) lets them go too; Unpack puts the new ones back.
                int x = CellX(ch, i), y = CellY(ch, i);
                if (InBounds(x, y)) Cells[y * W + x] = null;
            }
            packPool.Push(block);
            Array.Clear(ch.FireGeneration, 0, Chunks.Area);
            Array.Clear(ch.Marks, 0, Chunks.Area);
            ch.SnapValid = false;
            ch.Packed = p;
            return p;
        }

        /// <summary>Unpacks a packed chunk between ticks: a block from the pool (or a new one), each cell made by the source, the changes laid over.</summary>
        public void Unpack(int cx, int cy)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Packed != null) UnpackChunk(ch);
        }

        // A read of a packed chunk's cell (Chunk.CellAt), mid-tick or not: the chunk unpacks, and the read gets its cell.
        internal Cell UnpackOnRead(Chunk ch, int i)
        {
            ReadUnpacks++;
            UnpackChunk(ch);
            return ch.HeldAt(i);
        }

        void UnpackChunk(Chunk ch)
        {
            var p = ch.Packed;
            ch.Packed = null;
            var block = packPool.Count > 0 ? packPool.Pop() : new Cell[Chunks.Area];
            int k = 0;
            for (int i = 0; i < Chunks.Area; i++)
            {
                int x = CellX(ch, i), y = CellY(ch, i);
                if (!InBounds(x, y)) continue;
                var c = block[i] ?? new Cell();
                c.Reset();
                packSource.Make(x, y, c);
                if (k < p.At.Length && p.At[k] == i) LayPacked(p, k++, packStrings, c);
                c.ForgetDef();
                ch.Put(i, c);
                Cells[y * W + x] = c;
            }
            for (int f = 0; f < p.FireAt.Length; f++) ch.FireGeneration[p.FireAt[f]] = p.FireGen[f];
        }

        /// <summary>SaveChunk's record of a chunk as it stands (unpacked), for comparing with SavePacked.</summary>
        public JObject SaveChunkOf(int cx, int cy, IChunkSource source) => SaveChunk(ChunkOrThrow(cx, cy), source, new Cell());

        /// <summary>A chunk's changes against the source and its fire generations, leaving the chunk as it is (Pack's diff).</summary>
        public PackedChunk ChangesOf(int cx, int cy, IChunkSource source, PackStrings strings) => Changes(ChunkOrThrow(cx, cy), source, strings);

        static PackedChunk Changes(Chunk ch, IChunkSource source, PackStrings strings)
        {
            var made = new Cell();
            var at = new List<int>();
            var cells = new List<PackedCell>();
            var rares = new List<PackedRare>();
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = ch.CellAt(i);
                if (c == null) continue;
                made.Reset();
                source.Make(CellX(ch, i), CellY(ch, i), made);
                if (SameCell(c, made) && c.Liquid == made.Liquid && c.Sets == made.Sets) continue;
                at.Add(i);
                cells.Add(PackCell(c, made, strings, rares));
            }
            var fireAt = new List<int>();
            var fireGen = new List<int>();
            for (int i = 0; i < Chunks.Area; i++)
            {
                if (ch.FireGeneration[i] == 0) continue;
                fireAt.Add(i);
                fireGen.Add(ch.FireGeneration[i]);
            }
            return new PackedChunk
            {
                X = ch.X,
                Y = ch.Y,
                At = at.ToArray(),
                Cells = cells.ToArray(),
                Rares = rares.ToArray(),
                FireAt = fireAt.ToArray(),
                FireGen = fireGen.ToArray(),
            };
        }

        // A changed cell, against the cell the source makes there: with a PackedRare if either has a field few cells hold,
        // so laying it over the source's cell clears what the source had.
        static PackedCell PackCell(Cell c, Cell made, PackStrings strings, List<PackedRare> rares)
        {
            var p = new PackedCell
            {
                T = strings.Id(c.T),
                Cover = strings.Id(c.Cover),
                Liquid = checked((byte)c.Liquid),
                Frozen = c.Frozen,
                Water = (short)c.Water,
                Fire = (short)c.Fire,
                Height = (short)c.Height,
                DampUntil = c.DampUntil,
                Rare = -1,
            };
            bool narrow = p.Water == c.Water && p.Fire == c.Fire && p.Height == c.Height;
            if (narrow && !HasRare(c) && !HasRare(made)) return p;
            p.Rare = rares.Count;
            rares.Add(new PackedRare
            {
                Gas = strings.Id(c.Gas),
                Water = c.Water,
                Fire = c.Fire,
                Height = c.Height,
                Sets = c.Sets,
                Fuel = c.Fuel,
                GasAmt = c.GasAmt,
                Charge = c.Charge,
                Life = c.Life,
                Rust = c.Rust,
                Heat = c.Heat,
                Lit = c.Lit,
                Slick = c.Slick,
                Star = c.Star,
                Cracked = c.Cracked,
            });
            return p;
        }

        static bool HasRare(Cell c) =>
            c.Gas != null || c.Sets != 0 || c.Fuel != 0 || c.GasAmt != 0 || c.Charge != 0 || c.Life != 0 || c.Rust != 0 || c.Heat != 0 || c.Lit != 0 || c.Slick != 0 || c.Star != 0 || c.Cracked != 0;

        // Lays a changed cell's state over a cell the source made.
        static void LayPacked(PackedChunk p, int k, PackStrings strings, Cell c)
        {
            var pc = p.Cells[k];
            c.T = strings.Of(pc.T);
            c.Liquid = pc.Liquid;
            c.Frozen = pc.Frozen;
            c.Cover = strings.Of(pc.Cover);
            c.DampUntil = pc.DampUntil;
            if (pc.Rare < 0)
            {
                c.Water = pc.Water;
                c.Fire = pc.Fire;
                c.Height = pc.Height;
                return;
            }
            var r = p.Rares[pc.Rare];
            c.Water = r.Water;
            c.Fire = r.Fire;
            c.Height = r.Height;
            c.Gas = strings.Of(r.Gas);
            c.Sets = r.Sets;
            c.Fuel = r.Fuel;
            c.GasAmt = r.GasAmt;
            c.Charge = r.Charge;
            c.Life = r.Life;
            c.Rust = r.Rust;
            c.Heat = r.Heat;
            c.Lit = r.Lit;
            c.Slick = r.Slick;
            c.Star = r.Star;
            c.Cracked = r.Cracked;
        }

        /// <summary>A packed chunk's cell at local index i, into <paramref name="into"/>: the source's, with its change laid over.</summary>
        static void ReadPacked(PackedChunk p, int i, IChunkSource source, PackStrings strings, Cell into)
        {
            into.Reset();
            source.Make((p.X << Chunks.Shift) + (i & Chunks.Mask), (p.Y << Chunks.Shift) + (i >> Chunks.Shift), into);
            int k = p.Find(i);
            if (k >= 0) LayPacked(p, k, strings, into);
        }

        /// <summary>ChunkHash of a packed chunk, read from its source and changes: the same text, without unpacking it.</summary>
        public string PackedChunkHash(PackedChunk p, IChunkSource source, PackStrings strings)
        {
            var sum = Fnv1aSum.Start();
            var ch = ChunkOrThrow(p.X, p.Y);
            var c = new Cell();
            int k = 0;
            for (int i = 0; i < Chunks.Area; i++)
            {
                int x = CellX(ch, i), y = CellY(ch, i);
                if (!InBounds(x, y)) continue;
                c.Reset();
                source.Make(x, y, c);
                if (k < p.At.Length && p.At[k] == i) LayPacked(p, k++, strings, c);
                AddCell(ref sum, c, LiquidsOf(ch));
            }
            return sum.ToString();
        }

        /// <summary>
        /// CellHash of a world some of whose chunks are packed: the cells in row order as CellHash walks them, a packed
        /// chunk's read from its source and changes into one scratch cell, never unpacked. The same bits as CellHash.
        /// </summary>
        public string PackedCellHash()
        {
            var sum = Fnv1aSum.Start();
            var scratch = new Cell();
            for (int y = 0; y < H; y++)
            {
                Chunk ch = null;
                for (int x = 0; x < W; x++)
                {
                    if (ch == null || Chunks.LocalOf(x) == 0) ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
                    int i = LocalIndex(x, y);
                    var c = ch.HeldAt(i);
                    if (c == null)
                    {
                        ReadPacked(ch.Packed, i, packSource, packStrings, scratch);
                        c = scratch;
                    }
                    AddCell(ref sum, c, LiquidsIn(ch, c));
                }
            }
            return CloseHash(sum);
        }

        /// <summary>SaveChunk of a packed chunk: its record made from its changes, without the diff against the source.</summary>
        public JObject SavePacked(PackedChunk p, IChunkSource source, PackStrings strings)
        {
            var ch = ChunkOrThrow(p.X, p.Y);
            var o = SaveChunkHeader(ch);
            if (p.FireAt.Length > 0)
            {
                var fire = new JArray();
                for (int f = 0; f < p.FireAt.Length; f++) fire.Add(new JArray(p.FireAt[f], p.FireGen[f]));
                o["fireGen"] = fire;
            }
            if (p.Count == 0) return o;
            var cells = new JArray();
            Cell c = new Cell(), made = new Cell();
            for (int k = 0; k < p.Count; k++)
            {
                int i = p.At[k], x = CellX(ch, i), y = CellY(ch, i);
                made.Reset();
                source.Make(x, y, made);
                c.Reset();
                source.Make(x, y, c);
                LayPacked(p, k, strings, c);
                // A cell kept only for its Liquid or Sets on dry ground (SameCell doesn't see them) is no change to a save.
                if (!SameCell(c, made)) cells.Add(SaveCell(i, c, LiquidsOf(ch)));
            }
            if (cells.Count > 0) o["cells"] = cells;
            return o;
        }
    }
}
