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
        // Ticks a frame at most, however far behind the clock is (NetChem's guard).
        const int MostTicksAFrame = 4;

        double clock;
        int hostTick;
        // The tick a stamped reset happens at (0: none coming).
        int resetAt;
        // Commands taken and not yet stamped, in the order taken, each with the tick it lands on.
        readonly List<(int tick, LockstepChannel channel, object command)> pending = new List<(int, LockstepChannel, object)>();
        // The batches stamped that this host's world hasn't run yet, as sent: what a joiner gets after its snapshot.
        readonly Dictionary<int, byte[]> held = new Dictionary<int, byte[]>();
        readonly Dictionary<PeerId, string> names = new Dictionary<PeerId, string>();
        // Peers waiting for a snapshot (joining, or desynced), served at the next moment between ticks.
        readonly List<PeerId> joiners = new List<PeerId>();

        /// <summary>Host: the map it plays, sent in the hello.</summary>
        public string MapId { get; set; } = "";

        /// <summary>Host: the start's settings, sent in the hello (ours: the spawn it started in).</summary>
        public IDictionary<string, string> Start { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Host: the last tick stamped. The host's world runs a tick or so behind it.</summary>
        public int HostTick => hostTick;

        /// <summary>Host: just before a tick's batch is built, with its tick. What's stamped now (Stamp, delay 0) lands on it: a chunk plan.</summary>
        public event Action<int> Stamping;

        /// <summary>Host: a command it took and the tick it lands on (for what the view shows early: a cast's gesture).</summary>
        public event Action<LockstepTaken> Taken;

        /// <summary>A peer's name, as the adapter gave it when it joined (this peer's own: null).</summary>
        public string NameOf(PeerId peer) => names.TryGetValue(peer, out var name) ? name : null;

        /// <summary>
        /// Host: stamps a command as it is, with no naming and no refusal: what the host decides itself (a prop's effect,
        /// a chunk plan, a leaving player's Leave, a find granted to a peer).
        /// </summary>
        public void Stamp<T>(LockstepChannel<T> channel, T command, int delayTicks = 0)
        {
            IndexOf(channel);
            if (!IsHost) throw new InvalidOperationException("only the host stamps commands");
            started = true;
            Take(LocalPeer, channel, command, delayTicks, false);
        }

        /// <summary>
        /// Host: the world resets for everyone after <paramref name="delayTicks"/> (at least 1). Every peer hears
        /// ResetComing at once, and at that tick rebuilds the world, the mods in force re-stamped with it (J-014).
        /// A reset already coming isn't moved.
        /// </summary>
        public void StampReset(int delayTicks)
        {
            if (!IsHost || resetAt > hostTick) return;
            resetAt = hostTick + Math.Max(1, delayTicks);
            Send(Link.All, default, ResetMessage().Span);
            ResetComing?.Invoke(resetAt);
        }

        /// <summary>The adapter, on the host: a peer connected. It's sent the hello; it asks to join when it's ready.</summary>
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

        /// <summary>The adapter: a peer went. Its half-sent messages are dropped; the game stamps what its leaving means.</summary>
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

        // A command asked for (checked: named for who asked, and maybe refused) or stamped by the host itself.
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

        // The next tick's batch: what's due on every channel, in the channels' order and then the order taken, and on a
        // reset's tick the mods in force. Sent to everyone and held here from the same bytes.
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

        // Joiners get the host's world between two ticks, then every batch stamped past it, then the live ones.
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
