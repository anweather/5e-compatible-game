// The open world (D20, docs/design/core-package/open-world.md): a World with no bounds, whose chunks exist once they're
// filled. Beside the bounded worlds, not instead of them: our game's maps stay bounded, and nothing here runs on one.
//
// - Its chunks are the same Chunk objects in the same store, keyed by their coordinates in one sheet of rows, every layer
//   a run of its rows (ChunkKey). A chunk is made by a fill (FillChunk, a command), inactive and packed against its ground;
//   the plan (OpenWorldPlanner) then activates it. A filled chunk is never dropped: inactive, it's packed (D17).
// - A cell is on the world (InBounds) when its chunk is filled. A neighbour chunk not filled is null to its neighbour's
//   links and lookups, so fire, water and gas stop there, as at a bounded map's edge.
// - The source (FilledGround) makes each chunk's cells from its ground, so a chunk's changes and its save are against
//   what the host game sampled.
// - Every walk over its chunks (the step's spans, the hash, a save) takes them in key order, never the store's. Cells
//   have no world index (Idx throws): a set of cells uses CellKey. The hash is the changes hash, chunk by chunk: each
//   filled chunk's key and ground, then its cells that differ from the ground.
// - What needs a bounded map (its land, buildings, machines' links, the hold, the dragon, golf, the course, defense,
//   raiders) isn't for an open world: it runs the chemistry, casts, hands and strikes.
using System;
using System.Collections.Generic;
using System.Text;
using Chem.Lockstep;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        // An open world: its chunks are filled as players reach them (World.OpenWorld).
        World(Rules rules, uint seed)
        {
            ownerThread = Environment.CurrentManagedThreadId;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            IsOpen = true;
            edgeless = true;
            RngState = seed;
            Seed = seed;
            Weather = Sky.Weather(rules.Sim, seed, 0);
            Source = filled = new FilledGround();
        }

        /// <summary>
        /// An open world (D20): no bounds and no chunks until they're filled (FillChunk), every layer a run of rows
        /// (ChunkKey.Row). W and H are 0.
        /// </summary>
        public static World OpenWorld(Rules rules, uint seed = 1) => new World(rules, seed);

        /// <summary>Whether this is an open world (World.OpenWorld): chunks made by fills, no bounds.</summary>
        public bool IsOpen { get; }

        readonly FilledGround filled;

        static InvalidOperationException NoIndex() =>
            new InvalidOperationException("An open world has no cell index (it has no width): a cell is its x and its row, a set of cells is by CellKey");

        // Throws NoIndex, out of line: a method that throws isn't inlined, and Idx is read in the tick's passes.
        static int NoIndexAt() => throw NoIndex();

        static InvalidOperationException NotOpen(string what) => new InvalidOperationException($"Only an open world (World.OpenWorld) {what}");

        /// <summary>
        /// Fills a chunk of an open world from the ground the host game sampled: made inactive and packed against its
        /// ground, linked to the filled chunks round it. A chunk filled already keeps what it has (false). A command
        /// (TickBatch.Fills): between ticks, the same on every peer.
        /// </summary>
        public bool FillChunk(Chem.ChunkKey key, ChunkGround ground)
        {
            if (!IsOpen) throw NotOpen("is filled");
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (!key.Fits) throw new ArgumentOutOfRangeException(nameof(key), $"chunk {key} isn't one an open world holds (layers {Chem.ChunkKey.MinLayer} to {Chem.ChunkKey.MaxLayer}, at most {Chem.ChunkKey.MostChunks} chunks from 0)");
            FinishTick(); // a fill lands between whole ticks
            var (cx, cy) = key.Chunk;
            if (FindChunk(cx, cy) != null) return false;
            filled.Add(cx, cy, ground);
            AddChunk(cx, cy);
            var ch = FindChunk(cx, cy);
            ch.Active = false;
            ch.Packed = PackedChunk.Empty(cx, cy);
            return true;
        }

        /// <summary>Whether an open world's chunk is filled.</summary>
        public bool IsFilled(Chem.ChunkKey key)
        {
            var (cx, cy) = key.Chunk;
            return IsOpen && FindChunk(cx, cy) != null;
        }

        /// <summary>Whether an open world's chunk is filled and active.</summary>
        public bool IsActive(Chem.ChunkKey key)
        {
            var (cx, cy) = key.Chunk;
            return IsOpen && FindChunk(cx, cy) is Chunk ch && ch.Active;
        }

        /// <summary>The ground an open world's chunk was filled from, or null for one not filled.</summary>
        public ChunkGround GroundOf(Chem.ChunkKey key)
        {
            var (cx, cy) = key.Chunk;
            return IsOpen ? filled.Of(cx, cy) : null;
        }

        /// <summary>An open world's filled chunks, in key order (layer, row, column).</summary>
        public IReadOnlyList<Chem.ChunkKey> Filled
        {
            get
            {
                var list = new List<Chem.ChunkKey>();
                if (IsOpen) foreach (var ch in ChunksInOrder) list.Add(Chem.ChunkKey.KeyOfChunk(ch.X, ch.Y));
                return list;
            }
        }

        /// <summary>An open world's active chunks, in key order.</summary>
        public IReadOnlyList<Chem.ChunkKey> ActiveKeys
        {
            get
            {
                var list = new List<Chem.ChunkKey>();
                if (IsOpen) foreach (var ch in ActiveChunkArray) list.Add(Chem.ChunkKey.KeyOfChunk(ch.X, ch.Y));
                return list;
            }
        }

        // A cell for the weather to strike or drop a meteor on, from two draws of the world's generator: anywhere on a
        // bounded world, in a filled chunk of an open one (none filled: a cell on no chunk, which nothing happens to).
        (int x, int y) AnyCell()
        {
            double a = Rand(), b = Rand();
            if (!IsOpen) return ((int)(a * W), (int)(b * H));
            var all = ChunksInOrder;
            if (all.Length == 0) return (0, 0);
            double at = a * all.Length;
            int k = Math.Min(all.Length - 1, (int)at);
            var ch = all[k];
            int lx = Math.Min(Chunks.Mask, (int)((at - k) * Chunks.Size)), ly = Math.Min(Chunks.Mask, (int)(b * Chunks.Size));
            return ((ch.X << Chunks.Shift) + lx, (ch.Y << Chunks.Shift) + ly);
        }

        // ---------- saves ----------

        // Every filled chunk's ground, in key order: [x, y, its bytes in base64], by its coordinates in the sheet.
        JArray SaveFills()
        {
            var fills = new JArray();
            foreach (var ch in ChunksInOrder) fills.Add(new JArray(ch.X, ch.Y, Convert.ToBase64String(filled.Of(ch.X, ch.Y).Bytes())));
            return fills;
        }

        // A save's fills, before its chunks' records: each chunk made from its ground, active as a bounded world's chunk
        // starts (a saved record says when it isn't).
        void LoadFills(JArray fills)
        {
            foreach (var f in fills)
            {
                ChunkGround ground;
                try
                {
                    ground = ChunkGround.FromBytes(Convert.FromBase64String((string)f[2]));
                }
                catch (Exception e) when (e is FormatException || e is LockstepWireException)
                {
                    throw new SaveException($"This save doesn't fit: the ground of chunk ({f[0]}, {f[1]}) doesn't read ({e.Message})");
                }
                var key = Chem.ChunkKey.KeyOfChunk((int)f[0], (int)f[1]);
                if (!key.Fits || !FillChunk(key, ground)) throw new SaveException($"This save doesn't fit: chunk ({f[0]}, {f[1]}) is filled twice or out of the world");
                FindChunk((int)f[0], (int)f[1]).Active = true;
            }
        }

        // ---------- the open world's hash ----------
        // Over the filled chunks in key order, each its coordinates and ground's fingerprint, then the cells that differ
        // from its ground by local index: what every peer that filled the same chunks with the same grounds and played the
        // same ticks holds, and nothing a chunk not filled could add.

        interface IOpenSink
        {
            void Chunk(Chunk ch, ulong ground);
            void Cell(int i, Cell c, string[] liquids);
        }

        struct HashSink : IOpenSink
        {
            public World World;
            public Fnv1aSum Sum;

            public void Chunk(Chunk ch, ulong ground)
            {
                Sum.Add('#');
                Sum.Add(ch.X);
                Sum.Add(',');
                Sum.Add(ch.Y);
                Sum.Add(':');
                Sum.AddDigits(ground);
                Sum.Add(';');
            }

            public void Cell(int i, Cell c, string[] liquids)
            {
                Sum.Add('@');
                Sum.Add(i);
                Sum.Add(':');
                World.AddCell(ref Sum, c, liquids);
            }
        }

        struct TextSink : IOpenSink
        {
            public World World;
            public StringBuilder Text;

            public void Chunk(Chunk ch, ulong ground) =>
                Text.Append('#').Append(ch.X.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(ch.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                    .Append(ground.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');

            public void Cell(int i, Cell c, string[] liquids) =>
                World.AppendCell(Text.Append('@').Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':'), c, liquids);
        }

        // The filled chunks [from, to) of the key order into a sink, never unpacking one: a packed chunk's changes are laid
        // on a reset cell where they lie (as the changes hash's ChangedRun does), an unpacked chunk's cells are compared
        // with its ground.
        void WalkOpen<T>(ref T sink, int from, int to) where T : struct, IOpenSink
        {
            var all = ChunksInOrder;
            for (int k = from; k < to; k++)
            {
                var ch = all[k];
                sink.Chunk(ch, filled.Of(ch.X, ch.Y).Hash);
                if (ch.Held is ChunkBlock b)
                {
                    for (int i = 0; i < Chunks.Area; i++)
                    {
                        var c = b.Cells[i];
                        hashMade.Reset();
                        filled.Make(CellX(ch, i), CellY(ch, i), hashMade);
                        if (SameCell(c, hashMade) && c.Liquid == hashMade.Liquid && c.Sets == hashMade.Sets) continue;
                        sink.Cell(i, c, LiquidsIn(ch, c));
                    }
                    continue;
                }
                var p = ch.Packed;
                for (int j = 0; j < p.At.Length; j++)
                {
                    walkScratch.Reset();
                    LayPacked(p, j, walkScratch);
                    sink.Cell(p.At[j], walkScratch, LiquidsIn(ch, walkScratch));
                }
            }
        }

        string OpenCellHash()
        {
            var sink = new HashSink { World = this, Sum = Fnv1aSum.Start() };
            WalkOpen(ref sink, 0, ChunksInOrder.Length);
            return CloseHash(sink.Sum);
        }

        string OpenCellText()
        {
            var sink = new TextSink { World = this, Text = new StringBuilder() };
            WalkOpen(ref sink, 0, ChunksInOrder.Length);
            foreach (ulong thing in crackedThings) sink.Text.Append('!').Append(thing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sink.Text.Append(FitText()).ToString();
        }

        // One of slices slices of the filled chunks, by their key order.
        void OpenHashSlice(ref Fnv1aSum sum, int slice, int slices)
        {
            int n = ChunksInOrder.Length;
            var sink = new HashSink { World = this, Sum = sum };
            WalkOpen(ref sink, (int)((long)n * slice / slices), (int)((long)n * (slice + 1) / slices));
            sum = sink.Sum;
        }

        // Every filled chunk's cells, chunk by chunk in key order (AllCells on an open world, which has no rows to walk).
        IEnumerable<Cell> OpenCells()
        {
            foreach (var ch in ChunksInOrder)
                for (int i = 0; i < Chunks.Area; i++) yield return ch.CellAt(i);
        }
    }
}
