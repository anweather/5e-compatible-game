// A way of reaching Claude (backlog 27, "picker of precanned profiles"; J-071): the player's Settings pick one. Today
// that's the game's own loop with their API key, or their own Claude Code. Another harness is a new row here and its
// adapter behind IChatHarness: nothing else lists them.
using System.Collections.Generic;
using System.Linq;

namespace Chem.Mcp
{
    public sealed class ChatProfile
    {
        /// <summary>The game calls the API itself, with the player's key.</summary>
        public static readonly ChatProfile Api = new ChatProfile { Id = "api", Name = "API key" };

        /// <summary>The game runs the player's install.</summary>
        public static readonly ChatProfile ClaudeCode = new ChatProfile { Id = "claude-code", Name = "Claude Code", Program = "claude" };

        /// <summary>Every profile, in the order the picker shows them.</summary>
        public static readonly IReadOnlyList<ChatProfile> All = new[] { Api, ClaudeCode };

        /// <summary>Its id where it's stored, and its name in the picker.</summary>
        public string Id { get; private set; }
        public string Name { get; private set; }

        /// <summary>The program it runs by default; null if it runs none.</summary>
        public string Program { get; private set; }

        /// <summary>The profile with this id, or null.</summary>
        public static ChatProfile Of(string id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>Whether this can be the name of an environment variable.</summary>
        public static bool IsVariableName(string name) =>
            !string.IsNullOrEmpty(name) && !char.IsDigit(name[0]) && name.All(c => c == '_' || (c < 127 && char.IsLetterOrDigit(c)));
    }
}
