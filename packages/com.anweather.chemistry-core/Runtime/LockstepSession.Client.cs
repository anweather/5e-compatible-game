// A client's half of a lockstep session (LockstepSession.cs): the hello's checks, asking to join, loading the host's
// snapshot, and holding the batches, hash windows, resets and refusals the host sends.
//
// Not a port: the sandbox has no peers.
using System;
using System.Linq;

namespace Chem.Lockstep
{
    public sealed partial class LockstepSession
    {
        /// <summary>Client: the host's hello, once it checks out (the game may build the host's map here, then Join).</summary>
        public event Action<LockstepHello> HelloReceived;

        /// <summary>Client: a snapshot of the host's world loaded, at this network tick; it plays on from there.</summary>
        public event Action<int> Joined;

        /// <summary>
        /// Client: the host's hello, or null before it comes; set as it comes, so a game told it can't join (Failed) can
        /// still read the host's map (a peer on another map goes there, then joins again).
        /// </summary>
        public LockstepHello Hello { get; private set; }

        /// <summary>Client: asks the host for its world (AutoJoin does, as soon as the hello checks out).</summary>
        public void Join()
        {
            if (IsHost) return;
            Send(Link.Host, default, Message(LockstepKind.Join).Span);
        }

        /// <summary>Client: asks the host for a fresh snapshot, to put a desync right (RecoverDesyncs does on its own).</summary>
        public void RequestSnapshot()
        {
            if (IsHost) return;
            Send(Link.Host, default, Message(LockstepKind.Resync).Span);
        }

        void ClientReceived(LockstepKind kind, LockstepReader r)
        {
            switch (kind)
            {
                case LockstepKind.Hello:
                    Greeted(LockstepHello.Read(r));
                    break;
                case LockstepKind.Snapshot:
                    Load(r.Number(), r.Number(), r.Number(), r.Text());
                    break;
                case LockstepKind.Batch:
                    Hold(ReadBatch(r));
                    break;
                case LockstepKind.Hash:
                    HostWindow(r);
                    break;
                case LockstepKind.ResetComing:
                    ResetComing?.Invoke(r.Number());
                    break;
                case LockstepKind.Refused:
                    var channel = ChannelAt(r.Natural());
                    var command = channel.Read(r);
                    var peer = new PeerId(r.Number());
                    Refused?.Invoke(new LockstepRefusal(channel, command, peer, r.Text()));
                    break;
                default:
                    throw new LockstepWireException($"a client doesn't take a message of kind {kind}");
            }
        }

        void Greeted(LockstepHello hello)
        {
            Hello = hello;
            string why = Mismatch(hello);
            if (why != null)
            {
                Failed?.Invoke("Can't join: " + why);
                return;
            }
            HelloReceived?.Invoke(hello);
            if (AutoJoin) Join();
        }

        // Why this peer can't follow the host that sent this hello, or null.
        string Mismatch(LockstepHello hello)
        {
            if (hello.Protocol != Protocol) return $"the host speaks lockstep protocol {hello.Protocol}, this peer {Protocol}";
            string rules = World.Rules.Base.Fingerprint;
            if (hello.Rules != rules) return $"the host's rules differ from this peer's (host {hello.Rules}, here {rules}): same files?";
            var mine = channels.Select(c => c.Name).ToList();
            if (!hello.Channels.SequenceEqual(mine)) return $"the host's channels ({string.Join(", ", hello.Channels)}) aren't this peer's ({string.Join(", ", mine)})";
            return null;
        }

        // The host's world at a network tick: loaded over this peer's own map and rules, then the batches after it run.
        void Load(int at, int hostTickThen, int lastReset, string text)
        {
            try
            {
                World.Load(JoinSnapshot.Parse(text));
            }
            catch (SaveException e)
            {
                Failed?.Invoke("Can't join: " + e.Message);
                return;
            }
            tickBase = at - World.World.Tick;
            LastResetTick = lastReset;
            running = null;
            foreach (int t in batches.Keys.Where(t => t <= at).ToList()) batches.Remove(t);
            synced = true;
            knownUpTo = at;
            ExtendKnown();
            rolling = new RollingHash();
            hostHashes.Clear();
            ownHashes.Clear();
            DesyncSeen = false;
            catchUpTo = hostTickThen;
            caughtUp = false;
            Joined?.Invoke(at);
            if (NetTick >= catchUpTo)
            {
                caughtUp = true;
                CaughtUp?.Invoke(NetTick);
            }
        }

        void HostWindow(LockstepReader r)
        {
            int tick = r.Number();
            var parts = new ulong[r.Count()];
            for (int i = 0; i < parts.Length; i++) parts[i] = r.Hash();
            if (!synced) return;
            hostHashes[tick] = parts;
            CheckHash(tick);
        }
    }
}
