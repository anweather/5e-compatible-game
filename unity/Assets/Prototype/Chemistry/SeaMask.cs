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

        // Held a chunk at a time (core-mem-scale): a chunk all one kind (all sea, all dry: most of a map) is that byte alone;
        // only a chunk that mixes them keeps a byte a cell. On the Scale Island 38 MB a byte a cell, a few MB this way.
        readonly int chunksW;
        readonly byte[] uniform;
        readonly byte[][] mixed;

        /// <summary>How many cells are sea, and how many are in basins.</summary>
        public int SeaCells { get; }
        public int BasinCells { get; }

        SeaMask(int w, int h, byte[] kind)
        {
            W = w;
            H = h;
            foreach (byte k in kind)
            {
                if (k == Sea) SeaCells++;
                else if (k == Basin) BasinCells++;
            }
            chunksW = (w + Chunks.Size - 1) / Chunks.Size;
            int chunksH = (h + Chunks.Size - 1) / Chunks.Size;
            uniform = new byte[chunksW * chunksH];
            mixed = new byte[chunksW * chunksH][];
            for (int cy = 0; cy < chunksH; cy++)
                for (int cx = 0; cx < chunksW; cx++)
                {
                    int x0 = cx * Chunks.Size, y0 = cy * Chunks.Size;
                    byte first = kind[y0 * w + x0];
                    bool same = true;
                    for (int y = y0; y < Math.Min(h, y0 + Chunks.Size) && same; y++)
                        for (int x = x0; x < Math.Min(w, x0 + Chunks.Size); x++)
                            if (kind[y * w + x] != first)
                            {
                                same = false;
                                break;
                            }
                    int c = cy * chunksW + cx;
                    uniform[c] = first;
                    if (same) continue;
                    var cells = new byte[Chunks.Area];
                    for (int y = y0; y < Math.Min(h, y0 + Chunks.Size); y++)
                        for (int x = x0; x < Math.Min(w, x0 + Chunks.Size); x++) cells[(y - y0) * Chunks.Size + x - x0] = kind[y * w + x];
                    mixed[c] = cells;
                }
        }

        // A cell's kind, on the map.
        byte KindAt(int x, int y)
        {
            int c = (y >> Chunks.Shift) * chunksW + (x >> Chunks.Shift);
            var cells = mixed[c];
            return cells == null ? uniform[c] : cells[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
        }

        /// <summary>The bytes the mask holds now: a byte a chunk, and a byte a cell of the chunks that mix sea, basin and dry.</summary>
        public long Bytes
        {
            get
            {
                long b = uniform.Length + mixed.Length * 8L;
                foreach (var m in mixed)
                    if (m != null) b += m.Length;
                return b;
            }
        }

        /// <summary>The mask of a map's land (row by row, w × h, in land units) at its sea level: below it is water.</summary>
        public static SeaMask Of(int[] land, int w, int h, int seaLevel)
        {
            if (land == null || land.Length != w * h) throw new ArgumentException($"the land is not {w} × {h}", nameof(land));
            var kind = new byte[w * h];
            for (int i = 0; i < kind.Length; i++) kind[i] = land[i] < seaLevel ? Basin : Dry;
            return Flooded(kind, w, h);
        }

        /// <summary>The same, from land held a chunk at a time (LandChunks): read a band of rows at a time, never as an int a cell.</summary>
        public static SeaMask Of(LandChunks land, int seaLevel)
        {
            if (land == null) throw new ArgumentNullException(nameof(land));
            int w = land.W, h = land.H;
            var kind = new byte[w * h];
            var band = new int[Chunks.Size * w];
            for (int cy = 0; cy * Chunks.Size < h; cy++)
            {
                land.Band(cy, band);
                for (int ly = 0; ly < Chunks.Size && cy * Chunks.Size + ly < h; ly++)
                    for (int x = 0; x < w; x++) kind[(cy * Chunks.Size + ly) * w + x] = band[ly * w + x] < seaLevel ? Basin : Dry;
            }
            return Flooded(kind, w, h);
        }

        // Low cells (Basin) joined to the edge through low cells become sea; the rest of the low cells stay basins.
        static SeaMask Flooded(byte[] kind, int w, int h)
        {
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
        public bool IsSea(int x, int y) => In(x, y) ? KindAt(x, y) == Sea : SeaCells > 0;

        /// <summary>Whether a cell is in a basin: below the level, and walled off from the sea.</summary>
        public bool IsBasin(int x, int y) => In(x, y) && KindAt(x, y) == Basin;

        /// <summary>
        /// The cells within <paramref name="reach"/> cells (a square's half side) of water: the sea's, and the basins' too
        /// when <paramref name="basins"/>. What a surface over the water spans, so it runs up under the beach.
        /// </summary>
        public bool[] Near(int reach, bool basins)
        {
            var wet = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    byte k = KindAt(x, y);
                    wet[y * W + x] = k == Sea || (basins && k == Basin);
                }
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
                    byte k = KindAt(wx0 + x, wy0 + y);
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
