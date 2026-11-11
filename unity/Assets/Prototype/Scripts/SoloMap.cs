using UnityEngine;

// The map a player plays on when they start the game alone (#216), picked in the Esc menu (Game > Map) and kept between
// launches as the player's own setting (PlayerPrefs), never shared state: a host's map (NetSession.HostMap) and a joiner's
// (the host's) still come first while online. A player build starts on it; -map still wins, and the open Editor reads
// Temp/map instead (a probe on a second map). A saved map this copy no longer has is forgotten and the valley plays.
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
        if (ProbeLink.InVirtualPlayer || FindAnyObjectByType<SoloMap>() != null) return;
        var go = new GameObject("Solo map");
        DontDestroyOnLoad(go);
        go.AddComponent<SoloMap>();
    }

    /// <summary>The saved map's id, or null when none is saved or this copy no longer has it.</summary>
    public static string Saved()
    {
        string id = PlayerPrefs.GetString(Key, "");
        if (id.Length == 0) return null;
        maps ??= ChemGrid.Maps();
        return System.Array.FindIndex(maps, m => m.id == id) >= 0 ? id : null;
    }

    /// <summary>Keeps the pick for the next launch.</summary>
    public static void Save(string id)
    {
        PlayerPrefs.SetString(Key, id);
        PlayerPrefs.Save();
    }

    /// <summary>The map after <paramref name="current"/> in <paramref name="all"/>, wrapping; the first for one not in it.</summary>
    public static string Next((string id, string name)[] all, string current)
    {
        int at = System.Array.FindIndex(all, m => m.id == current);
        return all[(at + 1) % all.Length].id;
    }

    void OnEnable() => PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Game);

    void OnDisable() => PauseMenu.RemoveSection(DrawEntry);

    void DrawEntry()
    {
        // Online, the host's map is the one played, and hosting has its own pick.
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
