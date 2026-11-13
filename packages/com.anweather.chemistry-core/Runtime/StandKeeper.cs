// Which of the wizards' stands a late joiner's strike history keeps: the compaction hook of NetChem's
// strike channel. A joiner replays the history from the world's start (or the last
// reset) on a fresh world, so every command it leaves out must change nothing the replay does, at any
// tick: its past is the host's (#55, #164, #173), and only what no rule and no checksum reads may differ.
//
// A stand sets where its wizard is and its health outright (World.ApplyStrike), so leaving out a stand
// b of a player, between the kept stand a before it and the stand c after it, changes the core's stands
// only from b's tick to c's (the wizard is where a left it, not at b), and from c on they're the same.
// What reads the stands in that time (find them by World's `stands`):
//
// - a checksum (World.StrikeText, in CastHash): every window's end (RollingHash) and every Every'th tick;
// - a strike, aimed and knocked back by where everyone stands, and a leaving: these keep every stand up
//   to them, as they always did;
// - the rules that read them as they run: the golf turn (World.GolfWaiting), a run of the Hold starting
//   (how many stand), a hand attuning (not while down), and any trigger firing. The world counts each such
//   read (World.StandReads), and the host tells this which ticks had one (Ran);
// - the Hold through a run and at its plate and towers (World.HoldReadsStand): every stand on them and the
//   one that leaves them is kept, as a strike keeps what's up to it;
// - triggers that read every stand as they run (World.TriggersReadStands): a step trigger (World.Triggers.cs)
//   takes a stand as a step when the stand before it is on another cell and neither is down, and a tick
//   trigger fires round every wizard standing. A step's casts are in no other command: only replaying its
//   stand makes them. What a step does depends only on its own stand, the way it moved (signs) and state
//   that only firings change.
//
// So b is dropped only when all of these hold:
//
// - b comes after the last strike or leaving, and after the last switch between the two ways below;
// - c's tick has run on the host, and from b's tick to c's (both in: a rule may read a stand before the
//   tick's stands land) no rule read the stands and no checksum fell;
// - while triggers read stands: a too comes after the last strike (nobody was knocked between them), b was
//   a step from a (both up, so the same wizards stand), and c steps the same way from a as from b (both not
//   a step, or both the same signs): c does what it did, and b, dropped, fires nothing it did (it fired
//   nothing: no rule read the stands at its tick).
//
// A stand off the world is never kept: the core does nothing with it. A joiner that replays this history,
// or this history up to a tick and every stand after it (the live batches that reach it while it asks),
// then has the host's stands at every tick something reads them. The history grows by about a stand a
// checksum, and one a read, while players walk (#83): the stands no rule can read any more go.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A stand, strike or leaving as a history holds it: what StandKeeper reads of each (NetChem's StrikeIntent, the tests' StampedStrike).</summary>
    public interface IStampedStrike<in T>
    {
        int Tick { get; }
        StrikeKind Kind { get; }
        int X { get; }
        int Y { get; }
        int Hp { get; }

        bool SamePlayer(T other);
    }

    /// <summary>A strike command and the tick it was stamped for.</summary>
    public readonly struct StampedStrike : IStampedStrike<StampedStrike>
    {
        public StampedStrike(int tick, StrikeCommand command)
        {
            Tick = tick;
            Command = command;
        }

        public int Tick { get; }
        public StrikeCommand Command { get; }
        public StrikeKind Kind => Command.Kind;
        public int X => Command.X;
        public int Y => Command.Y;
        public int Hp => Command.Hp;

        public bool SamePlayer(StampedStrike other) => string.Equals(Command.Player, other.Command.Player, StringComparison.Ordinal);
    }

    public sealed class StandKeeper<T> where T : struct, IStampedStrike<T>
    {
        readonly Func<bool> readByTriggers;
        readonly Func<int, int, bool> inBounds;
        readonly Func<int, int, bool> keeps;
        readonly Func<int, int, int> deltaX;
        // The players whose last stand was one kept whatever else is true: their next one says when they left.
        readonly List<T> leaving = new List<T>();

        // The last tick with a strike (or a leaving) in the history: stands up to it are kept.
        int lastStrikeTick;
        // Where keeping while triggers read stands last took over from keeping without, or handed back: stands up to it are kept.
        int switchedAt, lastStand;
        bool byTriggers;
        // Host: the ticks its world has run (up to ranTo), the ones of those where a rule read the stands, the
        // world's count of reads so far, and its checksum window.
        int ranTo, reads, window = RollingHash.Every;
        readonly HashSet<int> read = new HashSet<int>();

        /// <param name="readByTriggers">Whether a trigger that reads stands is in force (World.TriggersReadStands), or may be by the time a stand stamped now runs.</param>
        /// <param name="inBounds">Whether a cell is in the world: the core ignores a stand off it.</param>
        /// <param name="keeps">Whether a stand on this cell, now, must stay in the history whatever else is true (World.HoldReadsStand); null for none.</param>
        /// <param name="deltaX">The cells from one x east to another as the core takes them (World.DeltaX: the short way round the seam on a world that wraps); null for x1 - x0.</param>
        public StandKeeper(Func<bool> readByTriggers, Func<int, int, bool> inBounds, Func<int, int, bool> keeps = null, Func<int, int, int> deltaX = null)
        {
            this.readByTriggers = readByTriggers;
            this.inBounds = inBounds;
            this.keeps = keeps;
            this.deltaX = deltaX ?? ((x0, x1) => x1 - x0);
        }

        /// <summary>
        /// Host: its world has run <paramref name="tick"/>, with this many reads of the stands since it began (World.StandReads),
        /// and checksums every <paramref name="windowTicks"/> ticks (RollingHash.WindowFor) as well as every RollingHash.Every.
        /// </summary>
        public void Ran(int tick, int readsNow, int windowTicks = RollingHash.Every)
        {
            if (readsNow != reads) read.Add(tick);
            reads = readsNow;
            ranTo = tick;
            window = Math.Max(1, windowTicks);
        }

        /// <summary>The compaction hook: adds a just-stamped stand, strike or leaving to the history, and drops the stands it makes needless.</summary>
        public void Record(List<T> history, T command)
        {
            if (command.Kind != StrikeKind.Stand)
            {
                lastStrikeTick = command.Tick;
                history.Add(command);
                return;
            }
            if (!inBounds(command.X, command.Y)) return;
            bool keep = keeps != null && keeps(command.X, command.Y);
            int was = leaving.FindIndex(l => l.SamePlayer(command));
            if (was >= 0) leaving.RemoveAt(was);
            if (was >= 0 || keep) lastStrikeTick = command.Tick;
            if (keep) leaving.Add(command);
            bool triggers = readByTriggers();
            if (triggers != byTriggers)
            {
                switchedAt = Math.Max(switchedAt, lastStand);
                byTriggers = triggers;
            }
            lastStand = command.Tick;
            history.Add(command);
            DropNeedless(history, command, Math.Max(lastStrikeTick, switchedAt));
        }

        /// <summary>Everything forgotten, as a fresh session starts.</summary>
        public void Clear()
        {
            lastStrikeTick = switchedAt = lastStand = ranTo = reads = 0;
            window = RollingHash.Every;
            byTriggers = false;
            read.Clear();
            leaving.Clear();
        }

        // One pass over a player's stands, oldest first, dropping each that the rules above allow.
        void DropNeedless(List<T> history, T player, int floor)
        {
            if (read.Count > 0) read.RemoveWhere(t => t <= floor);
            var stands = new List<int>();
            for (int i = 0; i < history.Count; i++)
                if (history[i].Kind == StrikeKind.Stand && history[i].SamePlayer(player)) stands.Add(i);
            var drop = new List<int>();
            int kept = -1;
            for (int k = 0; k + 1 < stands.Count; k++)
            {
                if (kept >= 0 && Needless(history[kept], history[stands[k]], history[stands[k + 1]], floor)) drop.Add(stands[k]);
                else kept = stands[k];
            }
            for (int i = drop.Count - 1; i >= 0; i--) history.RemoveAt(drop[i]);
        }

        bool Needless(T a, T b, T c, int floor) =>
            b.Tick > floor && c.Tick <= ranTo && !ReadFrom(b.Tick, c.Tick)
            && (!byTriggers || (a.Tick > lastStrikeTick && Step(a, b) != null && Step(a, c) == Step(b, c)));

        // Whether a rule read the stands, or a checksum fell, on a tick from one to the other, both in.
        bool ReadFrom(int from, int to)
        {
            if (FirstMultipleFrom(from, RollingHash.Every) <= to || FirstMultipleFrom(from, window) <= to) return true;
            foreach (int t in read)
                if (t >= from && t <= to) return true;
            return false;
        }

        // How the core takes s after r (World.ApplyStrike): a step's signs, or null when it's no step (the same cell, or either down).
        (int, int)? Step(T r, T s)
        {
            int dx = deltaX(r.X, s.X);
            return r.Hp <= 0 || s.Hp <= 0 || (dx == 0 && r.Y == s.Y) ? null : (Math.Sign(dx), Math.Sign(s.Y - r.Y));
        }

        static int FirstMultipleFrom(int tick, int n) => (tick + n - 1) / n * n;
    }
}
