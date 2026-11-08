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
    public static readonly (string what, string keys, string pad)[] Rows =
    {
        ("Move", "WASD", "Left stick"),
        ("Look", "Mouse", "Right stick"),
        ("Jump (again in the air: double jump)", "Space", "{South}"),
        ("Sprint", "Hold Shift", "Click {LS} (toggles)"),
        ("Dodge roll", "Left Ctrl", "Click the right stick"),
        ("Zoom", "Mouse wheel", "—"),
        ("Left hand: thrust (short range)", "Left click", "{LT}"),
        ("Left hand: throw (long range)", "Q", "{LB}"),
        ("Left hand: sweep (an area)", "Left click + Q", "{LT} + {LB}"),
        ("Right hand: thrust (short range)", "Right click", "{RT}"),
        ("Right hand: throw (long range)", "E", "{RB}"),
        ("Right hand: sweep (an area)", "Right click + E", "{RT} + {RB}"),
        ("Charge a gesture, then let go to fire", "Hold, release", "Hold, release"),
        ("Same gesture, both hands: merge", "Both at once", "Both at once"),
        ("Cancel a cast", "B", "{East}"),
        ("Attune the hands", "Tab", "hold D-pad ▼, sticks"),
        ("Spell list", "Tab", "Esc menu: Game, Spells"),
        ("In the Hold's attuning room: change a hand", "Left, right click", "{LT}, {RT}"),
        ("Kit spells: choose, cast", "[ ]   T", "D-pad ◀ ▶   ▲"),
        ("Debug: the spell radial", "F2", "F2"),
        ("In an animal's shape: change back", "Any hand's button", "Any hand's button"),
        ("Pick up / drop, sit", "E (near a thing)", "{West}"),
        ("Punch (empty-handed)", "F", "{East}"),
        ("Use what you hold", "F", "{North}"),
        ("Throw (hold: harder)", "G / right click", "{East}"),
        ("Reset the yard (solo)", "R", "{Select}"),
        ("Menu (then ↑ ↓ or W S, Enter)", "Esc", "{Start}"),
        ("Mods", "M", "Menu → Game → Mods"),
        ("Type to Claude", "Enter", "—"),
        ("Talk to Claude (let go to send)", "Hold H", "Hold both sticks in"),
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
        PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Settings);
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
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(HudScale.Height - 260f, 200f, 520f), () => Table());
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
        HudScale.Apply();
        if (shownAt < 0f || !EventLog.HudVisible) return;
        float age = Time.unscaledTime - shownAt;
        if (age > ShowSeconds || (PauseMenu.Instance != null && PauseMenu.Instance.Open)) return;
        MakeStyles();
        float alpha = Mathf.Clamp01((ShowSeconds - age) / FadeSeconds);
        var area = new Rect(HudScale.Width / 2f - 330f, 80f, 660f, Mathf.Min(HudScale.Height - 100f, 600f));
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
