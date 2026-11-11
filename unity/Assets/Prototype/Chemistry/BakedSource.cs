// A baked map's cells as the World's source (D17, docs/design/sparse-world.md, "The source for the real maps"): the
// map's cells.txt, one legend character a cell, held by chunk (a chunk all one character, the sea or a meadow, is that
// one byte), and turned into a cell by the map legend. The legend lived in the view (ChemGrid.PaintCell) until the
// sparse World: a cell made here is exactly the one painting its character made, under the rules the map is made with.
// Pure and read-only once made, so a packed chunk is read from it cell by cell (World.Pack.cs).
using System;
using System.Collections.Generic;
using System.IO;

namespace Chem
{
    /// <summary>A baked map's cells, by chunk: what the World makes every cell from before play, and packs chunks against.</summary>
    public sealed class BakedSource : IChunkSource
    {
        /// <summary>
        /// The map legend: one character a cell, the same as the web sandbox's scenarios. Its terrain, water, ice and cover,
        /// laid as World.PaintLegend lays them; any other character is dirt ('.').
        /// </summary>
        public static readonly IReadOnlyDictionary<char, (string t, int water, bool frozen, string cover)> Legend = new Dictionary<char, (string, int, bool, string)>
        {
            ['%'] = ("bedrock", 0, false, null),
            ['#'] = ("stone", 0, false, null),
            ['.'] = ("dirt", 0, false, null),
            [','] = ("grass", 0, false, null),
            ['"'] = ("brush", 0, false, null),
            ['T'] = ("tree", 0, false, null),
            ['='] = ("wood", 0, false, null),
            ['_'] = ("pit", 0, false, null),
            ['w'] = ("pit", 4, false, null),
            ['*'] = ("pit", 4, true, null),
            ['r'] = ("river", 4, false, null),
            ['~'] = ("dirt", 2, false, null),
            ['m'] = ("metal", 0, false, null),
            ['o'] = ("dirt", 0, false, "oil"),
            ['@'] = ("grass", 0, false, null),
            ['L'] = ("logwall", 0, false, null),
            ['P'] = ("pane", 0, false, null),
        };

        // A legend character's cell under the rules, as painting it makes it.
        readonly struct Made
        {
            public readonly string T, Cover;
            public readonly int Water;
            public readonly bool Frozen;

            public Made(Cell c)
            {
                T = c.T;
                Cover = c.Cover;
                Water = c.Water;
                Frozen = c.Frozen;
            }
        }

        readonly Made[] made = new Made[256];
        readonly int cw;
        // Each chunk's legend bytes (row-major, local index), or null when it's all one: then uniform holds it.
        readonly byte[][] blocks;
        readonly byte[] uniform;

        /// <summary>The map's size in cells.</summary>
        public int W { get; }
        public int H { get; }

        /// <summary>Where the map's '@' is (the last one, row by row), or (0, 0) without one.</summary>
        public (int x, int y) Start { get; }

        /// <summary>The map's cells by their FNV-1a, so a save of one map is never loaded over another.</summary>
        public string Version { get; }

        /// <summary>What it holds: the chunks' bytes, a chunk all one character a byte.</summary>
        public long Bytes { get; }

        /// <summary>The chunks all one character, of all of them.</summary>
        public int UniformChunks { get; }

        BakedSource(Rules rules, Rows rows)
        {
            W = rows.W;
            H = rows.H;
            Start = rows.Start;
            Version = "baked:" + rows.Hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            cw = Chunks.ChunkOf(W - 1) + 1;
            int ch = Chunks.ChunkOf(H - 1) + 1;
            blocks = new byte[cw * ch][];
            uniform = new byte[cw * ch];
            long bytes = 0;
            int one = 0;
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    int k = cy * cw + cx;
                    var b = cx < rows.Bands[cy].Count ? rows.Bands[cy][cx] : null;
                    if (b == null || IsUniform(b))
                    {
                        uniform[k] = b == null ? (byte)'.' : b[0];
                        one++;
                        bytes++;
                    }
                    else
                    {
                        blocks[k] = b;
                        bytes += b.Length;
                    }
                }
            Bytes = bytes;
            UniformChunks = one;
            var paint = new World(rules, 1, 1);
            for (int c = 0; c < 256; c++)
            {
                paint.At(0, 0).Reset();
                paint.PaintLegend(0, 0, (char)c);
                made[c] = new Made(paint.At(0, 0));
            }
        }

        static bool IsUniform(byte[] b)
        {
            for (int i = 1; i < b.Length; i++) if (b[i] != b[0]) return false;
            return true;
        }

        /// <summary>A map's cells from their text (rows top to bottom, one legend character a cell), under the rules it's made with.</summary>
        public static BakedSource FromText(string map, Rules rules)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var rows = new Rows();
            // A line at a time where it lies in the text, never the text split into rows (a big map's is 40 M characters).
            for (var rest = map.AsSpan(); ;)
            {
                int end = rest.IndexOf('\n');
                var line = end < 0 ? rest : rest.Slice(0, end);
                if (line.IndexOf('\r') >= 0) rows.Add(line.ToString().Replace("\r", ""));
                else rows.Add(line);
                if (end < 0) break;
                rest = rest.Slice(end + 1);
            }
            return new BakedSource(rules ?? throw new ArgumentNullException(nameof(rules)), rows.Finish());
        }

        /// <summary>A map folder's cells.txt, read a row at a time so the whole text is never held.</summary>
        public static BakedSource Read(string worldDir, Rules rules)
        {
            var rows = new Rows();
            using (var text = new StreamReader(Path.Combine(worldDir, MapFile.CellsName)))
                for (string row; (row = text.ReadLine()) != null;) rows.Add(row.Replace("\r", ""));
            return new BakedSource(rules ?? throw new ArgumentNullException(nameof(rules)), rows.Finish());
        }

        public void Make(int x, int y, Cell cell)
        {
            int k = (y >> Chunks.Shift) * cw + (x >> Chunks.Shift);
            var b = blocks[k];
            var m = made[b == null ? uniform[k] : b[(Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x)]];
            cell.T = m.T;
            cell.Water = m.Water;
            cell.Frozen = m.Frozen;
            if (m.Cover != null) cell.Cover = m.Cover;
        }

        /// <summary>
        /// The map's rows as they come, into a band of chunks at a time (64 rows): blank rows at either end left off, as the
        /// map's text is trimmed, a short row's end dirt.
        /// </summary>
        sealed class Rows
        {
            public readonly List<List<byte[]>> Bands = new List<List<byte[]>>();
            public int W, H;
            public (int x, int y) Start;
            public uint Hash = 0x811c9dc5;
            int blank;

            public void Add(ReadOnlySpan<char> row)
            {
                if (row.Length == 0)
                {
                    // Kept only if a row follows it (the text is trimmed of blank rows at its ends).
                    if (H > 0) blank++;
                    return;
                }
                for (; blank > 0; blank--) Put("");
                Put(row);
            }

            void Put(ReadOnlySpan<char> row)
            {
                int y = H++;
                if (y > 0) Mix('\n');
                if (Chunks.LocalOf(y) == 0) Bands.Add(new List<byte[]>());
                var band = Bands[Bands.Count - 1];
                W = Math.Max(W, row.Length);
                for (int x = 0; x < row.Length; x++)
                {
                    char c = row[x];
                    Mix(c);
                    if (c == '@') Start = (x, y);
                    int cx = Chunks.ChunkOf(x);
                    while (band.Count <= cx) band.Add(Dirt());
                    band[cx][(Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x)] = c < 256 ? (byte)c : (byte)'.';
                }
            }

            void Mix(char c) => Hash = (Hash ^ c) * 0x01000193;

            static byte[] Dirt()
            {
                var b = new byte[Chunks.Area];
                Array.Fill(b, (byte)'.');
                return b;
            }

            public Rows Finish()
            {
                if (H == 0 || W == 0) throw new FormatException("A map needs at least one cell");
                return this;
            }
        }
    }

    public sealed partial class World
    {
        /// <summary>
        /// Lays one map legend character on a cell (BakedSource.Legend; any other character is dirt), as a map's cells are
        /// painted: its terrain, then its water, ice and cover, each only where the rules let it lie.
        /// </summary>
        public void PaintLegend(int x, int y, char ch)
        {
            if (!BakedSource.Legend.TryGetValue(ch, out var spec)) spec = BakedSource.Legend['.'];
            Paint(x, y, spec.t);
            if (spec.water > 0) AddWater(x, y, spec.water);
            if (spec.frozen) At(x, y).Frozen = true;
            if (spec.cover != null) SetCover(x, y, spec.cover);
        }
    }
}
