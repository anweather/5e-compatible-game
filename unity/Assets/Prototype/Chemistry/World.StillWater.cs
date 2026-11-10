// Still water costs nothing (docs/WORLD_STREAMING.md, "Part 2, second slice",
// slice 6): a lake or the sea at rest is worked every tick (each wet cell
// looks at its four neighbours for somewhere lower), only to find nowhere.
// Before each water pass, every active chunk compares what the pass reads of
// its cells (water, height, frozen, terrain) with how they were at the last
// pass (and each cell's liquid kind). A chunk is still when nothing changed in it or its four neighbours,
// those neighbours are as active as they were, and no water moved in it or
// them last tick: then its springs and flows would do nothing, exactly, so
// the pass skips them. Water moving anywhere disturbs the chunks around it,
// and a disturbed chunk's remaining rows run as usual, in the same order the
// full pass would have run them (rows skipped before were visited before any
// change could reach them, and did nothing). So the result is exactly the full
// pass's: the sandbox, which has no chunks, needs no change, and the goldens
// stand. C# only.

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>Whether the water pass skips still chunks (on; off only to compare in tests).</summary>
        public bool SkipStillWater { get; set; } = true;

        /// <summary>How many chunks the last water pass found still, for the view's numbers.</summary>
        public int StillChunks { get; private set; }

        // Before the water pass: which of chunks [from, to) changed since the last pass. kinds: the
        // rules hold a liquid besides water, so each cell's kind is compared too (a rule switch
        // drops every snapshot, so a world of water alone never needs it).
        static void CheckWater(Chunk[] chunks, int from, int to, bool kinds)
        {
            for (int k = from; k < to; k++) CheckWater(chunks[k], kinds);
        }

        // Whether a chunk changed since the last pass, refreshing its snapshot (kept in its block).
        static void CheckWater(Chunk ch, bool kinds)
        {
            var b = ch.Block;
            var cells = b.Cells;
            int n = cells.Length;
            if (b.SnapWater == null)
            {
                b.SnapWater = new int[n];
                b.SnapHeight = new int[n];
                b.SnapFrozen = new bool[n];
                b.SnapT = new string[n];
                b.SnapKind = new byte[n];
            }
            bool same = ch.SnapValid;
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                if (c == null) continue;
                if (same && (b.SnapWater[i] != c.Water || b.SnapHeight[i] != c.Height || b.SnapFrozen[i] != c.Frozen || !ReferenceEquals(b.SnapT[i], c.T)))
                    same = false;
                if (kinds) same = SnapKind(b, i, c) && same;
                b.SnapWater[i] = c.Water;
                b.SnapHeight[i] = c.Height;
                b.SnapFrozen[i] = c.Frozen;
                b.SnapT[i] = c.T;
            }
            ch.Unchanged = same;
            ch.UnchangedFor = same ? ch.UnchangedFor + 1 : 0;
            ch.SnapValid = true;
            ch.WaterDisturbed = false;
        }

        // A cell's kind against the snapshot's, refreshing it: true if the same.
        static bool SnapKind(ChunkBlock b, int i, Cell c)
        {
            byte kind = (byte)c.Kind;
            bool same = b.SnapKind[i] == kind;
            b.SnapKind[i] = kind;
            return same;
        }

        // Once every active chunk is checked: which are still this tick.
        void MarkStill(Chunk[] chunks)
        {
            int still = 0;
            foreach (var ch in chunks)
            {
                ch.WaterStill = SkipStillWater && QuietAround(ch);
                if (ch.WaterStill) still++;
            }
            foreach (var ch in chunks)
                for (int d = 0; d < 4; d++) ch.SnapNeighbourActive[d] = Neighbour(ch, d)?.Active ?? false;
            StillChunks = still;
        }

        // Unchanged with no water moving last tick, and the same for each neighbour (an inactive one
        // is a wall: it only has to have stayed inactive).
        bool QuietAround(Chunk ch)
        {
            if (!Quiet(ch)) return false;
            for (int d = 0; d < 4; d++)
            {
                var n = Neighbour(ch, d);
                bool active = n != null && n.Active;
                if (active != ch.SnapNeighbourActive[d]) return false;
                if (active && !Quiet(n)) return false;
            }
            return true;
        }

        // With a thick liquid in the rules (Rules.Liquids.cs) it must have stood that many passes, so
        // the stillness spans one of its turns; with water alone, one pass, as before liquids.
        bool Quiet(Chunk ch)
        {
            int k = StillTicks();
            return ch.UnchangedFor >= k && ch.LastFlowTick < Tick - k;
        }

        static Chunk Neighbour(Chunk ch, int d) => d switch
        {
            0 => ch.North,
            1 => ch.East,
            2 => ch.South,
            _ => ch.West,
        };

        // Water moved in this chunk this tick: it and the chunks around it are disturbed.
        void WaterMoved(Chunk ch)
        {
            if (ch.LastFlowTick == Tick) return;
            ch.LastFlowTick = Tick;
            ch.WaterDisturbed = true;
            for (int d = 0; d < 4; d++)
            {
                var n = Neighbour(ch, d);
                if (n != null) n.WaterDisturbed = true;
            }
        }

        // Whether the water pass may skip this chunk's rows now.
        static bool Skips(Chunk ch) => ch.WaterStill && !ch.WaterDisturbed;

        /// <summary>After a rule switch the terrain's meaning may have changed: no snapshot holds.</summary>
        void ForgetWaterSnapshots()
        {
            foreach (var ch in chunks.Values) ch.SnapValid = false;
        }
    }
}
