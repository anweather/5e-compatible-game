// The world's props and items as data (world/things.json, docs/WORLD_STREAMING.md):
// per 64x64-cell chunk, a list of things, each a kind from the rules
// (entities.json or items.json), a cell inside the chunk, a turn, and an
// optional link (a pair of sending stones). The view spawns them
// (ThingSpawner); nothing is placed by hand in the scene.
//
// Every thing has a stable id, its chunk and its place in that chunk's list
// (ThingId). Ids are worked out from the file, never from a runtime counter,
// so the host, every client and a late joiner name the same thing the same
// way, and a chunk spawned again later gets the same ids. The fingerprint
// goes into the netcode checksum next to the cells' hash and the rules', so
// machines with different files are caught.
//
// Not a port: the sandbox has no props on its board. The file lives in
// world/, not rules/: rules say what exists and how it reacts (mods patch
// them); this says where things stand in one world.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>
    /// A thing's stable id: its chunk and its index in that chunk's list, packed into 64 bits
    /// (16 bits each for the chunk's x and y, 32 for the index). Distinct by construction, the
    /// same on every machine, and the chunk can be read back from it.
    /// </summary>
    public readonly struct ThingId : IEquatable<ThingId>
    {
        /// <summary>No thing (all bits set: chunk (-1, -1), an index no list reaches).</summary>
        public static readonly ThingId None = new ThingId(ulong.MaxValue);

        public ThingId(ulong value) { Value = value; }

        public ThingId(int chunkX, int chunkY, int index)
        {
            if (chunkX < short.MinValue || chunkX > short.MaxValue || chunkY < short.MinValue || chunkY > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(chunkX), "A chunk coordinate must fit in 16 bits");
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            Value = ((ulong)(ushort)(short)chunkX << 48) | ((ulong)(ushort)(short)chunkY << 32) | (uint)index;
        }

        /// <summary>The packed id, for the wire.</summary>
        public ulong Value { get; }
        public int ChunkX => (short)(ushort)(Value >> 48);
        public int ChunkY => (short)(ushort)(Value >> 32);
        public int Index => (int)(uint)Value;
        public bool IsNone => Value == ulong.MaxValue;

        public bool Equals(ThingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ThingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ThingId a, ThingId b) => a.Value == b.Value;
        public static bool operator !=(ThingId a, ThingId b) => a.Value != b.Value;

        /// <summary>"cx,cy#index", e.g. "1,0#3".</summary>
        public override string ToString() =>
            IsNone ? "none" : string.Format(CultureInfo.InvariantCulture, "{0},{1}#{2}", ChunkX, ChunkY, Index);
    }

    /// <summary>One thing in the world's data: what it is, where, and its id.</summary>
    public sealed class ThingSpawn
    {
        public ThingId Id { get; internal set; }
        /// <summary>Its id in rules/entities.json or rules/items.json.</summary>
        public string Kind { get; internal set; }
        public int ChunkX { get; internal set; }
        public int ChunkY { get; internal set; }
        /// <summary>The cell inside its chunk, 0..Chunks.Size-1.</summary>
        public int LocalX { get; internal set; }
        public int LocalY { get; internal set; }
        /// <summary>The world cell: chunk * Chunks.Size + local.</summary>
        public int CellX => ChunkX * Chunks.Size + LocalX;
        public int CellY => ChunkY * Chunks.Size + LocalY;
        /// <summary>Turn about the vertical, in degrees.</summary>
        public int Rotation { get; internal set; }
        /// <summary>Things sharing a link go together (a pair of sending stones); null for none.</summary>
        public string Link { get; internal set; }
    }

    /// <summary>The world's things (world/things.json), by chunk, with stable ids.</summary>
    public sealed class WorldThings
    {
        public const string FileName = "things.json";

        static readonly IReadOnlyList<ThingSpawn> NoThings = Array.Empty<ThingSpawn>();

        readonly List<ThingSpawn> all = new List<ThingSpawn>();
        readonly Dictionary<(int, int), List<ThingSpawn>> byChunk = new Dictionary<(int, int), List<ThingSpawn>>();
        readonly Dictionary<ThingId, ThingSpawn> byId = new Dictionary<ThingId, ThingSpawn>();
        string fingerprint;

        WorldThings() { }

        /// <summary>No things at all (a world without a things file).</summary>
        public static WorldThings Empty => new WorldThings();

        /// <summary>Every thing, chunk by chunk in file order.</summary>
        public IReadOnlyList<ThingSpawn> All => all;
        public int Count => all.Count;

        /// <summary>The chunks that have things, in file order.</summary>
        public IEnumerable<(int x, int y)> ChunkCoords => byChunk.Keys.Select(k => (k.Item1, k.Item2));

        /// <summary>The things in one chunk, in id order (empty if none).</summary>
        public IReadOnlyList<ThingSpawn> InChunk(int cx, int cy) =>
            byChunk.TryGetValue((cx, cy), out var list) ? list : NoThings;

        /// <summary>The thing with this id, or null.</summary>
        public ThingSpawn Find(ThingId id) => byId.TryGetValue(id, out var t) ? t : null;

        /// <summary>The first thing standing on a world cell, or null.</summary>
        public ThingSpawn At(int x, int y) => all.FirstOrDefault(t => t.CellX == x && t.CellY == y);

        /// <summary>The other things sharing <paramref name="thing"/>'s link (none without one).</summary>
        public IEnumerable<ThingSpawn> LinkedTo(ThingSpawn thing) =>
            thing?.Link == null ? Enumerable.Empty<ThingSpawn>() : all.Where(t => t.Link == thing.Link && t.Id != thing.Id);

        /// <summary>Reads world/things.json from <paramref name="worldDir"/>; no file means no things.</summary>
        public static WorldThings Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

        /// <summary>
        /// Builds the table from the file's text. Throws <see cref="FormatException"/> for a file
        /// that can't be read (not JSON, a chunk listed twice, a cell outside its chunk, a thing without a kind).
        /// </summary>
        public static WorldThings FromJson(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"things.json: {e.Message}", e); }
            var things = new WorldThings();
            foreach (var entry in (doc["chunks"] as JArray ?? new JArray()).OfType<JObject>())
                things.ReadChunk(entry);
            return things;
        }

        void ReadChunk(JObject entry)
        {
            var at = Pair(entry["chunk"], "chunk");
            if (byChunk.ContainsKey(at)) throw new FormatException($"things.json: chunk {at.Item1},{at.Item2} is listed twice");
            var list = new List<ThingSpawn>();
            byChunk[at] = list;
            foreach (var t in (entry["things"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var cell = Pair(t["cell"], "cell");
                if (cell.Item1 < 0 || cell.Item1 >= Chunks.Size || cell.Item2 < 0 || cell.Item2 >= Chunks.Size)
                    throw new FormatException($"things.json: cell {cell.Item1},{cell.Item2} in chunk {at.Item1},{at.Item2} is outside 0..{Chunks.Size - 1}");
                var kind = (string)t["kind"];
                if (string.IsNullOrEmpty(kind)) throw new FormatException($"things.json: a thing in chunk {at.Item1},{at.Item2} has no kind");
                var spawn = new ThingSpawn
                {
                    Id = new ThingId(at.Item1, at.Item2, list.Count),
                    Kind = kind,
                    ChunkX = at.Item1,
                    ChunkY = at.Item2,
                    LocalX = cell.Item1,
                    LocalY = cell.Item2,
                    Rotation = t["rot"] != null && t["rot"].Type != JTokenType.Null ? (int)Math.Round((double)t["rot"]) : 0,
                    Link = t["link"] != null && t["link"].Type != JTokenType.Null ? (string)t["link"] : null,
                };
                list.Add(spawn);
                all.Add(spawn);
                byId[spawn.Id] = spawn;
            }
        }

        static (int, int) Pair(JToken token, string what)
        {
            if (!(token is JArray a) || a.Count != 2) throw new FormatException($"things.json: \"{what}\" must be [x, y]");
            return ((int)a[0], (int)a[1]);
        }

        /// <summary>
        /// What doesn't fit the rules: kinds that are neither an entity nor an item, and links
        /// that don't join exactly two things. Empty when all is well.
        /// </summary>
        public List<string> Check(Rules rules)
        {
            var errors = new List<string>();
            foreach (var t in all)
                if (rules.Entity(t.Kind) == null && rules.Item(t.Kind) == null)
                    errors.Add($"things.json: {t.Id} is a \"{t.Kind}\", which is neither in entities.json nor items.json");
            foreach (var group in all.Where(t => t.Link != null).GroupBy(t => t.Link, StringComparer.Ordinal))
                if (group.Count() != 2)
                    errors.Add($"things.json: link \"{group.Key}\" joins {group.Count()} things; a link joins a pair");
            return errors;
        }

        /// <summary>
        /// A stable hash of every thing (id, kind, cell, turn, link), for the netcode checksum. Two
        /// machines with the same file get the same value; whitespace, notes and the order the
        /// chunks are listed in don't count.
        /// </summary>
        public string Fingerprint => fingerprint ??= Fnv1a.Hash(Canonical());

        string Canonical()
        {
            var sb = new StringBuilder();
            foreach (var t in all.OrderBy(t => t.Id.Value))
                sb.Append(t.Id.Value.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(t.Kind).Append(' ')
                  .Append(t.CellX.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(t.CellY.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(t.Rotation.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(t.Link ?? "").Append(';');
            return sb.ToString();
        }
    }
}
