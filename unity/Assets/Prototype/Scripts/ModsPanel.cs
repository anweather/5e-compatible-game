using System.Text;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The Mods page of the Esc menu (PauseMenu): every rules/mods/*.json with its
// title and idea, a tick by the ones in force, the rules' fingerprint, and why
// the last switch was refused. Switching goes through RuleSwitcher (solo: next
// tick; online: the host stamps it for everyone). The main page gets a
// "Mods" button that opens it.
//
//   Open            M (opens the menu on this page; M again closes it)
//                   Esc / Start, then Mods
//   Switch a mod    1-9 or click     highlight it, then A / Cross
//
// While the menu is open the wizard's own controls (spells, items, reset) are
// off, so the number keys don't also pick spells.
[RequireComponent(typeof(RuleSwitcher))]
public class ModsPanel : MonoBehaviour
{
    /// <summary>The page's id in the Esc menu.</summary>
    public const string PageId = "mods";
    const int Width = 600;
    const int SectionOrder = 500; // after Play together, before the version and credits

    RuleSwitcher switcher;
    GUIStyle row, rowOn, small, error;
    Vector2 scroll;
    PauseMenu.Page page;

    /// <summary>True while the Esc menu shows the Mods page.</summary>
    public static bool Open => PauseMenu.Instance != null && PauseMenu.Instance.Open && PauseMenu.Instance.CurrentPage == PageId;

    void Awake()
    {
        switcher = GetComponent<RuleSwitcher>();
        page = new PauseMenu.Page { Title = "Mods", Width = Width, Draw = DrawPage };
    }

    void OnEnable()
    {
        PauseMenu.AddPage(PageId, page);
        PauseMenu.AddSection(DrawEntry, SectionOrder);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(PageId);
    }

    void Update()
    {
        var keys = Keyboard.current;
        var controls = PauseMenu.Controls;
        bool typing = PauseMenu.Instance != null && PauseMenu.Instance.Open && GUIUtility.keyboardControl != 0; // the join code field has the keys
        if (controls != null && controls.Mods.WasPressedThisFrame() && !typing) SetOpen(!Open);
        if (Open && keys != null && !typing && switcher.Catalog.Mods.Count > 0) NumberKeys(keys);
    }

    void NumberKeys(Keyboard keys)
    {
        int count = Mathf.Min(9, switcher.Catalog.Mods.Count);
        for (int i = 0; i < count; i++)
            if (keys[Key.Digit1 + i].wasPressedThisFrame) Toggle(i);
    }

    /// <summary>Switches the mod at this place in the list on or off.</summary>
    public void Toggle(int index)
    {
        var mods = switcher.Catalog.Mods;
        if (index < 0 || index >= mods.Count) return;
        switcher.Toggle(mods[index].Id);
    }

    /// <summary>Opens the Esc menu on the Mods page, or closes the menu (also for scripted screenshots).</summary>
    public static void SetOpen(bool open)
    {
        var menu = PauseMenu.Instance;
        if (menu == null) return;
        if (open) menu.ShowPage(PageId);
        else if (Open) menu.SetOpen(false);
    }

    // --- drawing -----------------------------------------------------------------

    // On the main page: the button that opens this page, with what's on.
    void DrawEntry()
    {
        MakeStyles();
        if (PauseMenu.Button($"Mods   <color=#ffffff88>M</color>", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(PageId);
        GUILayout.Label($"<color=#ffffff99>{Summary()}</color>", small);
    }

    void DrawPage()
    {
        MakeStyles();
        var mods = switcher.Catalog.Mods;
        GUILayout.Label("<color=#ffffff99>Click, 1–9, or highlight and A to switch one on or off. M closes.</color>", small);
        GUILayout.Label($"Rules fingerprint <b>{switcher.Rules.Fingerprint}</b>   ·   {Summary()}", small);
        float height = Mathf.Clamp(mods.Count * 58f + 8f, 60f, Mathf.Max(120f, HudScale.Height - 340f));
        scroll = PauseMenu.ScrollView(scroll, height, () =>
        {
            for (int i = 0; i < mods.Count; i++) DrawRow(i, mods[i]);
            if (mods.Count == 0) GUILayout.Label("No mods in rules/mods/ yet. Save one there (docs/MODDING.md) and it shows up here.", small);
        });
        if (switcher.LastError != null) GUILayout.Label(switcher.LastError, error);
    }

    void DrawRow(int i, ModFile mod)
    {
        bool on = switcher.Rules.IsOn(mod.Id);
        string mark = on ? "<color=#7CFC7C>✔</color>" : "<color=#ffffff55>○</color>";
        string number = i < 9 ? $"{i + 1}" : " ";
        string idea = mod.Error != null ? $"<color=#ff8a80>{mod.Error}</color>" : mod.Idea;
        string label = $"{mark}  <b>{number}  {mod.Title}</b>\n<size=13><color=#ffffffbb>{Shorten(idea, 90)}</color></size>";
        if (PauseMenu.Button(label, on ? rowOn : row, GUILayout.Width(Width - 56), GUILayout.Height(52))) Toggle(i);
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
        if (row != null) return;
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
        row = new GUIStyle(GUI.skin.button) { fontSize = 17, richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(10, 10, 4, 4) };
        rowOn = new GUIStyle(row);
        rowOn.normal.background = PauseMenu.Solid(new Color(0.16f, 0.34f, 0.2f, 0.95f));
        rowOn.hover.background = PauseMenu.Solid(new Color(0.2f, 0.42f, 0.25f, 0.95f));
        error = new GUIStyle(small) { fontSize = 15 };
        error.normal.textColor = new Color(1f, 0.55f, 0.5f);
    }
}
