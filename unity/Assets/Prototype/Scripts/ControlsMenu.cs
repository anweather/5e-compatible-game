using UnityEngine;

// The controls, in one place: the Esc menu's Controls page (PauseMenu), and
// the same table on screen for the first seconds of a player's first game
// (remembered in PlayerPrefs), with where to find it again. Controller buttons
// are named as printed on the pad in use (PadGlyphs). Starts itself.
public class ControlsMenu : MonoBehaviour
{
    /// <summary>The Controls page's id in the Esc menu.</summary>
    public const string ControlsPage = "controls";
    const int SectionOrder = 490;
    const string SeenKey = "controls.seen";
    const float ShowSeconds = 12f, FadeSeconds = 1.5f;

    /// <summary>What, on the keyboard and mouse, and on a controller ({South} and so on: PadGlyphs tokens).</summary>
    static readonly (string what, string keys, string pad)[] Rows =
    {
        ("Move", "WASD", "Left stick"),
        ("Look", "Mouse", "Right stick"),
        ("Jump", "Space", "{South}"),
        ("Sprint", "Hold Shift", "Click {LS} (toggles)"),
        ("Zoom", "Mouse wheel", "D-pad ▲ ▼"),
        ("Spells: aim at an element, then a spell", "Hold Tab", "Hold {LB}, right stick"),
        ("Back to your last spell", "Tap Tab", "Tap {LB}"),
        ("Next / previous spell", "E / Q", "{RB}"),
        ("Open an element's spells", "1 – 8", "D-pad ◀ ▶ (steps)"),
        ("Cast (hold: stronger)", "Left click", "{RT}"),
        ("Pick up / drop", "E (near a thing)", "{West}"),
        ("Use what you hold", "F", "{North}"),
        ("Throw (hold: harder)", "G / right click", "{East}"),
        ("Reset the yard (solo)", "R", "{Select}"),
        ("Menu", "Esc", "{Start}"),
        ("Mods", "M", "Menu → Mods"),
    };

    static ControlsMenu instance;
    PauseMenu.Page page;
    GUIStyle cell, head, note;
    Vector2 scroll;
    float shownAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Controls menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ControlsMenu>();
    }

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Controls", Width = 640, Draw = DrawPage };
        PauseMenu.AddSection(DrawEntry, SectionOrder);
        PauseMenu.AddPage(ControlsPage, page);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(ControlsPage);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void DrawEntry()
    {
        if (PauseMenu.Button("Controls", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(ControlsPage);
    }

    void DrawPage()
    {
        MakeStyles();
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(Screen.height - 260f, 200f, 520f), () => Table());
    }

    // The first time a player is in a game with spells, the table shows for a few seconds.
    void Update()
    {
        if (shownAt >= 0f || !EventLog.HudVisible || PlayerPrefs.GetInt(SeenKey, 0) == 1 || EventLog.TrackedCaster == null) return;
        shownAt = Time.unscaledTime;
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
    }

    void OnGUI()
    {
        if (shownAt < 0f || !EventLog.HudVisible) return;
        float age = Time.unscaledTime - shownAt;
        if (age > ShowSeconds || (PauseMenu.Instance != null && PauseMenu.Instance.Open)) return;
        MakeStyles();
        float alpha = Mathf.Clamp01((ShowSeconds - age) / FadeSeconds);
        var area = new Rect(Screen.width / 2f - 330f, 80f, 660f, Mathf.Min(Screen.height - 100f, 600f));
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 10f, area.width - 32f, area.height - 20f));
        GUILayout.Label("<b>Controls</b>", head);
        Table();
        GUILayout.Space(6);
        GUILayout.Label("<i>Esc (or Start) → Controls shows these again</i>", note);
        GUILayout.EndArea();
        GUI.color = Color.white;
    }

    void Table()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("", head, GUILayout.Width(270));
        GUILayout.Label("<b>Keyboard and mouse</b>", head, GUILayout.Width(150));
        GUILayout.Label("<b>Controller</b>", head);
        GUILayout.EndHorizontal();
        foreach (var (what, keys, pad) in Rows)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(what, cell, GUILayout.Width(270));
            GUILayout.Label($"<b>{keys}</b>", cell, GUILayout.Width(150));
            GUILayout.Label($"<b>{PadGlyphs.Text(pad)}</b>", cell);
            GUILayout.EndHorizontal();
        }
    }

    void MakeStyles()
    {
        if (cell != null) return;
        cell = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
        head = new GUIStyle(cell) { fontSize = 16 };
        note = new GUIStyle(cell) { alignment = TextAnchor.MiddleCenter };
    }
}
