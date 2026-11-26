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
    ChangeMode? plannedMode;

    public RuleSet CurrentRules { get; private set; }

    public RuleSet UpcomingRules => queuedRules ?? CurrentRules;

    public event Action RulesChanged;

    public Rules LoadBaseRules() => RuleSet.BaseOf(Rules.Load(Folders.Rules), SceneTuning());

    public event Action<RoomCommand, RoomVerdict> RoomDecided;

    public void PlanRoom(ChangeMode mode) => plannedMode = mode;

    public void QueueRoom(RoomCommand command) => queuedRoom.Add(command);

    public void NoteRefused(RoomCommand command, RoomVerdict verdict)
    {
        EventLog.Add(verdict.Line);
        RoomDecided?.Invoke(command, verdict);
    }

    ModBudgets Budgets => budgets ??= ModBudgets.Load(Folders);

    void OpenRoom(RoomState was)
    {
        HearRoom();
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

    void HearRoom()
    {
        World.RoomDecided += (command, verdict) =>
        {
            if (command.Change == RoomChange.Revert && verdict.Done) Reverted();
            RoomDecided?.Invoke(command, verdict);
        };
    }

    public void QueueRules(RuleSet next) => queuedRules = next ?? throw new ArgumentNullException(nameof(next));

    Rules StartRules()
    {
        queuedRules = null;
        CurrentRules = RuleSet.Plain(LoadBaseRules());
        return CurrentRules.Effective;
    }

    ModPatch SceneTuning()
    {
        var parts = new List<string>();
        if (fireFadeCells > 0) parts.Add($"\"fire\": {{ \"fadeCells\": {fireFadeCells.ToString(CultureInfo.InvariantCulture)} }}");
        if (conductLimit > 0) parts.Add($"\"conductLimit\": {conductLimit.ToString(CultureInfo.InvariantCulture)}");
        return parts.Count == 0 ? null : ModPatch.Parse($"{{ \"title\": \"Scene tuning\", \"patch\": {{ \"sim\": {{ {string.Join(", ", parts)} }} }} }}");
    }

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

    public void RunBatch(TickBatch batch)
    {
        PrepareBatch(batch);
        World.Step();
    }

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
        if (!ReferenceEquals(CurrentRules, World.RoomRules)) EventLog.Add($"{Describe(before, CurrentRules)} from tick {World.Tick + 1} · rules {CurrentRules.Fingerprint}");
        var part = System.Diagnostics.Stopwatch.StartNew();
        RedrawLooks(before.Effective);
        RedrawLookFire();
        double redrawMs = part.Elapsed.TotalMilliseconds;
        part.Restart();
        bool recoloured = !before.Effective.SameLooks(CurrentRules.Effective);
        if (recoloured) RecolourWorld();
        double recolourMs = part.Elapsed.TotalMilliseconds;
        part.Restart();
        string slowest = TellRulesChanged();
        Debug.Log(string.Format(CultureInfo.InvariantCulture, "RULE SWITCH compose_ms={0:0.0} redraw_ms={1:0.0} recolour_ms={2:0.0} recoloured={3} listeners_ms={4:0.0}{5}",
            ComposeMs, redrawMs, recolourMs, recoloured, part.Elapsed.TotalMilliseconds, slowest));
    }

    string TellRulesChanged()
    {
        if (RulesChanged == null) return "";
        if (!Debug.isDebugBuild)
        {
            RulesChanged();
            return "";
        }
        var timer = new System.Diagnostics.Stopwatch();
        var cost = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var listener in RulesChanged.GetInvocationList())
        {
            timer.Restart();
            ((System.Action)listener).Invoke();
            string name = listener.Method.DeclaringType?.Name + "." + listener.Method.Name;
            cost.TryGetValue(name, out double had);
            cost[name] = had + timer.Elapsed.TotalMilliseconds;
        }
        return string.Concat(cost.OrderByDescending(kv => kv.Value).Take(3)
            .Select(kv => string.Format(CultureInfo.InvariantCulture, " {0}={1:0.0}", kv.Key, kv.Value)));
    }

    void RedrawLooks(Rules before)
    {
        var after = CurrentRules.Effective;
        var changed = new HashSet<string>(after.Terrain.Values
            .Where(t => before.Terrain.TryGetValue(t.Id, out var was) && !LookDef.Same(was.Look, t.Look))
            .Select(t => t.Id));
        if (changed.Count == 0 || views == null) return;
        QueueLooks(changed);
    }

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
