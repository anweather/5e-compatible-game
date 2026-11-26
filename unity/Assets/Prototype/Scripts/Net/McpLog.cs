using System;
using Chem.Mcp;
using Newtonsoft.Json.Linq;

// The rules MCP's call log (docs/design/chat-telemetry.md): one line per tool call, whoever made it, so
// a terminal session, which has no ChatRecorder, leaves numbers too, and each call shows whether its
// time went to the game or to the tool. Written on the server's own thread. Local only.
public static class McpLog
{
    const string Prefix = "mcp-log-";

    public static long WaitedMs { get; set; }

    public static string Folder { get; set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        WaitedMs = 0;
        Folder = null;
    }

    public static void Write(string tool, long ms, int chars, bool ok)
    {
        long waited = WaitedMs;
        WaitedMs = 0;
        ChatLog.Append(Folder, DateTime.UtcNow, new JObject
        {
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["tool"] = tool,
            ["waitMs"] = waited,
            ["ms"] = ms,
            ["chars"] = chars,
            ["ok"] = ok,
        }, Prefix);
    }
}
