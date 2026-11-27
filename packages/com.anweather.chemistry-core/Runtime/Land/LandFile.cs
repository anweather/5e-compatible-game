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

        public sealed class Writer : IDisposable
        {
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
                packed = new byte[w * 5];
            }

            public void Row(ReadOnlySpan<int> row)
            {
                if (row.Length != w) throw new ArgumentException($"a row of land needs {w} cells, got {row.Length}");
                if (y >= h) throw new InvalidOperationException($"land.json is {h} rows");
                int n = 0, before = above;
                for (int x = 0; x < w; x++)
                {
                    n = PutVarint(packed, n, Zigzag(row[x] - before));
                    before = row[x];
                }
                above = row[0];
                y++;
                deflate.Write(packed, 0, n);
            }

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

        public static int[] Read(string json, int w, int h)
        {
            var o = JObject.Parse(json);
            if ((int?)o["version"] != Version || (int?)o["w"] != w || (int?)o["h"] != h) return Array.Empty<int>();
            var land = new int[w * h];
            using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String((string)o["data"])), CompressionMode.Decompress))
            {
                var bytes = new Inflated(deflate);
                for (int k = 0; k < land.Length; k++)
                    land[k] = Previous(land, k, w) + Unzigzag(bytes.Varint());
            }
            return land;
        }

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

    public sealed class LandChunks
    {
        readonly int chunksW, chunksH;
        readonly byte[][] chunks;
        readonly int[] cached = new int[Chunks.Area];
        int cachedChunk = -1;
        readonly Action<int, int, int[]> source;

        LandChunks(int w, int h, Action<int, int, int[]> landInto) : this(w, h)
        {
            source = landInto;
            chunks = null;
        }

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

        public static LandChunks FromSource(int w, int h, Action<int, int, int[]> landInto) => new LandChunks(w, h, landInto ?? throw new ArgumentNullException(nameof(landInto)));

        public bool IsFromSource => source != null;

        void Fill(int c, int[] into)
        {
            if (source != null) source(c % chunksW, c / chunksW, into);
            else LandCodec.Decode(chunks[c], into);
        }

        public long Bytes
        {
            get
            {
                if (chunks == null) return 0;
                long b = 0;
                foreach (var c in chunks) b += (c?.Length ?? 0) + 24;
                return b;
            }
        }

        public static LandChunks Read(string json, int w, int h) => Read(json, w, h, null, 0);

        public static LandChunks Read(string json, int w, int h, int seaLevel, out SeaMask sea)
        {
            var low = new byte[(long)w * h];
            var land = Read(json, w, h, low, seaLevel);
            sea = land == null ? null : SeaMask.OfLow(low, w, h);
            return land;
        }

        static LandChunks Read(string json, int w, int h, byte[] low, int seaLevel)
        {
            var o = JObject.Parse(json);
            if ((int?)o["version"] != LandFile.Version || (int?)o["w"] != w || (int?)o["h"] != h) return null;
            var land = new LandChunks(w, h);
            var bands = new[] { new int[Chunks.Size * w], new int[Chunks.Size * w] };
            var cell = new int[Chunks.Area];
            var scratch = new byte[LandCodec.MostBytes];
            System.Threading.Tasks.Task encoding = null;
            int which = 0;
            var band = bands[0];
            int rowStart = 0;
            using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String((string)o["data"])), CompressionMode.Decompress))
            {
                var bytes = new LandFile.Inflated(deflate);
                for (int y = 0; y < h; y++)
                {
                    int r = (y & Chunks.Mask) * w;
                    for (int x = 0; x < w; x++)
                    {
                        int previous = x > 0 ? band[r + x - 1] : rowStart;
                        band[r + x] = previous + LandFile.Unzigzag(bytes.Varint());
                        if (low != null && band[r + x] < seaLevel) low[(long)y * w + x] = 1;
                    }
                    rowStart = band[r];
                    if ((y & Chunks.Mask) == Chunks.Mask || y == h - 1)
                    {
                        encoding?.Wait();
                        int cy = y >> Chunks.Shift;
                        var done = band;
                        encoding = System.Threading.Tasks.Task.Run(() => land.KeepBand(cy, done, cell, scratch));
                        which ^= 1;
                        band = bands[which];
                    }
                }
            }
            encoding?.Wait();
            land.cachedChunk = -1;
            return land;
        }

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

        void KeepBand(int cy, int[] band, int[] cell, byte[] scratch)
        {
            int rows = Math.Min(Chunks.Size, H - cy * Chunks.Size);
            for (int cx = 0; cx < chunksW; cx++) EncodeOne(cy, cx, rows, band, cell, scratch);
            cachedChunk = -1;
        }

        void KeepBand(int cy, int[] band) => KeepBand(cy, band, new int[Chunks.Area], new byte[LandCodec.MostBytes]);

        void EncodeOne(int cy, int cx, int rows, int[] band, int[] cell, byte[] scratch)
        {
            Array.Clear(cell, 0, cell.Length);
            int cols = Math.Min(Chunks.Size, W - cx * Chunks.Size);
            for (int ly = 0; ly < rows; ly++) Array.Copy(band, ly * W + cx * Chunks.Size, cell, ly * Chunks.Size, cols);
            chunks[cy * chunksW + cx] = LandCodec.Encode(cell, scratch);
        }

        public void ChunkInto(int cx, int cy, int[] into)
        {
            if (cx < 0 || cy < 0 || cx >= chunksW || cy >= chunksH) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the land");
            Fill(cy * chunksW + cx, into);
        }

        public int At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the land");
            int c = (y >> Chunks.Shift) * chunksW + (x >> Chunks.Shift);
            if (c != cachedChunk)
            {
                Fill(c, cached);
                cachedChunk = c;
            }
            return cached[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
        }

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

        public static LandChunks Uniform(int w, int h, int value)
        {
            var land = new LandChunks(w, h);
            var cell = new int[Chunks.Area];
            for (int i = 0; i < cell.Length; i++) cell[i] = value;
            var bytes = LandCodec.Encode(cell, new byte[LandCodec.MostBytes]);
            for (int c = 0; c < land.chunks.Length; c++) land.chunks[c] = bytes;
            return land;
        }

        public Reader NewReader() => new Reader(this);

        public sealed class Reader
        {
            readonly LandChunks land;
            readonly int[] buffer = new int[Chunks.Area];
            int chunk = -1;

            internal Reader(LandChunks land) => this.land = land;

            public int At(int x, int y)
            {
                if (x < 0 || y < 0 || x >= land.W || y >= land.H) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the land");
                int c = (y >> Chunks.Shift) * land.chunksW + (x >> Chunks.Shift);
                if (c != chunk)
                {
                    land.Fill(c, buffer);
                    chunk = c;
                }
                return buffer[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
            }
        }

        public void Row(int y, int[] into)
        {
            for (int x = 0; x < W; x++) into[x] = At(x, y);
        }
    }
}
