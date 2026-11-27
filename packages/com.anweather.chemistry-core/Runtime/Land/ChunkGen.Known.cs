// A known chunk (docs/design/world-squares.md section 3): decided, not built. Its dominant biome and its land on a 9 × 9
// grid, every 8th cell from its north-west corner to the next chunk's (so two chunks side by side share their edge
// samples and their meshes meet), with the landmarks' stamps in it and the water at each sample. The same land the chunk
// gets when it's made, read at 81 cells instead of 4096: a pure function of the seed, the plan and its coordinates, safe
// from any thread. The view draws it far off behind haze; nothing shared reads it.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class KnownChunk
    {
        public const int Side = 9, Step = ChunkGen.Size / (Side - 1);
        public const byte Dry = 0, Sea = 1, River = 2, Lake = 3;

        public int X, Y;
        public string Biome;
        public string[] Biomes = new string[Side * Side];
        public int[] Heights = new int[Side * Side];
        public byte[] Water = new byte[Side * Side];
        public byte[] Wood = new byte[Side * Side];

        public bool IsSea => Array.TrueForAll(Water, w => w == Sea);
    }

    public sealed partial class ChunkGen
    {
        public KnownChunk Know(int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= ChunksW || cy >= ChunksH) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the map");
            if (Hills != null) return KnowHills(cx, cy);
            if (Pinned?.Inner != null && IsPinned(cx, cy))
            {
                var own = Pinned.Inner.Know(cx - PinnedX / Size, cy - PinnedY / Size);
                own.X = cx;
                own.Y = cy;
                return own;
            }
            var k = new KnownChunk { X = cx, Y = cy };
            int x0 = cx * Size, y0 = cy * Size;
            var near = Nearby(x0, y0, x0 + Size, y0 + Size);
            var counts = new Dictionary<int, int>();
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
                    int x = WrapX ? x0 + i * KnownChunk.Step : Math.Min(CellsW - 1, x0 + i * KnownChunk.Step);
                    int y = Math.Min(CellsH - 1, y0 + j * KnownChunk.Step), q = j * KnownChunk.Side + i, xw = WrapCell(x);
                    if (InPinned(xw, y) && Pinned.Inner != null)
                    {
                        int bx = xw - PinnedX, by = y - PinnedY;
                        var edge = blockKnown[(bx / Size, by / Size)];
                        int e = (by % Size) / KnownChunk.Step * KnownChunk.Side + (bx % Size) / KnownChunk.Step;
                        k.Heights[q] = edge.Heights[e];
                        k.Water[q] = edge.Water[e];
                        k.Biomes[q] = edge.Biomes[e];
                        continue;
                    }
                    if (InPinned(xw, y))
                    {
                        int at = (y - PinnedY) * Square + xw - PinnedX;
                        char cell = Pinned.Rows[y - PinnedY][xw - PinnedX];
                        k.Heights[q] = Pinned.Land[at];
                        k.Water[q] = Pinned.Land[at] < SeaLevel ? KnownChunk.Sea : WaterOf(cell);
                        k.Biomes[q] = Pinned.Map;
                        continue;
                    }
                    var p = Point(x, y, near, out var b);
                    int h = p.Land;
                    for (int t = 0; t < local.Count; t++) h += local[t].At(x - localX[t], y - local[t].Y);
                    k.Heights[q] = h;
                    k.Water[q] = WaterOf(p.Wet, h);
                    int dom = b.Dominant;
                    k.Biomes[q] = table.Rows[dom].Id;
                    counts[dom] = (counts.TryGetValue(dom, out int n) ? n : 0) + 1;
                }
            if (IsPinned(cx, cy)) k.Biome = Pinned.Map;
            else
            {
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

        static byte WaterOf(byte wet, int h)
        {
            if (wet == WetSea) return h < SeaLevel ? KnownChunk.Sea : KnownChunk.Dry;
            if (wet == WetRiver) return KnownChunk.River;
            return wet == WetLake ? KnownChunk.Lake : KnownChunk.Dry;
        }

        static byte WaterOf(char cell)
        {
            if (cell == Stream) return KnownChunk.River;
            return cell == Pond ? KnownChunk.Lake : KnownChunk.Dry;
        }

        bool InPinned(int x, int y) => Pinned != null && x >= PinnedX && y >= PinnedY && x < PinnedX + PinnedW && y < PinnedY + PinnedH;
    }
}
