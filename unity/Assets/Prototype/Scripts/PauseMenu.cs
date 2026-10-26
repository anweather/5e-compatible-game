using UnityEngine;
using UnityEngine.InputSystem;

// The Esc menu: Resume, any sections other code adds (NetSession adds Play
// together; GameInfoMenu the version, credits and log folder), and Quit. It
// starts itself in every scene, like FpsProbe, so no scene or builder has to
// add it. While it is open the wizard's own controls and mouse look are off
// and the cursor is free, as with the mods panel; the game itself keeps
// running (online, the others are still playing).
//
//   Open / close    Esc
//   Choose          click
public class PauseMenu : MonoBehaviour
{
    const int Width = 360;

    static PauseMenu instance;
    static readonly System.Collections.Generic.List<(System.Action draw, int order)> sections = new();
    GUIStyle box, title, button;

    /// <summary>
    /// Adds a section drawn between Resume and Quit (IMGUI layout calls, inside the menu's
    /// width), so other parts of the game can put their controls in the menu without the
    /// menu knowing about them. Remove it again when its owner goes away.
    /// </summary>
    /// <param name="order">Lower draws higher up; equal orders keep the order they were added in.</param>
    public static void AddSection(System.Action draw, int order = 0)
    {
        if (sections.Exists(s => s.draw == draw)) return;
        int at = sections.FindIndex(s => s.order > order);
        sections.Insert(at < 0 ? sections.Count : at, (draw, order));
    }

    public static void RemoveSection(System.Action draw) => sections.RemoveAll(s => s.draw == draw);

    /// <summary>The order for sections that belong at the bottom, just above Quit (the version and credits).</summary>
    public const int LastOrder = 1000;

    /// <summary>The menu's button style, for sections that want matching buttons.</summary>
    public static GUIStyle ButtonStyle => instance != null ? instance.button : GUI.skin.button;

    // What opening the menu changed on the wizard, put back on close.
    PlayerControls pausedControls;
    Component lookInput;
    CursorLockMode cursorWas;
    bool cursorVisibleWas;

    public bool Open { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Pause menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PauseMenu>();
    }

    void Update()
    {
        var keys = Keyboard.current;
        if (keys == null || !keys.escapeKey.wasPressedThisFrame) return;
        // Esc closes the mods panel first, if it is open.
        var mods = FindFirstObjectByType<ModsPanel>();
        if (!Open && mods != null && mods.Open) mods.SetOpen(false);
        else SetOpen(!Open);
    }

    /// <summary>Opens or closes the menu.</summary>
    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        if (open) PauseWizard();
        else ResumeWizard();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void PauseWizard()
    {
        var caster = EventLog.TrackedCaster;
        if (caster != null && caster.controls != null && caster.controls.enabled)
        {
            pausedControls = caster.controls;
            pausedControls.enabled = false;
        }
        lookInput = caster != null ? caster.GetComponent(WizardRig.InputsComponent) : null;
        SetLook(false);
        cursorWas = Cursor.lockState;
        cursorVisibleWas = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ResumeWizard()
    {
        if (pausedControls != null) pausedControls.enabled = true;
        pausedControls = null;
        SetLook(true);
        lookInput = null;
        Cursor.lockState = cursorWas;
        Cursor.visible = cursorVisibleWas;
    }

    // Stops (or restarts) the mouse turning the camera; the last look input is cleared so it doesn't keep spinning.
    void SetLook(bool on)
    {
        if (lookInput == null) return;
        WizardRig.Field(lookInput, "cursorInputForLook", isPublic: true)?.SetValue(lookInput, on);
        var look = WizardRig.Field(lookInput, "look", isPublic: true);
        if (look != null && look.FieldType == typeof(Vector2)) look.SetValue(lookInput, Vector2.zero);
    }

    // --- drawing -----------------------------------------------------------------

    void OnGUI()
    {
        if (!Open) return;
        GUI.depth = -100; // over the rest of the HUD (lower draws on top)
        MakeStyles();
        // Centred, as tall as its contents.
        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(box, GUILayout.Width(Width));
        GUILayout.Label("<b>Paused</b>", title);
        GUILayout.Space(8);
        if (GUILayout.Button("Resume   <color=#ffffff88>Esc</color>", button)) SetOpen(false);
        foreach (var section in sections.ToArray())
        {
            GUILayout.Space(12);
            section.draw();
        }
        GUILayout.Space(12);
        if (GUILayout.Button("Quit", button)) Quit();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    void MakeStyles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12), alignment = TextAnchor.UpperCenter };
        box.normal.background = Solid(new Color(0.06f, 0.07f, 0.09f, 0.9f));
        title = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        button = new GUIStyle(GUI.skin.button) { fontSize = 19, richText = true, fixedHeight = 44 };
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
