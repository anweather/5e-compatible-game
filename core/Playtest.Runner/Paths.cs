using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

// Where things are, for the playtest runner: the repository, the bot's own
// checkout (.claude/worktrees/playtest-build, next to the main checkout), the
// player it runs, and a build's commit.
namespace Playtest.Runner
{
    public sealed class Paths
    {
        Paths(string root, string main)
        {
            Root = root;
            Main = main;
        }

        /// <summary>This checkout (the one the runner was started in).</summary>
        public string Root { get; }
        /// <summary>The main checkout, whose .claude/worktrees the bot's checkout lives in.</summary>
        public string Main { get; }

        public string Runs => Path.Combine(Root, "playtest", "runs");
        public string Coverage => Path.Combine(Root, "playtest", "coverage.json");
        public string KnownIssues => Path.Combine(Root, "playtest", "known-issues.json");
        public string Checkout => Path.Combine(Main, ".claude", "worktrees", "playtest-build");

        public const string PlayerName = "SpellChemistryValley";

        /// <summary>
        /// The player to run: the one given (--player: an exe, or a folder with one in it, such as a
        /// release's unpacked portable zip), else the bot's checkout's build if there is one, else this checkout's.
        /// </summary>
        public string Player(string given = null) => given != null ? GivenPlayer(given) : ChoosePlayer(Checkout, Root, File.Exists);

        /// <summary>An exe as given, or the player found in a folder (the shallowest, then by name); the folder's own exe path if there is none.</summary>
        public static string GivenPlayer(string given)
        {
            string full = Path.GetFullPath(given);
            if (!Directory.Exists(full)) return full;
            return Directory.EnumerateFiles(full, PlayerName + ".exe", SearchOption.AllDirectories)
                .OrderBy(f => f.Count(ch => ch == Path.DirectorySeparatorChar)).ThenBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault() ?? Path.Combine(full, PlayerName + ".exe");
        }

        public static string ChoosePlayer(string checkout, string root, Func<string, bool> exists)
        {
            string own = Exe(checkout);
            return exists(own) ? own : Exe(root);
        }

        static string Exe(string checkout) => Path.Combine(checkout, "unity", "Builds", "Windows", PlayerName + ".exe");

        /// <summary>The commit a build was made from (StreamingAssets/build.json), or "unknown".</summary>
        public static string BuildCommit(string exe)
        {
            string stamp = Path.Combine(Path.GetDirectoryName(exe) ?? "", PlayerName + "_Data", "StreamingAssets", "build.json");
            if (!File.Exists(stamp)) return "unknown";
            string commit = (string)JObject.Parse(File.ReadAllText(stamp))["commit"] ?? "unknown";
            return commit.EndsWith("-dirty", StringComparison.Ordinal) ? commit.Substring(0, commit.Length - "-dirty".Length) : commit;
        }

        /// <summary>The repository round a folder, and its main checkout.</summary>
        public static Paths Find(string from)
        {
            string root = Git.Run(from, "rev-parse --show-toplevel").Trim();
            string common = Git.Run(from, "rev-parse --path-format=absolute --git-common-dir").Trim();
            return new Paths(Path.GetFullPath(root), Path.GetDirectoryName(Path.GetFullPath(common)));
        }
    }
}
