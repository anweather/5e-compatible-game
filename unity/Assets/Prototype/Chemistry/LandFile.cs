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
            using var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true))
            {
                for (int k = 0; k < land.Length; k++)
                    WriteVarint(deflate, Zigzag(land[k] - Previous(land, k, w)));
            }
            var o = new JObject
            {
                ["version"] = Version,
                ["w"] = w,
                ["h"] = h,
                ["unitMetres"] = unitMetres,
                ["data"] = Convert.ToBase64String(packed.ToArray()),
            };
            return o.ToString(Newtonsoft.Json.Formatting.Indented) + "\n";
        }

        /// <summary>The land in land.json's text, for a w × h world; empty if it's for another size.</summary>
        public static int[] Read(string json, int w, int h)
        {
            var o = JObject.Parse(json);
            if ((int?)o["version"] != Version || (int?)o["w"] != w || (int?)o["h"] != h) return Array.Empty<int>();
            var land = new int[w * h];
            using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String((string)o["data"])), CompressionMode.Decompress);
            for (int k = 0; k < land.Length; k++)
                land[k] = Previous(land, k, w) + Unzigzag(ReadVarint(deflate));
            return land;
        }

        static int Previous(int[] land, int k, int w)
        {
            if (k == 0) return 0;
            return k % w == 0 ? land[k - w] : land[k - 1];
        }

        static uint Zigzag(int v) => (uint)((v << 1) ^ (v >> 31));
        static int Unzigzag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);

        static void WriteVarint(Stream s, uint v)
        {
            while (v >= 0x80)
            {
                s.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            s.WriteByte((byte)v);
        }

        static uint ReadVarint(Stream s)
        {
            uint v = 0;
            int shift = 0, b = 0x80;
            while (b >= 0x80)
            {
                b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException("land.json ends early");
                v |= (uint)(b & 0x7f) << shift;
                shift += 7;
            }
            return v;
        }
    }
}
