// The player save (docs/design/player-saves.md, step 3; J-038): one player's progress, on their own machine, apart from any
// world. It grew out of the Hold's progress file and keeps its name and its care: a save that won't load is kept beside
// it, the last good copy is used if there is one, and the points are never written lower. A save is written whole to a
// file beside it, then put in place by a rename, so a crash mid-write leaves the last save as it was. The text is the
// same for the same progress (keys in one order, known combos sorted). Engine-free; what a peer brings of it reaches the
// world only as a command (ComboCommand.Bring), never by a peer writing its own state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class PlayerSave
    {
        public const string FileName = "progress.json";
        public const int Version = 2;
        const int PointsOnly = 1;
        const string Backup = ".bak", Broken = ".broken", Fresh = ".new";

        readonly string path;

        public PlayerSave(string folder, string id = null)
        {
            path = Path.Combine(folder, FileName);
            Id = id;
        }

        public string Id { get; private set; }
        public int Points { get; private set; }
        public SortedSet<string> Known { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public string Trouble { get; private set; }

        public void Load()
        {
            Trouble = null;
            if (!File.Exists(path) && !File.Exists(path + Backup)) return;
            if (Read(path)) return;
            if (File.Exists(path)) File.Copy(path, Unused(path + Broken));
            if (Read(path + Backup))
            {
                Trouble = "Your progress save wouldn't load: the last good copy was used, and the bad one kept.";
                return;
            }
            Trouble = "Your progress save wouldn't load, and it's been kept beside the new one.";
        }

        public bool Raise(int points)
        {
            if (points <= Points) return false;
            Points = points;
            return true;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + Fresh, Text());
            if (!File.Exists(path)) File.Move(path + Fresh, path);
            else if (Readable(path)) File.Replace(path + Fresh, path, path + Backup);
            else File.Replace(path + Fresh, path, null);
        }

        public string Text()
        {
            var o = new JObject { ["version"] = Version };
            if (Id != null) o["id"] = Id;
            o["points"] = Points;
            o["known"] = new JArray(Known.Cast<object>().ToArray());
            using (var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" })
            {
                using (var json = new JsonTextWriter(text) { Formatting = Formatting.Indented, Indentation = 2 }) o.WriteTo(json);
                return text + "\n";
            }
        }

        bool Read(string file)
        {
            if (!TryRead(file, out var id, out int points, out var known)) return false;
            Id ??= id;
            Points = points;
            Known.Clear();
            Known.UnionWith(known);
            return true;
        }

        static bool Readable(string file) => TryRead(file, out _, out _, out _);

        static bool TryRead(string file, out string id, out int points, out List<string> known)
        {
            id = null;
            points = 0;
            known = new List<string>();
            try
            {
                if (!File.Exists(file)) return false;
                var o = JObject.Parse(File.ReadAllText(file));
                int? version = (int?)o["version"];
                if (version != PointsOnly && version != Version) return false;
                if (!(o["points"] is JValue v) || v.Type != JTokenType.Integer) return false;
                points = (int)v;
                if (points < 0) return false;
                if (version == PointsOnly) return true;
                if (o["id"] is JToken i && i.Type != JTokenType.String) return false;
                id = (string)o["id"];
                if (!(o["known"] is JArray combos) || combos.Any(c => c.Type != JTokenType.String)) return false;
                known.AddRange(combos.Select(c => (string)c));
                return true;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is OverflowException || e is InvalidCastException)
            {
                return false;
            }
        }

        static string Unused(string name)
        {
            string file = name;
            int n = 1;
            while (File.Exists(file)) file = name + (n++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return file;
        }
    }
}
