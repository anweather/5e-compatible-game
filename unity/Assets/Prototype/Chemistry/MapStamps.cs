// A map's own land stamps (docs/design/maps-as-data.md, section 3): world/maps/<id>/stamps.json, the cliffs, canyons and
// hills the map is shaped with beyond its land.json, laid on every peer alike when the world is made (World.LayMapStamps).
// Each is a LandStamp, so it's drawn, painted and dressed with rocks as a stamp an act lays is, but no undo or reset takes it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One of a map's stamps: its id and name, and the stamp.</summary>
    public sealed class MapStamp
    {
        public string Id { get; }
        public string Name { get; }
        public LandStamp Stamp { get; }

        /// <summary>A canyon's trail along its floor: the terrain it lays (dirt), or null for none.</summary>
        public string Trail { get; }

        public MapStamp(string id, string name, LandStamp stamp, string trail = null)
        {
            Id = id;
            Name = name;
            Stamp = stamp;
            Trail = trail;
        }
    }

    /// <summary>A map's stamps.json, in its order: none when there's no file.</summary>
    public sealed class MapStamps
    {
        public const string FileName = "stamps.json";

        public static readonly MapStamps Empty = new MapStamps(new List<MapStamp>());

        MapStamps(List<MapStamp> all) { All = all; }

        public IReadOnlyList<MapStamp> All { get; }

        /// <summary>Reads stamps.json from <paramref name="worldDir"/>; no file means no stamps.</summary>
        public static MapStamps Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

        /// <summary>
        /// The stamps in the file's text, each {id, name, at: [x, y], profile, radius, rise (land units), rough, seed}, and
        /// along: [x, y] or path: [[x, y], ...] from at for a line, and for a canyon a trail: the terrain laid along its floor. Throws <see cref="FormatException"/> for one that can't be read.
        /// </summary>
        public static MapStamps FromJson(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"{FileName}: {e.Message}", e); }
            var list = new List<MapStamp>();
            foreach (var o in (doc["stamps"] as JArray ?? new JArray()).OfType<JObject>()) list.Add(Read(o));
            var twice = list.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
            if (twice != null) throw new FormatException($"{FileName}: the id {twice.Key} is used twice");
            return new MapStamps(list);
        }

        static MapStamp Read(JObject o)
        {
            string id = (string)o["id"];
            if (string.IsNullOrEmpty(id)) throw new FormatException($"{FileName}: a stamp needs an \"id\"");
            if (!(o["at"] is JArray at) || at.Count != 2) throw new FormatException($"{FileName}: {id} needs \"at\": [x, y]");
            var along = o["along"] as JArray;
            var stamp = LandStamp.Make((int)at[0], (int)at[1], (int?)o["radius"] ?? 0, (int?)o["rise"] ?? 0, (string)o["profile"] ?? "",
                along != null && along.Count == 2 ? (int)along[0] : 0, along != null && along.Count == 2 ? (int)along[1] : 0,
                (int?)o["rough"] ?? 40, (int?)o["seed"] ?? 0);
            if (o["path"] is JArray path)
            {
                if (path.Any(p => !(p is JArray xy) || xy.Count != 2)) throw new FormatException($"{FileName}: {id}'s path is [[x, y], ...]");
                stamp = stamp.Along(path.Select(p => ((int)p[0], (int)p[1])));
            }
            if (!stamp.Known) throw new FormatException($"{FileName}: {id} isn't a land stamp (a profile, a radius, rough 0 to 100, and a line or a path of two points or more for a canyon or cliff)");
            string trail = (string)o["trail"];
            if (trail != null && stamp.Profile != LandStamp.Canyon) throw new FormatException($"{FileName}: {id} has a trail, and only a canyon's floor takes one");
            return new MapStamp(id, (string)o["name"] ?? id, stamp, trail);
        }

        /// <summary>Lays them on a world as it's made, and a canyon's trail on its floor's dry cells; why any was refused, by id.</summary>
        public List<string> LayInto(World world)
        {
            var refused = new List<string>();
            foreach (var s in All)
            {
                var why = world.LayMapStamps(new[] { s.Stamp });
                refused.AddRange(why.Select(w => $"{FileName}: {s.Id} isn't laid, {w}"));
                if (why.Count > 0 || s.Trail == null) continue;
                foreach (var (x, y) in s.Stamp.Trail())
                    if (world.InBounds(x, y) && world.At(x, y).Water == 0) world.Paint(x, y, s.Trail);
            }
            return refused;
        }
    }
}
