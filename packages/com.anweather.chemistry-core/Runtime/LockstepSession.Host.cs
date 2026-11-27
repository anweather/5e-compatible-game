// The host's half of a lockstep session (LockstepSession.cs): the clock, taking and stamping commands, the tick's batch,
// the reset, and joiners. Solo is a host with no one to send to.
//
// Not a port: the sandbox has no peers.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Lockstep
{
    public sealed partial class LockstepSession
    {
        const int MostTicksAFrame = 4;

        double clock;
        int hostTick;
        int resetAt;
        readonly List<(int tick, LockstepChannel channel, object command)> pending = new List<(int, LockstepChannel, object)>();
        readonly Dictionary<int, byte[]> held = new Dictionary<int, byte[]>();
        readonly Dictionary<PeerId, string> names = new Dictionary<PeerId, string>();
        readonly List<PeerId> joiners = new List<PeerId>();

        public string MapId { get; set; } = "";

        public IDictionary<string, string> Start { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public int HostTick => hostTick;

        public event Action<int> Stamping;

        public event Action<LockstepTaken> Taken;

        public string NameOf(PeerId peer) => names.TryGetValue(peer, out var name) ? name : null;

        public void Stamp<T>(LockstepChannel<T> channel, T command, int delayTicks = 0)
        {
            IndexOf(channel);
            if (!IsHost) throw new InvalidOperationException("only the host stamps commands");
            started = true;
            Take(LocalPeer, channel, command, delayTicks, false);
        }

        public void StampReset(int delayTicks)
        {
            if (!IsHost || resetAt > hostTick) return;
            resetAt = hostTick + Math.Max(1, delayTicks);
            Send(Link.All, default, ResetMessage().Span);
            ResetComing?.Invoke(resetAt);
        }

        public void PeerJoined(PeerId peer, string name)
        {
            if (!IsHost) return;
            started = true;
            names[peer] = name;
            var w = new LockstepWriter();
            new LockstepHello(Protocol, World.Rules.Base.Fingerprint, MapId, new SortedDictionary<string, string>(Start, StringComparer.Ordinal),
                channels.Select(c => c.Name).ToList()).Write(w);
            Send(Link.Peer, peer, w.Span);
        }

        public void PeerLeft(PeerId peer)
        {
            names.Remove(peer);
            joiners.Remove(peer);
            frames.Forget(peer);
        }

        void HostReceived(PeerId from, LockstepKind kind, LockstepReader r)
        {
            switch (kind)
            {
                case LockstepKind.Join:
                case LockstepKind.Resync:
                    if (!joiners.Contains(from)) joiners.Add(from);
                    break;
                case LockstepKind.Intent:
                    var channel = ChannelAt(r.Natural());
                    int delay = r.Number();
                    Take(from, channel, channel.Read(r), delay, true);
                    break;
                default:
                    throw new LockstepWireException($"a host doesn't take a message of kind {kind}");
            }
        }

        void Take(PeerId from, LockstepChannel channel, object command, int delayTicks, bool check)
        {
            if (check)
            {
                if (channel.HostOnly && from != LocalPeer)
                {
                    Refuse(channel, command, from, "only the host sends " + channel.Name);
                    return;
                }
                command = channel.SentBy(command, from);
                string why = channel.Refusal(command, from, this);
                if (why != null)
                {
                    Refuse(channel, command, from, why);
                    return;
                }
            }
            int tick = hostTick + 1 + Math.Clamp(delayTicks, 0, MaxDelayTicks);
            if (channel.OnePerTick && pending.Any(p => p.channel == channel && p.tick == tick)) return;
            pending.Add((tick, channel, command));
            Taken?.Invoke(new LockstepTaken(from, channel, command, tick));
        }

        void Refuse(LockstepChannel channel, object command, PeerId from, string why)
        {
            var w = Message(LockstepKind.Refused);
            w.Natural((uint)channels.IndexOf(channel));
            channel.Write(w, command);
            w.Number(from.Value);
            w.Text(why);
            Send(Link.All, default, w.Span);
            Refused?.Invoke(new LockstepRefusal(channel, command, from, why));
        }

        LockstepWriter ResetMessage()
        {
            var w = Message(LockstepKind.ResetComing);
            w.Number(resetAt);
            return w;
        }

        void RunClock(double elapsedSeconds)
        {
            double tickLength = 1.0 / Math.Max(1, World.World.Rules.Sim.TicksPerSecond);
            clock = Math.Min(clock + Math.Max(0, elapsedSeconds), MostOwedSeconds);
            for (int guard = MostTicksAFrame; clock >= tickLength && guard > 0; guard--)
            {
                clock -= tickLength;
                StampNext();
            }
        }

        void StampNext()
        {
            Stamping?.Invoke(hostTick + 1);
            hostTick++;
            var w = Message(LockstepKind.Batch);
            w.Number(hostTick);
            bool reset = hostTick == resetAt;
            w.Bool(reset);
            w.Bool(reset);
            if (reset)
            {
                var mods = World.Rules.Mods;
                w.Natural((uint)mods.Count);
                foreach (var m in mods)
                {
                    w.Text(m.Id);
                    w.Text(m.Json);
                }
            }
            var due = pending.Where(p => p.tick <= hostTick).ToList();
            pending.RemoveAll(p => p.tick <= hostTick);
            var on = channels.Where(c => due.Any(p => p.channel == c)).ToList();
            w.Natural((uint)on.Count);
            foreach (var channel in on)
            {
                var commands = due.Where(p => p.channel == channel).ToList();
                w.Natural((uint)channels.IndexOf(channel));
                w.Natural((uint)commands.Count);
                foreach (var p in commands) channel.Write(w, p.command);
            }
            var message = w.ToArray();
            Send(Link.All, default, message);
            held[hostTick] = message;
            var r = new LockstepReader(message, 1);
            Hold(ReadBatch(r));
        }

        void ServeJoiners()
        {
            if (joiners.Count == 0 || World.World.TickInProgress) return;
            int at = NetTick;
            var w = Message(LockstepKind.Snapshot);
            w.Number(at);
            w.Number(hostTick);
            w.Number(LastResetTick);
            w.Text(World.Snapshot().Text());
            foreach (var peer in joiners)
            {
                Send(Link.Peer, peer, w.Span);
                for (int t = at + 1; t <= hostTick; t++)
                    if (held.TryGetValue(t, out var batch)) Send(Link.Peer, peer, batch);
                if (resetAt > hostTick) Send(Link.Peer, peer, ResetMessage().Span);
            }
            joiners.Clear();
        }
    }
}
