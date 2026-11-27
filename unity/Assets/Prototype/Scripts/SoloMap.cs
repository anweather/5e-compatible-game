using Chem;
using UnityEngine;

// The map a player plays on when they start the game alone (#216), picked in the Esc menu (Game > Map) and kept between
// launches as the player's own setting (PlayerPrefs), never shared state: a host's map (NetSession.HostMap) and a joiner's
// (the host's) still come first while online. A player build starts on it; -map still wins, and the open Editor reads
// Temp/map instead (a probe on a second map). With none saved, or one this copy no longer has, a player starts on the hills
// (Andrew 2026-11-25: "Let's make the sparse island the default thing you load into"; the nine before, from 2026-11-23),
// unless a scripted run (-fps-probe, -playtest) asks for the valley it was written for; a copy without the hills plays the
// nine, and one without either the valley.
public sealed class SoloMap : MonoBehaviour
{
    const string Key = "solo-map";
    const int SectionOrder = 10;

    static (string id, string name)[] maps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Forget() => maps = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWithTheGame()
    {
        if (VirtualPlayer.Active || FindAnyObjectByType<SoloMap>() != null) return;
        var go = new GameObject("Solo map");
        DontDestroyOnLoad(go);
        go.AddComponent<SoloMap>();
    }

    public static string Saved()
    {
        string id = PlayerPrefs.GetString(Key, "");
        if (id.Length == 0) return null;
        maps ??= ChemGrid.Maps();
        return System.Array.FindIndex(maps, m => m.id == id) >= 0 ? id : null;
    }

    public const string FirstMap = "hills-12x10";

    public static readonly string[] FirstMaps = { FirstMap, "nine" };

    public static string Start(string[] args)
    {
        if (System.Array.IndexOf(args, "-fps-probe") >= 0 || System.Array.IndexOf(args, "-playtest") >= 0) return GameFolders.DefaultMap;
        if (Saved() is { } saved) return saved;
        maps ??= ChemGrid.Maps();
        return First(maps);
    }

    public static string First((string id, string name)[] have)
    {
        foreach (string id in FirstMaps)
            if (System.Array.FindIndex(have, m => m.id == id) >= 0) return id;
        return GameFolders.DefaultMap;
    }

    public static void Save(string id)
    {
        PlayerPrefs.SetString(Key, id);
        PlayerPrefs.Save();
    }

    public static string Next((string id, string name)[] all, string current)
    {
        int at = System.Array.FindIndex(all, m => m.id == current);
        return all[(at + 1) % all.Length].id;
    }

    void OnEnable() => PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Game);

    void OnDisable() => PauseMenu.RemoveSection(DrawEntry);

    void DrawEntry()
    {
        if (NetWizard.Local != null) return;
        maps ??= ChemGrid.Maps();
        if (maps.Length < 2) return;
        int at = System.Array.FindIndex(maps, m => m.id == ChemGrid.MapId);
        string name = at >= 0 ? maps[at].name : ChemGrid.MapId;
        if (!PauseMenu.Button($"Map: {name} (next: {maps[(at + 1) % maps.Length].name})", PauseMenu.ButtonStyle)) return;
        string next = Next(maps, ChemGrid.MapId);
        Save(next);
        SpawnPoints.StartIn = null;
        ChemGrid.PlayMap(next);
    }
}
