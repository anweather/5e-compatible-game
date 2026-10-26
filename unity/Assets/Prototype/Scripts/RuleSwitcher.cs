using System;
using System.IO;
using Chem;
using Newtonsoft.Json;
using UnityEngine;

// Decides which rules a running yard switches to, and sends the switch the
// right way (docs/LIVE_LAB.md):
//
// - Solo: the new rules are checked now and queued on the ChemGrid, which
//   puts them in force before the next tick.
// - Online host: checked now, then stamped into the next NetChem tick batch,
//   so every machine switches at the same tick.
// - Online client: asks the host, which checks the mod with its own copy of
//   the file and stamps it, or says why not.
//
// The mods panel (ModsPanel) and the file watcher (RulesWatcher, in the
// Editor and development builds) both come through here. A switch that
// doesn't check out is refused: the error is logged and shown in the panel,
// and the old rules stay.
[RequireComponent(typeof(ChemGrid))]
public class RuleSwitcher : MonoBehaviour
{
    ChemGrid grid;
    NetChem net;

    /// <summary>rules/mods/*.json.</summary>
    public ModCatalog Catalog { get; private set; }

    /// <summary>Why the last switch was refused, or null after one that worked.</summary>
    public string LastError { get; private set; }

    /// <summary>The rules in force on this machine.</summary>
    public RuleSet Rules => grid.CurrentRules;

    bool Online => net != null && net.Online;
    bool IsClient => Online && !net.IsServer;

    /// <summary>Whether this machine may change the rules from files: solo or the host.</summary>
    public bool OwnsRules => !IsClient;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        TryGetComponent(out net);
        Catalog = new ModCatalog(Path.Combine(ChemGrid.FindRulesDir(), "mods"));
        if (!TryGetComponent<ModsPanel>(out _)) gameObject.AddComponent<ModsPanel>();
        if ((Application.isEditor || Debug.isDebugBuild) && !TryGetComponent<RulesWatcher>(out _)) gameObject.AddComponent<RulesWatcher>();
    }

    // What a new switch builds on: a switch already queued or stamped but not yet in force counts.
    RuleSet Upcoming => Online ? net.HostUpcoming ?? grid.CurrentRules : grid.UpcomingRules;

    /// <summary>
    /// Switches a mod from rules/mods/ on or off. On a client it asks the host. Returns why it
    /// was refused, or null (also null on a client, whose answer comes later through <see cref="Refused"/>).
    /// </summary>
    public string Toggle(string id)
    {
        if (IsClient)
        {
            net.RequestToggle(id);
            EventLog.Add($"Asked the host to switch {Catalog.Find(id)?.Title ?? id}");
            return null;
        }
        var file = Catalog.Find(id);
        if (file == null) return Refuse($"No mod \"{id}\" in rules/mods/ here");
        if (file.Error != null) return Refuse($"{file.Title}: {file.Error}");
        var target = Upcoming;
        bool on = !target.IsOn(id);
        return Switch(() => target.WithMods(target.Toggled(file.AsActive())), $"{file.Title}: {(on ? "on" : "off")} at the next tick", file.Title);
    }

    /// <summary>The host refused this client's request.</summary>
    public void Refused(string why) => Refuse(why);

    /// <summary>
    /// A rules file was saved (from RulesWatcher, on the main thread): reload it, check it, and
    /// switch at the next tick if it is in force. A file that doesn't check out leaves the rules alone.
    /// </summary>
    public void FileChanged(string path)
    {
        if (!OwnsRules) return;
        string dir = Path.GetFileName(Path.GetDirectoryName(path));
        string shown = dir == "mods" ? "mods/" + Path.GetFileName(path) : Path.GetFileName(path);
        try
        {
            if (dir == "mods") ModFileChanged(path, shown);
            else BaseFileChanged(shown);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException || e is InvalidCastException || e is FormatException || e is System.Collections.Generic.KeyNotFoundException)
        {
            Refuse($"{shown}: {e.Message}");
        }
    }

    void ModFileChanged(string path, string shown)
    {
        var file = Catalog.Refresh(path);
        var target = Upcoming;
        string id = ModCatalog.IdOf(path);
        if (file == null)
        {
            if (target.IsOn(id)) Switch(() => target.WithMods(target.Toggled(new ActiveMod(id, ""))), $"{shown} deleted: switched off", shown);
            else EventLog.Add($"{shown} deleted");
            return;
        }
        if (file.Error != null)
        {
            Refuse($"{shown}: {file.Error}");
            return;
        }
        if (target.IsOn(id))
        {
            Switch(() => target.WithMods(target.Replaced(file.AsActive())), $"Rules reloaded: {shown}", shown);
            return;
        }
        // Off: check that it would fit, so a broken mod shows up as soon as it is saved.
        Switch(() => { target.WithMods(target.Toggled(file.AsActive())); return null; },
            $"Mod checked: {shown} ({file.Title}), off; switch it on in the mods panel (M)", shown);
    }

    void BaseFileChanged(string shown)
    {
        if (Online)
        {
            Refuse($"{shown} changed: base rules reload only in solo play (other machines have their own files)");
            return;
        }
        var target = Upcoming;
        Switch(() => target.WithBase(grid.LoadBaseRules()), $"Rules reloaded: {shown}", shown);
    }

    // Builds the new rules (which checks them) and, if they fit, sends them on their way.
    // A null result means "only checking". Returns the refusal, or null.
    string Switch(Func<RuleSet> build, string note, string what)
    {
        RuleSet next;
        try
        {
            next = build();
        }
        catch (ModException e)
        {
            return Refuse($"{what} refused, rules unchanged: {string.Join("; ", e.Errors)}");
        }
        LastError = null;
        EventLog.Add(note);
        if (next == null) return null;
        if (Online) net.StampRules(next);
        else grid.QueueRules(next);
        return null;
    }

    string Refuse(string why)
    {
        LastError = why;
        EventLog.Add($"<color=#ff8a80>{why}</color>");
        Debug.LogWarning("Rules: " + why);
        return why;
    }
}
