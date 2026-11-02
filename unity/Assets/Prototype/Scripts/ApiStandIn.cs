using System;
using System.Collections.Generic;
using System.Threading;
using Chem.Mcp;
using Newtonsoft.Json.Linq;

// The API loop's stand-in for the chatprompts probe, which spends nothing: the real server over the real
// HTTP, and canned replies for the API. Its first reply also asks for a shell, which must be turned away.
public sealed class ApiStandIn : IChatWire
{
    const string MessageStop = "{\"type\":\"message_stop\"}";

    readonly ApiWire real = new();
    int turn;

    public string Mcp(string url, string body, CancellationToken token) => real.Mcp(url, body, token);

    public WireAnswer Messages(string body, Action<string> data, CancellationToken token)
    {
        var said = (JArray)JObject.Parse(body)["messages"];
        turn = said.Count == 1 ? 0 : turn + 1;
        foreach (string line in turn == 0 ? AsksForTools() : Finishes()) data(line);
        return new WireAnswer { Status = 200 };
    }

    static IEnumerable<string> AsksForTools() => new[]
    {
        Start("m1"),
        Block(0, "whats_on", "t1"),
        Block(1, "Bash", "t2"),
        End("tool_use"),
        MessageStop,
    };

    static IEnumerable<string> Finishes() => new[]
    {
        Start("m2"),
        "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
        "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Done.\"}}",
        End("end_turn"),
        MessageStop,
    };

    static string Start(string id) =>
        "{\"type\":\"message_start\",\"message\":{\"id\":\"" + id + "\",\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":20,\"cache_read_input_tokens\":3000,\"output_tokens\":1}}}";

    static string Block(int index, string name, string id) =>
        "{\"type\":\"content_block_start\",\"index\":" + index + ",\"content_block\":{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"" + name + "\"}}";

    static string End(string reason) =>
        "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"" + reason + "\"},\"usage\":{\"output_tokens\":40}}";
}
