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

    public ModCatalog Catalog { get; private set; }

    public string LastError { get; private set; }

    public RuleSet Rules => grid.CurrentRules;

    bool Online => net != null && net.Online;
    bool IsClient => Online && !net.IsServer;

    public RoomState Room => grid.World.Room;

    public string Me => Online ? net.LocalPeer : NetChem.PeerName(0);

    public bool OwnsRules => !IsClient;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void MoveModsOutOfTheInstall()
    {
        if (Application.isEditor) return;
        foreach (string line in ModMove.FromInstall(ChemGrid.Folders.ShippedMods, ChemGrid.Folders.Mods)) Debug.Log("Mods: " + line);
    }

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        TryGetComponent(out net);
        Catalog = new ModCatalog(ChemGrid.Folders.ShippedMods, ChemGrid.Folders.Mods);
        if (!TryGetComponent<ModsPanel>(out _)) gameObject.AddComponent<ModsPanel>();
        if (!TryGetComponent<RowHouses>(out _)) gameObject.AddComponent<RowHouses>();
        if ((Application.isEditor || Debug.isDebugBuild) && !TryGetComponent<RulesWatcher>(out _)) gameObject.AddComponent<RulesWatcher>();
        grid.RoomDecided += Decided;
    }

    void OnDestroy() => grid.RoomDecided -= Decided;

    void Decided(RoomCommand command, RoomVerdict verdict)
    {
        if (command.Peer == Me) LastError = verdict.Done ? null : verdict.Line;
    }

    public string LapseNote(string id)
    {
        var world = grid.World;
        var mod = Room?.Mods.Find(m => m.Id == id);
        if (mod == null) return null;
        int at = world.LapsesAt(mod);
        if (at < 0) return null;
        int seconds = Mathf.Max(0, at - world.Tick) / Mathf.Max(1, world.Rules.Sim.TicksPerSecond);
        return $"lapses in {seconds / 60}:{seconds % 60:00}";
    }

    RuleSet Upcoming => grid.UpcomingRules;

    public string Toggle(string id)
    {
        if (Room.Mods.Exists(m => m.Id == id)) return Ask(RoomCommand.Revert(Me, id));
        var file = Catalog.Find(id);
        if (file == null) return Refuse($"No mod \"{id}\" here");
        if (file.Error != null) return Refuse($"{file.Title}: {file.Error}");
        return Ask(RoomCommand.Apply(Me, id, file.AsActive().Json, file.Title));
    }

    public string Ask(RoomCommand command)
    {
        if (Online) net.RequestRoom(command);
        else grid.QueueRoom(command);
        return null;
    }

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
