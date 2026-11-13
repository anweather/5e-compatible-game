// The mods on disk with their titles and ideas, for the in-game mods panel and
// the file watcher: the shipped ones in the install's rules/mods and the player's
// own in their data folder, where the player's copy wins an id both have. Reading
// a file only parses it; a mod that doesn't fit the rules is found when it is
// switched on (RuleSet). Where a file lives ends here: a mod goes into the room as
// its id and JSON, so no peer needs this folder and no hash can see it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class ModFile
    {
        ModFile(string id, string json, string title, string idea, string error)
        {
            Id = id;
            Json = json;
            Title = title;
            Idea = idea;
            Error = error;
        }

        /// <summary>The file name without .json: how the mod is named on the wire and in the panel.</summary>
        public string Id { get; }
        public string Json { get; }
        /// <summary>Its "title", or the id when it has none.</summary>
        public string Title { get; }
        /// <summary>Its "idea" (one sentence on what it changes), or empty.</summary>
        public string Idea { get; }
        /// <summary>Why it can't be read, or null when it parses.</summary>
        public string Error { get; }

        public ActiveMod AsActive() => new ActiveMod(Id, Json);

        /// <summary>A mod from its JSON text; never throws (a bad file comes back with <see cref="Error"/> set).</summary>
        public static ModFile FromJson(string id, string json)
        {
            string title = id, idea = "", error = null;
            try
            {
                var mod = ModPatch.Parse(json);
                title = mod.Title ?? id;
                if (JToken.Parse(json) is JObject o && o["idea"]?.Type == JTokenType.String) idea = (string)o["idea"];
            }
            catch (ModException e)
            {
                error = string.Join("; ", e.Errors);
            }
            return new ModFile(id, json, title, idea, error);
        }
    }

    public sealed class ModCatalog
    {
        readonly List<ModFile> mods = new List<ModFile>();

        public ModCatalog(string modsDir, string playerDir = null)
        {
            Dir = modsDir;
            PlayerDir = playerDir;
            Reload();
        }

        /// <summary>The shipped mods: rules/mods.</summary>
        public string Dir { get; }
        /// <summary>The player's own mods, or null where there are none (a headless run).</summary>
        public string PlayerDir { get; }
        /// <summary>The mods, sorted by id.</summary>
        public IReadOnlyList<ModFile> Mods => mods;

        public ModFile Find(string id) => mods.FirstOrDefault(m => m.Id == id);

        public static string IdOf(string path) => Path.GetFileNameWithoutExtension(path);

        /// <summary>The file a mod's id means: the first folder holding it, or null. Pass the player's folder first; a null folder is skipped.</summary>
        public static string FileOf(string id, params string[] dirs) =>
            dirs.Where(d => d != null).Select(d => Path.Combine(d, id + ".json")).FirstOrDefault(File.Exists);

        /// <summary>Reads every *.json in both folders again (none from one that doesn't exist).</summary>
        public void Reload()
        {
            mods.Clear();
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var dir in new[] { Dir, PlayerDir }.Where(d => d != null && Directory.Exists(d)))
                foreach (var path in Directory.GetFiles(dir, "*.json"))
                    files[IdOf(path)] = path;
            mods.AddRange(files.Values.Select(Read));
        }

        /// <summary>
        /// Reads a mod again after one of its files changed: the entry that id now means, which is the shipped
        /// one when the player's copy was deleted, or null if neither is left (it is then dropped from the list).
        /// IO errors (a file still being written) propagate to the caller.
        /// </summary>
        public ModFile Refresh(string path)
        {
            string id = IdOf(path);
            mods.RemoveAll(m => m.Id == id);
            string file = FileOf(id, PlayerDir, Dir);
            if (file == null) return null;
            var mod = Read(file);
            mods.Add(mod);
            mods.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return mod;
        }

        static ModFile Read(string path) => ModFile.FromJson(IdOf(path), File.ReadAllText(path));
    }
}
