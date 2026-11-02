// One headless Claude Code process for one message from the game's chat box (J-060): what it's
// started with, what its stream-json output says, where it's installed and how far its notes
// may grow. The process itself is the view's (ClaudeChat). The game only ever starts the player's
// own claude, signed in or not: it never sees a credential.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>What a line of Claude's output said: its words, a tool it's using, or that it finished.</summary>
    public enum ClaudeSaid
    {
        Words,
        Tool,
        Done,
        Failed,
    }

    public static class ClaudeRun
    {
        /// <summary>The notes the next process reads, beside .mcp.json, and how many lines of it are kept.</summary>
        public const string NotesFile = "notes.md";

        public const int MostNoteLines = 60;

        /// <summary>The MCP server's name in .mcp.json, and so in its tools' names.</summary>
        public const string ServerName = "spell-valley";

        const string ToolPrefix = "mcp__" + ServerName + "__";

        /// <summary>
        /// The arguments: one message read from stdin, the game's tools and the notes file allowed, and
        /// anything else refused rather than asked about, as nobody is there to answer a prompt.
        /// </summary>
        public static IReadOnlyList<string> Arguments() => new[]
        {
            "-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence",
            "--mcp-config", ".mcp.json", "--strict-mcp-config",
            "--permission-mode", "dontAsk", "--tools", "Read,Edit,Write",
            "--allowedTools", "mcp__" + ServerName, $"Read(./{NotesFile})", $"Edit(./{NotesFile})", $"Write(./{NotesFile})",
        };

        /// <summary>One line of stream-json as what the chat shows: none for what it doesn't.</summary>
        public static IEnumerable<(ClaudeSaid said, string text)> Read(string line)
        {
            JObject o;
            try
            {
                o = JObject.Parse(line);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                yield break;
            }
            string type = (string)o["type"];
            if (type == "result")
            {
                bool failed = (bool?)o["is_error"] == true || ((string)o["subtype"] ?? "success") != "success";
                string text = (string)o["result"];
                // Its words came on the way here; a failure's reason doesn't.
                yield return (failed ? ClaudeSaid.Failed : ClaudeSaid.Done, failed ? text ?? "Claude stopped." : text);
                yield break;
            }
            if (type != "assistant" || !(o["message"]?["content"] is JArray content)) yield break;
            foreach (var part in content.OfType<JObject>())
            {
                if ((string)part["type"] == "text" && !string.IsNullOrWhiteSpace((string)part["text"])) yield return (ClaudeSaid.Words, ((string)part["text"]).Trim());
                else if ((string)part["type"] == "tool_use") yield return (ClaudeSaid.Tool, ToolName((string)part["name"]));
            }
        }

        static string ToolName(string name) => name != null && name.StartsWith(ToolPrefix, StringComparison.Ordinal) ? name.Substring(ToolPrefix.Length) : name;

        /// <summary>Whether what came back means the player hasn't signed in to Claude.</summary>
        public static bool NeedsSignIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToLowerInvariant();
            return t.Contains("/login") || t.Contains("not logged in") || t.Contains("invalid api key") || t.Contains("please log in");
        }

        /// <summary>
        /// The player's claude: on the PATH, else in ~/.local/bin, where its installer puts it and where
        /// a game started from the desktop can't see it. Null if it isn't installed.
        /// </summary>
        public static string Find(string pathVariable, string home, Func<string, bool> exists)
        {
            var folders = (pathVariable ?? "").Split(Path.PathSeparator).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (!string.IsNullOrEmpty(home)) folders.Add(Path.Combine(home, ".local", "bin"));
            foreach (string folder in folders)
                foreach (string name in new[] { "claude.exe", "claude.cmd", "claude" })
                {
                    string at = Path.Combine(folder.Trim('"'), name);
                    if (exists(at)) return at;
                }
            return null;
        }

        /// <summary>Notes kept to their newest lines: the file's only ever read whole, so it mustn't grow for ever.</summary>
        public static string Trimmed(string notes, int most = MostNoteLines)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            var lines = notes.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return lines.Length <= most ? notes : string.Join("\n", lines.Skip(lines.Length - most)) + "\n";
        }

        /// <summary>The arguments as one command line, each quoted where it has to be.</summary>
        public static string CommandLine(IEnumerable<string> arguments) =>
            string.Join(" ", arguments.Select(a => a.IndexOfAny(new[] { ' ', '(', ')' }) >= 0 ? $"\"{a}\"" : a));
    }
}
