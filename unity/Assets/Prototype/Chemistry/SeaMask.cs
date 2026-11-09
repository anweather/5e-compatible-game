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

        // Spreads each true cell `reach` cells either way along the rows, or along the columns.
        bool[] Grow(bool[] wet, int reach, bool alongRows)
        {
            var grown = new bool[wet.Length];
            int lines = alongRows ? H : W, along = alongRows ? W : H, step = alongRows ? 1 : W, lineStep = alongRows ? W : 1;
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
}
