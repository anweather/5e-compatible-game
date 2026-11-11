// The sparse World (D17, docs/design/sparse-world.md): an inactive chunk may be packed, held as its changes against the
// world's source (the cells that differ from what the source makes there) with no Cell objects; an unpacked chunk holds
// its cells in a block from a pool. Every world has a source: the fill it was made with, or what MakeFrom made it from (a
// BakedSource for a baked map), and its chunks start packed with no changes, so a world never builds the Cell objects of
// chunks nothing reads.
//
// How a chunk is held never shows. Every reach for a cell goes through its chunk, and a read of a packed chunk's cell
// (Chunk.CellAt, Block) unpacks it first; the hash, the save, the dragon's audit and the starting state read a packed
// chunk where it lies (RowCells' read mode, ChunkReader) without unpacking it. Packing and unpacking change memory and
// time only, never shared state, so peers needn't agree on them; to make that easy to check, the core still decides
// packing between whole ticks from tick-driven facts (PackDue), and only the view's drawn hint (SetDrawn) differs.
//
// A chunk packs at the end of a tick once, for sim.json's streaming.packTicks ticks, it has been inactive, not beside an
// active chunk (the tick reads one chunk out: SoakIn, NearHeat, a spell's line) and not in the drawn hint; at most
// streaming.packsPerTick a tick, the longest unneeded first, then by row and column. A chunk that activates unpacks
// with the eight round it (SetActive), and before each tick so does any active chunk still packed (a world just made,
// MakeFrom, a load: BeginTick), so the tick never reads a packed chunk.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// One chunk's cells as their changes against its world's source (sparse-world.md, "A chunk, unpacked and packed"):
    /// the local indices of the cells that differ, ascending, and each one's state; its fire generations that aren't 0.
    /// Its header (Active, LastLive, Fit...) stays on the Chunk.
    /// </summary>
    public sealed class PackedChunk
    {
        public int X { get; internal set; }
        public int Y { get; internal set; }
        internal int[] At;
        internal PackedCell[] Cells;
        internal PackedRare[] Rares;
        internal int[] FireAt, FireGen;

        /// <summary>A chunk no different from its source, with no fire: how every chunk of a new world starts.</summary>
        internal static PackedChunk Empty(int x, int y) => new PackedChunk
        {
            X = x,
            Y = y,
            At = Array.Empty<int>(),
            Cells = Array.Empty<PackedCell>(),
            Rares = Array.Empty<PackedRare>(),
            FireAt = Array.Empty<int>(),
            FireGen = Array.Empty<int>(),
        };

        /// <summary>The cells that differ from the source.</summary>
        public int Count => At.Length;
        /// <summary>Of those, the ones with a field few cells hold (cover, gas, a timer...) or a value too wide for a short.</summary>
        public int RareCount => Rares.Length;
        /// <summary>Its fire generations that aren't 0.</summary>
        public int FireCount => FireAt.Length;

        /// <summary>What it holds in memory, arrays' headers included (64-bit .NET: 24 bytes an array, 16 an object).</summary>
        public long Bytes => 16 + 4 * 8 + Arr(At.Length * 4L) + Arr(Cells.Length * 20L) + Arr(Rares.Length * 60L) + Arr(FireAt.Length * 4L) + Arr(FireGen.Length * 4L);

        static long Arr(long payload) => 24 + payload;

        /// <summary>The first changed cell at local index i or after it (a binary search: a walk may start anywhere in a chunk).</summary>
        internal int From(int i) => Array.BinarySearch(At, i) is int k && k >= 0 ? k : ~k;
    }

    /// <summary>Every cell one terrain, as a world is made (new World's fill): its source until MakeFrom gives it another.</summary>
    public sealed class FillSource : IChunkSource
    {
        readonly string fill;

        public FillSource(string fill) => this.fill = fill ?? throw new ArgumentNullException(nameof(fill));

        public string Version => "fill:" + fill;

        public void Make(int x, int y, Cell cell) => cell.T = fill;
    }

    public sealed partial class World
    {
        // Blocks packed chunks gave back, for the next unpack (the design's block pool), at most PoolKeep of them.
        readonly Stack<ChunkBlock> blockPool = new Stack<ChunkBlock>();
        // The chunks that hold a block now, for PackDue.
        readonly List<Chunk> unpackedChunks = new List<Chunk>();
        readonly PackStrings packStrings = new PackStrings();
        // The view's drawn hint (SetDrawn): chunks it draws, kept unpacked.
        readonly HashSet<long> drawnHint = new HashSet<long>();

        /// <summary>Blocks the pool keeps for the next unpacks: the design's packing slack (~32 chunks behind a sprinter).</summary>
        public const int PoolKeep = 32;

        /// <summary>Where the world's cells come from before play: what a packed chunk's changes are against.</summary>
        public IChunkSource Source { get; private set; }

        /// <summary>Blocks the pool holds now.</summary>
        public int PackPoolCount => blockPool.Count;

        /// <summary>Chunks unpacked now: each holds a block of Cell objects (~356 KB).</summary>
        public int UnpackedCount => unpackedChunks.Count;

        /// <summary>Chunks packed since the world was made (by PackDue, Pack, MakeFrom or a load).</summary>
        public int Packs { get; private set; }

        /// <summary>Chunks unpacked since the world was made, for a read, a write or a tick.</summary>
        public int Unpacks { get; private set; }

        /// <summary>The time spent packing and unpacking since the world was made, in milliseconds: for a performance pass, never shared state.</summary>
        public double PackMs => packTime * 1000.0 / Stopwatch.Frequency;
        public double UnpackMs => unpackTime * 1000.0 / Stopwatch.Frequency;

        long packTime, unpackTime;

        // The thread that made the world: the only one that may pack or unpack its chunks (OwnThread).
        readonly int ownerThread;

        /// <summary>
        /// Whether the calling thread is the one that made the world. Only it may pack or unpack a chunk: the pool and the
        /// unpacked list aren't thread-safe, so a view worker reading a packed chunk's cells would corrupt them silently.
        /// A worker reads only unpacked chunks (IsPacked), or has the owner unpack them first.
        /// </summary>
        public bool OnOwnThread => Environment.CurrentManagedThreadId == ownerThread;

        // Packing and unpacking only on the owner's thread: anything else throws rather than corrupt the world.
        void OwnThread(Chunk ch, string what)
        {
            if (Environment.CurrentManagedThreadId == ownerThread) return;
            throw new InvalidOperationException(
                $"Chunk ({ch.X}, {ch.Y}) {what} on thread {Environment.CurrentManagedThreadId}, not the world's own ({ownerThread}): "
                + "only the thread that made the world packs or unpacks its chunks. A worker that reads cells must read unpacked "
                + "chunks only (World.IsPacked), or have the main thread unpack them first (World.Unpack).");
        }

        /// <summary>
        /// Inactive chunks a read unpacked (anyone's, through Chunk.CellAt or a per-cell array) that weren't in the view's
        /// drawn hint, since the world was made: a safety net that should stay 0 in play, since the chunks round an active
        /// one unpack with it and never pack, and the view reads the chunks it hints. The view's reads of its hinted
        /// chunks, and an active chunk read before its first tick, aren't counted.
        /// </summary>
        public int ReadUnpacks { get; private set; }

        /// <summary>
        /// What the core's per-cell state beside the cells holds outside the blocks (the sparse World, step 3; before it,
        /// arrays the size of the map): the packed chunks' land (SetLand), the starting state's changes (KeepStart) where
        /// they aren't the chunk's own packed changes, the land the stamps and the map moved, and the bare cells.
        /// </summary>
        public long MapArrayBytes
        {
            get
            {
                long bytes = (landMoved?.Bytes(4) ?? 0) + (landLaid?.Bytes(4) ?? 0) + (landCrag?.Bytes(4) ?? 0) + (bareCells?.Bytes(1) ?? 0);
                foreach (var ch in chunks.Values)
                {
                    if (ch.PackedLand != null) bytes += 24 + ch.PackedLand.Length;
                    if (ch.Start != null && ch.Start != ch.Packed) bytes += ch.Start.Bytes;
                }
                return bytes;
            }
        }

        /// <summary>Of MapArrayBytes, what the packed chunks' land holds (LandCodec's bytes).</summary>
        public long PackedLandBytes
        {
            get
            {
                long bytes = 0;
                foreach (var ch in chunks.Values) if (ch.PackedLand != null) bytes += 24 + ch.PackedLand.Length;
                return bytes;
            }
        }

        /// <summary>Lets the pool's blocks go, but for <paramref name="keep"/> of them.</summary>
        public void TrimPackPool(int keep)
        {
            while (blockPool.Count > Math.Max(0, keep)) blockPool.Pop();
        }

        /// <summary>Whether a chunk is packed now.</summary>
        public bool IsPacked(int cx, int cy) => ChunkOrThrow(cx, cy).Packed != null;

        /// <summary>A packed chunk's changes now, or null when it's unpacked: for tests and benches.</summary>
        public PackedChunk PackedOf(int cx, int cy) => ChunkOrThrow(cx, cy).Packed;

        /// <summary>
        /// The view's drawn hint: the chunks it draws now (the drawn tier), so they aren't packed under it and their cells
        /// stay the objects it read. Not a command and not shared state: it changes memory only, so a peer with no view
        /// gives none and peers needn't agree on it. Each call replaces the last; a chunk's hold (streaming.packTicks)
        /// counts from when it leaves the hint. Call it from the main thread, between ticks or not.
        /// </summary>
        public void SetDrawn(IEnumerable<(int cx, int cy)> chunks)
        {
            drawnHint.Clear();
            if (chunks == null) return;
            foreach (var (cx, cy) in chunks)
            {
                drawnHint.Add(Chunks.Key(FoldChunkX(cx), cy)); // a chunk past the seam is the one it folds to
                // A chunk that comes into the hint is needed from now, whether or not it's unpacked yet.
                if (FindChunk(cx, cy) is Chunk ch) ch.Needed = Tick;
            }
            // The view reads what it draws: on a generated map, made ahead.
            if (Source is IGeneratedSource g) g.Ahead(drawnHint.Select(k => ((int)(uint)k, (int)(k >> 32))));
        }

        /// <summary>
        /// On a world whose source makes its chunks as they're read (a generated map's, IGeneratedSource): asks for these
        /// chunks to be made ahead on a worker, nearest first, as they'll be read soon (ChunkPlanner's next chunks, the
        /// view's drawn hint). Not a command and not shared state: it changes time only. Nothing on another source.
        /// </summary>
        public void MakeAhead(IEnumerable<(int cx, int cy)> chunks)
        {
            if (Source is IGeneratedSource g && chunks != null) g.Ahead(chunks.Select(c => (FoldChunkX(c.cx), c.cy)));
        }

        /// <summary>MakeAhead, waiting for them: made now on every core, the chunks about to activate (ChunkStreaming.Apply).</summary>
        public void MakeNow(IEnumerable<(int cx, int cy)> chunks)
        {
            if (Source is IGeneratedSource g && chunks != null) g.MakeNow(chunks.Select(c => (FoldChunkX(c.cx), c.cy)));
        }

        /// <summary>The chunks in the view's drawn hint, as it last gave them.</summary>
        public int DrawnCount => drawnHint.Count;

        /// <summary>Packs an inactive chunk now (as PackDue would once its hold is up), between ticks. A read of its cells unpacks it again.</summary>
        public PackedChunk Pack(int cx, int cy)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Active) throw new InvalidOperationException($"Chunk ({cx}, {cy}) is active: only an inactive chunk packs");
            if (ch.Packed == null) PackChunk(ch);
            return ch.Packed;
        }

        /// <summary>Unpacks a packed chunk between ticks: a block from the pool (or a new one), each cell made by the source, the changes laid over.</summary>
        public void Unpack(int cx, int cy)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Packed != null) UnpackChunk(ch);
        }

        // A read of a packed chunk (Chunk.Block), mid-tick or not: it unpacks, and the read gets its block.
        internal ChunkBlock UnpackOnRead(Chunk ch)
        {
            OwnThread(ch, "would unpack for a read");
            if (!ch.Active && !drawnHint.Contains(Chunks.Key(ch.X, ch.Y))) ReadUnpacks++;
            UnpackChunk(ch);
            return ch.Held;
        }

        // Every chunk packed with no changes, against a source that makes every cell (new World, MakeFrom): no Cell objects.
        void PackAllEmpty(IChunkSource source)
        {
            Source = source;
            foreach (var ch in unpackedChunks)
            {
                var b = ch.Held;
                PackLand(ch, b);
                ch.Hold(null);
                if (blockPool.Count < PoolKeep) blockPool.Push(b);
                Packs++;
            }
            unpackedChunks.Clear();
            foreach (var ch in chunks.Values)
            {
                ch.Packed = PackedChunk.Empty(ch.X, ch.Y);
                ch.SnapValid = false;
            }
        }

        void UnpackChunk(Chunk ch)
        {
            OwnThread(ch, "would unpack");
            long started = Stopwatch.GetTimestamp();
            var p = ch.Packed;
            var b = blockPool.Count > 0 ? blockPool.Pop() : new ChunkBlock();
            Array.Clear(b.FireGeneration, 0, Chunks.Area);
            Array.Clear(b.Marks, 0, Chunks.Area);
            int k = 0;
            for (int i = 0; i < Chunks.Area; i++)
            {
                int x = CellX(ch, i), y = CellY(ch, i);
                if (!InBounds(x, y))
                {
                    b.Cells[i] = null;
                    continue;
                }
                var c = b.Cells[i] ?? new Cell();
                c.Reset();
                Source.Make(x, y, c);
                if (k < p.At.Length && p.At[k] == i) LayPacked(p, k++, c);
                c.ForgetDef();
                b.Cells[i] = c;
            }
            for (int f = 0; f < p.FireAt.Length; f++) b.FireGeneration[p.FireAt[f]] = p.FireGen[f];
            UnpackLand(ch, b);
            ch.Packed = null;
            ch.Hold(b);
            ch.SnapValid = false;
            ch.Needed = Tick;
            unpackedChunks.Add(ch);
            Unpacks++;
            unpackTime += Stopwatch.GetTimestamp() - started;
        }

        void PackChunk(Chunk ch)
        {
            OwnThread(ch, "would pack");
            long started = Stopwatch.GetTimestamp();
            var b = ch.Held;
            ch.Packed = Changes(ch, b);
            PackLand(ch, b);
            ch.Hold(null);
            ch.SnapValid = false;
            unpackedChunks.Remove(ch);
            if (blockPool.Count < PoolKeep) blockPool.Push(b);
            Packs++;
            packTime += Stopwatch.GetTimestamp() - started;
        }

        // Before a tick reads them: the active chunks still packed (a world just made, MakeFrom, a load) and the chunks
        // round them (the tick reads one chunk out), between ticks rather than by the tick's reads.
        void UnpackActive(Chunk[] active)
        {
            if (Source is IGeneratedSource g) MakeAround(g, active);
            foreach (var ch in active) UnpackAround(ch);
        }

        // On a generated map, the packed chunks about to unpack (the active ones and those round them) made first on every
        // core, rather than one at a time as each unpacks: a world just made, or loaded, has its reach to make at once.
        static void MakeAround(IGeneratedSource g, Chunk[] active)
        {
            List<(int cx, int cy)> packed = null;
            void Add(Chunk ch)
            {
                if (ch?.Packed == null) return;
                packed ??= new List<(int cx, int cy)>();
                packed.Add((ch.X, ch.Y));
            }
            foreach (var ch in active)
            {
                if (ch.Packed == null && !BesidePacked(ch)) continue;
                Add(ch);
                Add(ch.North);
                Add(ch.North?.East);
                Add(ch.North?.West);
                Add(ch.South);
                Add(ch.South?.East);
                Add(ch.South?.West);
                Add(ch.East);
                Add(ch.West);
            }
            if (packed != null) g.MakeNow(packed);
        }

        static bool BesidePacked(Chunk ch) =>
            ch.North?.Packed != null || ch.South?.Packed != null || ch.East?.Packed != null || ch.West?.Packed != null
            || ch.North?.East?.Packed != null || ch.North?.West?.Packed != null || ch.South?.East?.Packed != null || ch.South?.West?.Packed != null;

        // A chunk and the eight round it unpacked, those that are packed.
        void UnpackAround(Chunk ch)
        {
            if (ch.Packed != null) UnpackChunk(ch);
            var n = ch.North;
            var s = ch.South;
            UnpackIfPacked(n);
            UnpackIfPacked(n?.East);
            UnpackIfPacked(n?.West);
            UnpackIfPacked(s);
            UnpackIfPacked(s?.East);
            UnpackIfPacked(s?.West);
            UnpackIfPacked(ch.East);
            UnpackIfPacked(ch.West);
        }

        void UnpackIfPacked(Chunk ch)
        {
            if (ch?.Packed != null) UnpackChunk(ch);
        }

        /// <summary>
        /// Between whole ticks (EndTick): packs up to streaming.packsPerTick chunks whose hold is up, the longest unneeded
        /// first, then by row and column. A chunk is needed while it's active, beside an active chunk (one of eight) or in
        /// the view's drawn hint.
        /// </summary>
        void PackDue()
        {
            var s = Rules.Sim.Streaming;
            if (s.PacksPerTick <= 0 || unpackedChunks.Count == 0) return;
            foreach (var ch in unpackedChunks)
                if (ch.Active || BesideActive(ch) || drawnHint.Contains(Chunks.Key(ch.X, ch.Y))) ch.Needed = Tick;
            for (int n = 0; n < s.PacksPerTick; n++)
            {
                Chunk due = null;
                foreach (var ch in unpackedChunks)
                {
                    if (ch.Needed == Tick || Tick - ch.Needed < s.PackTicks) continue;
                    if (due == null || ch.Needed < due.Needed || (ch.Needed == due.Needed && (ch.Y < due.Y || (ch.Y == due.Y && ch.X < due.X)))) due = ch;
                }
                if (due == null) return;
                PackChunk(due);
            }
        }

        // Whether any of the eight chunks round it is active.
        static bool BesideActive(Chunk ch)
        {
            var n = ch.North;
            var s = ch.South;
            return (n != null && (n.Active || (n.East?.Active ?? false) || (n.West?.Active ?? false)))
                || (s != null && (s.Active || (s.East?.Active ?? false) || (s.West?.Active ?? false)))
                || (ch.East?.Active ?? false) || (ch.West?.Active ?? false);
        }

        /// <summary>
        /// A write to every cell (SnuffFires, a material's lapse): each chunk in turn, a packed one unpacked for it and packed
        /// again straight after if it's inactive, so a pass over a big map never holds it all unpacked. On a source that
        /// makes its chunks as they're read (IGeneratedSource) an untouched chunk is left as it is: its cells are the map's
        /// as made, which hold no fire and none of a mod's materials, so the write would change none of them.
        /// </summary>
        void WriteEveryCell(Action<Cell> write)
        {
            bool skipUntouched = Source is IGeneratedSource;
            foreach (var ch in SortedChunks(false))
            {
                if (skipUntouched && ch.Packed != null && ch.Packed.Count == 0) continue;
                bool repack = ch.Packed != null && !ch.Active;
                if (repack) UnpackChunk(ch);
                var cells = ch.Block.Cells;
                for (int i = 0; i < Chunks.Area; i++) if (cells[i] is Cell c) write(c);
                if (repack) PackChunk(ch);
            }
        }

        // ---------- the changes ----------

        // A chunk's changes against the source and its fire generations, from its block (Pack's diff).
        PackedChunk Changes(Chunk ch, ChunkBlock b)
        {
            var made = new Cell();
            var at = new List<int>();
            var cells = new List<PackedCell>();
            var rares = new List<PackedRare>();
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = b.Cells[i];
                if (c == null) continue;
                made.Reset();
                Source.Make(CellX(ch, i), CellY(ch, i), made);
                if (SameCell(c, made) && c.Liquid == made.Liquid && c.Sets == made.Sets) continue;
                at.Add(i);
                cells.Add(PackCell(c, made, rares));
            }
            var fireAt = new List<int>();
            var fireGen = new List<int>();
            for (int i = 0; i < Chunks.Area; i++)
            {
                if (b.FireGeneration[i] == 0) continue;
                fireAt.Add(i);
                fireGen.Add(b.FireGeneration[i]);
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
        PackedCell PackCell(Cell c, Cell made, List<PackedRare> rares)
        {
            var p = new PackedCell
            {
                T = packStrings.Id(c.T),
                Cover = packStrings.Id(c.Cover),
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
                Gas = packStrings.Id(c.Gas),
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
        void LayPacked(PackedChunk p, int k, Cell c)
        {
            var pc = p.Cells[k];
            c.T = packStrings.Of(pc.T);
            c.Liquid = pc.Liquid;
            c.Frozen = pc.Frozen;
            c.Cover = packStrings.Of(pc.Cover);
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
            c.Gas = packStrings.Of(r.Gas);
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

        // Some changes with the cell at local index i (world x, y) made c: a new PackedChunk, the old one left as it was (a
        // packed chunk's changes may be shared, as a chunk's starting state is). A cell the source makes so is no change.
        PackedChunk WithChange(PackedChunk p, int i, Cell c, int x, int y, IChunkSource source)
        {
            var made = new Cell();
            source.Make(x, y, made);
            bool same = SameCell(c, made) && c.Liquid == made.Liquid && c.Sets == made.Sets;
            var at = new List<int>(p.At);
            var cells = new List<PackedCell>(p.Cells);
            var rares = new List<PackedRare>(p.Rares);
            int k = Array.BinarySearch(p.At, i);
            if (k >= 0)
            {
                at.RemoveAt(k);
                cells.RemoveAt(k);
            }
            else k = ~k;
            if (!same)
            {
                at.Insert(k, i);
                cells.Insert(k, PackCell(c, made, rares));
            }
            return new PackedChunk { X = p.X, Y = p.Y, At = at.ToArray(), Cells = cells.ToArray(), Rares = rares.ToArray(), FireAt = p.FireAt, FireGen = p.FireGen };
        }

        // ---------- reading a chunk where it lies ----------

        /// <summary>
        /// Reads a chunk's cells in ascending local index without unpacking it (the hash, the save, the dragon's audit, the
        /// starting state): an unpacked chunk's own objects; a packed chunk's from the source and its changes into one
        /// scratch cell, keeping its place in the changes. What it returns is for reading only, and only until the next At.
        /// </summary>
        struct ChunkReader
        {
            readonly World world;
            readonly Chunk chunk;
            readonly ChunkBlock held;
            readonly PackedChunk packed;
            readonly IChunkSource source;
            readonly Cell scratch;
            int k;

            public ChunkReader(World world, Chunk chunk, Cell scratch, int from = 0)
            {
                this.world = world;
                this.chunk = chunk;
                this.scratch = scratch;
                held = chunk.Held;
                packed = chunk.Packed;
                source = world.Source;
                k = held == null ? packed.From(from) : 0;
            }

            /// <summary>A chunk's cells as some changes against a source make them (the starting state, World.Dragon), whatever the chunk holds now.</summary>
            public ChunkReader(World world, Chunk chunk, PackedChunk changes, IChunkSource source, Cell scratch, int from = 0)
            {
                this.world = world;
                this.chunk = chunk;
                this.scratch = scratch;
                this.source = source;
                held = null;
                packed = changes;
                k = changes.From(from);
            }

            /// <summary>Its cell at local index i, at or past the last one read (null outside the world's extent).</summary>
            public Cell At(int i)
            {
                if (held != null) return held.Cells[i];
                int x = CellX(chunk, i), y = CellY(chunk, i);
                if (!world.InBounds(x, y)) return null;
                scratch.Reset();
                source.Make(x, y, scratch);
                var p = packed;
                while (k < p.At.Length && p.At[k] < i) k++;
                if (k < p.At.Length && p.At[k] == i) world.LayPacked(p, k, scratch);
                return scratch;
            }
        }

        /// <summary>
        /// A packed chunk's save record, made from its changes when the save is against the world's own source: the cells
        /// SaveChunk would write, without diffing the chunk's every cell against the source.
        /// </summary>
        JObject SavePacked(Chunk ch)
        {
            var p = ch.Packed;
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
                Source.Make(x, y, made);
                c.Reset();
                Source.Make(x, y, c);
                LayPacked(p, k, c);
                // A cell kept only for its Liquid or Sets on dry ground (SameCell doesn't see them) is no change to a save.
                if (!SameCell(c, made)) cells.Add(SaveCell(i, c, LiquidsOf(ch)));
            }
            if (cells.Count > 0) o["cells"] = cells;
            return o;
        }

        /// <summary>SaveChunk's record of one chunk, packed or not, against a source: for comparing the two paths in tests.</summary>
        public JObject SaveChunkOf(int cx, int cy, IChunkSource source) => SaveChunk(ChunkOrThrow(cx, cy), source, new Cell());
    }
}
