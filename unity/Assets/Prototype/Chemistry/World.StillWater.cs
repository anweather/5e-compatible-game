// Still water costs nothing (docs/WORLD_STREAMING.md, "Part 2, second slice",
// slice 6): a lake or the sea at rest is worked every tick (each wet cell
// looks at its four neighbours for somewhere lower), only to find nowhere.
// Before each water pass, every active chunk compares what the pass reads of
// its cells (water, height, frozen, terrain) with how they were at the last
// pass. A chunk is still when nothing changed in it or its four neighbours,
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

        // Before the water pass: which of chunks [from, to) changed since the last pass.
        static void CheckWater(Chunk[] chunks, int from, int to)
        {
            for (int k = from; k < to; k++) CheckWater(chunks[k]);
        }

        // Whether a chunk changed since the last pass, refreshing its snapshot.
        static void CheckWater(Chunk ch)
        {
            int n = ch.Cells.Length;
            if (ch.SnapWater == null)
            {
                ch.SnapWater = new int[n];
                ch.SnapHeight = new int[n];
                ch.SnapFrozen = new bool[n];
                ch.SnapT = new string[n];
            }
            bool same = ch.SnapValid;
            var cells = ch.Cells;
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                if (c == null) continue;
                if (same && (ch.SnapWater[i] != c.Water || ch.SnapHeight[i] != c.Height || ch.SnapFrozen[i] != c.Frozen || !ReferenceEquals(ch.SnapT[i], c.T)))
                    same = false;
                ch.SnapWater[i] = c.Water;
                ch.SnapHeight[i] = c.Height;
                ch.SnapFrozen[i] = c.Frozen;
                ch.SnapT[i] = c.T;
            }
            ch.Unchanged = same;
            ch.SnapValid = true;
            ch.WaterDisturbed = false;
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

        bool Quiet(Chunk ch) => ch.Unchanged && ch.LastFlowTick < Tick - 1;

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
