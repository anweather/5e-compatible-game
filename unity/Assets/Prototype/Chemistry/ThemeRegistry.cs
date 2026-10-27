// The world themes (D11): world/looks.json (the looks a theme may paint) and
// world/themes/*.json, each checked as a file, merged with its mix-ins and
// checked again, like PocketTactics' src/core/world/theme/registry.ts. A
// mix-in ("mixin": true) is never a theme on its own.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class ThemeRegistry
    {
        readonly Dictionary<string, Theme> themes = new Dictionary<string, Theme>();

        /// <summary>The looks a theme may paint, and what each says (world/looks.json).</summary>
        public IReadOnlyDictionary<string, JObject> Looks { get; }

        /// <summary>The playable themes (no mix-ins), by id.</summary>
        public IReadOnlyDictionary<string, Theme> Themes => themes;

        ThemeRegistry(Dictionary<string, JObject> looks) => Looks = looks;

        /// <summary>Loads the themes under a world/ folder; throws with every problem found.</summary>
        public static ThemeRegistry Load(string worldDir)
        {
            var looksJson = JObject.Parse(File.ReadAllText(Path.Combine(worldDir, "looks.json")));
            var looks = looksJson.Properties().Where(p => !p.Name.StartsWith('$'))
                .ToDictionary(p => p.Name, p => (JObject)p.Value);
            var registry = new ThemeRegistry(looks);
            var files = new Dictionary<string, JObject>();
            var errors = new List<string>();
            foreach (var path in Directory.GetFiles(Path.Combine(worldDir, "themes"), "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var fileErrors = ThemeSchema.ValidateFile(json, looks.Keys);
                errors.AddRange(fileErrors.Select(e => $"{Path.GetFileName(path)}: {e}"));
                if (fileErrors.Count == 0) files[(string)json["id"]] = json;
            }
            foreach (var file in files.Values)
            {
                if ((bool?)file["mixin"] ?? false) continue;
                try
                {
                    var theme = ThemeSchema.Merge(file, id => files.TryGetValue(id, out var f) ? f : null);
                    var merged = ThemeSchema.Validate(theme, looks.Keys);
                    errors.AddRange(merged.Select(e => $"{theme.Id}: {e}"));
                    if (merged.Count == 0) registry.themes[theme.Id] = theme;
                }
                catch (InvalidOperationException e)
                {
                    errors.Add(e.Message);
                }
            }
            if (errors.Count > 0) throw new InvalidDataException("World themes:\n" + string.Join("\n", errors));
            return registry;
        }
    }
}
