// The ports of lockstep in the core (D19, docs/design/core-package/multiplayer-ports.md): what a host game implements
// so a LockstepSession can run over its network and its world. The transport carries bytes, reliably and in order on
// each link, and knows nothing of what they mean; the world port is the game's world as the session sees it: run a
// batch, rebuild for a reset, take and load a snapshot. Adapters (NGO in our game, NfE in Punchy, the loopback here)
// implement the transport and call back into the session: Received, PeerJoined, PeerLeft.
//
// Not a port: the sandbox has no peers.
using System;
using System.Collections.Generic;

namespace Chem.Lockstep
{
    /// <summary>What a peer is in a session: playing alone, ordering the ticks for others, or following the host's.</summary>
    public enum LockstepRole { Solo, Host, Client }

    /// <summary>A peer as the transport knows it: an int the adapter maps to its own connection id.</summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public PeerId(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool Equals(PeerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PeerId p && Equals(p);
        public override int GetHashCode() => Value;
        public override string ToString() => "peer " + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(PeerId a, PeerId b) => a.Equals(b);
        public static bool operator !=(PeerId a, PeerId b) => !a.Equals(b);
    }

    /// <summary>
    /// The network, as bytes: reliable and ordered on each link, so what one peer sends another arrives whole and in the
    /// order sent. Broadcast reaches every client, never the host itself (the host runs its own batches without a send).
    /// The adapter calls the session back: <see cref="LockstepSession.Received"/>, <see cref="LockstepSession.PeerJoined"/>
    /// and <see cref="LockstepSession.PeerLeft"/>. The session splits any message longer than MaxMessageBytes.
    /// </summary>
    public interface ILockstepTransport
    {
        LockstepRole Role { get; }

        /// <summary>The longest message the transport takes in one send; the session splits anything longer (32 at the least).</summary>
        int MaxMessageBytes { get; }

        void SendToHost(ReadOnlySpan<byte> message);
        void SendTo(PeerId peer, ReadOnlySpan<byte> message);
        void Broadcast(ReadOnlySpan<byte> message);
    }

    /// <summary>
    /// The game's world as the session drives it. The session steps World itself (World.StepSome); the port runs a
    /// batch's commands, rebuilds the world for an online reset and takes or loads a late joiner's snapshot.
    /// </summary>
    public interface ILockstepWorld
    {
        /// <summary>The world now: a reset or a snapshot replaces it, so read it afresh.</summary>
        World World { get; }

        /// <summary>The rules in force: the base and the mods (JoinSnapshot, the hello's fingerprint).</summary>
        RuleSet Rules { get; }

        /// <summary>Runs a batch's commands between ticks (TickBatch.Prepare), keeping the rules it returns.</summary>
        void Prepare(TickBatch batch);

        /// <summary>The online reset (J-014): the world again as it started, on the base rules; the reset's batch then re-stamps the mods.</summary>
        void Rebuild();

        /// <summary>The world between two ticks, for a joiner (JoinSnapshot.Take).</summary>
        JoinSnapshot Snapshot();

        /// <summary>Plays on from the host's snapshot (JoinSnapshot.Join); SaveException, saying why, if it doesn't fit.</summary>
        void Load(JoinSnapshot snapshot);
    }

    /// <summary>
    /// The world port for a world the core builds alone (tests, solo play, a game with no view of its own around the
    /// world): a function that makes the world as it starts, on the base rules.
    /// </summary>
    public sealed class LockstepWorld : ILockstepWorld
    {
        readonly Func<World> build;
        readonly Rules baseRules;
        readonly int[] land;
        readonly ModBudgets budgets;

        /// <param name="baseRules">The rules before any mod: a reset starts on them, and a snapshot's mods go over them.</param>
        /// <param name="build">Makes the world as it starts: at first, and at every reset.</param>
        /// <param name="land">A baked map's land (World.SetLand), which a snapshot loads over; null for none.</param>
        /// <param name="budgets">The mod budgets a room's snapshot needs (rules/budgets.json); null outside a room.</param>
        public LockstepWorld(Rules baseRules, Func<World> build, int[] land = null, ModBudgets budgets = null)
        {
            this.baseRules = baseRules ?? throw new ArgumentNullException(nameof(baseRules));
            this.build = build ?? throw new ArgumentNullException(nameof(build));
            this.land = land;
            this.budgets = budgets;
            Rebuild();
        }

        public World World { get; private set; }
        public RuleSet Rules { get; private set; }

        /// <summary>Why the last batch's rule switch didn't go on here (TickBatch.Prepare's errors); empty when it did.</summary>
        public IReadOnlyList<string> LastErrors { get; private set; } = Array.Empty<string>();

        public void Prepare(TickBatch batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            Rules = batch.Prepare(World, Rules, out var errors);
            LastErrors = errors;
        }

        public void Rebuild()
        {
            World = build();
            Rules = RuleSet.Plain(baseRules);
        }

        public JoinSnapshot Snapshot() => JoinSnapshot.Take(World, World.Source, Rules);

        public void Load(JoinSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var (world, rules) = snapshot.Join(baseRules, World.Source, land, budgets);
            World = world;
            Rules = rules;
        }
    }
}
