using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Chem.Mcp;

// ApiLoop's way out, in the view as the core has no HTTP: the Messages API over HTTPS, read as it streams, and the
// game's own MCP server on this machine. Raw HTTP, as Unity's runtime can't take the C# SDK. A key is read for each
// request (ApiKey) and goes out through Signed alone, as a header to api.anthropic.com, in nothing else. Redirects are
// off, as following one would carry the header to wherever it points. Whatever comes back is cleaned of the key before
// anyone reads a failure.
public sealed class ApiWire : IChatWire, IKeyLister
{
    const string Host = "api.anthropic.com", Address = "https://" + Host + "/v1/messages", Version = "2026-10-21", Json = "application/json", DataLine = "data: ";

    /// <summary>Where a key's models are asked for.</summary>
    public const string ModelsAddress = "https://" + Host + "/v1/models?limit=1000";

    static readonly HttpClientHandler Handler = new() { AllowAutoRedirect = false };

    // Each message's whole run is bounded by its watchdog, not by this client.
    static readonly HttpClient Client = new(Handler) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Whether a redirect would be followed, for a test: it must not be.</summary>
    public static bool FollowsRedirects => Handler.AllowAutoRedirect;

    /// <summary>Whether a key is there to use. Says no more than that. Opens a saved key, so not on the main thread in play.</summary>
    public static bool HasKey => !string.IsNullOrEmpty(Key);

    static string Key => ApiKey.Current;

    /// <summary>A request carrying a key: the one place one is put on the wire, and only ever to the API, over HTTPS.</summary>
    public static HttpRequestMessage Signed(HttpMethod method, string address, string key)
    {
        var request = new HttpRequestMessage(method, address);
        if (request.RequestUri.Host != Host || request.RequestUri.Scheme != Uri.UriSchemeHttps)
        {
            request.Dispose();
            throw new InvalidOperationException("A key goes to the API and nowhere else.");
        }
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", Version);
        return request;
    }

    /// <summary>Which models this key can use, off the main thread: free, and the check that it works at all.</summary>
    public KeyCheck Models(string key, CancellationToken token)
    {
        try
        {
            using var request = Signed(HttpMethod.Get, ModelsAddress, key);
            using var response = Client.SendAsync(request, token).GetAwaiter().GetResult();
            return ApiModels.Read((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return ApiModels.Unreached();
        }
    }

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
        using var request = Signed(HttpMethod.Post, Address, key);
        request.Content = new StringContent(body, Encoding.UTF8, Json);
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
