// world/land.json: the valley's land under each cell in water units, baked once
// from the terrain (Prototype > Bake Valley Land) and committed, so every peer
// on every platform reads the same integers (lockstep: floats sampled from each
// machine's own terrain could differ by a unit). World.SetLand takes it.
// The numbers are row-major, each written as its difference from the cell
// before it (the row's first from the row above's first), zigzag varint bytes,
// deflated and base64'd: a smooth valley packs small.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class LandFile
    {
        public const int Version = 1;

        /// <summary>The land as land.json's text.</summary>
        public static string Write(int[] land, int w, int h, double unitMetres)
        {
            if (land.Length != w * h) throw new ArgumentException($"land needs {w * h} cells, got {land.Length}");
            using var text = new MemoryStream();
            using (var rows = new Writer(text, w, h, unitMetres))
            {
                for (int y = 0; y < h; y++) rows.Row(new ReadOnlySpan<int>(land, y * w, w));
                rows.Finish();
            }
            return new UTF8Encoding(false).GetString(text.ToArray());
        }

        /// <summary>
        /// land.json written a row at a time, north to south, so a map's land is never held whole (a bake's, bake-lean):
        /// the same bytes as <see cref="Write"/>. <see cref="Finish"/> after the last row.
        /// </summary>
        public sealed class Writer : IDisposable
        {
            // Where the data goes in the file's JSON: the rest is written by Newtonsoft as Write's always was.
            const string Hole = "@land-data@";
            readonly Stream to;
            readonly string tail;
            readonly Base64Out base64;
            readonly DeflateStream deflate;
            readonly int w, h;
            readonly byte[] packed;
            int y, above;

            public Writer(Stream to, int w, int h, double unitMetres)
            {
                this.to = to;
                this.w = w;
                this.h = h;
                var o = new JObject
                {
                    ["version"] = Version,
                    ["w"] = w,
                    ["h"] = h,
                    ["unitMetres"] = unitMetres,
                    ["data"] = Hole,
                };
                string json = o.ToString(Newtonsoft.Json.Formatting.Indented) + "\n";
                int at = json.IndexOf(Hole, StringComparison.Ordinal);
                Ascii(to, json.Substring(0, at));
                tail = json.Substring(at + Hole.Length);
                base64 = new Base64Out(to);
                deflate = new DeflateStream(base64, CompressionLevel.Optimal, true);
                packed = new byte[w * 5]; // a varint is 5 bytes at most
            }

            /// <summary>The next row's <c>w</c> cells.</summary>
            public void Row(ReadOnlySpan<int> row)
            {
                if (row.Length != w) throw new ArgumentException($"a row of land needs {w} cells, got {row.Length}");
                if (y >= h) throw new InvalidOperationException($"land.json is {h} rows");
                int n = 0, before = above;
                for (int x = 0; x < w; x++)
                {
                    // Each cell as its difference from the one before it, the row's first from the row above's first.
                    n = PutVarint(packed, n, Zigzag(row[x] - before));
                    before = row[x];
                }
                above = row[0];
                y++;
                deflate.Write(packed, 0, n);
            }

            /// <summary>Ends the data and the file; every row written.</summary>
            public void Finish()
            {
                if (y != h) throw new InvalidOperationException($"land.json has {y} of its {h} rows");
                deflate.Dispose();
                base64.Finish();
                Ascii(to, tail);
            }

            public void Dispose()
            {
                deflate.Dispose();
            }

            static uint Zigzag(int v) => (uint)((v << 1) ^ (v >> 31));

            static int PutVarint(byte[] into, int at, uint v)
            {
                while (v >= 0x80)
                {
                    into[at++] = (byte)(v | 0x80);
                    v >>= 7;
                }
                into[at++] = (byte)v;
                return at;
            }

            static void Ascii(Stream s, string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>The land in land.json's text, for a w × h world; empty if it's for another size.</summary>
        public static int[] Read(string json, int w, int h)
        {
            var o = JObject.Parse(json);
            if ((int?)o["version"] != Version || (int?)o["w"] != w || (int?)o["h"] != h) return Array.Empty<int>();
            var land = new int[w * h];
            // Inflated a buffer at a time: a byte at a time from the DeflateStream took 3.6 s for the 1728 x 1728 test map
            // (cell-memory), and inflating it whole held a big map's every byte twice over (the sparse World's guard).
            using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String((string)o["data"])), CompressionMode.Decompress))
            {
                var bytes = new Inflated(deflate);
                for (int k = 0; k < land.Length; k++)
                    land[k] = Previous(land, k, w) + Unzigzag(bytes.Varint());
            }
            return land;
        }

        // The inflated bytes in a buffer's worth at a time.
        internal sealed class Inflated
        {
            readonly Stream from;
            readonly byte[] buffer = new byte[1 << 16];
            int at, have;

            public Inflated(Stream from) => this.from = from;

            public uint Varint()
            {
                uint v = 0;
                int shift = 0, b = 0x80;
                while (b >= 0x80)
                {
                    if (at == have)
                    {
                        have = from.Read(buffer, 0, buffer.Length);
                        at = 0;
                        if (have <= 0) throw new EndOfStreamException("land.json ends early");
                    }
                    b = buffer[at++];
                    v |= (uint)(b & 0x7f) << shift;
                    shift += 7;
                }
                return v;
            }
        }

        static int Previous(int[] land, int k, int w)
        {
            if (k == 0) return 0;
            return k % w == 0 ? land[k - w] : land[k - 1];
        }

        internal static int Unzigzag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);
    }

    // Bytes in, their base64 out to a stream, as Convert.ToBase64String of them all would give it.
    internal sealed class Base64Out : Stream
    {
        readonly Stream to;
        readonly byte[] left = new byte[3];
        int held;

        public Base64Out(Stream to) => this.to = to;

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0 && held > 0 && held < 3)
            {
                left[held++] = buffer[offset++];
                count--;
            }
            if (held == 3)
            {
                Put(left, 0, 3);
                held = 0;
            }
            int whole = count / 3 * 3;
            if (whole > 0) Put(buffer, offset, whole);
            for (int k = whole; k < count; k++) left[held++] = buffer[offset + k];
        }

        /// <summary>The last one or two bytes, padded.</summary>
        public void Finish()
        {
            if (held > 0) Put(left, 0, held);
            held = 0;
        }

        void Put(byte[] bytes, int offset, int count)
        {
            var chars = Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes, offset, count));
            to.Write(chars, 0, chars.Length);
        }

        public override void Flush() => to.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>
    /// A map's land (land.json, an int a cell in land units) held a chunk at a time, each chunk's as LandCodec's bytes:
    /// a byte or so a cell where it's smooth, a few bytes for a chunk of sea or flat land (core-mem-scale; on the Scale
    /// Island 162 MB as an int[], a few MB this way). Read from land.json a band of chunk rows at a time, never whole.
    /// Reading a cell decodes its chunk into a buffer kept for the next read: one reader at a time.
    /// </summary>
    public sealed class LandChunks
    {
        readonly int chunksW, chunksH;
        readonly byte[][] chunks;
        readonly int[] cached = new int[Chunks.Area];
        int cachedChunk = -1;

        LandChunks(int w, int h)
        {
            W = w;
            H = h;
            chunksW = (w + Chunks.Size - 1) / Chunks.Size;
            chunksH = (h + Chunks.Size - 1) / Chunks.Size;
            chunks = new byte[chunksW * chunksH][];
        }

        public int W { get; }
        public int H { get; }

        /// <summary>The bytes it holds: its chunks' encodings.</summary>
        public long Bytes
        {
            get
            {
                long b = 0;
                foreach (var c in chunks) b += (c?.Length ?? 0) + 24;
                return b;
            }
        }

        /// <summary>As LandFile.Read reads it, a chunk at a time; null where Read gives an empty array (another version or size).</summary>
        public static LandChunks Read(string json, int w, int h) => Read(json, w, h, null, 0);

        /// <summary>
        /// Read, and the sea mask of the land at <paramref name="seaLevel"/> from the same pass (SeaMask.Of without reading the
        /// land a second time); null for both where Read gives null.
        /// </summary>
        public static LandChunks Read(string json, int w, int h, int seaLevel, out SeaMask sea)
        {
            var low = new byte[(long)w * h];
            var land = Read(json, w, h, low, seaLevel);
            sea = land == null ? null : SeaMask.OfLow(low, w, h);
            return land;
        }

        // The reading, a band of rows at a time, each encoded as it's finished. With `low`, a byte a cell marks the land
        // under the sea level as it's decoded, so the sea mask needn't read the land again.
        static LandChunks Read(string json, int w, int h, byte[] low, int seaLevel)
        {
            var o = JObject.Parse(json);
            if ((int?)o["version"] != LandFile.Version || (int?)o["w"] != w || (int?)o["h"] != h) return null;
            var land = new LandChunks(w, h);
            var band = new int[Chunks.Size * w];
            var cell = new int[Chunks.Area];
            var scratch = new byte[LandCodec.MostBytes];
            int rowStart = 0; // the first cell of the row above: 0 above the top row
            using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String((string)o["data"])), CompressionMode.Decompress))
            {
                var bytes = new LandFile.Inflated(deflate);
                for (int y = 0; y < h; y++)
                {
                    int r = (y & Chunks.Mask) * w;
                    for (int x = 0; x < w; x++)
                    {
                        // LandFile's prediction: the cell before in the row, or at a row's start the row above's first (0 at the top).
                        int previous = x > 0 ? band[r + x - 1] : rowStart;
                        band[r + x] = previous + LandFile.Unzigzag(bytes.Varint());
                        if (low != null && band[r + x] < seaLevel) low[(long)y * w + x] = 1;
                    }
                    rowStart = band[r];
                    if ((y & Chunks.Mask) == Chunks.Mask || y == h - 1) land.KeepBand(y >> Chunks.Shift, band, cell, scratch);
                }
            }
            return land;
        }

        /// <summary>The same land from an int a cell (row by row, w × h).</summary>
        public static LandChunks Of(int[] units, int w, int h)
        {
            if (units == null || units.Length != w * h) throw new ArgumentException($"land needs {w * h} cells", nameof(units));
            var land = new LandChunks(w, h);
            var band = new int[Chunks.Size * w];
            for (int cy = 0; cy < land.chunksH; cy++)
            {
                int rows = Math.Min(Chunks.Size, h - cy * Chunks.Size);
                Array.Copy(units, cy * Chunks.Size * w, band, 0, rows * w);
                land.KeepBand(cy, band);
            }
            return land;
        }

        // A band of chunk rows (Size rows of W cells, those past the map's foot left as they are) into its chunks' bytes.
        // A band's chunks into their bytes, one after another, through the buffers given.
        void KeepBand(int cy, int[] band, int[] cell, byte[] scratch)
        {
            int rows = Math.Min(Chunks.Size, H - cy * Chunks.Size);
            for (int cx = 0; cx < chunksW; cx++)
            {
                Array.Clear(cell, 0, cell.Length);
                int cols = Math.Min(Chunks.Size, W - cx * Chunks.Size);
                for (int ly = 0; ly < rows; ly++) Array.Copy(band, ly * W + cx * Chunks.Size, cell, ly * Chunks.Size, cols);
                chunks[cy * chunksW + cx] = LandCodec.Encode(cell, scratch);
            }
            cachedChunk = -1;
        }

        void KeepBand(int cy, int[] band) => KeepBand(cy, band, new int[Chunks.Area], new byte[LandCodec.MostBytes]);

        /// <summary>A chunk's land, Area ints by local index (row by row in the chunk); 0 past the map's edge.</summary>
        public void ChunkInto(int cx, int cy, int[] into)
        {
            if (cx < 0 || cy < 0 || cx >= chunksW || cy >= chunksH) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the land");
            LandCodec.Decode(chunks[cy * chunksW + cx], into);
        }

        /// <summary>The land at a cell.</summary>
        public int At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the land");
            int c = (y >> Chunks.Shift) * chunksW + (x >> Chunks.Shift);
            if (c != cachedChunk)
            {
                LandCodec.Decode(chunks[c], cached);
                cachedChunk = c;
            }
            return cached[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
        }

        /// <summary>A band of chunk rows, row by row: Size rows of W cells (those past the map's foot left as they were).</summary>
        public void Band(int cy, int[] into)
        {
            var cell = new int[Chunks.Area];
            int rows = Math.Min(Chunks.Size, H - cy * Chunks.Size);
            for (int cx = 0; cx < chunksW; cx++)
            {
                ChunkInto(cx, cy, cell);
                int cols = Math.Min(Chunks.Size, W - cx * Chunks.Size);
                for (int ly = 0; ly < rows; ly++) Array.Copy(cell, ly * Chunks.Size, into, ly * W + cx * Chunks.Size, cols);
            }
        }

        /// <summary>The land, the same value everywhere (a map with no land.json: the sea level all over): a few bytes a chunk.</summary>
        public static LandChunks Uniform(int w, int h, int value)
        {
            var land = new LandChunks(w, h);
            var cell = new int[Chunks.Area];
            for (int i = 0; i < cell.Length; i++) cell[i] = value;
            var bytes = LandCodec.Encode(cell, new byte[LandCodec.MostBytes]);
            for (int c = 0; c < land.chunks.Length; c++) land.chunks[c] = bytes;
            return land;
        }

        /// <summary>
        /// A reader with a chunk buffer of its own, for reading from more than one thread at once (a reader each): At on the
        /// LandChunks itself keeps one buffer, so one reader at a time.
        /// </summary>
        public Reader NewReader() => new Reader(this);

        /// <summary>LandChunks.At with a buffer of its own.</summary>
        public sealed class Reader
        {
            readonly LandChunks land;
            readonly int[] buffer = new int[Chunks.Area];
            int chunk = -1;

            internal Reader(LandChunks land) => this.land = land;

            /// <summary>The land at a cell.</summary>
            public int At(int x, int y)
            {
                if (x < 0 || y < 0 || x >= land.W || y >= land.H) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the land");
                int c = (y >> Chunks.Shift) * land.chunksW + (x >> Chunks.Shift);
                if (c != chunk)
                {
                    LandCodec.Decode(land.chunks[c], buffer);
                    chunk = c;
                }
                return buffer[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
            }
        }

        /// <summary>One row of W cells.</summary>
        public void Row(int y, int[] into)
        {
            for (int x = 0; x < W; x++) into[x] = At(x, y);
        }
    }
}
