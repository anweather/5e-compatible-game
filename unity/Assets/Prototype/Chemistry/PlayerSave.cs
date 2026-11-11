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
    /// <summary>
    /// A player's save: their id (opaque, as the caller has it), the Hold's points and the combos they know. Version 1 is
    /// the Hold's progress file (points alone), still read; a save is written as version 2.
    /// </summary>
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

        /// <summary>Who it's for, as the caller names them; the save's own if none was given. Null for none.</summary>
        public string Id { get; private set; }
        /// <summary>The Hold's points: they only go up.</summary>
        public int Points { get; private set; }
        /// <summary>The combos the player knows, kept but not yet brought to a world (step 4 of the note).</summary>
        public SortedSet<string> Known { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>What went wrong loading, for the player to hear; null if nothing did.</summary>
        public string Trouble { get; private set; }

        /// <summary>Reads the save, or failing that its last good copy. A save half written (a crash before its rename) isn't read.</summary>
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

        /// <summary>Takes these points if they're more than it has; whether they were.</summary>
        public bool Raise(int points)
        {
            if (points <= Points) return false;
            Points = points;
            return true;
        }

        /// <summary>
        /// Writes the save: whole, to a file beside it, then put in place by a rename, the last good one kept as the copy.
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + Fresh, Text());
            if (!File.Exists(path)) File.Move(path + Fresh, path);
            else if (Readable(path)) File.Replace(path + Fresh, path, path + Backup);
            else File.Replace(path + Fresh, path, null); // a bad one was kept as .broken when it was loaded
        }

        /// <summary>The save as it's written: the same text for the same progress.</summary>
        public string Text()
        {
            var o = new JObject { ["version"] = Version };
            if (Id != null) o["id"] = Id;
            o["points"] = Points;
            o["known"] = new JArray(Known.Cast<object>().ToArray());
            return o.ToString(Formatting.Indented) + "\n";
        }

        // Reads a save into this one; false, and nothing changed, if it isn't a good save.
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

        // The first free name: bad saves are never overwritten.
        static string Unused(string name)
        {
            string file = name;
            int n = 1;
            while (File.Exists(file)) file = name + (n++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return file;
        }
    }
}
