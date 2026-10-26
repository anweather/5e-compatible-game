using System.Text;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The in-game mods panel: every rules/mods/*.json with its title and idea, a
// tick by the ones in force, the rules' fingerprint, and why the last switch
// was refused. Switching goes through RuleSwitcher (solo: next tick; online:
// the host stamps it for everyone).
//
//   Open / close    M                Start / Options
//   Switch a mod    1-9 or click     d-pad up / down, then X / Square
//
// While it is open the wizard's own controls (spells, items, reset) are off,
// so the number keys don't also pick spells, and the mouse is free to click.
[RequireComponent(typeof(RuleSwitcher))]
public class ModsPanel : MonoBehaviour
{
    const int Width = 560;

    RuleSwitcher switcher;
    int cursor;
    GUIStyle box, row, rowOn, small, error;
    Vector2 scroll;

    // What opening the panel changed on the wizard, put back on close.
    PlayerControls pausedControls;
    Component lookInput;
    CursorLockMode cursorWas;
    bool cursorVisibleWas;

    public bool Open { get; private set; }

    void Awake() => switcher = GetComponent<RuleSwitcher>();

    void Update()
    {
        var pad = Gamepad.current;
        var keys = Keyboard.current;
        if ((keys != null && keys.mKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame)) SetOpen(!Open);
        if (!Open || switcher.Catalog.Mods.Count == 0) return;
        if (keys != null) NumberKeys(keys);
        if (pad != null) PadKeys(pad);
    }

    void NumberKeys(Keyboard keys)
    {
        int count = Mathf.Min(9, switcher.Catalog.Mods.Count);
        for (int i = 0; i < count; i++)
            if (keys[Key.Digit1 + i].wasPressedThisFrame) Toggle(i);
    }

    void PadKeys(Gamepad pad)
    {
        int count = switcher.Catalog.Mods.Count;
        if (pad.dpad.up.wasPressedThisFrame) cursor = (cursor + count - 1) % count;
        if (pad.dpad.down.wasPressedThisFrame) cursor = (cursor + 1) % count;
        if (pad.buttonWest.wasPressedThisFrame) Toggle(cursor);
    }

    void Toggle(int index)
    {
        var mods = switcher.Catalog.Mods;
        if (index < 0 || index >= mods.Count) return;
        cursor = index;
        switcher.Toggle(mods[index].Id);
    }

    /// <summary>Opens or closes the panel (also for scripted screenshots).</summary>
    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        if (open) PauseWizard();
        else ResumeWizard();
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

    void OnDisable() => SetOpen(false);

    // --- drawing -----------------------------------------------------------------

    void OnGUI()
    {
        if (!Open || !EventLog.HudVisible) return;
        MakeStyles();
        var mods = switcher.Catalog.Mods;
        // Down to just above the event log (EventLog: 8 lines of 26 from 32 above the bottom).
        float height = Mathf.Min(Screen.height - 76 - 252, 120 + mods.Count * 50);
        GUILayout.BeginArea(new Rect(16, 76, Width, height), box);
        GUILayout.Label("<b>Mods</b>   <color=#ffffff99>M or Start closes · 1–9, click, or d-pad + X switches</color>", small);
        GUILayout.Label($"Rules fingerprint <b>{switcher.Rules.Fingerprint}</b>   ·   {Summary()}", small);
        scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        for (int i = 0; i < mods.Count; i++) DrawRow(i, mods[i]);
        if (mods.Count == 0) GUILayout.Label("No mods in rules/mods/ yet. Save one there (docs/MODDING.md) and it shows up here.", small);
        GUILayout.EndScrollView();
        if (switcher.LastError != null) GUILayout.Label(switcher.LastError, error);
        GUILayout.EndArea();
    }

    void DrawRow(int i, ModFile mod)
    {
        bool on = switcher.Rules.IsOn(mod.Id);
        string mark = on ? "<color=#7CFC7C>✔</color>" : "<color=#ffffff55>○</color>";
        string number = i < 9 ? $"{i + 1}" : " ";
        string idea = mod.Error != null ? $"<color=#ff8a80>{mod.Error}</color>" : mod.Idea;
        string focus = i == cursor && Gamepad.current != null ? "▶ " : "";
        string label = $"{focus}{mark}  <b>{number}  {mod.Title}</b>\n<size=13><color=#ffffffbb>{Shorten(idea, 90)}</color></size>";
        if (GUILayout.Button(label, on ? rowOn : row, GUILayout.Width(Width - 48))) Toggle(i);
    }

    string Summary()
    {
        var rules = switcher.Rules;
        if (rules.Mods.Count == 0) return "base rules";
        var sb = new StringBuilder("on: ");
        for (int i = 0; i < rules.Mods.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(switcher.Catalog.Find(rules.Mods[i].Id)?.Title ?? rules.Mods[i].Id);
        }
        if (!switcher.OwnsRules) sb.Append("   (the host switches for everyone)");
        return sb.ToString();
    }

    static string Shorten(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    void MakeStyles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10), alignment = TextAnchor.UpperLeft };
        box.normal.background = Solid(new Color(0.06f, 0.07f, 0.09f, 0.86f));
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
        row = new GUIStyle(GUI.skin.button) { fontSize = 17, richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(10, 10, 4, 4) };
        rowOn = new GUIStyle(row);
        rowOn.normal.background = Solid(new Color(0.16f, 0.34f, 0.2f, 0.95f));
        rowOn.hover.background = Solid(new Color(0.2f, 0.42f, 0.25f, 0.95f));
        error = new GUIStyle(small) { fontSize = 15 };
        error.normal.textColor = new Color(1f, 0.55f, 0.5f);
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
