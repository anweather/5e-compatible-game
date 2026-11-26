// A way of reaching Claude (backlog 27, "picker of precanned profiles"; J-071): the player's Settings pick one. Today
// that's the game's own loop with their API key, or their own Claude Code. Another harness is a new row here and its
// adapter behind IChatHarness: nothing else lists them.
using System.Collections.Generic;
using System.Linq;

namespace Chem.Mcp
{
    public sealed class ChatProfile
    {
        public static readonly ChatProfile Api = new ChatProfile { Id = "api", Name = "API key" };

        public static readonly ChatProfile ClaudeCode = new ChatProfile { Id = "claude-code", Name = "Claude Code", Program = "claude" };

        public static readonly IReadOnlyList<ChatProfile> All = new[] { Api, ClaudeCode };

        public string Id { get; private set; }
        public string Name { get; private set; }

        public string Program { get; private set; }

        public static ChatProfile Of(string id) => All.FirstOrDefault(p => p.Id == id);

        public static bool IsVariableName(string name) =>
            !string.IsNullOrEmpty(name) && !char.IsDigit(name[0]) && name.All(c => c == '_' || (c < 127 && char.IsLetterOrDigit(c)));
    }
}
