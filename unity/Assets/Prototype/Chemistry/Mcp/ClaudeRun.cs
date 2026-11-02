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

        internal const string ToolPrefix = "mcp__" + ServerName + "__";

        public const string TalkLine = "Press Win+H to talk instead of type.";

        /// <summary>The folder's CLAUDE.md, here so chatbench gives Claude what the game gives it.</summary>
        public const string Guide = "# Spell Chemistry Valley\n\n" +
            "You're in a terminal beside a running game: a 3D RPG on the open 5E rules, built on physics-based interactions between " +
            "spells. Its rules are data, and the `spell-valley` tools here change them in the game the player is watching, with no restart.\n\n" +
            "Use the tools. Ask `here` where the player is and `list` or `describe` what's in the rules, make a draft (`set_look`, " +
            "`set_spell`, `set_material`, `add_reaction`, `tune`), `validate` it, then `apply` it with a short title the players will read. " +
            "The tools' own instructions say more. If the tools don't connect, the game has closed or Claude is off on its Play with Claude page.\n\n" +
            "Keep your answer to a line or two: the player is in the middle of a game.\n\n" +
            "## Notes\n\n" +
            "Each message starts a new process that remembers nothing, so read `notes.md` here first. When you've finished, add a line or two " +
            "there saying what you changed and what's on now. The game keeps only its newest lines.\n\n" +
            "## What's outside the rules\n\n" +
            "Some things are in the game's code, not its rules, so no tool changes them yet. `outside_rules` lists them, each with the nearest " +
            "thing a tool can do. Before you say no, check it; then say so in a line, offer that instead, and don't look for source: there is none here.\n\n" +
            "What to try: \"make all fire blue\", \"make the fire near me green\", \"each player a different fire colour\", " +
            "\"make grass burn twice as fast\", \"when lightning hits water, leave fog\".\n\n" + TalkLine + "\n";

        /// <summary>
        /// The chat's system prompt, in place of Claude Code's own: what a harness needs and no game knowledge, which stays in the
        /// server for every harness. No double quotes: it goes on a command line. Measured in docs/telemetry/chat-context-2026-11-02.md.
        /// </summary>
        public const string ChatPrompt = "You are the chat inside a running game, Spell Chemistry Valley, answering one message from the player while they play. " +
            "Use only the spell-valley tools and notes.md. You have no shell and no other files, and this chat is for the game alone: if a message is about anything else, " +
            "say so in a line and offer what the tools can do. Each message starts a new process that remembers nothing. notes.md holds what came before: read it only if this " +
            "message may depend on that, and after you change something add a line or two there saying what you changed and what's on now. Answer in a line or two, in plain words.";

        /// <summary>
        /// The arguments: one message read from stdin, the game's tools and the notes file allowed, and
        /// anything else refused rather than asked about, as nobody is there to answer a prompt.
        /// </summary>
        public static IReadOnlyList<string> Arguments() => new[]
        {
            "-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence",
            "--system-prompt", ChatPrompt,
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

        internal static string ToolName(string name) => name != null && name.StartsWith(ToolPrefix, StringComparison.Ordinal) ? name.Substring(ToolPrefix.Length) : name;

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

        /// <summary>What the chat's save says when the player's message didn't ask for one.</summary>
        public const string SaveNotAsked = "Saving is the player's call, and their message didn't ask: the change is on without it. If they want it kept, tell them to say save.";

        /// <summary>Whether a message from the chat asks for a save: nobody is there to answer a prompt, so the word has to be theirs.</summary>
        public static bool AsksToSave(string message) =>
            (message ?? "").ToLowerInvariant().Split(' ', ',', '.', '!', '?', ':', ';', '"', '\'', '\n').Any(SaveWords.Contains);

        static readonly string[] SaveWords = { "save", "saved", "saves", "saving" };

        /// <summary>The arguments as one command line, each quoted where it has to be.</summary>
        public static string CommandLine(IEnumerable<string> arguments) =>
            string.Join(" ", arguments.Select(a => a.IndexOfAny(new[] { ' ', '(', ')' }) >= 0 ? $"\"{a}\"" : a));
    }
}
