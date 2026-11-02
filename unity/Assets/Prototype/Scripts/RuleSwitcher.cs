using System;
using System.IO;
using Chem;
using Newtonsoft.Json;
using UnityEngine;

// Decides which rules a running yard switches to, and sends the switch the
// right way (docs/LIVE_LAB.md):
//
// Every world is a room (World.Room.cs, docs/RULES_MCP.md), so a mod goes on or
// off as a RoomCommand, which the room's change mode and the checks decide:
//
// - Solo: queued on the ChemGrid, decided before the next tick.
// - Online: sent to the host with the mod's patch (NetChem.RequestRoom), and every
//   machine decides it at the tick the host stamps.
//
// The mods panel (ModsPanel) and the file watcher (RulesWatcher, in the Editor
// and development builds) both come through here. A change that doesn't check
// out is refused: the sentence goes to the log and the panel, and the old rules
// stay. A base rules file saved in solo doesn't go through the room's checks: it's
// queued on the ChemGrid.
[RequireComponent(typeof(ChemGrid))]
public class RuleSwitcher : MonoBehaviour
{
    ChemGrid grid;
    NetChem net;

    /// <summary>The shipped mods and the player's own.</summary>
    public ModCatalog Catalog { get; private set; }

    /// <summary>
    /// Where a player's saved mods live, beside notes.md: an update replaces the install, not this. Solo, host and Editor
    /// alike, so a save in the Editor leaves the repo's rules/mods, which is the shipped mods, alone.
    /// </summary>
    public static string PlayerModsDir => Path.Combine(ClaudeTerminal.Folder, "mods");

    /// <summary>Why the last switch was refused, or null after one that worked.</summary>
    public string LastError { get; private set; }

    /// <summary>The rules in force on this machine.</summary>
    public RuleSet Rules => grid.CurrentRules;

    bool Online => net != null && net.Online;
    bool IsClient => Online && !net.IsServer;

    /// <summary>The room this world is played in.</summary>
    public RoomState Room => grid.World.Room;

    /// <summary>This peer's name in the room.</summary>
    public string Me => Online ? net.LocalPeer : NetChem.PeerName(0);

    /// <summary>Whether this machine may change the rules from files: solo or the host.</summary>
    public bool OwnsRules => !IsClient;

    // Before anything reads the mods. Only a build has an install; the Editor's rules/mods is the repo's.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void MoveModsOutOfTheInstall()
    {
        if (Application.isEditor) return;
        foreach (string line in ModMove.FromInstall(Path.Combine(ChemGrid.FindRulesDir(), "mods"), PlayerModsDir)) Debug.Log("Mods: " + line);
    }

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        TryGetComponent(out net);
        Catalog = new ModCatalog(Path.Combine(ChemGrid.FindRulesDir(), "mods"), PlayerModsDir);
        if (!TryGetComponent<ModsPanel>(out _)) gameObject.AddComponent<ModsPanel>();
        if (!TryGetComponent<RowHouses>(out _)) gameObject.AddComponent<RowHouses>();
        if ((Application.isEditor || Debug.isDebugBuild) && !TryGetComponent<RulesWatcher>(out _)) gameObject.AddComponent<RulesWatcher>();
        grid.RoomDecided += Decided;
    }

    void OnDestroy() => grid.RoomDecided -= Decided;

    // This peer's own change: the panel says why it was refused, or stops saying.
    void Decided(RoomCommand command, RoomVerdict verdict)
    {
        if (command.Peer == Me) LastError = verdict.Done ? null : verdict.Line;
    }

    // What a new switch builds on: a switch already queued but not yet in force counts.
    RuleSet Upcoming => grid.UpcomingRules;

    /// <summary>
    /// Switches a mod from the catalog on or off, or takes off one that came another way. Returns
    /// why it was refused now, or null; the room's answer comes at the tick, in the log and LastError.
    /// </summary>
    public string Toggle(string id)
    {
        if (Room.Mods.Exists(m => m.Id == id)) return Ask(RoomCommand.Revert(Me, id));
        var file = Catalog.Find(id);
        if (file == null) return Refuse($"No mod \"{id}\" here");
        if (file.Error != null) return Refuse($"{file.Title}: {file.Error}");
        return Ask(RoomCommand.Apply(Me, id, file.AsActive().Json, file.Title));
    }

    /// <summary>Sends a room command on its way: decided before the next solo tick, or at the tick the host stamps.</summary>
    public string Ask(RoomCommand command)
    {
        if (Online) net.RequestRoom(command);
        else grid.QueueRoom(command);
        return null;
    }

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
            if (target.IsOn(id)) Ask(RoomCommand.Revert(Me, id));
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
            Ask(RoomCommand.Revert(Me, id));
            Ask(RoomCommand.Apply(Me, id, file.AsActive().Json, file.Title));
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

    // Builds the new rules (which checks them) and, if they fit, queues them. A null result means "only checking".
    void Switch(Func<RuleSet> build, string note, string what)
    {
        RuleSet next;
        try
        {
            next = build();
        }
        catch (ModException e)
        {
            Refuse($"{what} refused, rules unchanged: {string.Join("; ", e.Errors)}");
            return;
        }
        LastError = null;
        EventLog.Add(note);
        if (next != null) grid.QueueRules(next);
    }

    string Refuse(string why)
    {
        LastError = why;
        EventLog.Add($"<color=#ff8a80>{why}</color>");
        Debug.LogWarning("Rules: " + why);
        return why;
    }
}
