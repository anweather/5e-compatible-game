using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The Esc menu: Resume, any sections other code adds (NetSession adds Play
// together, ModsPanel the Mods entry, GameInfoMenu the version, credits and
// log folder), and Quit. Sections can also add sub-pages (Mods, Credits, Join
// by code) that open over the main page and go back to it. It starts itself in
// every scene, like FpsProbe, so no scene or builder has to add it. While it
// is open the wizard's own controls, movement and look are off and the cursor
// is free; the game itself keeps running (online, the others are still
// playing).
//
//   Open / close    Esc               Start / Options
//   Mods page       M
//   Move            (mouse)           d-pad or left stick up / down
//   Choose          click             A / Cross
//   Back            (Back button)     B / Circle (closes the menu on the main page)
//
// Controller focus: every button drawn through PauseMenu.Button is counted in
// drawing order (MenuNavigator) and the highlighted one gets a gold frame. A
// press of A can't click an IMGUI button, so MenuActivator runs the page's
// items once more in its own OnGUI pass, where Button returns true for the
// highlighted one. Doing that in a separate component's pass, not between this
// one's layout and repaint passes, means whatever the button changes (a page,
// a status line) never leaves this menu's layout out of step with its drawing.
public class PauseMenu : MonoBehaviour
{
    const int MainWidth = 360;

    static PauseMenu instance;
    static readonly List<(Action draw, int order)> sections = new();
    static readonly Dictionary<string, Page> pages = new();

    GUIStyle box, title, button, hint;
    Texture2D gold, goldTint;

    readonly MenuNavigator nav = new();
    MenuControls controls;
    bool activating;    // MenuActivator's pass: the highlighted Button returns true
    float padScroll;    // right stick, applied to scroll views once per frame
    Rect focusRect;     // where the highlighted button was drawn, for scroll views
    bool focusDrawn;

    /// <summary>A sub-page of the menu: its title, width and contents, and optionally its own controller handling.</summary>
    public sealed class Page
    {
        public string Title;
        public int Width = MainWidth;
        public Action Draw;
        /// <summary>Up (-1) or down (+1) on the controller; return true if handled (the highlight doesn't move).</summary>
        public Func<int, bool> Move;
        /// <summary>A on the controller; return true if handled (no item is activated).</summary>
        public Func<bool> Submit;
        /// <summary>B on the controller; return true if handled (the page stays open).</summary>
        public Func<bool> Back;
    }

    /// <summary>
    /// Adds a section drawn between Resume and Quit (IMGUI layout calls, inside the menu's
    /// width), so other parts of the game can put their controls in the menu without the
    /// menu knowing about them. Remove it again when its owner goes away. Buttons drawn with
    /// <see cref="Button"/> can be reached with a controller.
    /// </summary>
    /// <param name="order">Lower draws higher up; equal orders keep the order they were added in.</param>
    public static void AddSection(Action draw, int order = 0)
    {
        if (sections.Exists(s => s.draw == draw)) return;
        int at = sections.FindIndex(s => s.order > order);
        sections.Insert(at < 0 ? sections.Count : at, (draw, order));
    }

    public static void RemoveSection(Action draw) => sections.RemoveAll(s => s.draw == draw);

    /// <summary>Adds a sub-page, opened with <see cref="ShowPage"/>. Remove it again when its owner goes away.</summary>
    public static void AddPage(string id, Page page) => pages[id] = page;

    public static void RemovePage(string id)
    {
        pages.Remove(id);
        if (instance != null) instance.nav.Remove(id);
    }

    /// <summary>The order for sections that belong at the bottom, just above Quit (the version and credits).</summary>
    public const int LastOrder = 1000;

    /// <summary>The menu's button style, for sections that want matching buttons.</summary>
    public static GUIStyle ButtonStyle => instance != null ? instance.button : GUI.skin.button;

    /// <summary>The Esc menu, once it has started itself.</summary>
    public static PauseMenu Instance => instance;

    /// <summary>The menu's own controls (Esc / Start, M, and the controller navigation).</summary>
    public static MenuControls Controls => instance != null ? instance.controls : null;

    // What opening the menu changed on the wizard, put back on close.
    PlayerControls pausedControls;
    Behaviour pausedPlayerInput;
    Component lookInput;
    CursorLockMode cursorWas;
    bool cursorVisibleWas;

    public bool Open { get; private set; }

    /// <summary>The page showing: MenuNavigator.MainPage, or a sub-page's id.</summary>
    public string CurrentPage => nav.Page;

    /// <summary>The highlighted item on the current page, counted from 0 in drawing order.</summary>
    public int Focus => nav.Focus;

    /// <summary>How many controller-reachable items the current page has.</summary>
    public int ItemCount => nav.ItemCount;

    /// <summary>True while the controller highlight shows (the menu was last used with a controller).</summary>
    public bool Highlight { get; set; }

    /// <summary>The text of the highlighted button as last drawn (for probes and tests), or null.</summary>
    public string FocusedLabel { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Pause menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PauseMenu>();
        go.AddComponent<MenuActivator>();
    }

    void Awake() => controls = new MenuControls();

    void Update()
    {
        if (controls.Menu.WasPressedThisFrame())
        {
            bool pad = MenuControls.PressedOnPad(controls.Menu);
            SetOpen(!Open);
            Highlight = pad;
        }
        if (!Open) return;
        padScroll = controls.Scroll.ReadValue<float>();
        if (controls.Back.WasPressedThisFrame()) Back();
        int steps = controls.Steps();
        if (steps != 0) Navigate(steps);
        if (controls.Submit.WasPressedThisFrame()) Submit();
        var mouse = Mouse.current;
        if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 25f) Highlight = false;
    }

    /// <summary>Opens or closes the menu. It opens on the main page.</summary>
    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        nav.Reset();
        GUIUtility.keyboardControl = 0; // no text field keeps the keys (M, 1-9) after the menu
        if (open) PauseWizard();
        else ResumeWizard();
    }

    /// <summary>Opens the menu on a sub-page (B goes back to the main page). Nothing happens for an unknown page.</summary>
    public void ShowPage(string id)
    {
        if (!pages.ContainsKey(id)) return;
        if (Open && nav.Page == id) return;
        SetOpen(true);
        CloseSubPages(); // from the main page, so B goes back to the button that opened it
        nav.Push(id);
    }

    /// <summary>The controller's up (-1) or down (+1): moves the highlight, or the page handles it.</summary>
    public void Navigate(int delta)
    {
        if (!Open) return;
        if (!Highlight) { Highlight = true; return; } // the first press only shows where the highlight is
        var page = CurrentPageInfo();
        if (page?.Move != null && page.Move(delta)) return;
        nav.Move(delta);
    }

    /// <summary>The controller's A: activates the highlighted item, as a click would.</summary>
    public void Submit()
    {
        if (!Open) return;
        if (!Highlight) { Highlight = true; return; }
        var page = CurrentPageInfo();
        if (page?.Submit != null && page.Submit()) return;
        nav.Activate();
    }

    /// <summary>The controller's B: back from a sub-page, or closes the menu on the main page.</summary>
    public void Back()
    {
        if (!Open) return;
        Highlight = true;
        var page = CurrentPageInfo();
        if (page?.Back != null && page.Back()) return;
        if (!nav.Back()) SetOpen(false);
    }

    /// <summary>Back to the main page (for a sub-page that is done, such as Join by code after joining).</summary>
    public void CloseSubPages()
    {
        bool more = true;
        while (more) more = nav.Back();
    }

    Page CurrentPageInfo() => nav.AtTop ? null : pages.GetValueOrDefault(nav.Page);

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
        // Movement and jump too, so the stick and A can drive the menu without walking or jumping.
        if (caster != null && caster.TryGetComponent<PlayerInput>(out var input) && input.enabled)
        {
            pausedPlayerInput = input;
            input.enabled = false;
        }
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
        if (pausedPlayerInput != null) pausedPlayerInput.enabled = true;
        pausedPlayerInput = null;
        SetLook(true);
        lookInput = null;
        Cursor.lockState = cursorWas;
        Cursor.visible = cursorVisibleWas;
    }

    // Stops (or restarts) the mouse turning the camera; the last move and look inputs are cleared so they don't carry on.
    void SetLook(bool on)
    {
        if (lookInput == null) return;
        WizardRig.Field(lookInput, "cursorInputForLook", isPublic: true)?.SetValue(lookInput, on);
        foreach (var name in new[] { "look", "move" })
        {
            var field = WizardRig.Field(lookInput, name, isPublic: true);
            if (field != null && field.FieldType == typeof(Vector2)) field.SetValue(lookInput, Vector2.zero);
        }
        foreach (var name in new[] { "jump", "sprint" })
        {
            var field = WizardRig.Field(lookInput, name, isPublic: true);
            if (field != null && field.FieldType == typeof(bool)) field.SetValue(lookInput, false);
        }
    }

    // --- items -------------------------------------------------------------------

    /// <summary>
    /// A menu button the controller can reach: GUILayout.Button, counted for the highlight,
    /// framed in gold while highlighted, and true when A chooses it as well as when clicked.
    /// </summary>
    public static bool Button(string text, GUIStyle style = null, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.button;
        if (instance == null) return GUILayout.Button(text, style, options);
        return instance.Item(GUILayout.Button(text, style, options), text);
    }

    bool Item(bool clicked, string text)
    {
        int index = nav.NextItem();
        var e = Event.current.type;
        if (activating)
            return nav.TakeActivation(index) && GUI.enabled;
        // A page that handles A itself (Join by code) shows its own highlight instead.
        if (e == EventType.Repaint && Highlight && index == nav.Focus && CurrentPageInfo()?.Submit == null)
        {
            focusRect = GUILayoutUtility.GetLastRect();
            focusDrawn = true;
            FocusedLabel = text;
            DrawFocus(focusRect);
        }
        return clicked;
    }

    void DrawFocus(Rect r)
    {
        GUI.DrawTexture(r, goldTint);
        const float t = 3f;
        GUI.DrawTexture(new Rect(r.x - t, r.y - t, r.width + 2 * t, t), gold);
        GUI.DrawTexture(new Rect(r.x - t, r.yMax, r.width + 2 * t, t), gold);
        GUI.DrawTexture(new Rect(r.x - t, r.y, t, r.height), gold);
        GUI.DrawTexture(new Rect(r.xMax, r.y, t, r.height), gold);
    }

    /// <summary>
    /// A scroll view that keeps the controller highlight in sight and scrolls with the right
    /// stick. Draw its contents in <paramref name="content"/>; returns the new scroll position.
    /// </summary>
    public static Vector2 ScrollView(Vector2 scroll, float height, Action content)
    {
        var view = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
        bool repaint = Event.current.type == EventType.Repaint;
        if (instance != null) instance.focusDrawn = false;
        content();
        GUILayout.EndScrollView();
        if (instance == null || !repaint) return view;
        if (instance.focusDrawn)
        {
            var r = instance.focusRect; // in the scroll view's own coordinates
            if (r.yMin < view.y) view.y = r.yMin - 6f;
            else if (r.yMax > view.y + height) view.y = r.yMax - height + 6f;
        }
        view.y = Mathf.Max(0f, view.y - instance.padScroll * 900f * Time.unscaledDeltaTime);
        return view;
    }

    // --- drawing -----------------------------------------------------------------

    void OnGUI()
    {
        if (!Open) return;
        GUI.depth = -100; // over the rest of the HUD (lower draws on top)
        MakeStyles();
        nav.BeginPass();
        DrawMenu();
        if (Event.current.type == EventType.Layout) nav.EndLayout();
    }

    /// <summary>MenuActivator's pass: runs the page's items so the one A chose acts as clicked.</summary>
    internal void RunActivation()
    {
        if (!Open || !nav.ActivationPending) return;
        MakeStyles();
        activating = true;
        nav.BeginPass();
        try { DrawMenu(); }
        finally
        {
            activating = false;
            nav.ClearActivation();
        }
    }

    void DrawMenu()
    {
        var page = CurrentPageInfo();
        // Centred, as tall as its contents.
        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(box, GUILayout.Width(Mathf.Min(page?.Width ?? MainWidth, Screen.width - 32)));
        if (page != null) DrawPage(page);
        else DrawMain();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    void DrawMain()
    {
        GUILayout.Label("<b>Paused</b>", title);
        GUILayout.Space(8);
        string key = Highlight ? "Start" : "Esc";
        if (Button($"Resume   <color=#ffffff88>{key}</color>", button)) SetOpen(false);
        foreach (var section in sections.ToArray())
        {
            GUILayout.Space(12);
            section.draw();
        }
        GUILayout.Space(12);
        if (Button("Quit", button)) Quit();
        if (Highlight) GUILayout.Label("<color=#ffffff99>A choose  ·  B close</color>", hint);
    }

    void DrawPage(Page page)
    {
        GUILayout.Label($"<b>{page.Title}</b>", title);
        GUILayout.Space(8);
        page.Draw();
        GUILayout.Space(12);
        if (Button("Back   <color=#ffffff88>B / ○</color>", button)) nav.Back();
    }

    void MakeStyles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12), alignment = TextAnchor.UpperCenter };
        box.normal.background = Solid(new Color(0.06f, 0.07f, 0.09f, 0.9f));
        title = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        button = new GUIStyle(GUI.skin.button) { fontSize = 19, richText = true, fixedHeight = 44 };
        hint = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.MiddleCenter };
        gold = Solid(new Color(1f, 0.82f, 0.29f, 1f));
        goldTint = Solid(new Color(1f, 0.82f, 0.29f, 0.18f));
    }

    /// <summary>A one-pixel texture of one colour, for IMGUI backgrounds.</summary>
    public static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnDestroy()
    {
        controls?.Dispose();
        if (instance == this) instance = null;
    }
}
