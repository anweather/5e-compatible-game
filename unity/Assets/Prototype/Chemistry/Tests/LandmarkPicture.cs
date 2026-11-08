// A top-down picture of a landmark's rows on a flat patch (J-080 phase 1), with no Unity: the stamps' land shaded with
// contours every 5 m and water below the lake's level, the buildings' cells by terrain, the village laid where it
// can stand, the docks, the things as dots and a ring round the landmark's place. 4 px a cell, 256 cells across.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Tests
{
    internal static class LandmarkPicture
    {
        internal const int Cells = 256, Scale = 4, Size = Cells * Scale;
        /// <summary>Land below this many land units holds water (Places' "water", the lake's level).</summary>
        internal const int WaterBelow = -4;
        const int Contour = 48;

        internal sealed class Picture
        {
            public byte[] Pixels;
            public int Houses;
        }

        static readonly Dictionary<string, (double r, double g, double b)> Grounds = new Dictionary<string, (double, double, double)>
        {
            ["temperate"] = (0.33, 0.55, 0.22),
            ["mountains"] = (0.46, 0.43, 0.38),
            ["foggy-marsh"] = (0.36, 0.47, 0.33),
        };

        static readonly Dictionary<string, (double r, double g, double b)> Terrains = new Dictionary<string, (double, double, double)>
        {
            ["logwall"] = (0.36, 0.22, 0.12),
            ["tree"] = (0.08, 0.28, 0.09),
            ["deadfall"] = (0.55, 0.40, 0.24),
            ["stone"] = (0.55, 0.55, 0.57),
            ["rock"] = (0.47, 0.45, 0.42),
            ["dirt"] = (0.58, 0.47, 0.33),
            ["pane"] = (0.62, 0.82, 0.92),
            ["door"] = (0.25, 0.16, 0.08),
            ["thatch"] = (0.80, 0.68, 0.38),
            ["slate"] = (0.32, 0.35, 0.42),
            ["shingle"] = (0.52, 0.34, 0.20),
        };

        static readonly (double r, double g, double b) Dock = (0.66, 0.47, 0.27), Marker = (0.86, 0.14, 0.10), Dirt = (0.62, 0.51, 0.36);

        /// <summary>The land the stamps lay on flat ground, a patch of <paramref name="n"/> cells from (x0, y0), row by row.</summary>
        internal static int[] Land(LandmarkRows rows, int x0, int y0, int n)
        {
            var stamps = MapStamps.FromJson(rows.StampsJson).All;
            var land = new int[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    foreach (var s in stamps) land[y * n + x] += s.Stamp.At(x0 + x - s.Stamp.X, y0 + y - s.Stamp.Y);
            return land;
        }

        /// <summary>A boathouse's dock: the cell at its far end, out from the middle of the wall it's on.</summary>
        internal static (int x, int y) DockEnd(JObject b)
        {
            var (x0, y0, x1, y1, side, _, _) = DockOf(b);
            int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            switch (side)
            {
                case "north": return (mx, y0);
                case "south": return (mx, y1);
                case "west": return (x0, my);
                default: return (x1, my);
            }
        }

        // The dock's cells as a block, its wall, and its length and width in cells.
        static (int x0, int y0, int x1, int y1, string side, int length, int width) DockOf(JObject b)
        {
            var part = b["parts"].First(p => (string)p["kind"] == "dock");
            int ax = (int)b["at"][0], ay = (int)b["at"][1];
            int w = (int)b["joins"]["ne"][0] / 2, h = (int)b["joins"]["se"][1] / 2;
            int length = (int)part["length"] / 2, half = Math.Max(1, (int)part["width"] / 2) / 2;
            string side = (string)part["on"];
            int mx = ax + w / 2, my = ay + h / 2;
            switch (side)
            {
                case "north": return (mx - half, ay - length, mx + half, ay - 1, side, length, half);
                case "south": return (mx - half, ay + h, mx + half, ay + h + length - 1, side, length, half);
                case "west": return (ax - length, my - half, ax - 1, my + half, side, length, half);
                default: return (ax + w, my - half, ax + w + length - 1, my + half, side, length, half);
            }
        }

        /// <summary>The picture, framed on the middle of the landmark and its village.</summary>
        internal static Picture Draw(LandmarkRows rows, Rules rules)
        {
            int x0 = (rows.Centre.x + rows.VillageAt.x) / 2 - Cells / 2, y0 = (rows.Centre.y + rows.VillageAt.y) / 2 - Cells / 2;
            var land = Land(rows, x0, y0, Cells);
            var colour = new (double r, double g, double b)[Cells * Cells];
            var ground = Grounds.TryGetValue(rows.Form, out var g) ? g : Grounds["temperate"];
            for (int y = 0; y < Cells; y++)
                for (int x = 0; x < Cells; x++) colour[y * Cells + x] = Shaded(land, x, y, ground);
            var taken = new HashSet<(int x, int y)>();
            void Put(int wx, int wy, (double, double, double) c)
            {
                int x = wx - x0, y = wy - y0;
                if (x < 0 || y < 0 || x >= Cells || y >= Cells) return;
                colour[y * Cells + x] = c;
                taken.Add((wx, wy));
            }
            foreach (var b in rows.Buildings.OfType<JObject>())
            {
                foreach (var c in BuildingRaster.Cells(BuildingRow.Read((string)b["id"], b, rules.Buildings), rules.Buildings))
                    Put(c.X, c.Y, Terrains.TryGetValue(c.Terrain, out var t) ? t : (1, 0, 1));
                if (b["parts"]?.Any(p => (string)p["kind"] == "dock") != true) continue;
                var (dx0, dy0, dx1, dy1, _, _, _) = DockOf(b);
                for (int y = dy0; y <= dy1; y++)
                    for (int x = dx0; x <= dx1; x++) Put(x, y, Dock);
            }
            int houses = rows.Buildings.Count(b => (string)b["style"] == "village") + Village(rows, land, x0, y0, taken, Put);
            var pixels = Scaled(colour);
            foreach (var chunk in rows.Things.OfType<JObject>())
                foreach (var t in chunk["things"].OfType<JObject>())
                    Dot(pixels, (int)chunk["chunk"][0] * Chunks.Size + (int)t["cell"][0] - x0, (int)chunk["chunk"][1] * Chunks.Size + (int)t["cell"][1] - y0, ThingColour((string)t["kind"]));
            var circle = rows.Place["circle"];
            Ring(pixels, (int)circle["at"][0] - x0, (int)circle["at"][1] - y0, (int)circle["radius"]);
            return new Picture { Pixels = pixels, Houses = houses };
        }

        // Lays the village where it can stand: dry, gentle, inside the patch and off the landmark's cells.
        static int Village(LandmarkRows rows, int[] land, int x0, int y0, HashSet<(int x, int y)> taken, Action<int, int, (double, double, double)> put)
        {
            var spec = VillageLayout.FromJson(rows.VillagesJson).SingleOrDefault();
            if (spec == null) return 0;
            bool Buildable(int wx, int wy)
            {
                int x = wx - x0, y = wy - y0;
                if (x < 1 || y < 1 || x >= Cells - 1 || y >= Cells - 1) return false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (taken.Contains((wx + dx, wy + dy))) return false;
                int h = land[y * Cells + x];
                return h > WaterBelow + 2 && Math.Abs(land[y * Cells + x + 1] - h) <= 6 && Math.Abs(land[(y + 1) * Cells + x] - h) <= 6;
            }
            var plan = VillageLayout.Lay(spec, Buildable);
            foreach (var (x, y) in plan.Square.Cells()) put(x, y, Dirt);
            foreach (var lane in plan.Lanes)
                foreach (var (x, y) in lane.Cells()) put(x, y, Dirt);
            foreach (var b in plan.Buildings)
                foreach (var (x, y) in b.Footprint.Cells())
                {
                    bool edge = x == b.Footprint.X || y == b.Footprint.Y || x == b.Footprint.Right - 1 || y == b.Footprint.Bottom - 1;
                    var roof = Terrains.TryGetValue(b.RoofMaterial, out var r) ? r : Terrains["thatch"];
                    var wall = Terrains.TryGetValue(b.WallMaterial, out var w) ? w : Terrains["logwall"];
                    put(x, y, edge ? wall : roof);
                }
            put(plan.Well.x, plan.Well.y, (0.16, 0.42, 0.62));
            return plan.Buildings.Count;
        }

        static (double r, double g, double b) Shaded(int[] land, int x, int y, (double r, double g, double b) ground)
        {
            int H(int px, int py) => land[Math.Max(0, Math.Min(Cells - 1, py)) * Cells + Math.Max(0, Math.Min(Cells - 1, px))];
            int h = H(x, y);
            if (h < WaterBelow)
            {
                double deep = Math.Min(1, -h / 60.0);
                return (0.20 - 0.10 * deep, 0.45 - 0.15 * deep, 0.66 - 0.12 * deep);
            }
            // Light from the north-west, and the high ground towards bare ash and rock.
            double light = Math.Max(0.55, Math.Min(1.45, 1 + (H(x + 1, y + 1) - H(x - 1, y - 1)) * 0.02));
            double bare = Math.Min(1, Math.Max(0, h / 420.0));
            var (r, g, b) = (ground.r + (0.58 - ground.r) * bare, ground.g + (0.54 - ground.g) * bare, ground.b + (0.50 - ground.b) * bare);
            bool line = FloorDiv(h, Contour) != FloorDiv(H(x + 1, y), Contour) || FloorDiv(h, Contour) != FloorDiv(H(x, y + 1), Contour);
            double k = light * (line ? 0.78 : 1);
            return (Math.Min(1, r * k), Math.Min(1, g * k), Math.Min(1, b * k));
        }

        static int FloorDiv(int a, int b) => (int)BiomeMath.FloorDiv(a, b);

        static (double r, double g, double b) ThingColour(string kind) => kind switch
        {
            "boulder" => (0.25, 0.24, 0.24),
            "barrel" => (0.45, 0.25, 0.10),
            "crate" => (0.85, 0.70, 0.40),
            _ => (1, 1, 1),
        };

        static byte[] Scaled((double r, double g, double b)[] colour)
        {
            var pixels = new byte[Size * Size * 3];
            for (int py = 0; py < Size; py++)
                for (int px = 0; px < Size; px++)
                {
                    var c = colour[py / Scale * Cells + px / Scale];
                    Set(pixels, px, py, c);
                }
            return pixels;
        }

        static void Set(byte[] pixels, int px, int py, (double r, double g, double b) c)
        {
            if (px < 0 || py < 0 || px >= Size || py >= Size) return;
            int i = (py * Size + px) * 3;
            pixels[i] = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, c.r)));
            pixels[i + 1] = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, c.g)));
            pixels[i + 2] = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, c.b)));
        }

        static void Dot(byte[] pixels, int cx, int cy, (double, double, double) c)
        {
            for (int dy = -1; dy <= Scale; dy++)
                for (int dx = -1; dx <= Scale; dx++)
                {
                    bool rim = dx == -1 || dy == -1 || dx == Scale || dy == Scale;
                    Set(pixels, cx * Scale + dx, cy * Scale + dy, rim ? (1, 1, 1) : c);
                }
        }

        static void Ring(byte[] pixels, int cx, int cy, int radius)
        {
            double mx = (cx + 0.5) * Scale, my = (cy + 0.5) * Scale, r = (radius + 2) * Scale;
            for (int py = 0; py < Size; py++)
                for (int px = 0; px < Size; px++)
                {
                    double d = Math.Sqrt((px - mx) * (px - mx) + (py - my) * (py - my));
                    if (Math.Abs(d - r) < 1.6 && (int)(Math.Atan2(py - my, px - mx) * 24 / Math.PI + 48) % 2 == 0) Set(pixels, px, py, Marker);
                }
        }

        // ---------- PNG: RGB, 8 bits, one zlib stream ----------

        internal static byte[] Png(byte[] rgb, int width, int height)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, y * width * 3, width * 3);
            }
            byte[] data = raw.ToArray();
            var z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x9c);
            using (var deflate = new DeflateStream(z, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
            WriteBig(z, Adler(data));
            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var header = new MemoryStream();
            WriteBig(header, (uint)width);
            WriteBig(header, (uint)height);
            header.Write(new byte[] { 8, 2, 0, 0, 0 }, 0, 5);
            ChunkOut(png, "IHDR", header.ToArray());
            ChunkOut(png, "IDAT", z.ToArray());
            ChunkOut(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static void ChunkOut(Stream s, string type, byte[] data)
        {
            WriteBig(s, (uint)data.Length);
            var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            s.Write(typed, 0, typed.Length);
            WriteBig(s, Crc(typed));
        }

        static void WriteBig(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        static uint Crc(byte[] bytes)
        {
            uint c = 0xffffffffu;
            foreach (byte b in bytes)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xffffffffu;
        }

        static uint Adler(byte[] bytes)
        {
            uint a = 1, b = 0;
            foreach (byte v in bytes)
            {
                a = (a + v) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
