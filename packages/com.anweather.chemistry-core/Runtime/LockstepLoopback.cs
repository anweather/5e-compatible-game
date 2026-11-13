// The loopback transport (D19): N peers of a lockstep session in one process, for solo play and for the two-peer,
// late-join and desync tests under dotnet test. One host (or a solo peer) and any number of clients; Pump delivers
// what's due, and each peer may be given a delay in pumps that its messages take both ways. A link stays in order,
// as a real transport's reliable channel does: every message on it takes the same delay.
//
// Not a port: the sandbox has no peers.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Lockstep
{
    public sealed class LockstepLoopback
    {
        readonly List<LoopbackPeer> peers = new List<LoopbackPeer>();
        // Messages on their way, in the order sent: (due pump, from, to, bytes).
        readonly List<(int due, LoopbackPeer from, LoopbackPeer to, byte[] bytes)> onTheWay = new List<(int, LoopbackPeer, LoopbackPeer, byte[])>();

        /// <param name="maxMessageBytes">The longest send every peer's transport takes (the session splits longer messages).</param>
        public LockstepLoopback(int maxMessageBytes = 1024)
        {
            MaxMessageBytes = maxMessageBytes;
        }

        public int MaxMessageBytes { get; }

        /// <summary>Pumps so far.</summary>
        public int Now { get; private set; }

        /// <summary>The host, or a solo peer: peer 0.</summary>
        public LoopbackPeer Host { get; private set; }

        /// <summary>Every send so far, frames counted one by one, and the longest.</summary>
        public int Sends { get; private set; }

        public int LongestSend { get; private set; }

        /// <summary>The host (or, <paramref name="solo"/>, a peer alone), peer 0. There's one.</summary>
        public LoopbackPeer AddHost(string name = "host", bool solo = false)
        {
            if (Host != null) throw new InvalidOperationException("a loopback has one host");
            Host = new LoopbackPeer(this, new PeerId(0), name, solo ? LockstepRole.Solo : LockstepRole.Host, 0);
            peers.Add(Host);
            return Host;
        }

        /// <summary>A client, not yet connected; its messages take <paramref name="delay"/> pumps each way.</summary>
        public LoopbackPeer AddClient(string name, int delay = 0)
        {
            var client = new LoopbackPeer(this, new PeerId(peers.Count), name, LockstepRole.Client, Math.Max(0, delay));
            peers.Add(client);
            return client;
        }

        /// <summary>A client connects: the host's session hears it joined (and sends the hello).</summary>
        public void Connect(LoopbackPeer client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (client.Connected) return;
            client.Connected = true;
            Host.Session?.PeerJoined(client.Id, client.Name);
        }

        /// <summary>A client goes: what's on its way to or from it is lost, and the host's session hears it left.</summary>
        public void Disconnect(LoopbackPeer client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            client.Connected = false;
            onTheWay.RemoveAll(m => m.from == client || m.to == client);
            Host.Session?.PeerLeft(client.Id);
        }

        /// <summary>Delivers every message due by now, in the order sent (one sent while delivering may be due too), then the clock moves on.</summary>
        public void Pump()
        {
            while (true)
            {
                int i = onTheWay.FindIndex(m => m.due <= Now);
                if (i < 0) break;
                var (_, from, to, bytes) = onTheWay[i];
                onTheWay.RemoveAt(i);
                to.Session?.Received(from.Id, bytes);
            }
            Now++;
        }

        internal void Send(LoopbackPeer from, LoopbackPeer to, ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException($"a send of {bytes.Length} bytes, past the loopback's {MaxMessageBytes}");
            Sends++;
            LongestSend = Math.Max(LongestSend, bytes.Length);
            if (to == null || !to.Connected && to != Host || !from.Connected && from != Host) return;
            onTheWay.Add((Now + Math.Max(from.Delay, to.Delay), from, to, bytes.ToArray()));
        }

        internal LoopbackPeer Peer(PeerId id) => peers.FirstOrDefault(p => p.Id == id);

        internal IEnumerable<LoopbackPeer> Clients => peers.Where(p => p != Host && p.Connected);
    }

    /// <summary>One peer's end of a loopback: its transport, and the session it delivers to.</summary>
    public sealed class LoopbackPeer : ILockstepTransport
    {
        readonly LockstepLoopback net;

        internal LoopbackPeer(LockstepLoopback net, PeerId id, string name, LockstepRole role, int delay)
        {
            this.net = net;
            Id = id;
            Name = name;
            Role = role;
            Delay = delay;
        }

        public PeerId Id { get; }
        public string Name { get; }
        public LockstepRole Role { get; }
        public int MaxMessageBytes => net.MaxMessageBytes;

        /// <summary>Pumps each of its messages takes, to it or from it.</summary>
        public int Delay { get; }

        public bool Connected { get; internal set; }

        /// <summary>The session messages to this peer go to: set it once the session is made.</summary>
        public LockstepSession Session { get; set; }

        public void SendToHost(ReadOnlySpan<byte> message) => net.Send(this, net.Host, message);

        public void SendTo(PeerId peer, ReadOnlySpan<byte> message) => net.Send(this, net.Peer(peer), message);

        public void Broadcast(ReadOnlySpan<byte> message)
        {
            foreach (var client in net.Clients) net.Send(this, client, message);
        }
    }
}
