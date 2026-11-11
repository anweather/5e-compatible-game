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
        sealed class Inflated
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

        static int Unzigzag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);
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
}
