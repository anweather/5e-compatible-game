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
    public enum LockstepRole { Solo, Host, Client }

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

    public interface ILockstepTransport
    {
        LockstepRole Role { get; }

        int MaxMessageBytes { get; }

        void SendToHost(ReadOnlySpan<byte> message);
        void SendTo(PeerId peer, ReadOnlySpan<byte> message);
        void Broadcast(ReadOnlySpan<byte> message);
    }

    public interface ILockstepWorld
    {
        World World { get; }

        RuleSet Rules { get; }

        void Prepare(TickBatch batch);

        void Rebuild();

        JoinSnapshot Snapshot();

        void Load(JoinSnapshot snapshot);
    }

    public sealed class LockstepWorld : ILockstepWorld
    {
        readonly Func<World> build;
        readonly Rules baseRules;
        readonly int[] land;
        readonly ModBudgets budgets;

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
