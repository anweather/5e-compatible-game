using System.IO;
using UnityEngine;

// The bottom of the Esc menu (PauseMenu): the version this copy is ("alpha
// 0.1.0 (abc1234)", BuildInfo), a line and a download button when a newer
// alpha is out (UpdateCheck), an "Open log folder" button for bug reports, and
// a Credits section that unfolds the attribution (Credits). Starts itself.
public class GameInfoMenu : MonoBehaviour
{
    static GameInfoMenu instance;
    bool showCredits;
    GUIStyle small, credits;
    Vector2 scroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Game info menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameInfoMenu>();
    }

    void OnEnable() => PauseMenu.AddSection(Draw, PauseMenu.LastOrder);
    void OnDisable() => PauseMenu.RemoveSection(Draw);
    void OnDestroy() { if (instance == this) instance = null; }

    void Draw()
    {
        MakeStyles();
        var update = UpdateCheck.Available;
        if (update != null)
        {
            GUILayout.Label($"<color=#ffd24a><b>alpha {update.Version} is out</b></color>", small);
            if (GUILayout.Button("Open download page", PauseMenu.ButtonStyle)) UpdateCheck.OpenDownloadPage();
            GUILayout.Space(6);
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(showCredits ? "Hide credits" : "Credits", GUILayout.Height(32))) showCredits = !showCredits;
        if (GUILayout.Button("Open log folder", GUILayout.Height(32))) OpenLogFolder();
        GUILayout.EndHorizontal();
        if (showCredits)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(220));
            GUILayout.Label(CreditsText(), credits);
            GUILayout.EndScrollView();
        }
        GUILayout.Label($"<color=#ffffff88>{BuildInfo.Label}</color>", small);
    }

    static string CreditsText()
    {
        var assets = string.Join("\n", System.Array.ConvertAll(Credits.Assets, a => "· " + a));
        return $"{Credits.Game}\n\n<b>Rules</b>\n{Credits.Srd}\n\n<b>Assets</b>\n{assets}\n\n<b>Engine</b>\n{Credits.Engine}";
    }

    /// <summary>Opens the folder with Player.log (the Editor's own log in the Editor), to attach to a bug report.</summary>
    public static void OpenLogFolder()
    {
        string log = Application.consoleLogPath;
        string folder = string.IsNullOrEmpty(log) ? Application.persistentDataPath : Path.GetDirectoryName(log);
        Application.OpenURL(new System.Uri(folder).AbsoluteUri); // file:///..., spaces escaped
    }

    void MakeStyles()
    {
        if (small != null) return;
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        credits = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
    }
}
