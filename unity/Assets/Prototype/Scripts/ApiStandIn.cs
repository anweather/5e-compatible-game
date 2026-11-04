using System;
using System.Collections.Generic;
using System.Threading;
using Chem.Mcp;
using Newtonsoft.Json.Linq;

// The API loop's stand-in for the chatprompts probe, which spends nothing: the real server over the real
// HTTP, and canned replies for the API. Its first reply also asks for a shell and a save nobody asked for: both must be turned away.
// It answers a key's check too, by the key: no real key, no real call (J-071).
public sealed class ApiStandIn : IChatWire, IKeyLister
{
    /// <summary>What a key starts with to be taken, turned down, or met with no network.</summary>
    public const string GoodKey = "stand-in-good", BadKey = "stand-in-bad", OfflineKey = "stand-in-offline";

    const string MessageStop = "{\"type\":\"message_stop\"}";
    // The list holds a model the key can use and the game can't run, which the player must never be offered.
    const string Listed = "{\"data\":[{\"id\":\"claude-opus-5-5\"},{\"id\":\"claude-unpriced-1\"},{\"id\":\"claude-haiku-5-5\"}],\"has_more\":false}";

    readonly ApiWire real = new();
    int turn;

    public string Mcp(string url, string body, CancellationToken token) => real.Mcp(url, body, token);

    public KeyCheck Models(string key, CancellationToken token)
    {
        if (key.StartsWith(OfflineKey, StringComparison.Ordinal)) return ApiModels.Unreached();
        return key.StartsWith(GoodKey, StringComparison.Ordinal) ? ApiModels.Read(200, Listed) : ApiModels.Read(401, "{\"error\":{\"message\":\"invalid x-api-key " + key + "\"}}");
    }

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
        Block(2, "save", "t3"),
        "{\"type\":\"content_block_delta\",\"index\":2,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"draft\\\":\\\"d1\\\",\\\"title\\\":\\\"t\\\",\\\"idea\\\":\\\"i\\\"}\"}}",
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
