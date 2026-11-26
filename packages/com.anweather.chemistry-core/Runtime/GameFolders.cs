// Where the game's folders are, worked out in one place: rules/, the map's folder under world/maps/ beside it, and the
// player's data folder with the chat's folder (ClaudeFolder) and their saved mods in it. The view
// hands in the roots only Unity knows (a build's StreamingAssets, the project, the data folder);
// the MCP server, chatbench and the tests hand in where they start. Nothing else works a
// folder out (docs/ARCHITECTURE.md, "Where the game's folders are").
using System.IO;

namespace Chem
{
    public sealed class GameFolders
    {
        public const string DefaultMap = "valley";

        const string RulesName = "rules", WorldName = "world", MapsName = "maps", ModsName = "mods", Marker = "materials.json", EditorDataName = "EditorPlayerData";

        public GameFolders(string rules, string playerData = null, string map = DefaultMap)
        {
            Rules = rules;
            Repo = Path.GetDirectoryName(Path.GetFullPath(rules).TrimEnd('/', '\\')) ?? "";
            Map = map;
            Maps = Path.Combine(Repo, WorldName, MapsName);
            World = Path.Combine(Maps, map);
            ShippedMods = Path.Combine(rules, ModsName);
            PlayerData = playerData;
            Claude = playerData == null ? null : new ClaudeFolder(playerData);
        }

        GameFolders(GameFolders folders, string map) : this(folders.Rules, folders.PlayerData, map) => Claude = folders.Claude;

        public GameFolders WithMap(string map) => new GameFolders(this, map);

        public string Rules { get; }

        public string Repo { get; }

        public string Map { get; }

        public string Maps { get; }

        public string World { get; }

        public string ShippedMods { get; }

        public string PlayerData { get; }

        public ClaudeFolder Claude { get; }

        public string ApiKeyFile => PlayerData == null ? null : System.IO.Path.Combine(PlayerData, "api-key.dat");

        public string ChatSetupFile => PlayerData == null ? null : System.IO.Path.Combine(PlayerData, "chat-setup.dat");

        public string Mods => Claude?.Mods ?? ShippedMods;

        public static GameFolders ForGame(string streamingAssets, string project, bool editor, string playerData, string map = DefaultMap)
        {
            string packed = Path.Combine(streamingAssets, RulesName);
            return new GameFolders(!editor && File.Exists(Path.Combine(packed, Marker)) ? packed : RulesAbove(project),
                editor ? EditorPlayerData(project) : playerData, map);
        }

        public static string EditorPlayerData(string project) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(project).TrimEnd('/', '\\')) ?? project, "Library", EditorDataName);

        public static GameFolders InRepo(string start, string map = DefaultMap) => new GameFolders(RulesAbove(start), null, map);

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

    public sealed class ClaudeFolder
    {
        public const string Name = "claude-terminal";

        readonly string own;

        public ClaudeFolder(string playerData) => own = System.IO.Path.Combine(playerData, Name);

        public string Path => Test ?? own;

        public string Test { get; set; }

        public string Mods => System.IO.Path.Combine(Path, "mods");
    }
}
