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
    public readonly struct ThingId : IEquatable<ThingId>
    {
        public static readonly ThingId None = new ThingId(ulong.MaxValue);

        public ThingId(ulong value) { Value = value; }

        public ThingId(int chunkX, int chunkY, int index)
        {
            if (chunkX < short.MinValue || chunkX > short.MaxValue || chunkY < short.MinValue || chunkY > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(chunkX), "A chunk coordinate must fit in 16 bits");
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            Value = ((ulong)(ushort)(short)chunkX << 48) | ((ulong)(ushort)(short)chunkY << 32) | (uint)index;
        }

        public static ThingId Placed(int chunkX, int chunkY, int n) =>
            new ThingId(new ThingId(chunkX, chunkY, 0).Value | 0x80000000u | ((uint)n & 0x7FFFFFFFu));

        public ulong Value { get; }
        public int ChunkX => (short)(ushort)(Value >> 48);
        public int ChunkY => (short)(ushort)(Value >> 32);
        public int Index => (int)(uint)Value;
        public bool IsNone => Value == ulong.MaxValue;
        public bool IsPlaced => !IsNone && (Value & 0x80000000u) != 0;
        public int PlacedN => (int)(Value & 0x7FFFFFFFu);

        public bool Equals(ThingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ThingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ThingId a, ThingId b) => a.Value == b.Value;
        public static bool operator !=(ThingId a, ThingId b) => a.Value != b.Value;

        public override string ToString()
        {
            if (IsNone) return "none";
            return IsPlaced
                ? string.Format(CultureInfo.InvariantCulture, "{0},{1}#placed {2}", ChunkX, ChunkY, PlacedN)
                : string.Format(CultureInfo.InvariantCulture, "{0},{1}#{2}", ChunkX, ChunkY, Index);
        }
    }

    public sealed class ThingSpawn
    {
        public ThingId Id { get; internal set; }
        public string Kind { get; internal set; }
        public int ChunkX { get; internal set; }
        public int ChunkY { get; internal set; }
        public int LocalX { get; internal set; }
        public int LocalY { get; internal set; }
        public int CellX => ChunkX * Chunks.Size + LocalX;
        public int CellY => ChunkY * Chunks.Size + LocalY;
        public int Rotation { get; internal set; }
        public string Link { get; internal set; }
        public double Chance { get; internal set; } = 1;
        public bool FromMod { get; internal set; }
        public bool Placed { get; internal set; }
        public int Lapses { get; internal set; }
    }

    public static class ThingChance
    {
        const int Salt = 0x7468696e;

        public static bool Holds(uint seed, ThingId id, double chance)
        {
            if (chance >= 1) return true;
            uint roll = WorldHash.Hash4(unchecked((int)seed), Salt, unchecked((int)(id.Value >> 32)), unchecked((int)id.Value)) & 0xFFFF;
            return roll < (uint)Math.Round(chance * 65536);
        }
    }

    public sealed class WorldThings
    {
        public const string FileName = "things.json";

        static readonly IReadOnlyList<ThingSpawn> NoThings = Array.Empty<ThingSpawn>();

        readonly List<ThingSpawn> all = new List<ThingSpawn>();
        readonly Dictionary<(int, int), List<ThingSpawn>> byChunk = new Dictionary<(int, int), List<ThingSpawn>>();
        readonly Dictionary<ThingId, ThingSpawn> byId = new Dictionary<ThingId, ThingSpawn>();
        string fingerprint;

        WorldThings() { }

        public static WorldThings Empty => new WorldThings();

        public IReadOnlyList<ThingSpawn> All => all;
        public int Count => all.Count;

        public IEnumerable<(int x, int y)> ChunkCoords => byChunk.Keys.Select(k => (k.Item1, k.Item2));

        public IReadOnlyList<ThingSpawn> InChunk(int cx, int cy) =>
            byChunk.TryGetValue((cx, cy), out var list) ? list : NoThings;

        public ThingSpawn Find(ThingId id) => byId.TryGetValue(id, out var t) ? t : null;

        public ThingSpawn At(int x, int y) => all.FirstOrDefault(t => t.CellX == x && t.CellY == y);

        public IEnumerable<ThingSpawn> LinkedTo(ThingSpawn thing) =>
            thing?.Link == null ? Enumerable.Empty<ThingSpawn>() : all.Where(t => t.Link == thing.Link && t.Id != thing.Id);

        public static WorldThings Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

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
                    Chance = t["chance"] != null && t["chance"].Type != JTokenType.Null ? (double)t["chance"] : 1,
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

        public WorldThings With(Rules rules, uint seed)
        {
            if (rules.ModThings.Count == 0 && all.TrueForAll(t => t.Chance >= 1)) return this;
            var next = new WorldThings();
            foreach (var chunk in byChunk)
                foreach (var t in chunk.Value.Where(t => ThingChance.Holds(seed, t.Id, t.Chance))) next.Put(chunk.Key, t);
            var taken = new HashSet<ThingId>(all.Select(t => t.Id));
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rules.ModThings)
            {
                var chunk = (Chunks.ChunkOf(row.X), Chunks.ChunkOf(row.Y));
                var id = ModThingId(chunk, row, seen, taken);
                if (!ThingChance.Holds(seed, id, row.Chance)) continue;
                next.Put(chunk, new ThingSpawn
                {
                    Id = id,
                    Kind = row.Kind,
                    ChunkX = chunk.Item1,
                    ChunkY = chunk.Item2,
                    LocalX = row.X - chunk.Item1 * Chunks.Size,
                    LocalY = row.Y - chunk.Item2 * Chunks.Size,
                    Rotation = row.Rotation,
                    Chance = row.Chance,
                    FromMod = true,
                });
            }
            return next;
        }

        public WorldThings Plus(IReadOnlyList<ThingSpawn> placed)
        {
            if (placed.Count == 0) return this;
            var next = new WorldThings();
            foreach (var chunk in byChunk)
                foreach (var t in chunk.Value) next.Put(chunk.Key, t);
            foreach (var t in placed) next.Put((t.ChunkX, t.ChunkY), t);
            return next;
        }

        void Put((int, int) chunk, ThingSpawn thing)
        {
            if (!byChunk.TryGetValue(chunk, out var list)) byChunk[chunk] = list = new List<ThingSpawn>();
            list.Add(thing);
            all.Add(thing);
            byId[thing.Id] = thing;
        }

        const uint ModIndexes = 0x40000000;

        static ThingId ModThingId((int, int) chunk, ModThing row, Dictionary<string, int> seen, HashSet<ThingId> taken)
        {
            string key = string.Format(CultureInfo.InvariantCulture, "{0} {1},{2} {3}", row.Kind, row.X, row.Y, row.Rotation);
            seen.TryGetValue(key, out int nth);
            seen[key] = nth + 1;
            uint hash = uint.Parse(Fnv1a.Hash(key + "#" + nth.ToString(CultureInfo.InvariantCulture)), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint index = ModIndexes | (hash & (ModIndexes - 1));
            var id = new ThingId(chunk.Item1, chunk.Item2, (int)index);
            while (!taken.Add(id))
            {
                index = ModIndexes | ((index + 1) & (ModIndexes - 1));
                id = new ThingId(chunk.Item1, chunk.Item2, (int)index);
            }
            return id;
        }

        public (IReadOnlyList<ThingSpawn> added, IReadOnlyList<ThingId> removed) ChangesFrom(WorldThings before) =>
            (all.Where(t => before.Find(t.Id) == null).ToList(), before.all.Where(t => Find(t.Id) == null).Select(t => t.Id).ToList());

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
