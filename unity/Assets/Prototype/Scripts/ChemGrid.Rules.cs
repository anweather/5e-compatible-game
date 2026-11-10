using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chem;
using UnityEngine;

// The rules a ChemGrid's world runs on, and switching them while it runs
// (docs/LIVE_LAB.md). The base rules are rules/*.json plus this scene's
// tuning (fireFadeCells, conductLimit), applied as a mod so it is in the
// fingerprint and survives every switch. On top go the switched-on mods.
//
// A switch only ever happens between ticks: solo, a checked RuleSet is queued
// and put in force before the next step; online, NetChem runs the host's
// tick batches, which carry rule switches next to the casts (RunBatch).
// RuleSwitcher decides what to switch to; this applies it.
public partial class ChemGrid
{
    RuleSet queuedRules;
    readonly List<RoomCommand> queuedRoom = new();
    ModBudgets budgets;
    // What the next world's room is: the host's chosen mode, or none yet for a client until the host says.
    ChangeMode? plannedMode;
    bool roomAwaited;

    /// <summary>The rules in force: base, the switched-on mods, and what they compose to.</summary>
    public RuleSet CurrentRules { get; private set; }

    /// <summary>What the rules will be after the next tick (solo): the queued switch, or the current rules.</summary>
    public RuleSet UpcomingRules => queuedRules ?? CurrentRules;

    /// <summary>Raised after the world's rules changed, so props and items can read their numbers again.</summary>
    public event Action RulesChanged;

    /// <summary>rules/*.json with this scene's tuning on top. Throws if a file can't be read or the tuning doesn't fit.</summary>
    public Rules LoadBaseRules() => RuleSet.BaseOf(Rules.Load(Folders.Rules), SceneTuning());

    /// <summary>A room command was decided on this machine (or, early, refused by the host).</summary>
    public event Action<RoomCommand, RoomVerdict> RoomDecided;

    /// <summary>The next world built opens its room in this mode: the host's choice (the mods on stay).</summary>
    public void PlanRoom(ChangeMode mode) => plannedMode = mode;

    /// <summary>The next world built opens no room: a client's, until the host says which (JoinRoom).</summary>
    public void AwaitRoom() => roomAwaited = true;

    /// <summary>A client: the host's room, as it stood when the history starts, opened on the world not yet stepped.</summary>
    public void JoinRoom(ChangeMode mode, string host, IReadOnlyList<RoomMod> mods)
    {
        roomAwaited = false;
        if (World.Room != null) return;
        World.OpenRoom(mode, host, CurrentRules.Base, Budgets, mods);
        CurrentRules = World.RoomRules;
        RecolourWorld();
        RulesChanged?.Invoke();
    }

    /// <summary>Solo: a room command, decided before the next tick.</summary>
    public void QueueRoom(RoomCommand command) => queuedRoom.Add(command);

    /// <summary>The host refused a command before stamping it: everyone hears, as they would at the tick.</summary>
    public void NoteRefused(RoomCommand command, RoomVerdict verdict)
    {
        EventLog.Add(verdict.Line);
        RoomDecided?.Invoke(command, verdict);
    }

    ModBudgets Budgets => budgets ??= ModBudgets.Load(Folders);

    // Every world is a room, solo too (docs/RULES_MCP.md): a rebuilt one keeps the room it replaces.
    void OpenRoom(RoomState was)
    {
        World.RoomDecided += (command, verdict) =>
        {
            if (command.Change == RoomChange.Revert && verdict.Done) Reverted();
            RoomDecided?.Invoke(command, verdict);
        };
        if (roomAwaited)
        {
            World.AwaitRoom(Budgets);
            return;
        }
        var mode = plannedMode ?? was?.Mode ?? ChangeMode.Open;
        plannedMode = null;
        string host = was?.Host ?? NetChem.PeerName(0);
        try
        {
            World.OpenRoom(mode, host, CurrentRules.Base, Budgets, was?.Mods);
        }
        catch (ModException e)
        {
            Debug.LogWarning("The mods on don't fit a new world: " + string.Join("; ", e.Errors));
            World.OpenRoom(mode, host, CurrentRules.Base, Budgets);
        }
        CurrentRules = World.RoomRules;
        RecolourWorld();
    }

    /// <summary>Puts <paramref name="next"/> (already checked) in force before the next solo tick.</summary>
    public void QueueRules(RuleSet next) => queuedRules = next ?? throw new ArgumentNullException(nameof(next));

    // A new world starts on the base rules with no mods.
    Rules StartRules()
    {
        queuedRules = null;
        CurrentRules = RuleSet.Plain(LoadBaseRules());
        return CurrentRules.Effective;
    }

    // The inspector's per-scene numbers, as a mod over rules/sim.json (null when there are none).
    ModPatch SceneTuning()
    {
        var parts = new List<string>();
        if (fireFadeCells > 0) parts.Add($"\"fire\": {{ \"fadeCells\": {fireFadeCells.ToString(CultureInfo.InvariantCulture)} }}");
        if (conductLimit > 0) parts.Add($"\"conductLimit\": {conductLimit.ToString(CultureInfo.InvariantCulture)}");
        return parts.Count == 0 ? null : ModPatch.Parse($"{{ \"title\": \"Scene tuning\", \"patch\": {{ \"sim\": {{ {string.Join(", ", parts)} }} }} }}");
    }

    // A mod went off (a revert, or a lapse: World.Lapse.cs) and its temporary materials were swept from every cell,
    // inactive chunks too. The rules are read again before the next tick (FollowLapse), and every chunk is redrawn
    // once the switch is done (RedrawOwed): a sweep of a mod and its dependents redraws once, not once a mod.
    bool reverted, redrawOwed;

    void Reverted()
    {
        reverted = true;
        redrawOwed = true;
    }

    void RedrawOwed()
    {
        if (!redrawOwed) return;
        redrawOwed = false;
        RedrawEveryChunk();
    }

    // A lapse takes a mod off at the end of a tick with no room command, so nothing put the room's rules in force on
    // this side (a tick batch hands back the rules it was given): read them again, as on a revert, between ticks.
    void FollowLapse()
    {
        if (!reverted) return;
        reverted = false;
        RedrawOwed();
        if (World.Room == null || World.RoomRules == null || ReferenceEquals(CurrentRules, World.RoomRules)) return;
        var before = CurrentRules;
        CurrentRules = World.RoomRules;
        if (before != null) Announce(before);
    }

    // Solo: the queued switch takes effect at this tick boundary.
    void ApplyQueuedRules()
    {
        FollowLapse();
        var before = CurrentRules;
        composing.Restart();
        foreach (var command in queuedRoom) World.ApplyRoom(command);
        if (queuedRoom.Count > 0) CurrentRules = World.RoomRules;
        queuedRoom.Clear();
        if (queuedRules != null)
        {
            bool newBase = World.Room != null && queuedRules.Base.Fingerprint != World.RoomRules.Base.Fingerprint;
            if (newBase) World.RebaseRoom(queuedRules.Base);
            else World.ApplyRules(queuedRules.Effective);
            CurrentRules = newBase ? World.RoomRules : queuedRules;
            queuedRules = null;
        }
        ComposeDone(before);
        RedrawOwed();
    }

    /// <summary>Online: runs one of the host's tick batches (a rule switch if it has one, its casts, a step).</summary>
    public void RunBatch(TickBatch batch)
    {
        PrepareBatch(batch);
        World.Step();
    }

    /// <summary>
    /// Online: a tick batch's rule switch, casts and other commands, without the step, which the
    /// caller then runs in slices (World.StepSome) so a long tick doesn't stall one frame.
    /// </summary>
    public void PrepareBatch(TickBatch batch)
    {
        FollowLapse();
        var before = CurrentRules;
        composing.Restart();
        CurrentRules = batch.Prepare(World, CurrentRules, out var errors);
        if (errors.Count > 0)
        {
            EventLog.Add($"<color=#ff8a80>The host's rules don't fit this machine's: {errors[0]}</color>");
            Debug.LogWarning("Rule switch at tick " + batch.Tick + " refused here: " + string.Join("; ", errors));
        }
        ComposeDone(before);
        RedrawOwed();
    }

    readonly System.Diagnostics.Stopwatch composing = new();

    /// <summary>How long the last switch took to compose (ms), and when its drawing began (realtime).</summary>
    public double ComposeMs { get; private set; }
    public float AnnouncedAt { get; private set; }

    void ComposeDone(RuleSet before)
    {
        if (ReferenceEquals(before, CurrentRules)) return;
        ComposeMs = composing.Elapsed.TotalMilliseconds;
        Announce(before);
    }

    void Announce(RuleSet before)
    {
        AnnouncedAt = Time.realtimeSinceStartup;
        // A room's change has its own line in the world log.
        if (!ReferenceEquals(CurrentRules, World.RoomRules)) EventLog.Add($"{Describe(before, CurrentRules)} from tick {World.Tick + 1} · rules {CurrentRules.Fingerprint}");
        RedrawLooks(before.Effective);
        RedrawLookFire();
        // Every colour drawn from the rules is as it was unless a look changed: most switches change none (#181).
        if (!before.Effective.SameLooks(CurrentRules.Effective)) RecolourWorld();
        RulesChanged?.Invoke();
    }

    // What stands on the cells of a terrain whose look changed, drawn again from the rules now in
    // force: every peer runs this at the same switch, with no scene load and no asset. Only the view moves. The
    // chunks drawn so far are queued, nearest first, and built again a few rows a frame (RedrawSomeLooks); a chunk
    // drawn later takes the look in force then. Every view of the map at once was 300 to 345 ms on the nine (#186).
    void RedrawLooks(Rules before)
    {
        var after = CurrentRules.Effective;
        var changed = new HashSet<string>(after.Terrain.Values
            .Where(t => before.Terrain.TryGetValue(t.Id, out var was) && !LookDef.Same(was.Look, t.Look))
            .Select(t => t.Id));
        if (changed.Count == 0 || views == null) return;
        QueueLooks(changed);
    }

    /// <summary>"Wildfire on", "Damp world off", "Wildfire reloaded", "Base rules reloaded", joined.</summary>
    static string Describe(RuleSet before, RuleSet after)
    {
        var parts = new List<string>();
        if (before.Base.Fingerprint != after.Base.Fingerprint) parts.Add("Base rules reloaded");
        foreach (var m in after.Mods)
        {
            var old = before.Mods.FirstOrDefault(o => o.Id == m.Id);
            if (old.Id == null) parts.Add($"{TitleOf(m)} on");
            else if (old.Json != m.Json) parts.Add($"{TitleOf(m)} reloaded");
        }
        foreach (var m in before.Mods)
            if (!after.IsOn(m.Id)) parts.Add($"{TitleOf(m)} off");
        return parts.Count > 0 ? string.Join(", ", parts) : "Rules unchanged";
    }

    static string TitleOf(ActiveMod m) => ModFile.FromJson(m.Id, m.Json).Title;
}
