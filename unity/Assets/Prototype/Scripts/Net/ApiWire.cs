using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Chem.Mcp;

// ApiLoop's way out, in the view as the core has no HTTP: the Messages API over HTTPS, read as it streams, and the
// game's own MCP server on this machine. Raw HTTP, as Unity's runtime can't take the C# SDK. The key is here and only
// here: read from this process's environment for each request, sent as a header, in nothing else. Whatever comes
// back is cleaned of it before anyone reads a failure.
public sealed class ApiWire : IChatWire
{
    const string Address = "https://api.anthropic.com/v1/messages", Version = "2026-10-21", Json = "application/json", DataLine = "data: ";

    // Each message's whole run is bounded by its watchdog, not by this client.
    static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Whether a key is there to use. Says no more than that.</summary>
    public static bool HasKey => !string.IsNullOrEmpty(Key);

    static string Key => Environment.GetEnvironmentVariable(ClaudeRun.GameKey);

    public string Mcp(string url, string body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, Json) };
        try
        {
            using var response = Client.SendAsync(request, token).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (HttpRequestException)
        {
            throw new ChatWireException("The game's tools didn't answer.");
        }
    }

    public WireAnswer Messages(string body, Action<string> data, CancellationToken token)
    {
        string key = Key;
        if (string.IsNullOrEmpty(key)) return new WireAnswer { Status = 401, Failure = "There's no key for the API here." };
        using var request = new HttpRequestMessage(HttpMethod.Post, Address) { Content = new StringContent(body, Encoding.UTF8, Json) };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", Version);
        try
        {
            using var response = Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult();
            int status = (int)response.StatusCode;
            double? retry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            if (status != 200)
                return new WireAnswer { Status = status, RetryAfterSeconds = retry, Failure = Plain(status, response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), key) };
            Read(response, data, token);
            return new WireAnswer { Status = status };
        }
        catch (HttpRequestException e)
        {
            return new WireAnswer { Status = 0, Failure = ClaudeRun.Without(e.Message, key) };
        }
        catch (IOException e)
        {
            token.ThrowIfCancellationRequested();
            return new WireAnswer { Status = 0, Failure = ClaudeRun.Without(e.Message, key) };
        }
    }

    static void Read(HttpResponseMessage response, Action<string> data, CancellationToken stop)
    {
        using var body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var reader = new StreamReader(body, Encoding.UTF8);
        using var closing = stop.Register(response.Dispose); // a blocked read wakes when the stream goes
        try
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                if (line.StartsWith(DataLine, StringComparison.Ordinal)) data(line.Substring(DataLine.Length));
        }
        catch (ObjectDisposedException)
        {
            stop.ThrowIfCancellationRequested();
            throw;
        }
    }

    // A sentence for a failure, never the words that would make the chat ask a player to sign in to Claude.
    static string Plain(int status, string answer, string key)
    {
        if (status == 401) return "The API turned this key down.";
        if (status == 402) return "The API account has a billing problem.";
        string message = null;
        try
        {
            message = (string)Newtonsoft.Json.Linq.JObject.Parse(answer)["error"]?["message"];
        }
        catch (Newtonsoft.Json.JsonException)
        {
            // Not the API's own error body, so only the status says anything.
        }
        return ClaudeRun.Without($"The API answered {status}{(message == null ? "" : ": " + message)}.", key);
    }
}
