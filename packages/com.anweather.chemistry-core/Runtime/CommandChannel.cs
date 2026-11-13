using System.Collections.Generic;
using Chem;

// One kind of command over the lockstep network (NetChem): what the host has been asked for,
// stamping it for a tick, a peer holding it until that tick's batch arrives, and the history a
// late joiner replays. NetChem keeps the RPCs (Netcode generates them on its own methods) and
// sends what this gives it; the channel holds the lists. No Unity in here, so tests run it
// without a network.
//
// The host sends a tick's stamped commands just before the tick's batch, on the same reliable
// channel, so they usually come first and wait here; Receive adds them straight to the batch if
// it is already there. Either way they join it in the order the host stamped them.

/// <summary>An intent that carries the network tick it was stamped for.</summary>
public interface ITickStamped
{
    int Tick { get; }

    /// <summary>Stamps it for <paramref name="tick"/> (a struct, so on the copy it's called on).</summary>
    void StampFor(int tick);
}

public sealed class CommandChannel<TIntent> where TIntent : struct, ITickStamped
{
    readonly Dictionary<int, TickBatch> batches;
    readonly System.Action<TickBatch, TIntent> into;
    readonly System.Action<List<TIntent>, TIntent> record;
    readonly System.Func<TIntent, bool> takes;

    // Host only.
    readonly List<TIntent> pending = new();
    readonly List<TIntent> history = new();
    // Everyone: commands stamped for a tick whose batch hasn't arrived yet.
    readonly Dictionary<int, TIntent[]> waiting = new();
    // Joining: the history, sent just before the rest of it.
    TIntent[] past = System.Array.Empty<TIntent>();

    /// <param name="batches">The peer's batches by tick, which stamped commands join.</param>
    /// <param name="into">Adds one command to a batch (its own list there).</param>
    /// <param name="record">
    /// The compaction hook: adds a just-stamped command to the history, and may drop ones it makes
    /// needless (a stand a newer one replaces). Left out, every command is kept.
    /// </param>
    /// <param name="takes">
    /// Host: whether to take what's asked. One it doesn't is never stamped, so no peer runs it and no joiner replays it;
    /// the host decides, as a peer deciding for itself would desync. Left out, it takes everything.
    /// </param>
    public CommandChannel(Dictionary<int, TickBatch> batches, System.Action<TickBatch, TIntent> into, System.Action<List<TIntent>, TIntent> record = null, System.Func<TIntent, bool> takes = null)
    {
        this.batches = batches;
        this.into = into;
        this.record = record ?? ((h, c) => h.Add(c));
        this.takes = takes;
    }

    /// <summary>Host: someone asked for this; it goes out with the next stamp.</summary>
    public void Request(TIntent intent)
    {
        if (takes == null || takes(intent)) pending.Add(intent);
    }

    /// <summary>Host: something was asked that the next stamp sends.</summary>
    public bool HasPending => pending.Count > 0;

    /// <summary>
    /// Host: what was asked since the last stamp, stamped for <paramref name="tick"/> and recorded
    /// for joiners, in the order asked. Empty when nothing was: then nothing is sent.
    /// </summary>
    public TIntent[] Stamp(int tick)
    {
        if (pending.Count == 0) return System.Array.Empty<TIntent>();
        var stamped = pending.ToArray();
        pending.Clear();
        for (int i = 0; i < stamped.Length; i++)
        {
            stamped[i].StampFor(tick);
            record(history, stamped[i]);
        }
        return stamped;
    }

    /// <summary>Everyone: the host's stamped commands for a tick arrived; they join its batch, now or when it comes.</summary>
    public void Receive(int tick, TIntent[] commands)
    {
        if (batches.TryGetValue(tick, out var batch)) Into(batch, commands);
        else waiting[tick] = commands;
    }

    /// <summary>Everyone: the batch for <paramref name="tick"/> just arrived; commands waiting for it join it.</summary>
    public void Deliver(int tick)
    {
        if (waiting.Remove(tick, out var commands) && batches.TryGetValue(tick, out var batch)) Into(batch, commands);
    }

    /// <summary>Host: what a late joiner replays, oldest first.</summary>
    public TIntent[] HistoryForJoiner() => history.ToArray();

    /// <summary>Joining: the host's history arrived; it's replayed with the rest (Replay).</summary>
    public void Joining(TIntent[] history) => past = history;

    /// <summary>Joining: adds the history to a replay's batches (TickBatch.Timeline), each on its tick.</summary>
    public void Replay(Dictionary<int, TickBatch> timeline)
    {
        foreach (var c in past)
            if (timeline.TryGetValue(c.Tick, out var batch)) into(batch, c);
    }

    /// <summary>Host: an online reset at <paramref name="tick"/>; the history before it is no longer replayed.</summary>
    public void ForgetBefore(int tick) => history.RemoveAll(c => c.Tick < tick);

    /// <summary>Everything dropped, as a fresh session starts.</summary>
    public void Clear()
    {
        pending.Clear();
        history.Clear();
        waiting.Clear();
        past = System.Array.Empty<TIntent>();
    }

    void Into(TickBatch batch, TIntent[] commands)
    {
        foreach (var c in commands) into(batch, c);
    }
}
