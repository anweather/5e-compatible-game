using System.Collections.Generic;
using Chem;
using Unity.Collections;

// Which of the wizards' stands a late joiner's strike history keeps: the compaction hook of NetChem's
// strike channel (CommandChannel). A joiner replays the history from the world's start (or the last
// reset) on a fresh world, so every command it leaves out must change nothing the replay does.
//
// With no step trigger in force a stand only has to be there for the next strike or the end, so a
// player's newer stand replaces those since the last strike (a strike, or a leaving, keeps everything
// up to it). That is the rule this always had.
//
// While a step trigger is in force (or a room change that could switch one on hasn't run yet), a stand
// may be a step that fires one (World.Triggers.cs), and the casts a step fires are in no other command:
// only replaying that stand makes them. The core takes a stand as a step when the stand before it is on
// another cell and neither is down, and what a step does depends only on its own stand, the way it moved
// (dx, dy, signs) and state that only firings change. So a stand b of a player, between the kept stand a
// before it and the stand c after it, is dropped (#83) only when all of these hold:
//
// - a and b come after the last strike or leaving (none moved or hurt anyone between them);
// - b's tick has run on the host and no trigger fired in it, and b was a step from a: so none of its
//   triggers was due then, and in any replay it fires nothing, whatever stand comes before it;
// - c steps the same way from a as from b (both not a step, or both the same signs): c does what it did;
// - no checksum's tick (RollingHash.Every) falls from b's tick to the tick before c's: the stand a
//   checksum sees is the same on the joiner.
//
// Each kept stand then still steps as it first did, and a dropped one never fires: so a joiner that
// replays this history, or this history up to a tick and every stand after it (the live batches that
// reach it while it asks), fires the same casts at the same ticks and checks the same checksums.
// The history still grows by about a stand a firing and one a checksum while players walk.
//
// The Hold reads stands too: every stand of a run (a raider's blow goes by where a wizard stood), and every stand on its
// plate and the one that leaves it (a run starts by how long someone stood there), so these are all kept, as a strike keeps
// what's up to it.
public sealed class StandKeeper
{
    readonly System.Func<bool> stepsOn;
    readonly System.Func<int, int, bool> inBounds;
    readonly System.Func<int, int, bool> keeps;
    readonly HashSet<FixedString32Bytes> leaving = new();

    // The last tick with a strike (or a leaving) in the history: stands up to it are kept.
    int lastStrikeTick;
    // Where keeping by steps last took over from keeping the newest, or handed back: stands up to it are kept.
    int switchedAt, lastStand;
    bool bySteps;
    // Host: the ticks its world has run (up to ranTo), the ones of those where a trigger fired, and the
    // world's count of firings so far.
    int ranTo, firings;
    readonly HashSet<int> fired = new();

    /// <param name="stepsOn">Whether a step trigger is in force, or may be by the time a stand stamped now runs.</param>
    /// <param name="inBounds">Whether a cell is in the world: the core ignores a stand off it.</param>
    /// <param name="keeps">Whether a stand on this cell, now, must stay in the history whatever else is true; null for none.</param>
    public StandKeeper(System.Func<bool> stepsOn, System.Func<int, int, bool> inBounds, System.Func<int, int, bool> keeps = null)
    {
        this.stepsOn = stepsOn;
        this.inBounds = inBounds;
        this.keeps = keeps;
    }

    /// <summary>Host: its world has run <paramref name="tick"/>, with this many trigger firings since it began (World.TriggerFirings).</summary>
    public void Ran(int tick, int firingsNow)
    {
        if (firingsNow != firings) fired.Add(tick);
        firings = firingsNow;
        ranTo = tick;
    }

    /// <summary>The compaction hook: adds a just-stamped stand, strike or leaving to the history, and drops the stands it makes needless.</summary>
    public void Record(List<StrikeIntent> history, StrikeIntent command)
    {
        if ((StrikeKind)command.Kind != StrikeKind.Stand)
        {
            lastStrikeTick = command.Tick;
            history.Add(command);
            return;
        }
        bool keep = keeps != null && keeps(command.X, command.Y);
        if (leaving.Remove(command.Player) || keep) lastStrikeTick = command.Tick;
        if (keep) leaving.Add(command.Player);
        bool steps = stepsOn();
        if (steps != bySteps)
        {
            switchedAt = System.Math.Max(switchedAt, lastStand);
            bySteps = steps;
        }
        lastStand = command.Tick;
        int floor = System.Math.Max(lastStrikeTick, switchedAt);
        var player = command.Player;
        if (!steps) history.RemoveAll(s => (StrikeKind)s.Kind == StrikeKind.Stand && s.Player == player && s.Tick > floor);
        history.Add(command);
        if (steps) DropNeedless(history, player, floor);
    }

    /// <summary>Everything forgotten, as a fresh session starts.</summary>
    public void Clear()
    {
        lastStrikeTick = switchedAt = lastStand = ranTo = firings = 0;
        bySteps = false;
        fired.Clear();
        leaving.Clear();
    }

    // One pass over a player's stands that the core takes, oldest first, dropping each that the rules above allow.
    void DropNeedless(List<StrikeIntent> history, FixedString32Bytes player, int floor)
    {
        if (fired.Count > 0) fired.RemoveWhere(t => t <= floor);
        var stands = new List<int>();
        for (int i = 0; i < history.Count; i++)
        {
            var s = history[i];
            if ((StrikeKind)s.Kind == StrikeKind.Stand && s.Player == player && inBounds(s.X, s.Y)) stands.Add(i);
        }
        var drop = new List<int>();
        int kept = -1;
        for (int k = 0; k + 1 < stands.Count; k++)
        {
            if (kept >= 0 && Needless(history[kept], history[stands[k]], history[stands[k + 1]], floor)) drop.Add(stands[k]);
            else kept = stands[k];
        }
        for (int i = drop.Count - 1; i >= 0; i--) history.RemoveAt(drop[i]);
    }

    bool Needless(StrikeIntent a, StrikeIntent b, StrikeIntent c, int floor) =>
        b.Tick > floor && a.Tick > lastStrikeTick
        && b.Tick <= ranTo && !fired.Contains(b.Tick)
        && Step(a, b) != null
        && Step(a, c) == Step(b, c)
        && FirstChecksumFrom(b.Tick) >= c.Tick;

    // How the core takes s after r (World.ApplyStrike): a step's signs, or null when it's no step (the same cell, or either down).
    static (int, int)? Step(StrikeIntent r, StrikeIntent s) =>
        r.Hp <= 0 || s.Hp <= 0 || (r.X == s.X && r.Y == s.Y) ? null : (System.Math.Sign(s.X - r.X), System.Math.Sign(s.Y - r.Y));

    // The first tick from this one whose checksum peers compare (RollingHash: every Every'th).
    static int FirstChecksumFrom(int tick) => (tick + RollingHash.Every - 1) / RollingHash.Every * RollingHash.Every;
}
