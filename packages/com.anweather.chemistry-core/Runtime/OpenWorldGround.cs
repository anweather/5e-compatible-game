// What the host game says the ground is in an open world's chunk (D20, docs/design/core-package/open-world.md): each
// cell's terrain, water and height step, as it sampled them. Only the host samples; its answer travels as a fill
// (ChunkFill, a command on its own lockstep channel), so every peer makes the chunk from the same bytes on the same tick
// and no float has to agree across machines. The chunk's cells are then its changes against its ground (World.Pack.cs),
// as a bounded world's are against its source.
using System;
using System.Collections.Generic;
using Chem.Lockstep;

namespace Chem
{
    /// <summary>
    /// One chunk's ground as the host game sampled it: a terrain id, the water on it (units) and a height step for each
    /// of its Chunks.Area cells, by local index (ly * Chunks.Size + lx). Packed small for the wire (Bytes): the terrains
    /// once, then runs of cells alike.
    /// </summary>
    public sealed class ChunkGround
    {
        // The terrains the cells name, in the order first set; a cell holds its terrain's place in them.
        readonly List<string> terrains = new List<string>();
        readonly byte[] kinds = new byte[Chunks.Area];
        // Null while every cell has none.
        ushort[] water;
        sbyte[] height;

        /// <summary>A chunk of one terrain, no water and no height step, to set cells on.</summary>
        public ChunkGround(string fill = "dirt")
        {
            terrains.Add(fill ?? throw new ArgumentNullException(nameof(fill)));
        }

        /// <summary>The most water a cell's ground may hold, in units.</summary>
        public const int MostWater = ushort.MaxValue;

        /// <summary>The terrains a chunk's ground may name, at most.</summary>
        public const int MostTerrains = 255;

        /// <summary>Sets a cell's ground by its place in the chunk (0 to Chunks.Size - 1 each way).</summary>
        public ChunkGround Set(int lx, int ly, string terrain, int waterUnits = 0, int heightStep = 0)
        {
            if ((uint)lx >= Chunks.Size || (uint)ly >= Chunks.Size) throw new ArgumentOutOfRangeException(nameof(lx), $"({lx}, {ly}) isn't in a chunk");
            return SetAt((ly << Chunks.Shift) | lx, terrain, waterUnits, heightStep);
        }

        /// <summary>Sets a cell's ground by its local index.</summary>
        public ChunkGround SetAt(int i, string terrain, int waterUnits = 0, int heightStep = 0)
        {
            if ((uint)i >= Chunks.Area) throw new ArgumentOutOfRangeException(nameof(i));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (waterUnits < 0 || waterUnits > MostWater) throw new ArgumentOutOfRangeException(nameof(waterUnits), $"water {waterUnits} isn't 0 to {MostWater}");
            if (heightStep < sbyte.MinValue || heightStep > sbyte.MaxValue) throw new ArgumentOutOfRangeException(nameof(heightStep));
            kinds[i] = KindOf(terrain);
            if (waterUnits != 0 || water != null) (water ??= new ushort[Chunks.Area])[i] = (ushort)waterUnits;
            if (heightStep != 0 || height != null) (height ??= new sbyte[Chunks.Area])[i] = (sbyte)heightStep;
            bytes = null;
            return this;
        }

        byte KindOf(string terrain)
        {
            int k = terrains.IndexOf(terrain);
            if (k >= 0) return (byte)k;
            if (terrains.Count >= MostTerrains) throw new InvalidOperationException($"a chunk's ground names {MostTerrains} terrains at most");
            terrains.Add(terrain);
            return (byte)(terrains.Count - 1);
        }

        /// <summary>A cell's terrain, by local index.</summary>
        public string TerrainAt(int i) => terrains[kinds[i]];

        /// <summary>A cell's water in units, by local index.</summary>
        public int WaterAt(int i) => water?[i] ?? 0;

        /// <summary>A cell's height step, by local index.</summary>
        public int HeightAt(int i) => height?[i] ?? 0;

        /// <summary>Makes a cell (freshly reset) as its ground: what an open world's source does (IChunkSource.Make).</summary>
        internal void Make(int i, Cell cell)
        {
            cell.T = terrains[kinds[i]];
            if (water != null) cell.Water = water[i];
            if (height != null) cell.Height = height[i];
        }

        byte[] bytes;

        /// <summary>
        /// As it goes on the wire and in a snapshot: a version byte, the terrains, then runs of cells alike (a run's
        /// length, its terrain's place, its water and its height step). A chunk of one ground is a few bytes.
        /// </summary>
        public byte[] Bytes()
        {
            if (bytes != null) return bytes;
            var w = new LockstepWriter();
            w.Byte(Version);
            w.Natural((uint)terrains.Count);
            foreach (var t in terrains) w.Text(t);
            for (int i = 0; i < Chunks.Area;)
            {
                int run = 1;
                while (i + run < Chunks.Area && Alike(i, i + run)) run++;
                w.Natural((uint)run);
                w.Natural(kinds[i]);
                w.Natural((uint)WaterAt(i));
                w.Number(HeightAt(i));
                i += run;
            }
            return bytes = w.ToArray();
        }

        bool Alike(int a, int b) => kinds[a] == kinds[b] && WaterAt(a) == WaterAt(b) && HeightAt(a) == HeightAt(b);

        const byte Version = 1;

        /// <summary>A chunk's ground back from its Bytes; LockstepWireException if they aren't a ground's.</summary>
        public static ChunkGround FromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var r = new LockstepReader(bytes);
            if (r.Byte() != Version) throw new LockstepWireException("a chunk's ground of another version");
            int n = r.Count();
            if (n < 1 || n > MostTerrains) throw new LockstepWireException($"a chunk's ground naming {n} terrains");
            var names = new string[n];
            for (int k = 0; k < n; k++) names[k] = r.Text() ?? throw new LockstepWireException("a chunk's ground naming no terrain");
            var g = new ChunkGround(names[0]);
            for (int k = 1; k < n; k++) g.terrains.Add(names[k]);
            for (int i = 0; i < Chunks.Area;)
            {
                uint run = r.Natural(), kind = r.Natural(), units = r.Natural();
                int step = r.Number();
                if (run == 0 || run > Chunks.Area - i || kind >= n || units > MostWater || step < sbyte.MinValue || step > sbyte.MaxValue)
                    throw new LockstepWireException("a chunk's ground whose cells don't read");
                for (int end = i + (int)run; i < end; i++)
                {
                    g.kinds[i] = (byte)kind;
                    if (units != 0 || g.water != null) (g.water ??= new ushort[Chunks.Area])[i] = (ushort)units;
                    if (step != 0 || g.height != null) (g.height ??= new sbyte[Chunks.Area])[i] = (sbyte)step;
                }
            }
            if (!r.AtEnd) throw new LockstepWireException("a chunk's ground runs past its cells");
            g.bytes = bytes;
            return g;
        }

        /// <summary>A fingerprint of the ground (FNV-1a 64 of its Bytes), for the cells' hash.</summary>
        public ulong Hash => hash ??= HashOf(Bytes());

        static ulong HashOf(byte[] b)
        {
            unchecked
            {
                ulong h = 0xcbf29ce484222325;
                foreach (byte x in b) h = (h ^ x) * 0x100000001b3;
                return h;
            }
        }

        ulong? hash;
    }

    /// <summary>An open world's chunk filled from the ground the host game sampled, in a tick batch (TickBatch.Fills).</summary>
    public readonly struct ChunkFill
    {
        public ChunkFill(ChunkKey key, ChunkGround ground)
        {
            Key = key;
            Ground = ground ?? throw new ArgumentNullException(nameof(ground));
        }

        public ChunkKey Key { get; }
        public ChunkGround Ground { get; }

        public override string ToString() => "fill " + Key;
    }

    /// <summary>
    /// An open world's source (World.Source): each filled chunk's cells as its ground makes them. Its chunks come only
    /// through fills (World.FillChunk), so a world is never made from it (MakeFrom); a save carries the grounds.
    /// </summary>
    internal sealed class FilledGround : IChunkSource
    {
        readonly Dictionary<long, ChunkGround> grounds = new Dictionary<long, ChunkGround>();
        long lastKey = long.MinValue;
        ChunkGround last;

        public const string Name = "open";

        public string Version => Name;

        public void Add(int cx, int cy, ChunkGround ground)
        {
            grounds.Add(Chunks.Key(cx, cy), ground);
            lastKey = long.MinValue;
        }

        public ChunkGround Of(int cx, int cy) => grounds.TryGetValue(Chunks.Key(cx, cy), out var g) ? g : null;

        public void Make(int x, int y, Cell cell)
        {
            long key = Chunks.Key(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            if (key != lastKey)
            {
                if (!grounds.TryGetValue(key, out last))
                {
                    lastKey = long.MinValue;
                    return; // no chunk there: what a cell there would be isn't asked
                }
                lastKey = key;
            }
            last.Make((Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x), cell);
        }
    }
}
