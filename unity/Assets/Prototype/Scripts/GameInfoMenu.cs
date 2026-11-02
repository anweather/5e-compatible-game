using UnityEngine;

// The bottom of the Esc menu's main page (PauseMenu): the version this copy is
// ("alpha 0.1.0 (abc1234)", BuildInfo), and a line and a download button when a
// newer alpha is out (UpdateCheck). In its Game sub menu, an About and credits
// button that opens the attribution and licences (Credits) as a page. Starts itself.
public class GameInfoMenu : MonoBehaviour
{
    /// <summary>The Credits page's id in the Esc menu.</summary>
    public const string CreditsPage = "credits";

    static GameInfoMenu instance;
    GUIStyle small, credits;
    Vector2 scroll;
    PauseMenu.Page creditsPage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Game info menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameInfoMenu>();
    }

    void OnEnable()
    {
        creditsPage ??= new PauseMenu.Page { Title = "About and credits", Width = 560, Draw = DrawCredits };
        PauseMenu.AddSection(Draw, PauseMenu.LastOrder, PauseMenu.Submenu.Main);
        PauseMenu.AddSection(DrawAbout, PauseMenu.LastOrder, PauseMenu.Submenu.Game);
        PauseMenu.AddPage(CreditsPage, creditsPage);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(Draw);
        PauseMenu.RemoveSection(DrawAbout);
        PauseMenu.RemovePage(CreditsPage);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void Draw()
    {
        MakeStyles();
        var update = UpdateCheck.Available;
        if (update != null)
        {
            GUILayout.Label($"<color=#ffd24a><b>alpha {update.Version} is out</b></color>", small);
            if (!SelfUpdate.Installed)
            {
                if (PauseMenu.Button("Open download page", PauseMenu.ButtonStyle)) UpdateCheck.OpenDownloadPage();
            }
            else if (SelfUpdate.Progress >= 0)
                GUILayout.Label($"Downloading... {SelfUpdate.Progress}%", small);
            else if (PauseMenu.Button("Update and restart", PauseMenu.ButtonStyle)) _ = SelfUpdate.DownloadAndRestart();
            if (SelfUpdate.Error != null) GUILayout.Label($"<color=#ff8a80>Update failed: {SelfUpdate.Error}</color>", small);
            GUILayout.Space(6);
        }
        GUILayout.Label($"<color=#ffffff88>{BuildInfo.Label}</color>", small);
    }

    void DrawAbout()
    {
        if (PauseMenu.Button("About and credits", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(CreditsPage);
    }

    // The Credits page: the attribution, scrolled with the mouse wheel or the right stick.
    void DrawCredits()
    {
        MakeStyles();
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(Screen.height - 260f, 160f, 460f), () => GUILayout.Label(CreditsText(), credits));
        GUILayout.Label($"<color=#ffffff88>{BuildInfo.Label}</color>", small);
    }

    static string CreditsText()
    {
        var assets = string.Join("\n", System.Array.ConvertAll(Credits.Assets, a => "· " + a));
        return $"{Credits.Game}\n\n<b>Rules</b>\n{Credits.Srd}\n\n<b>Licences</b>\n{Credits.Licences}\n\n<b>Assets</b>\n{assets}\n\n<b>Models and sounds</b>\n{Credits.Models}\n{Credits.Sounds}\n\n<b>Engine</b>\n{Credits.Engine}\n\n<b>Libraries</b>\n{Credits.Libraries}\n\n<b>With thanks</b>\n{Credits.Contributions}";
    }

    void MakeStyles()
    {
        if (small != null) return;
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        credits = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
    }
}
