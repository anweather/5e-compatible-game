using UnityEngine;
using UnityEngine.InputSystem;

// The Esc menu: Resume or Quit. It starts itself in every scene, like FpsProbe,
// so no scene or builder has to add it. While it is open the wizard's own
// controls and mouse look are off and the cursor is free, as with the mods panel;
// the game itself keeps running (online, the others are still playing).
//
//   Open / close    Esc
//   Choose          click, or Enter to quit
public class PauseMenu : MonoBehaviour
{
    const int Width = 320;

    static PauseMenu instance;
    GUIStyle box, title, button;

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
        if (keys == null) return;
        if (keys.escapeKey.wasPressedThisFrame)
        {
            // Esc closes the mods panel first, if it is open.
            var mods = FindFirstObjectByType<ModsPanel>();
            if (!Open && mods != null && mods.Open) mods.SetOpen(false);
            else SetOpen(!Open);
        }
        else if (Open && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame)) Quit();
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
        const float height = 190;
        GUILayout.BeginArea(new Rect((Screen.width - Width) / 2f, (Screen.height - height) / 2f, Width, height), box);
        GUILayout.Label("<b>Paused</b>", title);
        GUILayout.Space(8);
        if (GUILayout.Button("Resume   <color=#ffffff88>Esc</color>", button)) SetOpen(false);
        GUILayout.Space(6);
        if (GUILayout.Button("Quit   <color=#ffffff88>Enter</color>", button)) Quit();
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
