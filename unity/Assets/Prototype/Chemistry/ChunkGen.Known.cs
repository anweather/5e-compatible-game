// A known chunk (docs/design/world-squares.md section 3): decided, not built. Its dominant biome and its land on a 9 × 9
// grid, every 8th cell from its north-west corner to the next chunk's (so two chunks side by side share their edge
// samples and their meshes meet), with the landmarks' stamps in it and the water at each sample. The same land the chunk
// gets when it's made, read at 81 cells instead of 4096: a pure function of the seed, the plan and its coordinates, safe
// from any thread. The view draws it far off behind haze; nothing shared reads it.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A known chunk: its dominant biome and coarse land, for the view's far ring.</summary>
    public sealed class KnownChunk
    {
        /// <summary>Samples a side, and cells between two samples.</summary>
        public const int Side = 9, Step = ChunkGen.Size / (Side - 1);
        public const byte Dry = 0, Sea = 1, River = 2, Lake = 3;

        public int X, Y;
        /// <summary>The biome most of its samples are (a biomes.json id), or the pinned map's name.</summary>
        public string Biome;
        /// <summary>Land units (0.125 m) at cell (8i, 8j) from its north-west corner, row-major, <see cref="Side"/> a row.</summary>
        public int[] Heights = new int[Side * Side];
        /// <summary>What water stands at each sample: <see cref="Dry"/>, <see cref="Sea"/>, <see cref="River"/> or <see cref="Lake"/>.</summary>
        public byte[] Water = new byte[Side * Side];

        /// <summary>Whether every sample is open sea.</summary>
        public bool IsSea => Array.TrueForAll(Water, w => w == Sea);
    }

    public sealed partial class ChunkGen
    {
        /// <summary>Map chunk (<paramref name="cx"/>, <paramref name="cy"/>) as a known chunk: safe from any thread.</summary>
        public KnownChunk Know(int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= ChunksW || cy >= ChunksH) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the map");
            var k = new KnownChunk { X = cx, Y = cy };
            int x0 = cx * Size, y0 = cy * Size;
            var near = Nearby(x0, y0, x0 + Size, y0 + Size);
            var counts = new Dictionary<int, int>();
            // The landmarks' stamps whose footprint reaches it.
            var local = new List<LandStamp>();
            var localX = new List<int>();
            foreach (var (s, _, _) in stamps)
            {
                int reach = s.Bound, sx = StampX(s, x0);
                if (sx + reach < x0 || sx - reach > x0 + Size || s.Y + reach < y0 || s.Y - reach > y0 + Size) continue;
                local.Add(s);
                localX.Add(sx);
            }
            for (int j = 0; j < KnownChunk.Side; j++)
                for (int i = 0; i < KnownChunk.Side; i++)
                {
                    // The east edge's samples of the last column are the first column's west edge, round a world that wraps.
                    int x = WrapX ? x0 + i * KnownChunk.Step : Math.Min(CellsW - 1, x0 + i * KnownChunk.Step);
                    int y = Math.Min(CellsH - 1, y0 + j * KnownChunk.Step), q = j * KnownChunk.Side + i, xw = WrapCell(x);
                    if (InPinned(xw, y))
                    {
                        int at = (y - PinnedY) * Square + xw - PinnedX;
                        char cell = Pinned.Rows[y - PinnedY][xw - PinnedX];
                        k.Heights[q] = Pinned.Land[at];
                        k.Water[q] = Pinned.Land[at] < SeaLevel ? KnownChunk.Sea : WaterOf(cell);
                        continue;
                    }
                    var p = Point(x, y, near, out var b);
                    int h = p.Land;
                    for (int t = 0; t < local.Count; t++) h += local[t].At(x - localX[t], y - local[t].Y);
                    k.Heights[q] = h;
                    k.Water[q] = WaterOf(p.Wet, h);
                    int dom = b.Dominant;
                    counts[dom] = (counts.TryGetValue(dom, out int n) ? n : 0) + 1;
                }
            if (IsPinned(cx, cy)) k.Biome = Pinned.Map;
            else
            {
                // The most samples, the lower row on a tie, as a made chunk names its biome.
                int top = -1, most = 0;
                foreach (var c in counts)
                    if (c.Value > most || (c.Value == most && c.Key < top))
                    {
                        top = c.Key;
                        most = c.Value;
                    }
                k.Biome = table.Rows[top].Id;
            }
            return k;
        }

        // A generated cell's water, as its cell says (CellOf): the sea's edge above the sea level is a beach.
        static byte WaterOf(byte wet, int h)
        {
            if (wet == WetSea) return h < SeaLevel ? KnownChunk.Sea : KnownChunk.Dry;
            if (wet == WetRiver) return KnownChunk.River;
            return wet == WetLake ? KnownChunk.Lake : KnownChunk.Dry;
        }

        // A pinned cell's water above the sea level.
        static byte WaterOf(char cell)
        {
            if (cell == Stream) return KnownChunk.River;
            return cell == Pond ? KnownChunk.Lake : KnownChunk.Dry;
        }

        bool InPinned(int x, int y) => Pinned != null && x >= PinnedX && y >= PinnedY && x < PinnedX + Square && y < PinnedY + Square;
    }
}
