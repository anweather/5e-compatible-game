using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The Esc menu: Resume, the sub menus (Game, Multiplayer, Settings, Claude, Developer), and
// Quit. Other code adds its sections to a sub menu (NetSession adds Play together
// to Multiplayer, ModsPanel the Mods entry to Game), or to the main page if the
// player must see it at once (GameInfoMenu's update notice and version). Sections
// can also add sub-pages (Mods, Credits, Join by code) that open over the page
// they're opened from and go back to it. It starts itself in
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
    static readonly List<(Action draw, int order, Submenu submenu)> sections = new();
    static readonly Dictionary<string, Page> pages = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        sections.Clear();
        pages.Clear();
        KeysTaken = false;
        fieldFrame = -2;
    }

    GUIStyle box, title, button;
    Texture2D gold, goldTint;

    readonly MenuNavigator nav = new();
    MenuControls controls;
    bool activating;
    float padScroll;
    Rect focusRect;
    bool focusDrawn;

    public enum Submenu { Main, Game, Multiplayer, Settings, Claude, Developer }

    static readonly Submenu[] Submenus = { Submenu.Game, Submenu.Multiplayer, Submenu.Settings, Submenu.Claude, Submenu.Developer };

    public sealed class Page
    {
        public string Title;
        public int Width = MainWidth;
        public Action Draw;
        public Func<int, bool> Move;
        public Func<bool> Submit;
        public Func<bool> Back;
        public bool ForKeys;
    }

    public static void AddSection(Action draw, int order, Submenu submenu)
    {
        if (sections.Exists(s => s.draw == draw)) return;
        int at = sections.FindIndex(s => s.order > order);
        sections.Insert(at < 0 ? sections.Count : at, (draw, order, submenu));
    }

    public static void RemoveSection(Action draw) => sections.RemoveAll(s => s.draw == draw);

    public static void AddPage(string id, Page page) => pages[id] = page;

    public static void RemovePage(string id)
    {
        pages.Remove(id);
        if (instance != null) instance.nav.Remove(id);
    }

    public static string PageOf(Submenu submenu) => $"menu-{submenu}";

    public const int LastOrder = 1000;

    public static GUIStyle ButtonStyle => instance != null ? instance.button : GUI.skin.button;

    public static PauseMenu Instance => instance;

    public static MenuControls Controls => instance != null ? instance.controls : null;

    PlayerControls pausedControls;
    Behaviour pausedPlayerInput;
    Component lookInput;
    CursorLockMode cursorWas;
    bool cursorVisibleWas;

    public bool Open { get; private set; }

    public string CurrentPage => nav.Page;

    public int Focus => nav.Focus;

    public int ItemCount => nav.ItemCount;

    public bool Highlight { get; set; }

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

    public static bool KeysTaken { get; set; }

    public static void FieldHasKeys() => fieldFrame = Time.frameCount;

    static int fieldFrame = -2;

    void Awake()
    {
        controls = new MenuControls();
        foreach (var submenu in Submenus)
            AddPage(PageOf(submenu), new Page { Title = submenu.ToString(), Draw = () => DrawSections(submenu) });
    }

    void Update()
    {
        if (Open && Time.frameCount - fieldFrame <= 1)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) GUIUtility.keyboardControl = 0;
            return;
        }
        if (controls.Menu.WasPressedThisFrame() && !KeysTaken)
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

    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        nav.Reset();
        GUIUtility.keyboardControl = 0;
        if (open) PauseWizard();
        else ResumeWizard();
    }

    public void ShowPage(string id)
    {
        if (!pages.ContainsKey(id)) return;
        if (Open && nav.Page == id) return;
        SetOpen(true);
        if (!OnSubmenu) CloseSubPages();
        nav.Push(id);
    }

    public void Navigate(int delta)
    {
        if (!Open) return;
        if (!Highlight) { Highlight = true; return; }
        var page = CurrentPageInfo();
        if (page?.Move != null && page.Move(delta)) return;
        nav.Move(delta);
    }

    public void FocusAt(int item) => nav.FocusAt(item);

    public void Submit()
    {
        if (!Open) return;
        if (!Highlight) { Highlight = true; return; }
        var page = CurrentPageInfo();
        if (page?.Submit != null && page.Submit()) return;
        nav.Activate();
    }

    public void Back()
    {
        if (!Open) return;
        Highlight = true;
        var page = CurrentPageInfo();
        if (page?.Back != null && page.Back()) return;
        if (!nav.Back()) SetOpen(false);
    }

    public void CloseSubPages()
    {
        bool more = true;
        while (more) more = nav.Back();
    }

    bool OnSubmenu => Array.Exists(Submenus, s => PageOf(s) == nav.Page);

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
        var caster = EventLog.TrackedWizard;
        var wizardControls = EventLog.TrackedControls;
        if (caster != null && wizardControls != null && wizardControls.enabled)
        {
            pausedControls = wizardControls;
            pausedControls.enabled = false;
        }
        lookInput = caster != null ? caster.GetComponent(WizardRig.InputsComponent) : null;
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

    public static Vector2 ScrollView(Vector2 scroll, float height, Action content, bool across = true)
    {
        var view = across ? GUILayout.BeginScrollView(scroll, GUILayout.Height(height))
            : GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(height));
        bool repaint = Event.current.type == EventType.Repaint;
        if (instance != null) instance.focusDrawn = false;
        content();
        GUILayout.EndScrollView();
        if (instance == null || !repaint) return view;
        if (instance.focusDrawn)
        {
            var r = instance.focusRect;
            if (r.yMin < view.y) view.y = r.yMin - 6f;
            else if (r.yMax > view.y + height) view.y = r.yMax - height + 6f;
        }
        view.y = Mathf.Max(0f, view.y - instance.padScroll * 900f * Time.unscaledDeltaTime);
        return view;
    }

    int activations, activationsAtLayout;

    void OnGUI()
    {
        HudScale.Apply();
        if (!Open) return;
        GUI.depth = -100;
        if (Event.current.type == EventType.Layout) activationsAtLayout = activations;
        else if (activations != activationsAtLayout) return;
        MakeStyles();
        nav.BeginPass();
        DrawMenu();
        if (Event.current.type == EventType.Layout) nav.EndLayout();
    }

    internal void RunActivation()
    {
        if (!Open || !nav.ActivationPending) return;
        MakeStyles();
        activations++;
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
        GUILayout.BeginArea(new Rect(0, 0, HudScale.Width, HudScale.Height));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(box, GUILayout.Width(Mathf.Min(page?.Width ?? MainWidth, HudScale.Width - 32)));
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
        string key = Highlight ? PadGlyphs.Label(PadButton.Start) : "Esc";
        if (Button($"Resume   <color=#ffffff88>{key}</color>", button)) SetOpen(false);
        GUILayout.Space(12);
        foreach (var submenu in Submenus)
            if (sections.Exists(s => s.submenu == submenu) && Button(submenu.ToString(), button)) ShowPage(PageOf(submenu));
        DrawSections(Submenu.Main);
        GUILayout.Space(12);
        if (Button("Quit", button)) Quit();
    }

    static void DrawSections(Submenu submenu)
    {
        foreach (var section in sections.ToArray())
        {
            if (section.submenu != submenu) continue;
            GUILayout.Space(12);
            section.draw();
        }
    }

    void DrawPage(Page page)
    {
        GUILayout.Label($"<b>{page.Title}</b>", title);
        GUILayout.Space(8);
        page.Draw();
        GUILayout.Space(12);
        string back = Highlight && !page.ForKeys ? $"Back   <color=#ffffff88>{PadGlyphs.Label(PadButton.East)}</color>" : "Back";
        if (Button(back, button) && (page.Back == null || !page.Back())) nav.Back();
    }

    void MakeStyles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12), alignment = TextAnchor.UpperCenter };
        box.normal.background = Solid(new Color(0.06f, 0.07f, 0.09f, 0.9f));
        title = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        button = new GUIStyle(GUI.skin.button) { fontSize = 19, richText = true, fixedHeight = 44 };
        gold = Solid(new Color(1f, 0.82f, 0.29f, 1f));
        goldTint = Solid(new Color(1f, 0.82f, 0.29f, 0.18f));
    }

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
