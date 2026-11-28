// Lockstep in the core (D19, docs/design/core-package/multiplayer-ports.md): one session a process, over a transport
// port and a world port, the logic NetChem kept in the view's NGO layer. Every peer runs the same world from the same
// batches, and because the world is deterministic they stay identical.
//
// - The host owns the clock (Update's elapsed time; a client's is ignored). Each tick it stamps what was asked for it,
//   on every channel, into one batch, broadcasts it and runs it like everyone else. A command asked with an input delay
//   lands that many ticks later (0 to MaxDelayTicks), the same tick on every peer.
// - A peer runs only through the batches it holds (Advance), a tick in slices within the frame's budget (StepSome):
//   a batch's commands land between whole ticks, so peers slicing their frames differently compute the same ticks.
// - Every hash window (RollingHash.Every ticks) the host broadcasts its checksum's parts, the cells' from the chunk
//   digest (World.ChunkHash.cs), and the chunks whose hash changed in the window; a client that disagrees raises
//   Desynced, naming the chunks that differ, and asks for a fresh snapshot (RecoverDesyncs), which it loads and plays on from.
// - A joiner is sent a snapshot of the host's world between two ticks (JoinSnapshot), then the batches the host had
//   stamped past it, then the live ones. Unlike NetChem, no history is kept to replay.
// - An online reset (J-014) is a batch that rebuilds the world before it runs, the mods in force re-stamped in it. The
//   session's ticks keep counting; the rebuilt world's own count starts again at 0 (tickBase).
//
// Not a port: the sandbox has no peers.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Lockstep
{
    public sealed partial class LockstepSession
    {
        public const int Protocol = 2;

        public const int MaxDelayTicks = 30;

        const int HashesKept = 4;

        readonly ILockstepTransport transport;
        readonly List<LockstepChannel> channels = new List<LockstepChannel>();
        readonly LockstepFrames frames = new LockstepFrames();
        bool started;

        readonly Dictionary<int, TickBatch> batches = new Dictionary<int, TickBatch>();
        int knownUpTo;
        TickBatch running;
        int tickBase;
        bool synced, caughtUp;
        int catchUpTo;
        int ownChunksAt = -1;
        readonly Dictionary<int, HashWindow> hostHashes = new Dictionary<int, HashWindow>(), ownHashes = new Dictionary<int, HashWindow>();

        public LockstepSession(ILockstepTransport transport, ILockstepWorld world, PeerId localPeer = default)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            World = world ?? throw new ArgumentNullException(nameof(world));
            LocalPeer = localPeer;
            if (transport.MaxMessageBytes < LockstepFrames.MinMessageBytes)
                throw new ArgumentException($"a transport must take {LockstepFrames.MinMessageBytes} bytes a send at the least", nameof(transport));
            synced = caughtUp = IsHost;
            HashBaseline();
        }

        public LockstepRole Role => transport.Role;

        public bool IsHost => transport.Role != LockstepRole.Client;

        public ILockstepWorld World { get; }

        public PeerId LocalPeer { get; }

        public IReadOnlyList<LockstepChannel> Channels => channels;

        public int NetTick => tickBase + World.World.Tick;

        public int KnownUpTo => knownUpTo;

        public bool Synced => synced;

        public bool IsCaughtUp => caughtUp;

        public bool DesyncSeen { get; private set; }

        public int LastResetTick { get; private set; }

        public bool AutoJoin { get; set; } = true;

        public bool RecoverDesyncs { get; set; } = true;

        public double MostOwedSeconds { get; set; } = 1.0;

        public event Action<TickBatch> TickApplied;

        public event Action<int> CaughtUp;

        public event Action<LockstepDesync> Desynced;

        public event Action<LockstepRefusal> Refused;

        public event Action<int> ResetComing;

        public event Action<string> Failed;

        public LockstepChannel<T> Register<T>(LockstepChannel<T> channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (started) throw new InvalidOperationException("channels are registered before the session starts");
            if (channels.Any(c => c.Name == channel.Name)) throw new ArgumentException($"a channel named {channel.Name} is registered already", nameof(channel));
            channels.Add(channel);
            return channel;
        }

        public void Request<T>(LockstepChannel<T> channel, T command, int delayTicks = 0)
        {
            int index = IndexOf(channel);
            started = true;
            if (IsHost)
            {
                Take(LocalPeer, channel, command, delayTicks, true);
                return;
            }
            if (channel.HostOnly)
            {
                Refused?.Invoke(new LockstepRefusal(channel, command, LocalPeer, "only the host sends " + channel.Name));
                return;
            }
            var w = Message(LockstepKind.Intent);
            w.Natural((uint)index);
            w.Number(delayTicks);
            channel.Write(w, command);
            Send(Link.Host, default, w.Span);
        }

        public string Checksum() => string.Join("/", JoinSnapshot.ChecksumOf(World.World));

        public void Update(double elapsedSeconds, Func<bool> over = null)
        {
            started = true;
            over ??= () => false;
            if (IsHost) RunClock(elapsedSeconds);
            Advance(over);
            if (IsHost && !World.World.TickInProgress) ServeJoiners();
        }

        public void Received(PeerId from, ReadOnlySpan<byte> bytes)
        {
            started = true;
            try
            {
                if (!frames.TryJoin(from, bytes.ToArray(), out var message)) return;
                var r = new LockstepReader(message);
                var kind = (LockstepKind)r.Byte();
                if (IsHost) HostReceived(from, kind, r);
                else ClientReceived(kind, r);
            }
            catch (LockstepWireException e)
            {
                Failed?.Invoke($"A message from {from} didn't read: {e.Message}");
            }
        }

        int IndexOf(LockstepChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            int index = channels.IndexOf(channel);
            if (index < 0) throw new ArgumentException($"the channel {channel.Name} isn't registered with this session", nameof(channel));
            return index;
        }

        LockstepChannel ChannelAt(uint index) =>
            index < channels.Count ? channels[(int)index] : throw new LockstepWireException($"channel {index} of {channels.Count}");

        static LockstepWriter Message(LockstepKind kind)
        {
            var w = new LockstepWriter();
            w.Byte((byte)kind);
            return w;
        }

        enum Link { Host, Peer, All }

        void Send(Link link, PeerId peer, ReadOnlySpan<byte> message)
        {
            if (transport.Role == LockstepRole.Solo) return;
            foreach (var frame in frames.Split(message, transport.MaxMessageBytes))
            {
                if (link == Link.Host) transport.SendToHost(frame);
                else if (link == Link.Peer) transport.SendTo(peer, frame);
                else transport.Broadcast(frame);
            }
        }

        TickBatch ReadBatch(LockstepReader r)
        {
            var batch = new TickBatch(r.Number()) { Reset = r.Bool() };
            if (r.Bool())
            {
                var mods = new List<ActiveMod>();
                for (int i = r.Count(); i > 0; i--) mods.Add(new ActiveMod(r.Text() ?? "", r.Text()));
                batch.Mods = mods;
            }
            for (int c = r.Count(); c > 0; c--)
            {
                var channel = ChannelAt(r.Natural());
                for (uint n = r.Natural(); n > 0; n--) channel.AddTo(batch, channel.Read(r));
            }
            return batch;
        }

        void Hold(TickBatch batch)
        {
            if (synced && batch.Tick <= NetTick) return;
            batches[batch.Tick] = batch;
            ExtendKnown();
        }

        void ExtendKnown()
        {
            if (!synced) return;
            while (batches.ContainsKey(knownUpTo + 1)) knownUpTo++;
        }

        void Advance(Func<bool> over)
        {
            if (!synced) return;
            if (World.World.TickInProgress)
            {
                if (!World.World.StepSome(over)) return;
                Applied();
            }
            while (NetTick < knownUpTo && !over())
            {
                int next = NetTick + 1;
                if (batches.TryGetValue(next, out var batch)) batches.Remove(next);
                else batch = new TickBatch(next);
                if (batch.Reset) Rebuild(next);
                World.Prepare(batch);
                running = batch;
                if (!World.World.StepSome(over)) return;
                Applied();
            }
        }

        void Rebuild(int tick)
        {
            LastResetTick = tick;
            World.Rebuild();
            tickBase = tick - 1;
            HashBaseline();
        }

        void Applied()
        {
            int tick = NetTick;
            var batch = running ?? new TickBatch(tick);
            running = null;
            if (IsHost) held.Remove(tick);
            if (tick % RollingHash.Every == 0)
            {
                var chunks = WindowChunks(World.World);
                FinishWindow(tick, WindowParts(World.World, World.World.ChunkDigestHash()), chunks);
            }
            TickApplied?.Invoke(batch);
            if (!caughtUp && tick >= catchUpTo)
            {
                caughtUp = true;
                CaughtUp?.Invoke(tick);
            }
            if (IsHost) ServeJoiners();
        }

        static ulong[] WindowParts(World w, string cells) => new[]
        {
            Fnv1a64.Hash(cells), Fnv1a64.Hash(w.Rules.Fingerprint), Fnv1a64.Hash(w.ThingsNow.Fingerprint), Fnv1a64.Hash(w.SkyHash()),
            Fnv1a64.Hash(w.DefenseHash()), Fnv1a64.Hash(w.CastHash()), Fnv1a64.Hash(w.DragonSystem.Hash()), Fnv1a64.Hash(w.LandHash()),
        };

        ChunkHashEntry[] WindowChunks(World w)
        {
            var changed = w.RefreshChunkHashes();
            ownChunksAt = NetTick;
            return changed.ToArray();
        }

        void HashBaseline() => World.World.RefreshChunkHashes();

        sealed class HashWindow
        {
            public ulong[] Parts;
            public ChunkHashEntry[] Chunks;
        }

        const ulong HeldChunk = 1UL << 32;

        static void WriteChunks(LockstepWriter w, ChunkHashEntry[] chunks)
        {
            w.Natural((uint)chunks.Length);
            foreach (var c in chunks)
            {
                w.Number(c.X);
                w.Number(c.Y);
                w.Hash(c.Held ? HeldChunk | c.Hash : 0);
            }
        }

        static ChunkHashEntry[] ReadChunks(LockstepReader r)
        {
            var chunks = new ChunkHashEntry[r.Count()];
            for (int i = 0; i < chunks.Length; i++)
            {
                int x = r.Number(), y = r.Number();
                ulong h = r.Hash();
                chunks[i] = new ChunkHashEntry(x, y, (h & HeldChunk) != 0, unchecked((uint)h));
            }
            return chunks;
        }

        void FinishWindow(int tick, ulong[] parts, ChunkHashEntry[] chunks)
        {
            if (IsHost)
            {
                var w = Message(LockstepKind.Hash);
                w.Number(tick);
                w.Natural((uint)parts.Length);
                foreach (ulong p in parts) w.Hash(p);
                WriteChunks(w, chunks);
                Send(Link.All, default, w.Span);
                return;
            }
            ownHashes[tick] = new HashWindow { Parts = parts, Chunks = chunks };
            DropOldHashes(ownHashes, tick);
            DropOldHashes(hostHashes, tick);
            CheckHash(tick);
        }

        static void DropOldHashes(Dictionary<int, HashWindow> hashes, int tick)
        {
            foreach (int t in hashes.Keys.Where(t => t < tick - RollingHash.Every * HashesKept).ToList()) hashes.Remove(t);
        }

        void CheckHash(int tick)
        {
            if (DesyncSeen || !hostHashes.TryGetValue(tick, out var expected) || !ownHashes.TryGetValue(tick, out var here)) return;
            hostHashes.Remove(tick);
            ownHashes.Remove(tick);
            var differ = new List<string>();
            for (int p = 0; p < JoinSnapshot.PartNames.Count; p++)
                if (p >= expected.Parts.Length || expected.Parts[p] != here.Parts[p]) differ.Add(JoinSnapshot.PartNames[p]);
            if (differ.Count == 0) return;
            DesyncSeen = true;
            var chunks = differ.Contains(JoinSnapshot.PartNames[0]) ? DifferingChunks(tick, expected.Chunks, here.Chunks) : Array.Empty<(int cx, int cy)>();
            Desynced?.Invoke(new LockstepDesync(tick, differ, chunks));
            if (RecoverDesyncs) RequestSnapshot();
        }

        IReadOnlyList<(int cx, int cy)> DifferingChunks(int tick, ChunkHashEntry[] host, ChunkHashEntry[] own)
        {
            if (ownChunksAt != tick) return Array.Empty<(int cx, int cy)>();
            var w = World.World;
            var named = new SortedSet<(int cy, int cx)>();
            var hostHas = new HashSet<(int cx, int cy)>();
            foreach (var e in host)
            {
                hostHas.Add((e.X, e.Y));
                if (w.CachedChunkHash(e.X, e.Y) != e) named.Add((e.Y, e.X));
            }
            foreach (var e in own)
                if (!hostHas.Contains((e.X, e.Y))) named.Add((e.Y, e.X));
            return named.Select(k => (k.cx, k.cy)).ToList();
        }
    }
}
