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

        void BuildChunks(string fill)
        {
            for (int cy = 0; cy <= Chunks.ChunkOf(H - 1); cy++)
                for (int cx = 0; cx <= Chunks.ChunkOf(W - 1); cx++) AddChunk(cx, cy);
            PackAllEmpty(new FillSource(fill));
        }

        void AddChunk(int cx, int cy)
        {
            var ch = new Chunk(cx, cy) { Fit = fits, Owner = this };
            chunks.Add(Chunks.Key(cx, cy), ch);
            StaleHash(ch);
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
            chunkCoords = null;
            sortedChunks = null;
        }

        internal Chunk FindChunk(int cx, int cy)
        {
            cx = FoldChunkX(cx);
            cy = FoldChunkY(cy);
            var last = lastChunk;
            if (last != null && last.X == cx && last.Y == cy) return last;
            if (!chunks.TryGetValue(Chunks.Key(cx, cy), out var ch)) return null;
            lastChunk = ch;
            return ch;
        }

        static int LocalIndex(int x, int y) => (Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x);

        Cell Locate(int x, int y, out Chunk ch, out int i)
        {
            x = FoldX(x);
            y = FoldY(y);
            i = LocalIndex(x, y);
            ch = InBounds(x, y) ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) : null;
            return ch?.CellAt(i);
        }

        Cell LocateActive(int x, int y, out Chunk ch, out int i)
        {
            x = FoldX(x);
            y = FoldY(y);
            i = LocalIndex(x, y);
            ch = InBounds(x, y) ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) : null;
            return ch != null && ch.Active ? ch.CellAt(i) : null;
        }

        public bool IsActiveAt(int x, int y) => LocateActive(x, y, out _, out _) != null;

        internal IEnumerable<(int x, int y, int dir)> ActiveNeighbours(int x, int y)
        {
            foreach (var n in Neighbours(x, y))
                if (IsActiveAt(n.x, n.y)) yield return n;
        }

        public IReadOnlyList<(int cx, int cy)> ChunkCoords
        {
            get
            {
                if (chunkCoords != null) return chunkCoords;
                var list = new List<(int cx, int cy)>();
                foreach (var ch in SortedChunks(false)) list.Add((ch.X, ch.Y));
                return chunkCoords = list.AsReadOnly();
            }
        }

        IReadOnlyList<(int cx, int cy)> chunkCoords;
        Chunk[] sortedChunks;

        public bool HasChunk(int cx, int cy) => FindChunk(cx, cy) != null;

        List<(int cx, int cy)> activeCoords;

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

        public bool IsActive(int cx, int cy) => ChunkOrThrow(cx, cy).Active;

        public event Action<ChunkSwitch> ChunkSwitched;

        public void SetActive(int cx, int cy, bool active)
        {
            FinishTick();
            var ch = ChunkOrThrow(cx, cy);
            if (ch.Active == active) return;
            ch.Active = active;
            if (active)
            {
                UnpackAround(ch);
                MakeFit(ch);
                Resume(ch);
            }
            else Stop(ch);
            spans = null;
            activeCoords = null;
            ChunkSwitched?.Invoke(new ChunkSwitch(ch.X, ch.Y, active));
        }

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

        RowSpan[] Spans
        {
            get
            {
                if (spans == null) BuildSpans();
                return spans;
            }
        }

        Chunk[] ActiveChunkArray
        {
            get
            {
                if (spans == null) BuildSpans();
                return activeChunks;
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

        List<Chunk> SortedChunks(bool activeOnly)
        {
            var all = ChunksInOrder;
            var list = new List<Chunk>(activeOnly ? 16 : all.Length);
            foreach (var ch in all) if (ch.Active || !activeOnly) list.Add(ch);
            return list;
        }

        Chunk[] ChunksInOrder
        {
            get
            {
                if (sortedChunks == null)
                {
                    var all = new Chunk[chunks.Count];
                    chunks.Values.CopyTo(all, 0);
                    Array.Sort(all, ByRowThenColumn);
                    sortedChunks = all;
                }
                return sortedChunks;
            }
        }

        static readonly Comparison<Chunk> ByRowThenColumn = (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);

        void AddRowSpans(List<Chunk> active, int first, int last, List<RowSpan> list)
        {
            int y0 = active[first].Y * Chunks.Size;
            int lyFrom = IsOpen ? 0 : Math.Max(0, -y0), lyTo = IsOpen ? Chunks.Size : Math.Min(Chunks.Size, H - y0);
            for (int ly = lyFrom; ly < lyTo; ly++)
            {
                for (int k = first; k <= last; k++)
                {
                    int x0 = active[k].X * Chunks.Size;
                    int lxFrom = IsOpen ? 0 : Math.Max(0, -x0), lxTo = IsOpen ? Chunks.Size : Math.Min(Chunks.Size, W - x0);
                    if (lxFrom < lxTo) list.Add(new RowSpan(active[k], (ly << Chunks.Shift) + lxFrom, (ly << Chunks.Shift) + lxTo));
                }
            }
        }

        public Cell CellAt(int index)
        {
            if (IsOpen) throw NoIndex();
            return index >= 0 && index < W * H ? Locate(index % W, index / W, out _, out _) : null;
        }

        public ReadOnlySpan<Cell> ChunkCells(int cx, int cy)
        {
            var ch = FindChunk(cx, cy);
            return ch == null ? ReadOnlySpan<Cell>.Empty : ch.ActiveCells;
        }

        public IEnumerable<Cell> AllCells()
        {
            if (IsOpen)
            {
                foreach (var c in OpenCells()) yield return c;
                yield break;
            }
            var run = new RowCells(this, 0, W * H);
            while (run.MoveNext()) yield return run.Cell;
        }

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

        readonly Cell walkScratch = new Cell();

        void Mark(int x, int y, byte mark)
        {
            if (Locate(x, y, out var ch, out int i) != null) ch.Marks[i] |= mark;
        }

        static int CellX(Chunk ch, int i) => (ch.X << Chunks.Shift) + (i & Chunks.Mask);
        static int CellY(Chunk ch, int i) => (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);

        public string CellHash()
        {
            if (IsOpen) return OpenCellHash();
            var sum = Fnv1aSum.Start();
            HashCells(ref sum, 0, W * H);
            return CloseHash(sum);
        }

        public string CellText()
        {
            if (IsOpen) return OpenCellText();
            var sb = new StringBuilder();
            if (HashesChanges)
                for (var run = new ChangedRun(this, 0, W * H, walkScratch, hashMade); run.MoveNext();)
                    AppendCell(sb.Append('@').Append(run.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':'), run.Cell, LiquidsIn(run.Chunk, run.Cell));
            else
                for (var run = new RowCells(this, 0, W * H, walkScratch); run.MoveNext();) AppendCell(sb, run.Cell, LiquidsIn(run.Chunk, run.Cell));
            foreach (ulong thing in crackedThings) sb.Append('!').Append(thing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sb.Append(FitText()).Append(WrapText).ToString();
        }

        public void HashSlice(ref Fnv1aSum sum, int slice, int slices)
        {
            if (IsOpen)
            {
                OpenHashSlice(ref sum, slice, slices);
                return;
            }
            long n = (long)W * H;
            HashCells(ref sum, (int)(n * slice / slices), (int)(n * (slice + 1) / slices));
        }

        void HashCells(ref Fnv1aSum sum, int from, int to)
        {
            if (HashesChanges)
            {
                for (var run = new ChangedRun(this, from, to, walkScratch, hashMade); run.MoveNext();)
                {
                    sum.Add('@');
                    sum.Add(run.Index);
                    sum.Add(':');
                    AddCell(ref sum, run.Cell, LiquidsIn(run.Chunk, run.Cell));
                }
                return;
            }
            for (var run = new RowCells(this, from, to, walkScratch); run.MoveNext();) AddCell(ref sum, run.Cell, LiquidsIn(run.Chunk, run.Cell));
        }

        public bool HashesChanges => Source is IGeneratedSource;

        readonly Cell hashMade = new Cell();

        struct ChangedRun
        {
            readonly World world;
            readonly int to;
            readonly Cell scratch, made;
            int x, y, end, k, i;
            PackedChunk packed;
            ChunkBlock held;

            public ChangedRun(World world, int from, int to, Cell scratch, Cell made)
            {
                this.world = world;
                this.to = to;
                this.scratch = scratch;
                this.made = made;
                x = from % world.W;
                y = from / world.W;
                end = -1;
                k = i = 0;
                packed = null;
                held = null;
                Chunk = null;
                Cell = null;
                Index = 0;
            }

            public Cell Cell;
            public Chunk Chunk;
            public int Index;

            public bool MoveNext()
            {
                while (true)
                {
                    if (end < 0 && !NextSegment()) return false;
                    if (packed != null)
                    {
                        if (k < packed.At.Length && packed.At[k] < end)
                        {
                            Cell = scratch;
                            scratch.Reset();
                            world.LayPacked(packed, k, scratch);
                            Done(packed.At[k++]);
                            return true;
                        }
                    }
                    else
                        for (; i < end; i++)
                        {
                            var c = held.Cells[i];
                            if (c == null) continue;
                            made.Reset();
                            world.Source.Make(CellX(Chunk, i), CellY(Chunk, i), made);
                            if (SameCell(c, made) && c.Liquid == made.Liquid && c.Sets == made.Sets) continue;
                            Cell = c;
                            Done(i++);
                            return true;
                        }
                    end = -1;
                }
            }

            void Done(int local) => Index = ((Chunk.Y << Chunks.Shift) + (local >> Chunks.Shift)) * world.W + (Chunk.X << Chunks.Shift) + (local & Chunks.Mask);

            bool NextSegment()
            {
                int at = y * world.W + x;
                if (at >= to || y >= world.H) return false;
                int lx = Chunks.LocalOf(x), ly = Chunks.LocalOf(y);
                int run = Math.Min(Math.Min(Chunks.Size - lx, world.W - x), to - at);
                Chunk = world.FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
                held = Chunk.Held;
                packed = held == null ? Chunk.Packed : null;
                int start = (ly << Chunks.Shift) | lx;
                end = start + run;
                if (packed != null) k = packed.From(start);
                else i = start;
                x += run;
                if (x == world.W)
                {
                    x = 0;
                    y++;
                }
                return true;
            }
        }

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

        string WrapText
        {
            get
            {
                if (WrapY) return "~torus";
                return WrapX ? "~ew" : "";
            }
        }

        public string ChunkHash(int cx, int cy)
        {
            var sum = Fnv1aSum.Start();
            var ch = ChunkOrThrow(cx, cy);
            var read = new ChunkReader(this, ch, new Cell());
            for (int i = 0; i < Chunks.Area; i++) if (read.At(i) is Cell c) AddCell(ref sum, c, LiquidsOf(ch));
            return sum.ToString();
        }

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
