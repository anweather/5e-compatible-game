using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Chem.Mcp;

// The chat's other harness (#59, backlog 27): ApiLoop on a thread of its own, over ApiWire. GameChat uses it for a player
// with their own key (J-071), and the chatprompts probe's --harness api for a run. Its lines go where Claude Code's do: the recorder,
// the probe's tap and what the chat shows. The player sees a reply's words when the block ends, as with Claude Code.
public sealed class ApiChat : IChatHarness
{
    /// <summary>How long one message may run before it's stopped, as with Claude Code.</summary>
    const int MostSeconds = 240;

    readonly ConcurrentQueue<(ClaudeSaid said, string text)> heard = new();
    readonly IChatWire wire;
    readonly ApiBudget budget;
    readonly string model, prefixTtl;
    CancellationTokenSource stopping;
    volatile bool busy, timedOut;

    public ApiChat(IChatWire wire, ApiBudget budget, string model, string prefixTtl)
    {
        this.wire = wire;
        this.budget = budget;
        this.model = model;
        this.prefixTtl = prefixTtl;
    }

    public bool Busy => busy;

    /// <summary>The model it asks for.</summary>
    public string Model => model;

    public bool Take(out (ClaudeSaid said, string text) line)
    {
        if (!heard.TryDequeue(out line)) return false;
        if (line.said is ClaudeSaid.Done or ClaudeSaid.Failed) busy = false;
        return true;
    }

    public bool Send(string message, string folder, string url, ChatRecorder recorder, Func<long> clock)
    {
        if (busy) return false;
        ClaudeTerminal.WriteFolder(folder, url);
        busy = true;
        timedOut = false;
        var stop = new CancellationTokenSource();
        stopping = stop;
        recorder.Started(clock());
        new Thread(() => Follow(message, folder, url, recorder, clock, stop)) { IsBackground = true, Name = "API chat" }.Start();
        return true;
    }

    // On its own thread: every line the loop says, then how it ended.
    void Follow(string message, string folder, string url, ChatRecorder recorder, Func<long> clock, CancellationTokenSource stop)
    {
        using var watchdog = new Timer(_ =>
        {
            timedOut = true;
            stop.Cancel();
        }, null, MostSeconds * 1000, Timeout.Infinite);
        bool ended = false;
        void Line(string line)
        {
            long at = clock();
            ClaudeChat.Tap?.Invoke(line, at);
            recorder.Line(line, at);
            foreach (var said in ClaudeRun.Read(line))
            {
                heard.Enqueue(said);
                ended |= said.Item1 is ClaudeSaid.Done or ClaudeSaid.Failed;
            }
        }
        string failure = null;
        try
        {
            new ApiLoop(wire, budget, model, Line, clock) { PrefixTtl = prefixTtl }.Run(message, folder, url, stop.Token);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            failure = ClaudeRun.Without(e.Message, ApiKey.Current);
            UnityEngine.Debug.LogWarning("API chat: " + e.GetType().Name + ": " + failure);
        }
        ChatOutcome? how = null;
        if (stop.IsCancellationRequested) how = timedOut ? ChatOutcome.TimedOut : ChatOutcome.Stopped;
        recorder.Ended(clock(), how);
        if (!ended) heard.Enqueue((ClaudeSaid.Failed, failure ?? "Claude stopped before it answered."));
    }

    public void Cancel() => stopping?.Cancel();
}
