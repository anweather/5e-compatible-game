// Lockstep in the core (D19, docs/design/core-package/multiplayer-ports.md): one session a process, over a transport
// port and a world port, the logic NetChem kept in the view's NGO layer. Every peer runs the same world from the same
// batches, and because the world is deterministic they stay identical.
//
// - The host owns the clock (Update's elapsed time; a client's is ignored). Each tick it stamps what was asked for it,
//   on every channel, into one batch, broadcasts it and runs it like everyone else. A command asked with an input delay
//   lands that many ticks later (0 to MaxDelayTicks), the same tick on every peer.
// - A peer runs only through the batches it holds (Advance), a tick in slices within the frame's budget (StepSome):
//   a batch's commands land between whole ticks, so peers slicing their frames differently compute the same ticks.
// - Every hash window (RollingHash) the host broadcasts its checksum's parts; a client that disagrees raises Desynced
//   and asks for a fresh snapshot (RecoverDesyncs), which it loads and plays on from.
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
        /// <summary>The wire's version: a peer refuses a host that speaks another.</summary>
        public const int Protocol = 1;

        /// <summary>The longest input delay a command may ask for, in ticks (NetChem's).</summary>
        public const int MaxDelayTicks = 30;

        // Hash windows kept waiting for the other side's, at most (a window one side never had would wait for ever).
        const int HashesKept = 4;

        readonly ILockstepTransport transport;
        readonly List<LockstepChannel> channels = new List<LockstepChannel>();
        readonly LockstepFrames frames = new LockstepFrames();
        bool started;

        // Everyone: the batches held and not yet run, how far they run unbroken, and the batch of the tick in progress.
        readonly Dictionary<int, TickBatch> batches = new Dictionary<int, TickBatch>();
        int knownUpTo;
        TickBatch running;
        // The network tick the world's tick 0 sits at: 0 until a reset rebuilds the world, or as a snapshot says.
        int tickBase;
        bool synced, caughtUp;
        int catchUpTo;
        // The desync check: windows still waiting for the other side's.
        RollingHash rolling = new RollingHash();
        readonly Dictionary<int, ulong[]> hostHashes = new Dictionary<int, ulong[]>(), ownHashes = new Dictionary<int, ulong[]>();

        /// <param name="transport">The network, as bytes (the adapter's).</param>
        /// <param name="world">The game's world, as the session drives it.</param>
        /// <param name="localPeer">This peer's id, as the transport knows it: the host's own requests come from it.</param>
        public LockstepSession(ILockstepTransport transport, ILockstepWorld world, PeerId localPeer = default)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            World = world ?? throw new ArgumentNullException(nameof(world));
            LocalPeer = localPeer;
            if (transport.MaxMessageBytes < LockstepFrames.MinMessageBytes)
                throw new ArgumentException($"a transport must take {LockstepFrames.MinMessageBytes} bytes a send at the least", nameof(transport));
            synced = caughtUp = IsHost;
        }

        public LockstepRole Role => transport.Role;

        /// <summary>The host or solo: it owns the clock.</summary>
        public bool IsHost => transport.Role != LockstepRole.Client;

        public ILockstepWorld World { get; }

        public PeerId LocalPeer { get; }

        /// <summary>The channels registered, in order: the same on every peer.</summary>
        public IReadOnlyList<LockstepChannel> Channels => channels;

        /// <summary>The network's tick: the world's own, plus the ticks before the last reset.</summary>
        public int NetTick => tickBase + World.World.Tick;

        /// <summary>The last tick this peer holds the batch of, unbroken from now: the host's own last stamped.</summary>
        public int KnownUpTo => knownUpTo;

        /// <summary>Whether this peer has a world in step with the host's: the host always, a client once a snapshot is in.</summary>
        public bool Synced => synced;

        /// <summary>Whether this peer plays live: the host always, a client once it has run to where the host was when it joined.</summary>
        public bool IsCaughtUp => caughtUp;

        /// <summary>True once this client has found a hash window that differs from the host's, until a snapshot puts it right.</summary>
        public bool DesyncSeen { get; private set; }

        /// <summary>The network tick of the last online reset (0 for none).</summary>
        public int LastResetTick { get; private set; }

        /// <summary>Client: asks to join as soon as the hello checks out. Off, the game calls Join itself (after loading the host's map).</summary>
        public bool AutoJoin { get; set; } = true;

        /// <summary>Client: asks the host for a fresh snapshot when it desyncs.</summary>
        public bool RecoverDesyncs { get; set; } = true;

        /// <summary>Host: the most time the clock owes ticks for; past it, peers keep the host's pace, whatever it is.</summary>
        public double MostOwedSeconds { get; set; } = 1.0;

        /// <summary>After a tick is run on this peer, with its batch.</summary>
        public event Action<TickBatch> TickApplied;

        /// <summary>Client: it has run to the host's tick at its snapshot, and plays live from here.</summary>
        public event Action<int> CaughtUp;

        /// <summary>Client: a hash window differs from the host's.</summary>
        public event Action<LockstepDesync> Desynced;

        /// <summary>A command the host wouldn't take (every peer hears it), or a host-only one a client asked for.</summary>
        public event Action<LockstepRefusal> Refused;

        /// <summary>An online reset is stamped for this network tick: every peer hears it as soon as the host stamps it.</summary>
        public event Action<int> ResetComing;

        /// <summary>Something went wrong that the session can't go on from as it is: a hello or snapshot that doesn't fit, a message that doesn't read.</summary>
        public event Action<string> Failed;

        /// <summary>
        /// Adds a channel; every peer registers the same, in the same order, before the session starts (its first Update,
        /// request or message).
        /// </summary>
        public LockstepChannel<T> Register<T>(LockstepChannel<T> channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (started) throw new InvalidOperationException("channels are registered before the session starts");
            if (channels.Any(c => c.Name == channel.Name)) throw new ArgumentException($"a channel named {channel.Name} is registered already", nameof(channel));
            channels.Add(channel);
            return channel;
        }

        /// <summary>
        /// Asks for a command, as this peer: it lands on every peer at the same tick, <paramref name="delayTicks"/> after the
        /// host's next (0 to MaxDelayTicks). The host names who asked and may refuse it (Refused).
        /// </summary>
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

        /// <summary>The whole checksum as NetChem builds it: JoinSnapshot.ChecksumOf's parts, joined by '/'.</summary>
        public string Checksum() => string.Join("/", JoinSnapshot.ChecksumOf(World.World));

        /// <summary>
        /// Once a frame: the host's clock moves on <paramref name="elapsedSeconds"/> and stamps the ticks due; then every
        /// peer runs the batches it holds while <paramref name="over"/> (the frame's work budget; null for none) says not
        /// to stop.
        /// </summary>
        public void Update(double elapsedSeconds, Func<bool> over = null)
        {
            started = true;
            over ??= () => false;
            if (IsHost) RunClock(elapsedSeconds);
            Advance(over);
            if (IsHost && !World.World.TickInProgress) ServeJoiners();
        }

        /// <summary>The adapter: a message arrived from <paramref name="from"/> (the host, on a client).</summary>
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

        // A batch as the host sent it: every peer, the host too, runs what these bytes say.
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

        // A batch arrived: held until its tick, unless that tick has run or is running here (a tick in progress is
        // counted in the world's Tick already, so NetTick is it).
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

        // Batch t runs while the world is at tick t-1; stepping then brings it to t. A tick a busy frame left part done
        // carries on first. (World.World is read afresh each time: a reset or a snapshot replaces it.)
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

        // The online reset (J-014) at a network tick: the world as it started, counting from 0 again from here.
        void Rebuild(int tick)
        {
            LastResetTick = tick;
            World.Rebuild();
            tickBase = tick - 1;
        }

        // A tick has finished: the desync check, listeners, and on the host a joiner waiting for a moment between ticks.
        void Applied()
        {
            int tick = NetTick;
            var batch = running ?? new TickBatch(tick);
            running = null;
            if (IsHost) held.Remove(tick);
            string cells = rolling.Add(World.World, tick);
            if (cells != null) FinishWindow(tick, WindowParts(World.World, cells));
            TickApplied?.Invoke(batch);
            if (!caughtUp && tick >= catchUpTo)
            {
                caughtUp = true;
                CaughtUp?.Invoke(tick);
            }
            if (IsHost) ServeJoiners();
        }

        // A window's parts in JoinSnapshot.PartNames' order, the cells' from the rolling hash: fixed 64-bit hashes.
        static ulong[] WindowParts(World w, string cells) => new[]
        {
            Fnv1a64.Hash(cells), Fnv1a64.Hash(w.Rules.Fingerprint), Fnv1a64.Hash(w.ThingsNow.Fingerprint), Fnv1a64.Hash(w.SkyHash()),
            Fnv1a64.Hash(w.DefenseHash()), Fnv1a64.Hash(w.CastHash()), Fnv1a64.Hash(w.DragonHash()), Fnv1a64.Hash(w.LandHash()),
        };

        void FinishWindow(int tick, ulong[] parts)
        {
            if (IsHost)
            {
                var w = Message(LockstepKind.Hash);
                w.Number(tick);
                w.Natural((uint)parts.Length);
                foreach (ulong p in parts) w.Hash(p);
                Send(Link.All, default, w.Span);
                return;
            }
            ownHashes[tick] = parts;
            int window = RollingHash.WindowFor(World.World);
            DropOldHashes(ownHashes, tick, window);
            DropOldHashes(hostHashes, tick, window);
            CheckHash(tick);
        }

        static void DropOldHashes(Dictionary<int, ulong[]> hashes, int tick, int window)
        {
            foreach (int t in hashes.Keys.Where(t => t < tick - window * HashesKept).ToList()) hashes.Remove(t);
        }

        void CheckHash(int tick)
        {
            if (DesyncSeen || !hostHashes.TryGetValue(tick, out var expected) || !ownHashes.TryGetValue(tick, out var here)) return;
            hostHashes.Remove(tick);
            ownHashes.Remove(tick);
            var differ = new List<string>();
            for (int p = 0; p < JoinSnapshot.PartNames.Count; p++)
                if (p >= expected.Length || expected[p] != here[p]) differ.Add(JoinSnapshot.PartNames[p]);
            if (differ.Count == 0) return;
            DesyncSeen = true;
            Desynced?.Invoke(new LockstepDesync(tick, differ));
            if (RecoverDesyncs) RequestSnapshot();
        }
    }
}
