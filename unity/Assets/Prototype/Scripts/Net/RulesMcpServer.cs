using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Chem.Mcp;
using UnityEngine;

// The rules MCP served by the running game (docs/RULES_MCP.md phase 3), so a player adds
// it to their Claude once and Claude changes the game in front of them. Streamable HTTP
// on 127.0.0.1 only, one JSON answer to each POST (McpRpc), on a thread of its own: only
// what touches the world crosses to the main thread (GameRoomDesk), so a long simulate
// doesn't stall a frame. A web page can't reach it: a request must name this address as
// its Host, carry JSON, and come from no other Origin (a browser always says one).
// There's nothing to share or sign in to, and nothing crosses the network here: a change goes
// out as a room command like any other.
public class RulesMcpServer : MonoBehaviour
{
    /// <summary>The first port tried; a second copy of the game on this machine takes the next.</summary>
    public const int FirstPort = 47777;
    const int Ports = 8;
    const int MaxBody = 1024 * 1024;

    readonly ConcurrentQueue<Action> onMainThread = new();
    HttpListener listener;
    Thread thread;

    /// <summary>The address Claude connects to; null if no port was free.</summary>
    public static string Url { get; private set; }

    /// <summary>The line a player runs once, to add the game to Claude Code; null if it isn't serving.</summary>
    public static string ConnectLine => Url == null ? null : $"claude mcp add --transport http spell-valley {Url}";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWithTheGame()
    {
        if (ProbeLink.InVirtualPlayer || FindAnyObjectByType<RulesMcpServer>() != null) return;
        var go = new GameObject("Rules MCP");
        DontDestroyOnLoad(go);
        go.AddComponent<RulesMcpServer>();
    }

    /// <summary>Runs on the main thread at the next frame.</summary>
    public void Post(Action act) => onMainThread.Enqueue(act);

    void Start()
    {
        for (int port = FirstPort; port < FirstPort + Ports && listener == null; port++) TryListen(port);
        if (listener == null)
        {
            Debug.LogWarning($"Rules MCP: no free port from {FirstPort}, so Claude can't reach this game");
            return;
        }
        var rpc = new McpRpc(new RulesTools(new Workbench(ChemGrid.FindRulesDir()) { Desk = new GameRoomDesk(this) }), "spell-chemistry-valley", Application.version);
        thread = new Thread(() => Serve(listener, rpc)) { IsBackground = true, Name = "Rules MCP" };
        thread.Start();
    }

    void TryListen(int port)
    {
        var trying = new HttpListener();
        trying.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            trying.Start();
            listener = trying;
            Url = $"http://127.0.0.1:{port}/mcp";
        }
        catch (Exception e) when (e is HttpListenerException || e is System.Net.Sockets.SocketException)
        {
            trying.Close();
        }
    }

    void Update()
    {
        while (onMainThread.TryDequeue(out var act)) act();
    }

    void OnDestroy()
    {
        Url = null;
        if (listener != null) listener.Close();
    }

    static void Serve(HttpListener listener, McpRpc rpc)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                return; // closed
            }
            try
            {
                Answer(context, rpc);
            }
            catch (Exception e) when (e is IOException || e is HttpListenerException || e is ObjectDisposedException)
            {
                Debug.LogWarning("Rules MCP: " + e.Message); // the client went away
            }
        }
    }

    static void Answer(HttpListenerContext context, McpRpc rpc)
    {
        var request = context.Request;
        int refused = Refusal(request);
        if (refused != 0)
        {
            Reply(context, refused, null);
            return;
        }
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
        string answer = rpc.Handle(body);
        Reply(context, answer == null ? 202 : 200, answer);
    }

    // The status a request is turned away with, or 0.
    static int Refusal(HttpListenerRequest request)
    {
        if (request.Url.AbsolutePath != "/mcp") return 404;
        if (request.HttpMethod != "POST") return 405;
        if (!Local(request.UserHostName) || !OriginIsLocal(request.Headers["Origin"])) return 403;
        if ((request.ContentType ?? "").IndexOf("application/json", StringComparison.OrdinalIgnoreCase) < 0) return 415;
        return request.ContentLength64 > MaxBody ? 413 : 0;
    }

    static bool OriginIsLocal(string origin) =>
        string.IsNullOrEmpty(origin) || (Uri.TryCreate(origin, UriKind.Absolute, out var from) && Local(from.Host));

    static bool Local(string host)
    {
        string name = (host ?? "").Split(':')[0];
        return name == "127.0.0.1" || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    static void Reply(HttpListenerContext context, int status, string json)
    {
        var response = context.Response;
        response.StatusCode = status;
        if (json != null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        response.Close();
    }
}
