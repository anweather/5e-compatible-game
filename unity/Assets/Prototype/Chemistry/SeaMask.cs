// Where a map's land is under its sea level (docs/design/maps-as-data.md, phase 7): the sea is the low land joined to the
// map's edge through more low land; a hollow below the level that doesn't reach the edge is a basin, a lake's or a
// stamped cove's. The view draws its sea, its shore map and its currents from this, so a cove cut into a map's land
// shows as water; nothing here decides shared state.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A map's land as sea, basin or dry, cell by cell.</summary>
    public sealed class SeaMask
    {
        const byte Dry = 0, Sea = 1, Basin = 2;

        public int W { get; }
        public int H { get; }
        readonly byte[] kind;

        /// <summary>How many cells are sea, and how many are in basins.</summary>
        public int SeaCells { get; }
        public int BasinCells { get; }

        SeaMask(int w, int h, byte[] kind)
        {
            W = w;
            H = h;
            this.kind = kind;
            foreach (byte k in kind)
            {
                if (k == Sea) SeaCells++;
                else if (k == Basin) BasinCells++;
            }
        }

        /// <summary>The mask of a map's land (row by row, w × h, in land units) at its sea level: below it is water.</summary>
        public static SeaMask Of(int[] land, int w, int h, int seaLevel)
        {
            if (land == null || land.Length != w * h) throw new ArgumentException($"the land is not {w} × {h}", nameof(land));
            var kind = new byte[w * h];
            for (int i = 0; i < kind.Length; i++) kind[i] = land[i] < seaLevel ? Basin : Dry;
            // From every low cell on the edge, through the low cells beside each other: the sea.
            var open = new Queue<int>();
            void Reach(int x, int y)
            {
                int i = y * w + x;
                if (kind[i] != Basin) return;
                kind[i] = Sea;
                open.Enqueue(i);
            }
            for (int x = 0; x < w; x++)
            {
                Reach(x, 0);
                Reach(x, h - 1);
            }
            for (int y = 0; y < h; y++)
            {
                Reach(0, y);
                Reach(w - 1, y);
            }
            while (open.Count > 0)
            {
                int i = open.Dequeue(), x = i % w, y = i / w;
                if (x > 0) Reach(x - 1, y);
                if (x < w - 1) Reach(x + 1, y);
                if (y > 0) Reach(x, y - 1);
                if (y < h - 1) Reach(x, y + 1);
            }
            return new SeaMask(w, h, kind);
        }

        bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        /// <summary>Whether a cell is sea: below the level and joined to the map's edge. Off the map is sea when any edge is.</summary>
        public bool IsSea(int x, int y) => In(x, y) ? kind[y * W + x] == Sea : SeaCells > 0;

        /// <summary>Whether a cell is in a basin: below the level, and walled off from the sea.</summary>
        public bool IsBasin(int x, int y) => In(x, y) && kind[y * W + x] == Basin;

        /// <summary>
        /// The cells within <paramref name="reach"/> cells (a square's half side) of water: the sea's, and the basins' too
        /// when <paramref name="basins"/>. What a surface over the water spans, so it runs up under the beach.
        /// </summary>
        public bool[] Near(int reach, bool basins)
        {
            var wet = new bool[W * H];
            for (int i = 0; i < wet.Length; i++) wet[i] = kind[i] == Sea || (basins && kind[i] == Basin);
            return Grow(Grow(wet, reach, true), reach, false);
        }

        /// <summary>
        /// Near for a rectangle only (island-startup-2): the same answers for its cells as Near's, from the cells within
        /// reach of it, not a pass over the whole map. Row-major, w x h.
        /// </summary>
        public bool[] NearIn(int x0, int y0, int w, int h, int reach, bool basins)
        {
            int wx0 = Math.Max(0, x0 - reach), wy0 = Math.Max(0, y0 - reach);
            int wx1 = Math.Min(W - 1, x0 + w - 1 + reach), wy1 = Math.Min(H - 1, y0 + h - 1 + reach);
            var result = new bool[w * h];
            if (wx1 < wx0 || wy1 < wy0) return result;
            int ww = wx1 - wx0 + 1, wh = wy1 - wy0 + 1;
            var wet = new bool[ww * wh];
            for (int y = 0; y < wh; y++)
                for (int x = 0; x < ww; x++)
                {
                    byte k = kind[(wy0 + y) * W + wx0 + x];
                    wet[y * ww + x] = k == Sea || (basins && k == Basin);
                }
            var grown = Grow(Grow(wet, ww, wh, reach, true), ww, wh, reach, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int gx = x0 + x - wx0, gy = y0 + y - wy0;
                    if (gx >= 0 && gy >= 0 && gx < ww && gy < wh) result[y * w + x] = grown[gy * ww + gx];
                }
            return result;
        }

        /// <summary>
        /// Near, a chunk at a time as its cells are asked (island-startup-2): each chunk worked out by NearIn the first time
        /// one of its cells is, and kept; so a view asking round the players holds and works out those chunks, not the map.
        /// </summary>
        public SeaNear NearByChunk(int reach, bool basins) => new SeaNear(this, reach, basins);

        // Spreads each true cell `reach` cells either way along the rows, or along the columns.
        bool[] Grow(bool[] wet, int reach, bool alongRows) => Grow(wet, W, H, reach, alongRows);

        // Grow, over a w x h block.
        static bool[] Grow(bool[] wet, int width, int height, int reach, bool alongRows)
        {
            var grown = new bool[wet.Length];
            int lines = alongRows ? height : width, along = alongRows ? width : height, step = alongRows ? 1 : width, lineStep = alongRows ? width : 1;
            for (int line = 0; line < lines; line++)
            {
                int last = int.MinValue / 2;
                for (int k = 0; k < along; k++)
                {
                    int i = line * lineStep + k * step;
                    if (wet[i]) last = k;
                    grown[i] = k - last <= reach;
                }
                last = int.MaxValue / 2;
                for (int k = along - 1; k >= 0; k--)
                {
                    int i = line * lineStep + k * step;
                    if (wet[i]) last = k;
                    grown[i] |= last - k <= reach;
                }
            }
            return grown;
        }
    }

    /// <summary>The cells near water (SeaMask.Near), worked out a chunk at a time as they're asked, and kept.</summary>
    public sealed class SeaNear
    {
        readonly SeaMask sea;
        readonly int reach;
        readonly bool basins;
        readonly Dictionary<long, bool[]> made = new Dictionary<long, bool[]>();

        internal SeaNear(SeaMask sea, int reach, bool basins)
        {
            this.sea = sea;
            this.reach = reach;
            this.basins = basins;
        }

        /// <summary>How many chunks are worked out.</summary>
        public int Chunks => made.Count;

        /// <summary>Whether the cell is near water, as Near(reach, basins) says; false off the map.</summary>
        public bool this[int x, int y]
        {
            get
            {
                if (x < 0 || y < 0 || x >= sea.W || y >= sea.H) return false;
                int cx = Chem.Chunks.ChunkOf(x), cy = Chem.Chunks.ChunkOf(y);
                long key = Chem.Chunks.Key(cx, cy);
                if (!made.TryGetValue(key, out var cells))
                    made[key] = cells = sea.NearIn(cx * Chem.Chunks.Size, cy * Chem.Chunks.Size, Chem.Chunks.Size, Chem.Chunks.Size, reach, basins);
                return cells[(y - cy * Chem.Chunks.Size) * Chem.Chunks.Size + x - cx * Chem.Chunks.Size];
            }
        }
    }
}
