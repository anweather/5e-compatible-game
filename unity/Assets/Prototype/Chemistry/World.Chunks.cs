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

        /// <summary>Creates the chunks covering the world's extent and fills them with fresh cells.</summary>
        void BuildChunks(string fill)
        {
            for (int cy = 0; cy <= Chunks.ChunkOf(H - 1); cy++)
                for (int cx = 0; cx <= Chunks.ChunkOf(W - 1); cx++) AddChunk(cx, cy);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    var c = new Cell(fill);
                    FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)).Cells[LocalIndex(x, y)] = c;
                    Cells[y * W + x] = c;
                }
            }
        }

        void AddChunk(int cx, int cy)
        {
            var ch = new Chunk(cx, cy);
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
        Chunk FindChunk(int cx, int cy)
        {
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
            i = LocalIndex(x, y);
            ch = InBounds(x, y) ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) : null;
            return ch?.Cells[i];
        }

        /// <summary>Like Locate, but null for a cell in a paused chunk too.</summary>
        Cell LocateActive(int x, int y, out Chunk ch, out int i)
        {
            var c = Locate(x, y, out ch, out i);
            return c != null && ch.Active ? c : null;
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
            // Activating, it catches up what it missed (World.CatchUp) and counts as live, so a
            // chunk activated for what is about to cross into it (ChunkPlanner) isn't let go as
            // settled before it arrives.
            if (active) Resume(ch);
            else Stop(ch);
            spans = null;
            activeCoords = null;
            ChunkSwitched?.Invoke(new ChunkSwitch(cx, cy, active));
        }

        /// <summary>Activates or deactivates every loaded chunk (no catching up), raising ChunkSwitched for each that changed, by row then column.</summary>
        public void SetAllActive(bool active)
        {
            FinishTick();
            var changed = new List<Chunk>();
            foreach (var ch in SortedChunks(false))
            {
                if (ch.Active == active) continue;
                ch.Active = active;
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
        /// aren't 0, so worlds nobody has terraformed or grown hash as they did before them.
        /// </summary>
        public string CellHash()
        {
            var sb = new StringBuilder();
            foreach (var c in Cells) AppendCell(sb, c);
            return Fnv1a.Hash(sb.ToString());
        }

        /// <summary>The same fingerprint over one chunk's cells (those inside the world), row by row.</summary>
        public string ChunkHash(int cx, int cy)
        {
            var sb = new StringBuilder();
            foreach (var c in ChunkOrThrow(cx, cy).Cells) if (c != null) AppendCell(sb, c);
            return Fnv1a.Hash(sb.ToString());
        }

        static void AppendCell(StringBuilder sb, Cell c)
        {
            sb.Append(c.T).Append(',').Append(c.Water).Append(',').Append(c.Frozen ? 1 : 0).Append(',')
              .Append(c.Cover ?? "").Append(',').Append(c.Fire).Append(',').Append(c.Fuel).Append(',')
              .Append(c.Gas ?? "").Append(',').Append(c.GasAmt).Append(',').Append(c.Charge);
            if (c.Height != 0) sb.Append(",h").Append(c.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Life != 0) sb.Append(",l").Append(c.Life.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (c.Rust != 0) sb.Append(",r").Append(c.Rust.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(';');
        }
    }
}
