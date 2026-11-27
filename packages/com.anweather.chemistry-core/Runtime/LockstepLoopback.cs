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
        readonly List<(int due, LoopbackPeer from, LoopbackPeer to, byte[] bytes)> onTheWay = new List<(int, LoopbackPeer, LoopbackPeer, byte[])>();

        public LockstepLoopback(int maxMessageBytes = 1024)
        {
            MaxMessageBytes = maxMessageBytes;
        }

        public int MaxMessageBytes { get; }

        public int Now { get; private set; }

        public LoopbackPeer Host { get; private set; }

        public int Sends { get; private set; }

        public int LongestSend { get; private set; }

        public LoopbackPeer AddHost(string name = "host", bool solo = false)
        {
            if (Host != null) throw new InvalidOperationException("a loopback has one host");
            Host = new LoopbackPeer(this, new PeerId(0), name, solo ? LockstepRole.Solo : LockstepRole.Host, 0);
            peers.Add(Host);
            return Host;
        }

        public LoopbackPeer AddClient(string name, int delay = 0)
        {
            var client = new LoopbackPeer(this, new PeerId(peers.Count), name, LockstepRole.Client, Math.Max(0, delay));
            peers.Add(client);
            return client;
        }

        public void Connect(LoopbackPeer client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (client.Connected) return;
            client.Connected = true;
            Host.Session?.PeerJoined(client.Id, client.Name);
        }

        public void Disconnect(LoopbackPeer client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            client.Connected = false;
            onTheWay.RemoveAll(m => m.from == client || m.to == client);
            Host.Session?.PeerLeft(client.Id);
        }

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

        public int Delay { get; }

        public bool Connected { get; internal set; }

        public LockstepSession Session { get; set; }

        public void SendToHost(ReadOnlySpan<byte> message) => net.Send(this, net.Host, message);

        public void SendTo(PeerId peer, ReadOnlySpan<byte> message) => net.Send(this, net.Peer(peer), message);

        public void Broadcast(ReadOnlySpan<byte> message)
        {
            foreach (var client in net.Clients) net.Send(this, client, message);
        }
    }
}
