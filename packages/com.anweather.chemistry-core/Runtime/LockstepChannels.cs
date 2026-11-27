// A lockstep session's command channels (D19): each kind of command a game sends, registered with the session by name.
// A channel says how its command goes on the wire, which list of a TickBatch it joins, and what the host does as it
// takes one: names who asked (a peer's say about who it is is never trusted) and may refuse it, saying why. A game
// registers the ones it has, in the same order on every peer: ours all of TickBatch's, Punchy only casts.
//
// The standard channels below are TickBatch's lists. Effects, chunks and the dragon are the host's alone: its physics,
// its chunk plan and its summons; a client asking for one is refused.
//
// Not a port: the sandbox has no peers.
using System;
using System.Globalization;

namespace Chem.Lockstep
{
    public abstract class LockstepChannel
    {
        protected LockstepChannel(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a channel needs a name", nameof(name));
            Name = name;
        }

        public string Name { get; }

        public bool HostOnly { get; set; }

        public bool OnePerTick { get; set; }

        public abstract void Write(LockstepWriter w, object command);

        public abstract object Read(LockstepReader r);

        public abstract void AddTo(TickBatch batch, object command);

        internal abstract object SentBy(object command, PeerId from);
        internal abstract string Refusal(object command, PeerId from, LockstepSession session);
    }

    public sealed class LockstepChannel<T> : LockstepChannel
    {
        readonly Action<LockstepWriter, T> write;
        readonly Func<LockstepReader, T> read;
        readonly Action<TickBatch, T> addTo;

        public LockstepChannel(string name, Action<LockstepWriter, T> write, Func<LockstepReader, T> read, Action<TickBatch, T> addTo) : base(name)
        {
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.addTo = addTo ?? throw new ArgumentNullException(nameof(addTo));
        }

        public Func<T, PeerId, T> Named { get; set; }

        public Func<T, PeerId, LockstepSession, string> Refuses { get; set; }

        public override void Write(LockstepWriter w, object command) => write(w, (T)command);
        public override object Read(LockstepReader r) => read(r);
        public override void AddTo(TickBatch batch, object command) => addTo(batch, (T)command);
        internal override object SentBy(object command, PeerId from) => Named == null ? command : Named((T)command, from);
        internal override string Refusal(object command, PeerId from, LockstepSession session) => Refuses?.Invoke((T)command, from, session);
    }

    public static class LockstepChannels
    {
        public const int MaxPatchChars = 64 * 1024;

        public static LockstepChannel<CastCommand> Casts(Func<PeerId, string> player) => new LockstepChannel<CastCommand>("casts", (w, c) =>
            {
                w.Text(c.Spell);
                w.Number(c.X);
                w.Number(c.Y);
                w.Number(c.Dir);
                w.Number(c.Charge);
                w.Number(c.AimX);
                w.Number(c.AimY);
                w.Text(c.By);
            },
            r => new CastCommand(r.Text(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Text()),
            (b, c) => b.Casts.Add(c))
        {
            Named = (c, from) => new CastCommand(c.Spell, c.X, c.Y, c.Dir, World.ClampCharge(c.Charge), c.AimX, c.AimY, player(from)),
        };

        public static LockstepChannel<EffectCommand> Effects() => new LockstepChannel<EffectCommand>("effects", (w, e) =>
            {
                w.Number((int)e.Kind);
                w.Number(e.X);
                w.Number(e.Y);
                w.Text(e.Entity);
                w.Hash(e.Thing.Value);
            },
            r => new EffectCommand((EffectKind)r.Number(), r.Number(), r.Number(), r.Text(), new ThingId(r.Hash())),
            (b, e) => b.Effects.Add(e))
        { HostOnly = true };

        public static LockstepChannel<ChunkSwitch> Chunks() => new LockstepChannel<ChunkSwitch>("chunks", (w, s) =>
            {
                w.Number(s.CX);
                w.Number(s.CY);
                w.Bool(s.Active);
            },
            r => new ChunkSwitch(r.Number(), r.Number(), r.Bool()),
            (b, s) => b.Chunks.Add(s))
        { HostOnly = true };

        public static LockstepChannel<ChunkFill> Fills() => new LockstepChannel<ChunkFill>("fills", (w, f) =>
            {
                w.Number(f.Key.Layer);
                w.Number(f.Key.X);
                w.Number(f.Key.Y);
                var bytes = f.Ground.Bytes();
                w.Natural((uint)bytes.Length);
                w.Bytes(bytes);
            },
            r => new ChunkFill(new ChunkKey(r.Number(), r.Number(), r.Number()), ReadGround(r)),
            (b, f) => b.Fills.Add(f))
        { HostOnly = true };

        static ChunkGround ReadGround(LockstepReader r)
        {
            var bytes = new byte[r.Count()];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = r.Byte();
            return ChunkGround.FromBytes(bytes);
        }

        public static LockstepChannel<DefenseCommand> Defense() => new LockstepChannel<DefenseCommand>("defense", (w, d) =>
            {
                w.Number((int)d.Kind);
                w.Number(d.GateX);
                w.Number(d.GateY);
                w.Number(d.FlagX);
                w.Number(d.FlagY);
            },
            r => new DefenseCommand((DefenseKind)r.Number(), r.Number(), r.Number(), r.Number(), r.Number()),
            (b, d) => b.Defense.Add(d));

        public static LockstepChannel<ComboCommand> Combos(Func<PeerId, string> player, bool devBuild) => new LockstepChannel<ComboCommand>("combos", (w, c) =>
            {
                w.Text(c.Player);
                w.Number((int)c.Kind);
                w.Text(c.Element);
                w.Number(c.Step);
                w.Bool(c.Hold);
            },
            r => new ComboCommand(r.Text(), (ComboCommandKind)r.Number(), r.Text(), r.Number(), r.Bool()),
            (b, c) => b.Combos.Add(c))
        {
            Named = (c, from) => new ComboCommand(player(from), c.Kind, c.Element, c.Step, c.Hold),
            Refuses = (c, from, session) => c.TakenBy(devBuild) ? null : "a moment is only for a dev build or the Editor",
        };

        public static LockstepChannel<DragonCommand> Dragon() => new LockstepChannel<DragonCommand>("dragon", (w, d) => { }, r => new DragonCommand(),
            (b, d) => b.Dragon.Add(d))
        { HostOnly = true, OnePerTick = true };

        public static LockstepChannel<CourseCommand> Course(Func<PeerId, string> player) => new LockstepChannel<CourseCommand>("course", (w, c) =>
            {
                w.Text(c.Player);
                w.Number(c.X);
                w.Number(c.Y);
            },
            r => new CourseCommand(r.Text(), r.Number(), r.Number()),
            (b, c) => b.Course.Add(c))
        {
            Named = (c, from) => new CourseCommand(player(from), c.X, c.Y),
        };

        public static LockstepChannel<StrikeCommand> Strikes(Func<PeerId, string> player) => new LockstepChannel<StrikeCommand>("strikes", (w, s) =>
            {
                w.Text(s.Player);
                w.Number((int)s.Kind);
                w.Number(s.X);
                w.Number(s.Y);
                w.Number(s.AimX);
                w.Number(s.AimY);
                w.Number(s.Hp);
            },
            r => new StrikeCommand(r.Text(), (StrikeKind)r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number()),
            (b, s) => b.Strikes.Add(s))
        {
            Named = (s, from) => new StrikeCommand(player(from), s.Kind, s.X, s.Y, s.AimX, s.AimY, s.Hp),
        };

        public static LockstepChannel<RoomCommand> Room(Func<PeerId, string> peer) => new LockstepChannel<RoomCommand>("room", (w, c) =>
            {
                w.Number((int)c.Change);
                w.Text(c.Peer);
                w.Text(c.Mod);
                w.Text(c.Json);
                w.Text(c.Title);
            },
            r => new RoomCommand((RoomChange)r.Number(), r.Text(), r.Text(), r.Text(), r.Text()),
            (b, c) => b.Room.Add(c))
        {
            Named = (c, from) => c.As(peer(from)),
            Refuses = (c, from, session) => RoomRefusal(c, session),
        };

        static string RoomRefusal(RoomCommand c, LockstepSession session)
        {
            if (c.Change == RoomChange.Found) return "only the host grants a find, with the thing";
            var room = session.World.World.Room;
            if (room == null) return "this world isn't a room";
            return RoomPolicy.Refusal(room.Mode, room.Host, c.Peer)
                ?? (c.Json.Length > MaxPatchChars ? "that patch is too long for a live change" : null);
        }

        public static string PlayerName(PeerId peer) => "p" + peer.Value.ToString(CultureInfo.InvariantCulture);

        public static string PeerName(PeerId peer) => "Player " + (peer.Value + 1).ToString(CultureInfo.InvariantCulture);
    }
}
