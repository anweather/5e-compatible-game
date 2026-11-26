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
    public sealed class PackStrings
    {
        readonly Dictionary<string, ushort> ids = new Dictionary<string, ushort>(StringComparer.Ordinal) { [""] = 0 };
        readonly List<string> strings = new List<string> { null };

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

        internal string Of(ushort id) => strings[id];
    }

    internal struct PackedCell
    {
        public ushort T, Cover;
        public byte Liquid;
        public bool Frozen;
        public short Water, Fire, Height;
        public int DampUntil;
        public int Rare;
    }

    internal struct PackedRare
    {
        public ushort Gas;
        public int Water, Fire, Height, Sets, Fuel, GasAmt, Charge, Life, Rust, Heat, Lit, Slick, Star, Cracked;
    }

    public sealed class PackedChunk
    {
        public int X { get; internal set; }
        public int Y { get; internal set; }
        internal int[] At;
        internal PackedCell[] Cells;
        internal PackedRare[] Rares;
        internal int[] FireAt, FireGen;

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

        public int Count => At.Length;
        public int RareCount => Rares.Length;
        public int FireCount => FireAt.Length;

        public long Bytes => 16 + 4 * 8 + Arr(At.Length * 4L) + Arr(Cells.Length * 20L) + Arr(Rares.Length * 60L) + Arr(FireAt.Length * 4L) + Arr(FireGen.Length * 4L);

        static long Arr(long payload) => 24 + payload;

        internal int From(int i) => Array.BinarySearch(At, i) is int k && k >= 0 ? k : ~k;
    }

    public sealed class FillSource : IChunkSource
    {
        readonly string fill;

        public FillSource(string fill) => this.fill = fill ?? throw new ArgumentNullException(nameof(fill));

        public string Version => "fill:" + fill;

        public void Make(int x, int y, Cell cell) => cell.T = fill;
    }

    public sealed partial class World
    {
        readonly Stack<ChunkBlock> blockPool = new Stack<ChunkBlock>();
        readonly List<Chunk> unpackedChunks = new List<Chunk>();
        readonly PackStrings packStrings = new PackStrings();
        readonly HashSet<long> drawnHint = new HashSet<long>();

        public const int PoolKeep = 32;

        public IChunkSource Source { get; private set; }

        public int PackPoolCount => blockPool.Count;

        public int UnpackedCount => unpackedChunks.Count;

        public int Packs { get; private set; }

        public int Unpacks { get; private set; }

        public double PackMs => packTime * 1000.0 / Stopwatch.Frequency;
        public double UnpackMs => unpackTime * 1000.0 / Stopwatch.Frequency;

        long packTime, unpackTime;

        readonly int ownerThread;

        public bool OnOwnThread => Environment.CurrentManagedThreadId == ownerThread;

        void OwnThread(Chunk ch, string what)
        {
            if (Environment.CurrentManagedThreadId == ownerThread) return;
            throw new InvalidOperationException(
                $"Chunk ({ch.X}, {ch.Y}) {what} on thread {Environment.CurrentManagedThreadId}, not the world's own ({ownerThread}): "
                + "only the thread that made the world packs or unpacks its chunks. A worker that reads cells must read unpacked "
                + "chunks only (World.IsPacked), or have the main thread unpack them first (World.Unpack).");
        }

        public int ReadUnpacks { get; private set; }

        public long MapArrayBytes
        {
            get
            {
                long bytes = (landMoved?.Bytes(4) ?? 0) + (landLaid?.Bytes(4) ?? 0) + (landCrag?.Bytes(4) ?? 0) + (bareCells?.Bytes(1) ?? 0);
                foreach (var ch in ChunksInOrder)
                {
                    if (ch.PackedLand != null) bytes += 24 + ch.PackedLand.Length;
                    if (ch.Start != null && ch.Start != ch.Packed) bytes += ch.Start.Bytes;
                }
                return bytes;
            }
        }

        public long PackedLandBytes
        {
            get
            {
                long bytes = 0;
                foreach (var ch in ChunksInOrder) if (ch.PackedLand != null) bytes += 24 + ch.PackedLand.Length;
                return bytes;
            }
        }

        public void TrimPackPool(int keep)
        {
            while (blockPool.Count > Math.Max(0, keep)) blockPool.Pop();
        }

        public bool IsPacked(int cx, int cy) => ChunkOrThrow(cx, cy).Packed != null;

        public PackedChunk PackedOf(int cx, int cy) => ChunkOrThrow(cx, cy).Packed;

        public void SetDrawn(IEnumerable<(int cx, int cy)> chunks)
        {
            drawnHint.Clear();
            if (chunks == null) return;
            foreach (var (cx, cy) in chunks)
            {
                drawnHint.Add(Chunks.Key(FoldChunkX(cx), FoldChunkY(cy)));
                if (FindChunk(cx, cy) is Chunk ch) ch.Needed = Tick;
            }
            if (Source is IGeneratedSource g) g.Ahead(drawnHint.Select(k => ((int)(uint)k, (int)(k >> 32))));
        }

        public void MakeAhead(IEnumerable<(int cx, int cy)> chunks)
        {
            if (Source is IGeneratedSource g && chunks != null) g.Ahead(chunks.Select(c => (FoldChunkX(c.cx), FoldChunkY(c.cy))));
        }

        public void MakeNow(IEnumerable<(int cx, int cy)> chunks)
        {
            if (Source is IGeneratedSource g && chunks != null) g.MakeNow(chunks.Select(c => (FoldChunkX(c.cx), FoldChunkY(c.cy))));
        }

        public int DrawnCount => drawnHint.Count;

        public PackedChunk Pack(int cx, int cy)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Active) throw new InvalidOperationException($"Chunk ({cx}, {cy}) is active: only an inactive chunk packs");
            if (ch.Packed == null) PackChunk(ch);
            return ch.Packed;
        }

        public void Unpack(int cx, int cy)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Packed != null) UnpackChunk(ch);
        }

        internal ChunkBlock UnpackOnRead(Chunk ch)
        {
            OwnThread(ch, "would unpack for a read");
            if (!ch.Active && !drawnHint.Contains(Chunks.Key(ch.X, ch.Y))) ReadUnpacks++;
            UnpackChunk(ch);
            return ch.Held;
        }

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
            foreach (var ch in ChunksInOrder)
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
            if (Source is IGeneratedSource g) g.Keep(ch.X, ch.Y, true);
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
            if (Source is IGeneratedSource g) g.Keep(ch.X, ch.Y, false);
            if (blockPool.Count < PoolKeep) blockPool.Push(b);
            Packs++;
            packTime += Stopwatch.GetTimestamp() - started;
        }

        void UnpackActive(Chunk[] active)
        {
            if (Source is IGeneratedSource g) MakeAround(g, active);
            foreach (var ch in active) UnpackAround(ch);
        }

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

        void PackDue()
        {
            var s = Rules.Sim.Streaming;
            if (s.PacksPerTick <= 0 || unpackedChunks.Count == 0) return;
            foreach (var ch in unpackedChunks)
                if (ch.Active || BesideActive(ch) || drawnHint.Contains(Chunks.Key(ch.X, ch.Y))) ch.Needed = Tick;
            var near = NearChunks();
            for (int n = 0; n < s.PacksPerTick; n++)
            {
                Chunk due = null;
                int dueAway = 0;
                foreach (var ch in unpackedChunks)
                {
                    if (ch.Needed == Tick) continue;
                    int away = AwayFrom(ch, near);
                    bool left = away > s.Keep;
                    if (!left && Tick - ch.Needed < s.PackTicks) continue;
                    int rank = left ? away : 0;
                    if (due == null || rank > dueAway || (rank == dueAway && (ch.Needed < due.Needed || (ch.Needed == due.Needed && (ch.Y < due.Y || (ch.Y == due.Y && ch.X < due.X))))))
                    {
                        due = ch;
                        dueAway = rank;
                    }
                }
                if (due == null) return;
                PackChunk(due);
            }
        }

        List<(int x, int y)> NearChunks()
        {
            var near = new List<(int x, int y)>();
            if (drawnHint.Count > 0)
                foreach (long k in drawnHint) near.Add(((int)(uint)k, (int)(k >> 32)));
            else
                foreach (var a in ActiveChunkArray) near.Add((a.X, a.Y));
            return near;
        }

        int AwayFrom(Chunk ch, List<(int x, int y)> near)
        {
            int best = int.MaxValue, across = Chunks.ChunkOf(W - 1) + 1, down = Chunks.ChunkOf(H - 1) + 1;
            foreach (var a in near)
            {
                int dx = Math.Abs(a.x - ch.X), dy = Math.Abs(a.y - ch.Y);
                if (WrapX) dx = Math.Min(dx, across - dx);
                if (WrapY) dy = Math.Min(dy, down - dy);
                int d = Math.Max(dx, dy);
                if (d < best) best = d;
            }
            return best;
        }

        static bool BesideActive(Chunk ch)
        {
            var n = ch.North;
            var s = ch.South;
            return (n != null && (n.Active || (n.East?.Active ?? false) || (n.West?.Active ?? false)))
                || (s != null && (s.Active || (s.East?.Active ?? false) || (s.West?.Active ?? false)))
                || (ch.East?.Active ?? false) || (ch.West?.Active ?? false);
        }

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
                if (!SameCell(c, made)) cells.Add(SaveCell(i, c, LiquidsOf(ch)));
            }
            if (cells.Count > 0) o["cells"] = cells;
            return o;
        }

        public JObject SaveChunkOf(int cx, int cy, IChunkSource source) => SaveChunk(ChunkOrThrow(cx, cy), source, new Cell());
    }
}
