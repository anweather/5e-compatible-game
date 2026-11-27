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
        public event Action<LockstepHello> HelloReceived;

        public event Action<int> Joined;

        public LockstepHello Hello { get; private set; }

        public void Join()
        {
            if (IsHost) return;
            Send(Link.Host, default, Message(LockstepKind.Join).Span);
        }

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

        string Mismatch(LockstepHello hello)
        {
            if (hello.Protocol != Protocol) return $"the host speaks lockstep protocol {hello.Protocol}, this peer {Protocol}";
            string rules = World.Rules.Base.Fingerprint;
            if (hello.Rules != rules) return $"the host's rules differ from this peer's (host {hello.Rules}, here {rules}): same files?";
            var mine = channels.Select(c => c.Name).ToList();
            if (!hello.Channels.SequenceEqual(mine)) return $"the host's channels ({string.Join(", ", hello.Channels)}) aren't this peer's ({string.Join(", ", mine)})";
            return null;
        }

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
            HashBaseline();
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
            var chunks = ReadChunks(r);
            if (!synced) return;
            hostHashes[tick] = new HashWindow { Parts = parts, Chunks = chunks };
            CheckHash(tick);
        }
    }
}
