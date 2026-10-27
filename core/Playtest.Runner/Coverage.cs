using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// What the bot has done lately (playtest/coverage.json), so the next runs lean
// elsewhere: a count per goal key, older counts fading by a fifth each run.
namespace Playtest.Runner
{
    public static class Coverage
    {
        /// <summary>The counts after a run: each old count times 0.8 (rounded down), plus the run's goals; zeros are dropped.</summary>
        public static SortedDictionary<string, int> After(IReadOnlyDictionary<string, int> before, IEnumerable<string> goals)
        {
            var counts = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            if (before != null)
                foreach (var (key, n) in before) counts[key] = n * 4 / 5;
            foreach (string g in goals ?? Enumerable.Empty<string>()) counts[g] = (counts.TryGetValue(g, out int n) ? n : 0) + 1;
            foreach (string key in counts.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) counts.Remove(key);
            return counts;
        }

        public static void Update(string file, IEnumerable<string> goals)
        {
            var before = File.Exists(file) ? JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(file)) : null;
            File.WriteAllText(file, JsonConvert.SerializeObject(After(before, goals), Formatting.Indented));
        }
    }
}
