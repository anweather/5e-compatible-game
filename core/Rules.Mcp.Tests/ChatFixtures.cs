using System.IO;
using Chem.Mcp;
using Newtonsoft.Json.Linq;

namespace Chem.Tests
{
    /// <summary>
    /// The stream fixtures in chat-fixtures/ fed to a recorder, each line "arriving" Step ms after the last: ChatRecordTests'
    /// and the bench's (Chat.Bench.Tests links this file).
    /// </summary>
    internal static class ChatFixtures
    {
        internal const int Step = 100;

        internal static string Fixtures => Path.Combine(ChemistryTests.Folders.Repo, "core", "Rules.Mcp.Tests", "chat-fixtures");

        internal static string[] Lines(string name) => File.ReadAllLines(Path.Combine(Fixtures, name + ".jsonl"));

        internal static ChatRecorder Fed(string[] lines)
        {
            var recorder = new ChatRecorder("a prompt", "typed", 1000, new ChatWho { Id = "r1", GameVersion = "0.1.2", Commit = "abc", RoomMode = "open", Players = 2, ModsOn = new[] { "bigger" } });
            recorder.Started(1000 + Step);
            for (int i = 0; i < lines.Length; i++) recorder.Line(lines[i], 1000 + Step * (i + 2));
            return recorder;
        }

        internal static JObject Record(string name) => Fed(Lines(name)).Record();
    }
}
