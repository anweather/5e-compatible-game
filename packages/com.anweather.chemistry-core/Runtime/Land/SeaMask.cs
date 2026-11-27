// Where a map's land is under its sea level (docs/design/maps-as-data.md, phase 7): the sea is the low land joined to the
// map's edge through more low land; a hollow below the level that doesn't reach the edge is a basin, a lake's or a
// stamped cove's. The view draws its sea, its shore map and its currents from this, so a cove cut into a map's land
// shows as water; nothing here decides shared state.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class SeaMask
    {
        const byte Dry = 0, Sea = 1, Basin = 2;

        public int W { get; }
        public int H { get; }

        readonly int chunksW;
        readonly byte[] uniform;
        readonly byte[][] mixed;

        public int SeaCells { get; }
        public int BasinCells { get; }

        readonly bool offMapSea;

        SeaMask(int w, int h, byte[] uniform, byte[][] mixed, int seaCells, int basinCells, bool offMapSea)
        {
            W = w;
            H = h;
            chunksW = (w + Chunks.Size - 1) / Chunks.Size;
            this.uniform = uniform;
            this.mixed = mixed;
            SeaCells = seaCells;
            BasinCells = basinCells;
            this.offMapSea = offMapSea;
        }

        SeaMask(int w, int h, byte[] kind)
        {
            W = w;
            H = h;
            foreach (byte k in kind)
            {
                if (k == Sea) SeaCells++;
                else if (k == Basin) BasinCells++;
            }
            offMapSea = SeaCells > 0;
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

        byte KindAt(int x, int y)
        {
            int c = (y >> Chunks.Shift) * chunksW + (x >> Chunks.Shift);
            var cells = mixed[c];
            return cells == null ? uniform[c] : cells[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
        }

        internal bool UniformAround(int cx, int cy, bool basins, out bool wet)
        {
            wet = false;
            int chunksH = uniform.Length / chunksW;
            bool? seen = null;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= chunksW || y >= chunksH) continue;
                    int c = y * chunksW + x;
                    if (mixed[c] != null) return false;
                    bool w = uniform[c] == Sea || (basins && uniform[c] == Basin);
                    if (seen.HasValue && seen.Value != w) return false;
                    seen = w;
                }
            wet = seen ?? false;
            return true;
        }

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

        public static SeaMask Of(int[] land, int w, int h, int seaLevel)
        {
            if (land == null || land.Length != w * h) throw new ArgumentException($"the land is not {w} × {h}", nameof(land));
            var kind = new byte[w * h];
            for (int i = 0; i < kind.Length; i++) kind[i] = land[i] < seaLevel ? Basin : Dry;
            return Flooded(kind, w, h);
        }

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

        public static SeaMask OfKnown(ChunkGen gen)
        {
            if (gen == null) throw new ArgumentNullException(nameof(gen));
            int cw = gen.ChunksW, ch = gen.ChunksH, w = gen.CellsW, h = gen.CellsH;
            var uniform = new byte[cw * ch];
            var mixed = new byte[cw * ch][];
            var sea = new int[cw * ch];
            var basin = new int[cw * ch];
            System.Threading.Tasks.Parallel.For(0, cw * ch, c =>
            {
                var (kind, cells, seaHere, basinHere) = OfKnownChunk(gen.Know(c % cw, c / cw), Math.Min(Chunks.Size, w - c % cw * Chunks.Size), Math.Min(Chunks.Size, h - c / cw * Chunks.Size));
                uniform[c] = kind;
                mixed[c] = cells;
                sea[c] = seaHere;
                basin[c] = basinHere;
            });
            int seaCells = 0, basinCells = 0;
            for (int c = 0; c < sea.Length; c++)
            {
                seaCells += sea[c];
                basinCells += basin[c];
            }
            return new SeaMask(w, h, uniform, mixed, seaCells, basinCells, offMapSea: false);
        }

        static (byte kind, byte[] cells, int sea, int basin) OfKnownChunk(KnownChunk k, int cols, int rows)
        {
            var kinds = new byte[KnownChunk.Side * KnownChunk.Side];
            bool same = true;
            for (int q = 0; q < kinds.Length; q++)
            {
                kinds[q] = KindOf(k.Water[q]);
                same &= kinds[q] == kinds[0];
            }
            if (same) return (kinds[0], null, kinds[0] == Sea ? cols * rows : 0, kinds[0] == Basin ? cols * rows : 0);
            var (cells, sea, basin) = Mixed(kinds, cols, rows);
            return (kinds[0], cells, sea, basin);
        }

        static (byte[] cells, int sea, int basin) Mixed(byte[] kinds, int cols, int rows)
        {
            var cells = new byte[Chunks.Area];
            int sea = 0, basin = 0;
            for (int ly = 0; ly < Chunks.Size; ly++)
                for (int lx = 0; lx < Chunks.Size; lx++)
                {
                    int i = (lx + KnownChunk.Step / 2) / KnownChunk.Step, j = (ly + KnownChunk.Step / 2) / KnownChunk.Step;
                    byte kind = kinds[j * KnownChunk.Side + i];
                    cells[ly * Chunks.Size + lx] = kind;
                    bool on = lx < cols && ly < rows;
                    if (on && kind == Sea) sea++;
                    else if (on && kind == Basin) basin++;
                }
            return (cells, sea, basin);
        }

        static byte KindOf(byte water)
        {
            if (water == KnownChunk.Sea) return Sea;
            return water == KnownChunk.Lake ? Basin : Dry;
        }

        internal static SeaMask OfLow(byte[] low, int w, int h)
        {
            for (int i = 0; i < low.Length; i++) low[i] = low[i] != 0 ? Basin : Dry;
            return Flooded(low, w, h);
        }

        static SeaMask Flooded(byte[] kind, int w, int h)
        {
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

        public bool IsSea(int x, int y) => In(x, y) ? KindAt(x, y) == Sea : offMapSea;

        public bool IsBasin(int x, int y) => In(x, y) && KindAt(x, y) == Basin;

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

        public SeaNear NearByChunk(int reach, bool basins) => new SeaNear(this, reach, basins);

        bool[] Grow(bool[] wet, int reach, bool alongRows) => Grow(wet, W, H, reach, alongRows);

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

        public int Chunks => made.Count;

        public bool this[int x, int y]
        {
            get
            {
                if (x < 0 || y < 0 || x >= sea.W || y >= sea.H) return false;
                int cx = Chem.Chunks.ChunkOf(x), cy = Chem.Chunks.ChunkOf(y);
                if (reach <= Chem.Chunks.Size && sea.UniformAround(cx, cy, basins, out bool wet)) return wet;
                long key = Chem.Chunks.Key(cx, cy);
                if (!made.TryGetValue(key, out var cells))
                    made[key] = cells = sea.NearIn(cx * Chem.Chunks.Size, cy * Chem.Chunks.Size, Chem.Chunks.Size, Chem.Chunks.Size, reach, basins);
                return cells[(y - cy * Chem.Chunks.Size) * Chem.Chunks.Size + x - cx * Chem.Chunks.Size];
            }
        }
    }
}
