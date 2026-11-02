using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The spell list (docs/SPELL_RADIAL.md): Tab on the keyboard, in place of the
// wheel, which stays the controller's. A page of the Esc menu, like the grimoire,
// with the spells by element, then all of them, then the ones the wheel leaves out
// (SpellList). Click a spell to pick it, or use the arrows and Enter; Tab or Esc
// goes back to play. Picking goes through SpellCaster.Select, as the wheel does.
public class SpellListMenu : MonoBehaviour
{
    public const string PageId = "spells";
    const int Width = 980;
    const float TabsWidth = 220f, RowHeight = 66f;

    static SpellListMenu instance;
    PauseMenu.Page page;
    SpellCaster caster;
    Rules listedFor;
    IReadOnlyList<SpellTab> tabs = new List<SpellTab>();
    GUIStyle row, tabButton, hint;
    Vector2 scroll;
    int tab;
    int openedFrame = -1;

    /// <summary>The tabs showing, for the probe.</summary>
    public static IReadOnlyList<SpellTab> Tabs => instance != null ? instance.tabs : new List<SpellTab>();

    /// <summary>The tab showing, for the probe.</summary>
    public static int Tab => instance != null ? instance.tab : 0;

    public static bool Open => PauseMenu.Instance != null && PauseMenu.Instance.Open && PauseMenu.Instance.CurrentPage == PageId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Spell list");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SpellListMenu>();
    }

    /// <summary>Opens the menu on the spell list, on the tab and row of this wizard's spell.</summary>
    public static void Show(SpellCaster caster)
    {
        var menu = PauseMenu.Instance;
        if (menu == null || instance == null) return;
        instance.caster = caster;
        instance.openedFrame = Time.frameCount;
        instance.Fill();
        instance.tab = SpellList.TabOf(instance.tabs, caster.SelectedId);
        menu.SetOpen(true);
        menu.ShowPage(PageId);
        menu.Highlight = true;
        menu.FocusAt(instance.RowOf(caster.SelectedId));
    }

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Spells", Width = Width, Draw = DrawPage, Back = BackToPlay, ForKeys = true };
        PauseMenu.AddPage(PageId, page);
    }

    void OnDisable() => PauseMenu.RemovePage(PageId);
    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        var keys = Keyboard.current;
        if (keys == null || !Open || openedFrame == Time.frameCount) return; // the Tab that opened it isn't one that closes it
        var menu = PauseMenu.Instance;
        if (keys.tabKey.wasPressedThisFrame) menu.SetOpen(false);
        else if (keys.upArrowKey.wasPressedThisFrame) menu.Navigate(-1);
        else if (keys.downArrowKey.wasPressedThisFrame) menu.Navigate(1);
        else if (keys.leftArrowKey.wasPressedThisFrame) Turn(-1);
        else if (keys.rightArrowKey.wasPressedThisFrame) Turn(1);
        else if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame) menu.Submit();
    }

    // Back from the page goes to play: it was opened from there.
    bool BackToPlay()
    {
        PauseMenu.Instance.SetOpen(false);
        return true;
    }

    void Turn(int direction)
    {
        if (tabs.Count > 0) ShowTab((tab + direction + tabs.Count) % tabs.Count);
    }

    void ShowTab(int index)
    {
        tab = index;
        scroll = Vector2.zero;
        PauseMenu.Instance.FocusAt(0);
    }

    void Fill()
    {
        var rules = caster != null && caster.grid != null && caster.grid.World != null ? caster.grid.World.Rules : null;
        if (rules == null || rules == listedFor) return;
        listedFor = rules;
        tabs = SpellList.From(rules);
        tab = Mathf.Clamp(tab, 0, Mathf.Max(0, tabs.Count - 1));
    }

    int RowOf(string spellId)
    {
        if (tab >= tabs.Count) return 0;
        for (int i = 0; i < tabs[tab].SpellIds.Count; i++)
            if (tabs[tab].SpellIds[i] == spellId) return i;
        return 0;
    }

    void DrawPage()
    {
        MakeStyles();
        Fill();
        if (tabs.Count == 0)
        {
            GUILayout.Label("No wizard here yet.", row);
            return;
        }
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(TabsWidth));
        for (int i = 0; i < tabs.Count; i++)
        {
            string name = i == tab ? $"<b><color={tabs[i].Color}>{tabs[i].Name}</color></b>" : tabs[i].Name;
            if (GUILayout.Button(name, tabButton, GUILayout.Width(TabsWidth))) ShowTab(i);
        }
        GUILayout.EndVertical();
        GUILayout.Space(12);
        GUILayout.BeginVertical();
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(HudScale.Height - 280f, 240f, 640f), () =>
        {
            foreach (var id in tabs[Mathf.Min(tab, tabs.Count - 1)].SpellIds) Row(id);
        });
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Label("<color=#ffffff99>↑ ↓ spell  ·  ← → element  ·  Enter picks  ·  Tab closes</color>", hint);
    }

    void Row(string spellId)
    {
        var def = listedFor?.Spell(spellId);
        if (def == null) return;
        string name = spellId == caster.SelectedId ? $"<color=#ffd24a>{def.Name}</color>" : def.Name;
        string line = def.Blurb != null ? SpellRadialHud.FirstSentence(WithoutLevel(def.Blurb)) : "";
        string text = $"<b>{name}</b>   <color=#ffffff88>{SpellRadialHud.LevelOf(def)}</color>\n<size=15><color=#ffffffbb>{line}</color></size>";
        if (!PauseMenu.Button(text, row, GUILayout.Height(RowHeight))) return;
        caster.Select(spellId);
        PauseMenu.Instance.SetOpen(false);
    }

    // Blurbs can open with "Cantrip." or "Level 3.", which the row already says.
    static string WithoutLevel(string blurb)
    {
        int stop = blurb.IndexOf(". ", System.StringComparison.Ordinal);
        bool level = stop > 0 && (blurb.StartsWith("Cantrip", System.StringComparison.Ordinal) || blurb.StartsWith("Level", System.StringComparison.Ordinal)) && stop <= "Level 10".Length;
        return level ? blurb.Substring(stop + 2) : blurb;
    }

    void MakeStyles()
    {
        if (row != null) return;
        row = new GUIStyle(GUI.skin.button) { fontSize = 20, richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 4, 4) };
        tabButton = new GUIStyle(GUI.skin.button) { fontSize = 19, richText = true, fixedHeight = 44 };
        hint = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, alignment = TextAnchor.MiddleCenter };
    }
}
