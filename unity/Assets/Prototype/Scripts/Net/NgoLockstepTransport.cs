using System;
using System.Collections.Generic;
using Chem.Lockstep;

// The core's lockstep transport port over Netcode for GameObjects (D19, docs/design/core-package/multiplayer-ports.md):
// bytes only. NetChem owns the two RPCs that carry them (to the host, to one client; NGO's reliable, sequenced,
// fragmented delivery) and hands this what arrives; this keeps it in order for the session, which runs in NetChem's
// Update, never inside an RPC handler.
//
// - A client's id is its PeerId (the host is 0, NGO's ServerClientId).
// - A client is a peer once its NetChem has spawned and said so (Ready): before that an RPC for it could arrive at an
//   object it hasn't spawned yet.
// - To the host goes straight out; to the clients goes through an outbox per client, at most BytesAFrame each a frame,
//   so a joiner's snapshot (hundreds of KB on the valley) never fills NGO's send queue. Each link stays in order.
//
// Not a port: the sandbox has no peers.
public sealed class NgoLockstepTransport : ILockstepTransport
{
    /// <summary>The longest send: the session splits longer messages (a snapshot) into pieces of this.</summary>
    public const int MessageBytes = 16 * 1024;

    /// <summary>What goes to each client a frame at most; the rest waits, in order, for the next.</summary>
    public const int BytesAFrame = 128 * 1024;

    readonly Action<byte[]> toHost;
    readonly Action<ulong, byte[]> toClient;
    readonly Dictionary<ulong, Queue<byte[]>> outbox = new();
    readonly List<ulong> clients = new();
    readonly Queue<(Arrival what, ulong client, byte[] bytes)> inbox = new();

    enum Arrival { Ready, Bytes, Left }

    /// <param name="toHost">Sends a client's bytes to the host (an RPC to the server).</param>
    /// <param name="toClient">Sends the host's bytes to one client (an RPC to that client).</param>
    public NgoLockstepTransport(LockstepRole role, Action<byte[]> toHost, Action<ulong, byte[]> toClient)
    {
        Role = role;
        this.toHost = toHost ?? throw new ArgumentNullException(nameof(toHost));
        this.toClient = toClient ?? throw new ArgumentNullException(nameof(toClient));
    }

    public LockstepRole Role { get; }

    public int MaxMessageBytes => MessageBytes;

    /// <summary>The bytes still waiting to go to the clients.</summary>
    public int Waiting
    {
        get
        {
            int n = 0;
            foreach (var q in outbox.Values)
                foreach (var b in q) n += b.Length;
            return n;
        }
    }

    public static PeerId PeerOf(ulong clientId) => new PeerId(checked((int)clientId));

    public static ulong ClientOf(PeerId peer) => (ulong)peer.Value;

    public void SendToHost(ReadOnlySpan<byte> message) => toHost(message.ToArray());

    public void SendTo(PeerId peer, ReadOnlySpan<byte> message)
    {
        if (outbox.TryGetValue(ClientOf(peer), out var q)) q.Enqueue(message.ToArray());
    }

    public void Broadcast(ReadOnlySpan<byte> message)
    {
        if (clients.Count == 0) return;
        var bytes = message.ToArray();
        foreach (ulong c in clients) outbox[c].Enqueue(bytes);
    }

    /// <summary>Host: a client's NetChem has spawned and is ready for the session's hello.</summary>
    public void Ready(ulong client) => inbox.Enqueue((Arrival.Ready, client, null));

    /// <summary>Bytes from a client (on the host) or from the host (on a client).</summary>
    public void Arrived(ulong from, byte[] bytes) => inbox.Enqueue((Arrival.Bytes, from, bytes));

    /// <summary>Host: a client went.</summary>
    public void Left(ulong client) => inbox.Enqueue((Arrival.Left, client, null));

    /// <summary>
    /// Hands what arrived to the session, in order: a client becoming a peer (<paramref name="joined"/>, after which
    /// broadcasts reach it), bytes, and a client gone (<paramref name="left"/>, before which its outbox is dropped).
    /// </summary>
    public void Deliver(LockstepSession session, Action<ulong> joined, Action<ulong> left)
    {
        while (inbox.Count > 0)
        {
            var (what, client, bytes) = inbox.Dequeue();
            switch (what)
            {
                case Arrival.Ready:
                    if (outbox.ContainsKey(client)) break;
                    outbox[client] = new Queue<byte[]>();
                    clients.Add(client);
                    joined(client);
                    break;
                case Arrival.Bytes:
                    session.Received(PeerOf(client), bytes);
                    break;
                case Arrival.Left:
                    outbox.Remove(client);
                    clients.Remove(client);
                    left(client);
                    break;
            }
        }
    }

    /// <summary>Sends what waits for each client, up to BytesAFrame each (always one message at the least).</summary>
    public void Flush()
    {
        foreach (ulong c in clients)
        {
            var q = outbox[c];
            for (int sent = 0; q.Count > 0 && sent < BytesAFrame;)
            {
                var bytes = q.Dequeue();
                sent += bytes.Length;
                toClient(c, bytes);
            }
        }
    }
}
