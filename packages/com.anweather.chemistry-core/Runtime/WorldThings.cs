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

        /// <summary>
        /// The id of the <paramref name="n"/>th thing a spell placed (World.Placed): its index has the top bit set,
        /// above every file's and mod's (ModIndexes), so it never clashes with one. Its Index reads negative.
        /// </summary>
        public static ThingId Placed(int chunkX, int chunkY, int n) =>
            new ThingId(new ThingId(chunkX, chunkY, 0).Value | 0x80000000u | ((uint)n & 0x7FFFFFFFu));

        /// <summary>The packed id, for the wire.</summary>
        public ulong Value { get; }
        public int ChunkX => (short)(ushort)(Value >> 48);
        public int ChunkY => (short)(ushort)(Value >> 32);
        public int Index => (int)(uint)Value;
        public bool IsNone => Value == ulong.MaxValue;
        /// <summary>A spell placed it (<see cref="Placed"/>); <see cref="PlacedN"/> is then its place in the world's count.</summary>
        public bool IsPlaced => !IsNone && (Value & 0x80000000u) != 0;
        public int PlacedN => (int)(Value & 0x7FFFFFFFu);

        public bool Equals(ThingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ThingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ThingId a, ThingId b) => a.Value == b.Value;
        public static bool operator !=(ThingId a, ThingId b) => a.Value != b.Value;

        /// <summary>"cx,cy#index", e.g. "1,0#3"; a placed thing's "cx,cy#placed n", e.g. "1,0#placed 4".</summary>
        public override string ToString()
        {
            if (IsNone) return "none";
            return IsPlaced
                ? string.Format(CultureInfo.InvariantCulture, "{0},{1}#placed {2}", ChunkX, ChunkY, PlacedN)
                : string.Format(CultureInfo.InvariantCulture, "{0},{1}#{2}", ChunkX, ChunkY, Index);
        }
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
        /// <summary>How likely it is to be in a given world at all, 0 to 1 (<see cref="ThingChance"/>).</summary>
        public double Chance { get; internal set; } = 1;
        /// <summary>A mod added it (the rules' "world" section): its first pick-up is logged (RoomCommand.Found).</summary>
        public bool FromMod { get; internal set; }
        /// <summary>A spell placed it (World.Placed): it's there from the tick of the cast, and lapses at <see cref="Lapses"/>.</summary>
        public bool Placed { get; internal set; }
        /// <summary>The tick a placed thing goes, 0 for one that stays (and every thing not placed).</summary>
        public int Lapses { get; internal set; }
    }

    /// <summary>
    /// Whether a thing with a chance is in a world: decided once by a hash of the world's seed and the thing's
    /// id, with no draw from the world's generator, so every peer agrees at any tick and the cells don't move.
    /// </summary>
    public static class ThingChance
    {
        const int Salt = 0x7468696e;

        /// <summary>The chance in 65536ths, so what's compared is whole.</summary>
        public static bool Holds(uint seed, ThingId id, double chance)
        {
            if (chance >= 1) return true;
            uint roll = WorldHash.Hash4(unchecked((int)seed), Salt, unchecked((int)(id.Value >> 32)), unchecked((int)id.Value)) & 0xFFFF;
            return roll < (uint)Math.Round(chance * 65536);
        }
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
        /// These things as they stand in a world under these rules: the ones the mods on add, appended to
        /// their chunks' lists, and without any whose chance didn't hold for this seed. A pure function, so
        /// a revert is only working it out again. This table when there's nothing to change, so the
        /// fingerprint of a world with no mod's things is the file's.
        /// </summary>
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

        /// <summary>These things and <paramref name="placed"/> after them (World.Placed); this table when there are none.</summary>
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

        // Mods come and go: reverting one mustn't renumber what a later one added to the same chunk, which
        // would move its id, its chance and who found it. So a mod's thing doesn't count places in the list:
        // its index, above any a file reaches, is a hash of the row (and which of several like it this is).
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

        /// <summary>What's here and not in <paramref name="before"/>, and what's gone: what the view spawns and despawns at a rule switch, by id.</summary>
        public (IReadOnlyList<ThingSpawn> added, IReadOnlyList<ThingId> removed) ChangesFrom(WorldThings before) =>
            (all.Where(t => before.Find(t.Id) == null).ToList(), before.all.Where(t => Find(t.Id) == null).Select(t => t.Id).ToList());

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
