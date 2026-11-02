// The seam under the chat box (#59, backlog 27): what answers a player's message. A harness takes one
// message at a time and says what happened in ClaudeRun's terms, and it feeds a ChatRecorder. Claude Code
// behind ClaudeChat is one adapter and the game's own loop over the Messages API behind ApiChat another.
// Neither decides shared state: anything that changes the world goes through the MCP server as the room's commands.
using System;

namespace Chem.Mcp
{
    public interface IChatHarness
    {
        /// <summary>Whether a message is still being answered.</summary>
        bool Busy { get; }

        /// <summary>The next thing heard, on the main thread; false when there's nothing yet.</summary>
        bool Take(out (ClaudeSaid said, string text) line);

        /// <summary>Starts on one message, in a folder ready for it. False, with the reason heard, if it can't.</summary>
        bool Send(string message, string folder, string url, ChatRecorder recorder, Func<long> clock);

        /// <summary>Stops a message in progress: the player asked, or the game is closing.</summary>
        void Cancel();
    }
}
