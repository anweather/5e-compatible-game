// Where the game's folders are, worked out in one place: rules/, world/ beside it, and the
// player's data folder with the chat's folder (ClaudeFolder) and their saved mods in it. The view
// hands in the roots only Unity knows (a build's StreamingAssets, the project, the data folder);
// the MCP server, chatbench and the tests hand in where they start. Nothing else works a
// folder out (docs/ARCHITECTURE.md, "Where the game's folders are").
using System.IO;

namespace Chem
{
    /// <summary>The game's folders: rules/, world/ beside it, and the player's own when there is a player.</summary>
    public sealed class GameFolders
    {
        const string RulesName = "rules", WorldName = "world", ModsName = "mods", Marker = "materials.json";

        /// <summary>A given rules folder (the MCP server's --rules, a test's copy), world/ beside it, and the player's data folder if there is one.</summary>
        public GameFolders(string rules, string playerData = null)
        {
            Rules = rules;
            Repo = Path.GetDirectoryName(Path.GetFullPath(rules).TrimEnd('/', '\\')) ?? "";
            World = Path.Combine(Repo, WorldName);
            ShippedMods = Path.Combine(rules, ModsName);
            PlayerData = playerData;
            Claude = playerData == null ? null : new ClaudeFolder(playerData);
        }

        /// <summary>rules/: the rules JSON, looks and budgets.</summary>
        public string Rules { get; }

        /// <summary>
        /// The folder rules/ is in: the repo's root, with docs/, tools/ and the rest of a checkout, for anything run from one
        /// (the Editor, the tests, a build being packed); a build reading its pack has only rules/ and world/ there.
        /// </summary>
        public string Repo { get; }

        /// <summary>world/, beside rules/: things, places, buildings, land and themes.</summary>
        public string World { get; }

        /// <summary>rules/mods: the shipped mods, which come with the build.</summary>
        public string ShippedMods { get; }

        /// <summary>The player's data folder, which an update leaves alone; null with no player (the MCP server, chatbench, the tests).</summary>
        public string PlayerData { get; }

        /// <summary>The chat's folder in the player's data folder; null with no player.</summary>
        public ClaudeFolder Claude { get; }

        /// <summary>
        /// Where a save goes, read before the shipped mods: the player's, beside notes.md, in the Editor too, so a save
        /// there leaves the repo's rules/mods alone. With no player it's rules/mods, which is how the MCP server ships a mod.
        /// </summary>
        public string Mods => Claude?.Mods ?? ShippedMods;

        /// <summary>
        /// The game's: a build's rules packed into its StreamingAssets, else (the Editor, or a build without them) the repo's
        /// rules/ found by walking up from the project. A fixed "../../rules" would miss it for Multiplayer Play Mode's
        /// virtual players, whose project copies live deeper (unity/Library/VP/&lt;id&gt;/).
        /// </summary>
        public static GameFolders ForGame(string streamingAssets, string project, bool editor, string playerData)
        {
            string packed = Path.Combine(streamingAssets, RulesName);
            return new GameFolders(!editor && File.Exists(Path.Combine(packed, Marker)) ? packed : RulesAbove(project), playerData);
        }

        /// <summary>The repo's rules/ above a folder, with no player: anything started inside the repo.</summary>
        public static GameFolders InRepo(string start) => new GameFolders(RulesAbove(start));

        static string RulesAbove(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, RulesName);
                if (File.Exists(Path.Combine(candidate, Marker))) return candidate;
            }
            throw new DirectoryNotFoundException($"No rules/ folder with {Marker} above {start}");
        }
    }

    /// <summary>
    /// The chat's folder, claude-terminal in the player's data folder: where the terminal and the chat run claude, with its
    /// .mcp.json, CLAUDE.md, notes.md and the player's saved mods.
    /// </summary>
    public sealed class ClaudeFolder
    {
        /// <summary>Its name in the player's data folder.</summary>
        public const string Name = "claude-terminal";

        readonly string own;

        public ClaudeFolder(string playerData) => own = System.IO.Path.Combine(playerData, Name);

        /// <summary>The folder: a probe's while <see cref="Test"/> is set, else the player's.</summary>
        public string Path => Test ?? own;

        /// <summary>
        /// A probe's folder in place of the player's, as an Editor shares its data folder with their game and their notes.md
        /// is there. While it's set, a sign-in failure opens no terminal: nobody is at the keys.
        /// </summary>
        public string Test { get; set; }

        /// <summary>The player's saved mods.</summary>
        public string Mods => System.IO.Path.Combine(Path, "mods");
    }
}
