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

        public string Id { get; }
        public string Json { get; }
        public string Title { get; }
        public string Idea { get; }
        public string Error { get; }

        public ActiveMod AsActive() => new ActiveMod(Id, Json);

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

        public string Dir { get; }
        public string PlayerDir { get; }
        public IReadOnlyList<ModFile> Mods => mods;

        public ModFile Find(string id) => mods.FirstOrDefault(m => m.Id == id);

        public static string IdOf(string path) => Path.GetFileNameWithoutExtension(path);

        public static string FileOf(string id, params string[] dirs) =>
            dirs.Where(d => d != null).Select(d => Path.Combine(d, id + ".json")).FirstOrDefault(File.Exists);

        public void Reload()
        {
            mods.Clear();
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var dir in new[] { Dir, PlayerDir }.Where(d => d != null && Directory.Exists(d)))
                foreach (var path in Directory.GetFiles(dir, "*.json"))
                    files[IdOf(path)] = path;
            mods.AddRange(files.Values.Select(Read));
        }

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
