// What a lockstep session says (D19): the kinds of message on the wire, and the values its events carry. Every message
// is a kind byte then its fields in LockstepWriter's encoding; LockstepSession writes and reads them.
//
// - Hello (host to a peer that connected): the protocol, the base rules' fingerprint, the map, the start's settings
//   and the channels' names. The peer checks them before it asks to join.
// - Join / Resync (peer to host): send me a snapshot (Join: I've just come; Resync: I've desynced).
// - Intent (peer to host): a command asked for, on a channel, with its input delay.
// - Batch (host to all): a tick's batch, every channel's commands in it, in the channels' order.
// - Hash (host to all): a hash window's parts (JoinSnapshot.PartNames), each FNV-1a 64 of its string.
// - Snapshot (host to a joiner): the net tick it was taken at, the host's tick then, the last reset's tick and the
//   snapshot's text (JoinSnapshot.Text); the batches after it follow.
// - ResetComing (host to all): an online reset is stamped for this tick (a countdown).
// - Refused (host to all): a command the host wouldn't take, on its channel, from whom and why.
//
// Not a port: the sandbox has no peers.
using System.Collections.Generic;
using System.Linq;

namespace Chem.Lockstep
{
    internal enum LockstepKind : byte { Hello = 1, Join, Intent, Batch, Hash, Snapshot, ResetComing, Refused, Resync }

    /// <summary>The host's hello: what a peer checks, and the map and start it builds, before it joins.</summary>
    public sealed class LockstepHello
    {
        public LockstepHello(int protocol, string rules, string map, IReadOnlyDictionary<string, string> start, IReadOnlyList<string> channels)
        {
            Protocol = protocol;
            Rules = rules ?? "";
            Map = map ?? "";
            Start = start ?? new Dictionary<string, string>();
            Channels = channels ?? System.Array.Empty<string>();
        }

        /// <summary>The host's LockstepSession.Protocol.</summary>
        public int Protocol { get; }
        /// <summary>The host's base rules' fingerprint (before any mod): a peer with other files can't follow it.</summary>
        public string Rules { get; }
        /// <summary>The map the host plays (docs/design/maps-as-data.md): a peer on another builds the host's.</summary>
        public string Map { get; }
        /// <summary>The host's start settings, by name (ours: the spawn it started in).</summary>
        public IReadOnlyDictionary<string, string> Start { get; }
        /// <summary>The host's channels' names, in the order registered.</summary>
        public IReadOnlyList<string> Channels { get; }

        internal void Write(LockstepWriter w)
        {
            w.Byte((byte)LockstepKind.Hello);
            w.Number(Protocol);
            w.Text(Rules);
            w.Text(Map);
            var start = Start.OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
            w.Natural((uint)start.Count);
            foreach (var kv in start)
            {
                w.Text(kv.Key);
                w.Text(kv.Value);
            }
            w.Natural((uint)Channels.Count);
            foreach (var c in Channels) w.Text(c);
        }

        internal static LockstepHello Read(LockstepReader r)
        {
            int protocol = r.Number();
            string rules = r.Text(), map = r.Text();
            var start = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
            for (int i = r.Count(); i > 0; i--) start[r.Text() ?? ""] = r.Text();
            var channels = new List<string>();
            for (int i = r.Count(); i > 0; i--) channels.Add(r.Text());
            return new LockstepHello(protocol, rules, map, start, channels);
        }
    }

    /// <summary>A hash window this peer's world disagrees with the host's on: its tick and the parts that differ.</summary>
    public sealed class LockstepDesync
    {
        public LockstepDesync(int tick, IReadOnlyList<string> parts)
        {
            Tick = tick;
            Parts = parts;
        }

        public int Tick { get; }
        /// <summary>The checksum's parts that differ, by JoinSnapshot.PartNames.</summary>
        public IReadOnlyList<string> Parts { get; }

        /// <summary>What it means, for the log: other files, other rules, or a world gone its own way.</summary>
        public string Text
        {
            get
            {
                if (Parts.Contains("things")) return $"Desync at tick {Tick}: this peer's world/things.json differs from the host's, so props have different ids. Same files?";
                if (Parts.Contains("rules")) return $"Desync at tick {Tick}: this peer runs different rules from the host's. Same mods, same files?";
                return $"Desync at tick {Tick}: this world no longer matches the host's ({string.Join(", ", Parts)})";
            }
        }
    }

    /// <summary>A command the host wouldn't take: its channel, the command, who asked and why.</summary>
    public sealed class LockstepRefusal
    {
        public LockstepRefusal(LockstepChannel channel, object command, PeerId peer, string why)
        {
            Channel = channel;
            Command = command;
            Peer = peer;
            Why = why;
        }

        public LockstepChannel Channel { get; }
        public object Command { get; }
        public PeerId Peer { get; }
        /// <summary>One sentence without its capital (RoomPolicy's style).</summary>
        public string Why { get; }
    }

    /// <summary>Host: a command it took, stamped for a tick (what the view may show early: a cast's gesture).</summary>
    public sealed class LockstepTaken
    {
        public LockstepTaken(PeerId from, LockstepChannel channel, object command, int tick)
        {
            From = from;
            Channel = channel;
            Command = command;
            Tick = tick;
        }

        public PeerId From { get; }
        public LockstepChannel Channel { get; }
        public object Command { get; }
        public int Tick { get; }
    }
}
