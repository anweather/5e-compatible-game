// The mods on disk: every rules/mods/*.json with its title and idea, for the
// in-game mods panel and the file watcher. Reading a file only parses it; a
// mod that doesn't fit the rules is found when it is switched on (RuleSet).
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

        public ModCatalog(string modsDir)
        {
            Dir = modsDir;
            Reload();
        }

        public string Dir { get; }
        /// <summary>The mods, sorted by id.</summary>
        public IReadOnlyList<ModFile> Mods => mods;

        public ModFile Find(string id) => mods.FirstOrDefault(m => m.Id == id);

        public static string IdOf(string path) => Path.GetFileNameWithoutExtension(path);

        /// <summary>Reads every *.json in the folder again (none if it doesn't exist).</summary>
        public void Reload()
        {
            mods.Clear();
            if (Dir == null || !Directory.Exists(Dir)) return;
            foreach (var path in Directory.GetFiles(Dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                mods.Add(Read(path));
        }

        /// <summary>
        /// Reads one file again after it changed: the new entry, or null if it was deleted (it is then
        /// dropped from the list). IO errors (a file still being written) propagate to the caller.
        /// </summary>
        public ModFile Refresh(string path)
        {
            string id = IdOf(path);
            mods.RemoveAll(m => m.Id == id);
            if (!File.Exists(path)) return null;
            var mod = Read(path);
            mods.Add(mod);
            mods.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return mod;
        }

        static ModFile Read(string path) => ModFile.FromJson(IdOf(path), File.ReadAllText(path));
    }
}
