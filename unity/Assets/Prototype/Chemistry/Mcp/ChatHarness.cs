// The seam under the chat box (#59, backlog 27): what answers a player's message. A harness takes one
// message at a time and says what happened in ClaudeRun's terms, and it feeds a ChatRecorder. Claude Code
// behind ClaudeChat is one adapter and the game's own loop over the Messages API behind ApiChat another.
// Neither decides shared state: anything that changes the world goes through the MCP server as the room's commands.
using System;

namespace Chem.Mcp
{
    public interface IChatHarness
    {
        bool Busy { get; }

        bool Take(out (ClaudeSaid said, string text) line);

        bool Send(string message, string folder, string url, ChatRecorder recorder, Func<long> clock);

        void Cancel();
    }
}
