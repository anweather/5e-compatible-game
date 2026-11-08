// A map's ground pack, ground/ in its folder (docs/design/maps-as-data.md, section 1): what its chunks draw, as plain data:
// heights, paint, grass and trees, and the river's channel, read by the view and by nothing that decides shared state.
// ground.json says how to read it; each kind is one deflated file with the chunks in order.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One tree on a chunk, as the terrain holds it: its place is 0 to 1 of the chunk.</summary>
    public struct GroundTree
    {
        public float X, Y, Z, Width, Height, Rotation;
        public uint Color, LightmapColor;
        public int Prototype;
    }

    /// <summary>One chunk's ground. Heights are steps of <see cref="GroundPack.HeightSteps"/> over its range, [z, x] flat.</summary>
    public sealed class GroundChunk
    {
        public int I, J, Resolution, Texels, DetailResolution;
        public ushort[] Heights;
        /// <summary>Paint, 0 to 255: layer by layer, each [z, x] flat.</summary>
        public byte[] Paint;
        /// <summary>Clumps of grass: prototype by prototype, each [z, x] flat.</summary>
        public byte[] Details;
        public GroundTree[] Trees;
    }

    /// <summary>A map's ground/ folder.</summary>
    public sealed class GroundPack
    {
        public const string FolderName = "ground", FileName = "ground.json";
        public const int Version = 1, HeightSteps = 32766;
        const string HeightsFile = "heights.bin", PaintFile = "paint.bin", DetailsFile = "details.bin", TreesFile = "trees.bin", ChannelFile = "channel.bin";

        /// <summary>The height of a chunk's step 0 in metres, and of its top step.</summary>
        public double Bottom, Top;
        /// <summary>What the paint layers, grass and trees are, in order, by the names of their assets.</summary>
        public string[] Layers = Array.Empty<string>(), Grasses = Array.Empty<string>(), TreeKinds = Array.Empty<string>();
        public List<GroundChunk> Chunks = new List<GroundChunk>();
        /// <summary>The river's cells, and how far each one's ground is cut below its banks, in metres.</summary>
        public int[] ChannelCells = Array.Empty<int>();
        public float[] ChannelDepths = Array.Empty<float>();

        /// <summary>The ground at one of a chunk's samples, in metres.</summary>
        public double Metres(GroundChunk chunk, int sx, int sz) => Bottom + (Top - Bottom) * chunk.Heights[sz * chunk.Resolution + sx] / HeightSteps;

        /// <summary>Whether this map's folder has a ground pack.</summary>
        public static bool In(string worldDir) => File.Exists(Path.Combine(worldDir, FolderName, FileName));

        /// <summary>Writes the pack into a map's folder, in place of any there.</summary>
        public void Save(string worldDir)
        {
            string dir = Path.Combine(worldDir, FolderName);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var head = new JObject
            {
                ["about"] = "What this map's chunks draw (docs/design/maps-as-data.md). Exported, not written by hand.",
                ["version"] = Version,
                ["bottom"] = Bottom,
                ["top"] = Top,
                ["layers"] = new JArray(Layers.Cast<object>().ToArray()),
                ["grasses"] = new JArray(Grasses.Cast<object>().ToArray()),
                ["treeKinds"] = new JArray(TreeKinds.Cast<object>().ToArray()),
                ["chunks"] = new JArray(Chunks.Select(c => (object)new JArray(c.I, c.J, c.Resolution, c.Texels, c.DetailResolution, c.Trees.Length)).ToArray()),
                ["channelCells"] = ChannelCells.Length,
            };
            // LF on every machine: a map's hash is over its bytes, and peers on other systems must agree (MapIdentity).
            File.WriteAllText(Path.Combine(dir, FileName), head.ToString(Formatting.Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            Pack(dir, HeightsFile, w => Chunks.ForEach(c => WriteHeights(w, c)));
            Pack(dir, PaintFile, w => Chunks.ForEach(c => w.Write(c.Paint)));
            Pack(dir, DetailsFile, w => Chunks.ForEach(c => w.Write(c.Details)));
            Pack(dir, TreesFile, w => Chunks.ForEach(c => WriteTrees(w, c)));
            Pack(dir, ChannelFile, w =>
            {
                foreach (int cell in ChannelCells) w.Write(cell);
                foreach (float depth in ChannelDepths) w.Write(depth);
            });
        }

        /// <summary>The pack in a map's folder; throws if it's not there or not this version.</summary>
        public static GroundPack Load(string worldDir)
        {
            string dir = Path.Combine(worldDir, FolderName);
            var head = JObject.Parse(File.ReadAllText(Path.Combine(dir, FileName)));
            if ((int?)head["version"] != Version) throw new InvalidDataException($"{FileName} is version {head["version"]}, not {Version}");
            var pack = new GroundPack
            {
                Bottom = (double)head["bottom"],
                Top = (double)head["top"],
                Layers = head["layers"].Values<string>().ToArray(),
                Grasses = head["grasses"].Values<string>().ToArray(),
                TreeKinds = head["treeKinds"].Values<string>().ToArray(),
            };
            var trees = new List<int>();
            foreach (var c in head["chunks"].Cast<JArray>())
            {
                pack.Chunks.Add(new GroundChunk { I = (int)c[0], J = (int)c[1], Resolution = (int)c[2], Texels = (int)c[3], DetailResolution = (int)c[4] });
                trees.Add((int)c[5]);
            }
            Unpack(dir, HeightsFile, r => pack.Chunks.ForEach(c => ReadHeights(r, c)));
            Unpack(dir, PaintFile, r => pack.Chunks.ForEach(c => c.Paint = r.ReadBytes(c.Texels * c.Texels * pack.Layers.Length)));
            Unpack(dir, DetailsFile, r => pack.Chunks.ForEach(c => c.Details = r.ReadBytes(c.DetailResolution * c.DetailResolution * pack.Grasses.Length)));
            Unpack(dir, TreesFile, r =>
            {
                for (int k = 0; k < pack.Chunks.Count; k++) pack.Chunks[k].Trees = ReadTrees(r, trees[k]);
            });
            int cells = (int)head["channelCells"];
            Unpack(dir, ChannelFile, r =>
            {
                pack.ChannelCells = Enumerable.Range(0, cells).Select(_ => r.ReadInt32()).ToArray();
                pack.ChannelDepths = Enumerable.Range(0, cells).Select(_ => r.ReadSingle()).ToArray();
            });
            return pack;
        }

        static void Pack(string dir, string file, Action<BinaryWriter> write)
        {
            using var stream = File.Create(Path.Combine(dir, file));
            using var deflate = new DeflateStream(stream, CompressionLevel.Optimal);
            using var writer = new BinaryWriter(deflate);
            write(writer);
        }

        static void Unpack(string dir, string file, Action<BinaryReader> read)
        {
            using var stream = File.OpenRead(Path.Combine(dir, file));
            using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate);
            read(reader);
        }

        // Each height as its step from the one before, low bytes then high: land is smooth, so this deflates far better.
        static void WriteHeights(BinaryWriter w, GroundChunk c)
        {
            var steps = new ushort[c.Heights.Length];
            for (int k = 0; k < steps.Length; k++) steps[k] = (ushort)(c.Heights[k] - (k == 0 ? 0 : c.Heights[k - 1]));
            foreach (ushort s in steps) w.Write((byte)s);
            foreach (ushort s in steps) w.Write((byte)(s >> 8));
        }

        static void ReadHeights(BinaryReader r, GroundChunk c)
        {
            int n = c.Resolution * c.Resolution;
            byte[] low = r.ReadBytes(n), high = r.ReadBytes(n);
            c.Heights = new ushort[n];
            for (int k = 0; k < n; k++) c.Heights[k] = (ushort)((k == 0 ? 0 : c.Heights[k - 1]) + (low[k] | (high[k] << 8)));
        }

        static void WriteTrees(BinaryWriter w, GroundChunk c)
        {
            foreach (var t in c.Trees)
            {
                w.Write(t.X);
                w.Write(t.Y);
                w.Write(t.Z);
                w.Write(t.Width);
                w.Write(t.Height);
                w.Write(t.Rotation);
                w.Write(t.Color);
                w.Write(t.LightmapColor);
                w.Write(t.Prototype);
            }
        }

        static GroundTree[] ReadTrees(BinaryReader r, int count)
        {
            var trees = new GroundTree[count];
            for (int k = 0; k < count; k++)
            {
                trees[k] = new GroundTree
                {
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    Width = r.ReadSingle(),
                    Height = r.ReadSingle(),
                    Rotation = r.ReadSingle(),
                    Color = r.ReadUInt32(),
                    LightmapColor = r.ReadUInt32(),
                    Prototype = r.ReadInt32(),
                };
            }
            return trees;
        }
    }

    /// <summary>Whether the ground drawn agrees with the land the core reads, on a few cells picked the same way every time.</summary>
    public static class LandCheck
    {
        public const int Cells = 64;

        /// <summary>Every cell of the sample whose drawn ground is further than <paramref name="most"/> metres from its land.</summary>
        public static List<string> Disagreements(Func<int, int, float> drawn, int[] land, int w, int h, double unit, double most)
        {
            var said = new List<string>();
            for (int k = 0; k < Cells; k++)
            {
                uint at = WorldHash.Hash4(k, 1, 2, 3) % (uint)(w * h);
                int x = (int)at % w, y = (int)at / w;
                double metres = land[at] * unit, ground = drawn(x, y);
                if (Math.Abs(metres - ground) > most) said.Add($"({x}, {y}): land {metres:0.00} m, drawn {ground:0.00} m");
            }
            return said;
        }
    }
}
