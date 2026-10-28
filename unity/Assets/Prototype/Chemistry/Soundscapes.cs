// What the world sounds like where a listener stands (world/soundscapes.json, backlog
// #9): birdsong in the woods, the river, surf and gulls on the shore, chatter and a
// fiddle at the inn in the evening. Each soundscape holds by what is round the
// listener's cell (terrain, water, a village, a building) and the hour on the world
// clock (Sky.InHours); every one that holds plays its loops, and a loop in several plays
// once at its loudest. Worked out when asked, from the cells and the clock, so it is the
// same on every peer and nothing is kept. The view plays the loops (Gameplay); this only
// says which. C# only: the sandbox has no sound.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A loop to play and how loud (0 to 1).</summary>
    public readonly struct SoundLoop : IEquatable<SoundLoop>
    {
        public SoundLoop(string sound, double volume)
        {
            Sound = sound;
            Volume = volume;
        }

        public string Sound { get; }
        public double Volume { get; }

        public bool Equals(SoundLoop other) => Sound == other.Sound && BitConverter.DoubleToInt64Bits(Volume) == BitConverter.DoubleToInt64Bits(other.Volume);
        public override bool Equals(object obj) => obj is SoundLoop o && Equals(o);
        public override int GetHashCode() => (Sound?.GetHashCode() ?? 0) ^ Volume.GetHashCode();
        public override string ToString() => $"{Sound} {Volume:0.##}";
        public static bool operator ==(SoundLoop a, SoundLoop b) => a.Equals(b);
        public static bool operator !=(SoundLoop a, SoundLoop b) => !a.Equals(b);
    }

    /// <summary>Where a soundscape holds: what is round the listener (soundscapes.json "near").</summary>
    public sealed class SoundscapeNear
    {
        /// <summary>Terrain ids to count, or a terrain tag to count, or cells with at least this much water.</summary>
        public string[] Terrain = Array.Empty<string>();
        public string Tag;
        public int? Water;
        /// <summary>Cells round the listener to look in (a square), and how many must match.</summary>
        public int Within, AtLeast = 1;
        /// <summary>Inside a village (its bounds grown by Within).</summary>
        public bool Village;
        /// <summary>Within Within cells of a village building of this kind (inn, shop, house).</summary>
        public string Building;
    }

    /// <summary>One soundscape: where it holds, when, and the loops it plays.</summary>
    public sealed class SoundscapeDef
    {
        public string Id;
        public SoundscapeNear Near = new SoundscapeNear();
        /// <summary>[from, to] hours on the world clock (Sky.InHours); null for any hour.</summary>
        public (double from, double to)? Hours;
        public List<SoundLoop> Loops = new List<SoundLoop>();
    }

    /// <summary>The world's soundscapes, and which hold where.</summary>
    public sealed class Soundscapes
    {
        public const string FileName = "soundscapes.json";

        readonly List<SoundscapeDef> all;

        Soundscapes(List<SoundscapeDef> all) => this.all = all;

        public IReadOnlyList<SoundscapeDef> All => all;

        public static Soundscapes Empty => new Soundscapes(new List<SoundscapeDef>());

        /// <summary>world/soundscapes.json, or none if the file isn't there.</summary>
        public static Soundscapes Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }

        /// <summary>The table from its text. Throws <see cref="FormatException"/> for one that isn't JSON or has no soundscapes list.</summary>
        public static Soundscapes Parse(string json)
        {
            JObject doc;
            try
            {
                doc = JObject.Parse(json ?? "");
            }
            catch (JsonReaderException e)
            {
                throw new FormatException("soundscapes.json isn't JSON: " + e.Message, e);
            }
            if (!(doc["soundscapes"] is JArray list)) throw new FormatException("soundscapes.json needs a \"soundscapes\" list");
            return new Soundscapes(list.OfType<JObject>().Select(ParseOne).ToList());
        }

        static SoundscapeDef ParseOne(JObject o)
        {
            var near = o["near"] as JObject ?? new JObject();
            var def = new SoundscapeDef
            {
                Id = (string)o["id"],
                Near = new SoundscapeNear
                {
                    Terrain = (near["terrain"] as JArray)?.Select(t => (string)t).ToArray() ?? Array.Empty<string>(),
                    Tag = (string)near["tag"],
                    Water = (int?)near["water"],
                    Within = (int?)near["within"] ?? 0,
                    AtLeast = (int?)near["atLeast"] ?? 1,
                    Village = (bool?)near["village"] ?? false,
                    Building = (string)near["building"],
                },
            };
            if (o["hours"] is JArray h && h.Count == 2) def.Hours = ((double)h[0], (double)h[1]);
            foreach (var l in (o["loops"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                def.Loops.Add(new SoundLoop((string)l["sound"], (double?)l["volume"] ?? 1));
            return def;
        }

        /// <summary>Problems with the table against the rules (unknown terrain or tag, bad hours or volume); empty means fine.</summary>
        public List<string> Check(Rules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = Enum.GetNames(typeof(BuildingKind)).Select(k => k.ToLowerInvariant()).ToList();
            foreach (var s in all)
            {
                string what = $"soundscape {s.Id ?? "(no id)"}";
                if (string.IsNullOrEmpty(s.Id)) errors.Add("a soundscape has no id");
                else if (!ids.Add(s.Id)) errors.Add($"{what}: duplicate id");
                var n = s.Near;
                foreach (var t in n.Terrain.Where(t => !rules.Terrain.ContainsKey(t))) errors.Add($"{what}: unknown terrain {t}");
                if (n.Tag != null && !rules.Terrain.Values.Any(t => t.Has(n.Tag))) errors.Add($"{what}: no terrain has the tag {n.Tag}");
                if (n.Building != null && !kinds.Contains(n.Building)) errors.Add($"{what}: building must be one of {string.Join(", ", kinds)}");
                bool counts = n.Terrain.Length > 0 || n.Tag != null || n.Water.HasValue;
                if (!counts && !n.Village && n.Building == null) errors.Add($"{what}: near says nothing (terrain, tag, water, village or building)");
                if (n.Within < 0 || n.AtLeast < 1) errors.Add($"{what}: within must be at least 0 and atLeast at least 1");
                if (s.Hours is (double from, double to) && !(from >= 0 && from <= 24 && to >= 0 && to <= 24)) errors.Add($"{what}: hours must be [from, to], hours from 0 to 24");
                if (s.Loops.Count == 0) errors.Add($"{what}: no loops");
                foreach (var l in s.Loops.Where(l => string.IsNullOrEmpty(l.Sound) || l.Volume < 0 || l.Volume > 1))
                    errors.Add($"{what}: a loop needs a sound and a volume from 0 to 1");
            }
            return errors;
        }

        /// <summary>The ids of the soundscapes that hold at cell (x, y) now, in the table's order.</summary>
        public List<string> Matching(World world, IReadOnlyList<VillagePlan> villages, int x, int y)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int minute = Sky.MinuteOfDay(world.Minutes);
            return all.Where(s => (!s.Hours.HasValue || Sky.InHours(s.Hours.Value.from, s.Hours.Value.to, minute))
                                  && Holds(world, villages, s.Near, x, y)).Select(s => s.Id).ToList();
        }

        /// <summary>
        /// The loops to play at cell (x, y) now: every holding soundscape's, each sound once at its
        /// loudest, sorted by sound. The view fades between one answer and the next.
        /// </summary>
        public List<SoundLoop> At(World world, IReadOnlyList<VillagePlan> villages, int x, int y)
        {
            var loudest = new Dictionary<string, double>(StringComparer.Ordinal);
            var ids = new HashSet<string>(Matching(world, villages, x, y), StringComparer.Ordinal);
            foreach (var s in all.Where(s => ids.Contains(s.Id)))
                foreach (var l in s.Loops)
                    loudest[l.Sound] = loudest.TryGetValue(l.Sound, out var v) ? Math.Max(v, l.Volume) : l.Volume;
            return loudest.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new SoundLoop(kv.Key, kv.Value)).ToList();
        }

        static bool Holds(World world, IReadOnlyList<VillagePlan> villages, SoundscapeNear n, int x, int y)
        {
            villages ??= Array.Empty<VillagePlan>();
            if (n.Village && !villages.Any(v => v.Bounds.Grown(n.Within).Contains(x, y))) return false;
            if (n.Building != null && !villages.Any(v => v.Buildings.Any(b =>
                    string.Equals(b.Kind.ToString(), n.Building, StringComparison.OrdinalIgnoreCase) && b.Footprint.Grown(n.Within).Contains(x, y))))
                return false;
            if (n.Terrain.Length == 0 && n.Tag == null && !n.Water.HasValue) return true;
            int count = 0;
            for (int cy = y - n.Within; cy <= y + n.Within; cy++)
                for (int cx = x - n.Within; cx <= x + n.Within; cx++)
                {
                    var c = world.At(cx, cy);
                    if (c == null) continue;
                    bool match = (n.Terrain.Length > 0 && Array.IndexOf(n.Terrain, c.T) >= 0)
                                 || (n.Tag != null && world.HasTag(c, n.Tag))
                                 || (n.Water.HasValue && c.Water >= n.Water.Value);
                    if (match && ++count >= n.AtLeast) return true;
                }
            return false;
        }
    }
}
