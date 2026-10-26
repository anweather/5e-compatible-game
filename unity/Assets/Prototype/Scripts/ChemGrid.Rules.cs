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

    /// <summary>The rules in force: base, the switched-on mods, and what they compose to.</summary>
    public RuleSet CurrentRules { get; private set; }

    /// <summary>What the rules will be after the next tick (solo): the queued switch, or the current rules.</summary>
    public RuleSet UpcomingRules => queuedRules ?? CurrentRules;

    /// <summary>Raised after the world's rules changed, so props and items can read their numbers again.</summary>
    public event Action RulesChanged;

    /// <summary>rules/*.json with this scene's tuning on top. Throws if a file can't be read or the tuning doesn't fit.</summary>
    public Rules LoadBaseRules() => RuleSet.BaseOf(Rules.Load(FindRulesDir()), SceneTuning());

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

    // Solo: the queued switch takes effect at this tick boundary.
    void ApplyQueuedRules()
    {
        if (queuedRules == null) return;
        var before = CurrentRules;
        CurrentRules = queuedRules;
        queuedRules = null;
        World.ApplyRules(CurrentRules.Effective);
        Announce(before);
    }

    /// <summary>Online: runs one of the host's tick batches (a rule switch if it has one, its casts, a step).</summary>
    public void RunBatch(TickBatch batch)
    {
        var before = CurrentRules;
        CurrentRules = batch.RunOn(World, CurrentRules, out var errors);
        if (errors.Count > 0)
        {
            EventLog.Add($"<color=#ff8a80>The host's rules don't fit this machine's: {errors[0]}</color>");
            Debug.LogWarning("Rule switch at tick " + batch.Tick + " refused here: " + string.Join("; ", errors));
        }
        if (!ReferenceEquals(before, CurrentRules)) Announce(before);
    }

    void Announce(RuleSet before)
    {
        EventLog.Add($"{Describe(before, CurrentRules)} from tick {World.Tick + 1} · rules {CurrentRules.Fingerprint}");
        RulesChanged?.Invoke();
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
