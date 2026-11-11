// Chunked cell storage and the active-chunk set (docs/WORLD_STREAMING.md, part 1).
// Cells live in 64×64 chunks keyed by world-wide chunk coordinate. Step simulates
// only active chunks; a paused chunk is frozen: nothing the simulation does writes
// to it (ticks, reactions, spells, blasts, earth shaping), and cells beside it treat
// it as a wall for fire, water and gas. Map building (Paint, AddWater, SetCover)
// still writes anywhere.
using System;
using System.Collections.Generic;
using System.Text;

namespace Chem
{
    public sealed partial class World
    {
        readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        Chunk lastChunk;

        // The active chunks' cells as row segments, in world row-major order (y, then x),
        // whatever order the store keeps them in. Rebuilt when the active set changes.
        RowSpan[] spans;
        Chunk[] activeChunks;

        readonly struct RowSpan
        {
            public readonly Chunk Chunk;
            public readonly int Start, End;

            public RowSpan(Chunk chunk, int start, int end)
            {
                Chunk = chunk;
                Start = start;
                End = end;
            }
        }

        /// <summary>
        /// Creates the chunks covering the world's extent, every one packed against a source of the fill (World.Pack.cs):
        /// no Cell objects until something reads a chunk or it ticks.
        /// </summary>
        void BuildChunks(string fill)
        {
            for (int cy = 0; cy <= Chunks.ChunkOf(H - 1); cy++)
                for (int cx = 0; cx <= Chunks.ChunkOf(W - 1); cx++) AddChunk(cx, cy);
            PackAllEmpty(new FillSource(fill));
        }

        void AddChunk(int cx, int cy)
        {
            var ch = new Chunk(cx, cy) { Fit = fits, Owner = this }; // made under the rules in force
            chunks.Add(Chunks.Key(cx, cy), ch);
            ch.North = FindChunk(cx, cy - 1);
            ch.South = FindChunk(cx, cy + 1);
            ch.West = FindChunk(cx - 1, cy);
            ch.East = FindChunk(cx + 1, cy);
            if (ch.North != null) ch.North.South = ch;
            if (ch.South != null) ch.South.North = ch;
            if (ch.West != null) ch.West.East = ch;
            if (ch.East != null) ch.East.West = ch;
            spans = null;
            activeCoords = null;
        }

        // Lookups come in runs inside one chunk, so the last chunk found is checked first.
        // A chunk x past the seam folds round (FoldChunkX), so the links, the planner and every lookup cross it.
        Chunk FindChunk(int cx, int cy)
        {
            cx = FoldChunkX(cx);
            var last = lastChunk;
            if (last != null && last.X == cx && last.Y == cy) return last;
            if (!chunks.TryGetValue(Chunks.Key(cx, cy), out var ch)) return null;
            lastChunk = ch;
            return ch;
        }

        static int LocalIndex(int x, int y) => (Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x);

        /// <summary>The cell at (x, y) with its chunk and local index, or null off the world.</summary>
        Cell Locate(int x, int y, out Chunk ch, out int i)
        {
            x = FoldX(x);
            i = LocalIndex(x, y);
            ch = InBounds(x, y) ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) : null;
            return ch?.CellAt(i);
        }

        /// <summary>Like Locate, but null for a cell in a paused chunk too.</summary>
        Cell LocateActive(int x, int y, out Chunk ch, out int i)
        {
            // The chunk is checked before its cell is read, so an inactive chunk's packed cells stay packed.
            x = FoldX(x);
            i = LocalIndex(x, y);
            ch = InBounds(x, y) ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) : null;
            return ch != null && ch.Active ? ch.CellAt(i) : null;
        }

        /// <summary>Whether (x, y) is a cell the simulation may change: on the world, in an active chunk.</summary>
        public bool IsActiveAt(int x, int y) => LocateActive(x, y, out _, out _) != null;

        /// <summary>Neighbours in N, E, S, W order that the simulation may change.</summary>
        IEnumerable<(int x, int y, int dir)> ActiveNeighbours(int x, int y)
        {
            foreach (var n in Neighbours(x, y))
                if (IsActiveAt(n.x, n.y)) yield return n;
        }

        // ---------- the active set ----------

        /// <summary>Every loaded chunk's coordinate, sorted by row then column.</summary>
        public IReadOnlyList<(int cx, int cy)> ChunkCoords
        {
            get
            {
                var list = new List<(int cx, int cy)>();
                foreach (var ch in SortedChunks(false)) list.Add((ch.X, ch.Y));
                return list;
            }
        }

        public bool HasChunk(int cx, int cy) => FindChunk(cx, cy) != null;

        List<(int cx, int cy)> activeCoords;

        /// <summary>The simulated chunks' coordinates, by row then column (kept until a chunk wakes or sleeps).</summary>
        public IReadOnlyList<(int cx, int cy)> ActiveChunks
        {
            get
            {
                if (activeCoords != null) return activeCoords;
                activeCoords = new List<(int cx, int cy)>();
                foreach (var ch in SortedChunks(false)) if (ch.Active) activeCoords.Add((ch.X, ch.Y));
                return activeCoords;
            }
        }

        /// <summary>Whether a loaded chunk is simulated. Chunks start active.</summary>
        public bool IsActive(int cx, int cy) => ChunkOrThrow(cx, cy).Active;

        /// <summary>
        /// A chunk activated (Active) or deactivated, raised by SetActive and SetAllActive once it
        /// has switched (and, activating, caught up), so its cells are as they now are. Switches
        /// only come from commands (ChunkSwitch in tick batches, or the solo plan), so every peer
        /// raises the same ones at the same tick. For the view (props sleeping with their chunk):
        /// a handler must not change the world, only send commands.
        /// </summary>
        public event Action<ChunkSwitch> ChunkSwitched;

        /// <summary>
        /// Activates or deactivates a loaded chunk. An inactive chunk keeps its cells exactly as
        /// they are; fire, water and gas next to it stop at its edge. Call it between ticks, the
        /// same on every peer.
        /// </summary>
        public void SetActive(int cx, int cy, bool active)
        {
            FinishTick(); // chunk switches land between whole ticks (StepSome)
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Active == active) return;
            ch.Active = active;
            // Activating, it's made to fit the rules if a switch while it was inactive could
            // have unfitted it (World.Fit.cs), catches up what it missed (World.CatchUp) and
            // counts as live, so a chunk activated for what is about to cross into it
            // (ChunkPlanner) isn't let go as settled before it arrives.
            if (active)
            {
                UnpackAround(ch); // an active chunk is unpacked, and those round it (World.Pack.cs)
                MakeFit(ch);
                Resume(ch);
            }
            else Stop(ch);
            spans = null;
            activeCoords = null;
            ChunkSwitched?.Invoke(new ChunkSwitch(ch.X, ch.Y, active)); // its own (folded) coordinates
        }

        /// <summary>Activates or deactivates every loaded chunk (no catching up, but made to fit the rules), raising ChunkSwitched for each that changed, by row then column.</summary>
        public void SetAllActive(bool active)
        {
            FinishTick();
            var changed = new List<Chunk>();
            foreach (var ch in SortedChunks(false))
            {
                if (ch.Active == active) continue;
                ch.Active = active;
                if (active) MakeFit(ch);
                changed.Add(ch);
            }
            spans = null;
            activeCoords = null;
            foreach (var ch in changed) ChunkSwitched?.Invoke(new ChunkSwitch(ch.X, ch.Y, active));
        }

        Chunk ChunkOrThrow(int cx, int cy) =>
            FindChunk(cx, cy) ?? throw new ArgumentOutOfRangeException(nameof(cx), $"No chunk at ({cx}, {cy})");

        /// <summary>The active chunks' row segments in world row-major order.</summary>
        RowSpan[] Spans
        {
            get
            {
                if (spans == null) BuildSpans();
                return spans;
            }
        }

        void BuildSpans()
        {
            var active = SortedChunks(true);
            var list = new List<RowSpan>();
            for (int first = 0; first < active.Count;)
            {
                int last = first;
                while (last + 1 < active.Count && active[last + 1].Y == active[first].Y) last++;
                AddRowSpans(active, first, last, list);
                first = last + 1;
            }
            spans = list.ToArray();
            activeChunks = active.ToArray();
        }

        /// <summary>Loaded chunks (or only the active ones) by row then column, never in the store's own order.</summary>
        List<Chunk> SortedChunks(bool activeOnly)
        {
            var list = new List<Chunk>();
            foreach (var ch in chunks.Values) if (ch.Active || !activeOnly) list.Add(ch);
            list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            return list;
        }

        /// <summary>Row segments for one row of chunks (active[first..last], left to right), clipped to the extent.</summary>
        void AddRowSpans(List<Chunk> active, int first, int last, List<RowSpan> list)
        {
            int y0 = active[first].Y * Chunks.Size;
            int lyFrom = Math.Max(0, -y0), lyTo = Math.Min(Chunks.Size, H - y0);
            for (int ly = lyFrom; ly < lyTo; ly++)
            {
                for (int k = first; k <= last; k++)
                {
                    int x0 = active[k].X * Chunks.Size;
                    int lxFrom = Math.Max(0, -x0), lxTo = Math.Min(Chunks.Size, W - x0);
                    if (lxFrom < lxTo) list.Add(new RowSpan(active[k], (ly << Chunks.Shift) + lxFrom, (ly << Chunks.Shift) + lxTo));
                }
            }
        }

        // ---------- cells through their chunks ----------
        // Every reach for a cell goes through its chunk (D9), never a map-sized array, so how a chunk holds its
        // cells can change under these (the sparse World, docs/design/sparse-world.md).

        /// <summary>The cell at a world index (Idx(x, y)), through its chunk; null off the world.</summary>
        public Cell CellAt(int index) => index >= 0 && index < W * H ? Locate(index % W, index / W, out _, out _) : null;

        /// <summary>
        /// The cells of chunk (cx, cy), read-only, by local index (ly * Chunks.Size + lx; null outside the world's
        /// extent), or empty for a chunk not loaded. For a view's pass over a whole chunk (drawing its cells, the fire
        /// lights), found once per chunk instead of per cell: a per-cell At there costs the frame ~0.6 ms on the nine.
        /// </summary>
        public ReadOnlySpan<Cell> ChunkCells(int cx, int cy)
        {
            var ch = FindChunk(cx, cy);
            return ch == null ? ReadOnlySpan<Cell>.Empty : ch.ActiveCells;
        }

        /// <summary>
        /// Every cell of the world in row order (Idx), through their chunks, to read or change. Not for the tick: it
        /// allocates its enumerator, and it unpacks every packed chunk it passes (World.Pack.cs), so on a big map it holds
        /// the whole map unpacked: count over ActiveChunks there.
        /// </summary>
        public IEnumerable<Cell> AllCells()
        {
            var run = new RowCells(this, 0, W * H);
            while (run.MoveNext()) yield return run.Cell;
        }

        /// <summary>
        /// Walks the cells of world indices [from, to) in row order through their chunks, finding a chunk once
        /// for each row of it crossed: no allocation, so the tick's passes (HashSlice) use it. Given a scratch cell it
        /// only reads, and reads a packed chunk where it lies (ChunkReader), into the scratch, never unpacking it; without
        /// one, its cells are the chunks' own objects, a packed chunk unpacked for them.
        /// </summary>
        struct RowCells
        {
            readonly World world;
            readonly int to;
            readonly Cell scratch;
            ChunkReader reader;
            int index, x, y;

            public RowCells(World world, int from, int to, Cell scratch = null)
            {
                this.world = world;
                this.to = to;
                this.scratch = scratch;
                reader = default;
                index = from - 1;
                x = from % world.W - 1;
                y = from / world.W;
                Chunk = null;
                Cell = null;
            }

            /// <summary>The cell walked to and its chunk.</summary>
            public Cell Cell;
            public Chunk Chunk;

            public bool MoveNext()
            {
                if (++index >= to) return false;
                if (++x == world.W)
                {
                    x = 0;
                    y++;
                    Chunk = null;
                }
                else if (Chunks.LocalOf(x) == 0) Chunk = null;
                int i = LocalIndex(x, y);
                if (Chunk == null)
                {
                    Chunk = world.FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
                    if (scratch != null) reader = new ChunkReader(world, Chunk, scratch, i);
                }
                Cell = scratch != null ? reader.At(i) : Chunk.CellAt(i);
                return true;
            }
        }

        // The cell the hash's, the audit's and SetLand's walks read a packed chunk's cells into (RowCells, ChunkReader): the World is
        // read on the main thread only.
        readonly Cell walkScratch = new Cell();

        /// <summary>Sets one of the per-tick marks (FlowMark, ...) on a cell.</summary>
        void Mark(int x, int y, byte mark)
        {
            if (Locate(x, y, out var ch, out int i) != null) ch.Marks[i] |= mark;
        }

        static int CellX(Chunk ch, int i) => (ch.X << Chunks.Shift) + (i & Chunks.Mask);
        static int CellY(Chunk ch, int i) => (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);

        // ---------- hashes ----------

        /// <summary>
        /// Stable fingerprint of the cells (FNV-1a), matching the TS golden script in the tests
        /// (cellHash in sandbox/test/sim.test.ts). Height and Life are written only when they
        /// aren't 0, and a liquid only when it isn't water, so worlds nobody has terraformed,
        /// grown or poured a liquid on hash as they did before them.
        /// </summary>
        public string CellHash()
        {
            var sum = Fnv1aSum.Start();
            HashCells(ref sum, 0, W * H);
            return CloseHash(sum);
        }

        /// <summary>What CellHash hashes, as text: for a test that the running hash agrees with it.</summary>
        public string CellText()
        {
            var sb = new StringBuilder();
            for (var run = new RowCells(this, 0, W * H, walkScratch); run.MoveNext();) AppendCell(sb, run.Cell, LiquidsIn(run.Chunk, run.Cell));
            foreach (ulong thing in crackedThings) sb.Append('!').Append(thing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sb.Append(FitText()).Append(WrapText).ToString();
        }

        /// <summary>Adds one of <paramref name="slices"/> slices of the cells to a running hash (RollingHash): row-major, by the world's size alone.</summary>
        public void HashSlice(ref Fnv1aSum sum, int slice, int slices)
        {
            long n = (long)W * H;
            HashCells(ref sum, (int)(n * slice / slices), (int)(n * (slice + 1) / slices));
        }

        // Adds the cells of world indices [from, to) to a running hash, in row order.
        void HashCells(ref Fnv1aSum sum, int from, int to)
        {
            for (var run = new RowCells(this, from, to, walkScratch); run.MoveNext();) AddCell(ref sum, run.Cell, LiquidsIn(run.Chunk, run.Cell));
        }

        /// <summary>What a hash of the cells ends with: the things cracked so far, then the chunks not yet made to fit the rules (FitText).</summary>
        public string CloseHash(Fnv1aSum sum)
        {
            foreach (ulong thing in crackedThings)
            {
                sum.Add('!');
                sum.AddDigits(thing);
            }
            sum.Add(FitText());
            sum.Add(WrapText);
            return sum.ToString();
        }

        // What a hash of the cells ends with on a world that wraps, after FitText: "~ew", so it differs from the same cells
        // on a world that doesn't (they behave apart at the edges). Nothing on one that doesn't, so its hash is as it was.
        string WrapText => WrapX ? "~ew" : "";

        /// <summary>The same fingerprint over one chunk's cells (those inside the world), row by row.</summary>
        public string ChunkHash(int cx, int cy)
        {
            var sum = Fnv1aSum.Start();
            var ch = ChunkOrThrow(cx, cy);
            var read = new ChunkReader(this, ch, new Cell());
            for (int i = 0; i < Chunks.Area; i++) if (read.At(i) is Cell c) AddCell(ref sum, c, LiquidsOf(ch));
            return sum.ToString();
        }

        // What AppendCell writes, into a running hash; a liquid by its id in liquids (LiquidsOf: null for the rules in force).
        void AddCell(ref Fnv1aSum sum, Cell c, string[] liquids)
        {
            sum.Add(c.T);
            sum.Add(',');
            sum.Add(c.Water);
            sum.Add(',');
            sum.Add(c.Frozen ? '1' : '0');
            sum.Add(',');
            sum.Add(c.Cover);
            sum.Add(',');
            sum.Add(c.Fire);
            sum.Add(',');
            sum.Add(c.Fuel);
            sum.Add(',');
            sum.Add(c.Gas);
            sum.Add(',');
            sum.Add(c.GasAmt);
            sum.Add(',');
            sum.Add(c.Charge);
            AddIfSet(ref sum, 'h', c.Height);
            AddIfSet(ref sum, 'l', c.Life);
            AddIfSet(ref sum, 'r', c.Rust);
            AddIfSet(ref sum, 't', c.Heat);
            AddIfSet(ref sum, 'b', c.Lit);
            AddIfSet(ref sum, 'k', c.Slick);
            AddIfSet(ref sum, 's', c.Star);
            AddIfSet(ref sum, 'c', c.Cracked);
            AddIfSet(ref sum, 'p', c.Setting);
            // A liquid other than water by its id (never its index), so a world of water hashes as it did.
            if (c.Kind != 0)
            {
                sum.Add(',');
                sum.Add('q');
                sum.Add(LiquidIdIn(liquids, c));
            }
            sum.Add(';');
        }

        static void AddIfSet(ref Fnv1aSum sum, char tag, int value)
        {
            if (value == 0) return;
            sum.Add(',');
            sum.Add(tag);
            sum.Add(value);
        }

        void AppendCell(StringBuilder sb, Cell c, string[] liquids)
        {
            sb.Append(c.T).Append(',').Append(c.Water).Append(',').Append(c.Frozen ? 1 : 0).Append(',')
              .Append(c.Cover ?? "").Append(',').Append(c.Fire).Append(',').Append(c.Fuel).Append(',')
              .Append(c.Gas ?? "").Append(',').Append(c.GasAmt).Append(',').Append(c.Charge);
            if (c.Height != 0) sb.Append(",h").Append(c.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Life != 0) sb.Append(",l").Append(c.Life.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Rust != 0) sb.Append(",r").Append(c.Rust.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Heat != 0) sb.Append(",t").Append(c.Heat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Lit != 0) sb.Append(",b").Append(c.Lit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Slick != 0) sb.Append(",k").Append(c.Slick.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Star != 0) sb.Append(",s").Append(c.Star.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Cracked != 0) sb.Append(",c").Append(c.Cracked.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Setting != 0) sb.Append(",p").Append(c.Setting.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Kind != 0) sb.Append(",q").Append(LiquidIdIn(liquids, c));
            sb.Append(';');
        }
    }
}
