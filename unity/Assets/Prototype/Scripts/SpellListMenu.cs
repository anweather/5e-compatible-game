using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The spell list (D16, docs/design/unified-casting.md): Tab on the keyboard opens it, and on a
// pad the Esc menu's Game > Spells (the d-pad's down is the hands menu, HandsMenu, and opens this
// only without the hands), a page of the Esc menu. With the hands its first tab is the hands:
// the two of them, where you attune each (a short moment, not mid-cast, and in a Hold run only
// in the keep's attuning room), then the hand table for what they hold, then the kit on the
// d-pad and the grimoire's discoveries. Then every spell by element, all of them, and the
// ones the radial leaves out (SpellList): picking one is what Cast casts in the debug layout
// (F2), through SpellCaster.Select as the radial does. Click, or the arrows (or W S) and
// Enter, as on every page of the menu (MenuControls); ← → (A D, or the d-pad's) change tab,
// and Tab or Esc goes back to play.
public class SpellListMenu : MonoBehaviour
{
    public const string PageId = "spells";
    /// <summary>The hands' tab's name.</summary>
    public const string HandsName = "Your hands";
    const int Width = 980;
    const float TabsWidth = 220f, RowHeight = 66f, HandLabel = 110f, ElementWidth = 118f, TableColumn = 150f;

    static SpellListMenu instance;
    PauseMenu.Page page;
    SpellCaster caster;
    Rules listedFor;
    bool listedHands;
    IReadOnlyList<SpellTab> tabs = new List<SpellTab>();
    GUIStyle row, tabButton, hint, cell, head;
    Vector2 scroll;
    int tab;
    int openedFrame = -1;
    bool openedInPlay;
    string said;
    const int SectionOrder = 40; // the Game sub menu, before the Grimoire

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

    /// <summary>
    /// Opens the menu on the spell list: with the hands on their tab, else on the tab and row of this wizard's spell.
    /// From the pad (<paramref name="byPad"/>) the highlight shows at the first press, so the d-pad that opened it doesn't move it.
    /// </summary>
    public static void Show(SpellCaster caster, bool byPad = false)
    {
        var menu = PauseMenu.Instance;
        if (menu == null || instance == null) return;
        instance.caster = caster;
        instance.openedFrame = Time.frameCount;
        instance.said = null;
        instance.openedInPlay = true;
        instance.Fill();
        bool hands = instance.HandsShown;
        instance.tab = hands ? 0 : SpellList.TabOf(instance.tabs, caster.SelectedId);
        menu.SetOpen(true);
        menu.ShowPage(PageId);
        menu.Highlight = !byPad;
        menu.FocusAt(hands ? 0 : instance.RowOf(caster.SelectedId));
    }

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Spells", Width = Width, Draw = DrawPage, Back = BackToPlay, ForKeys = true };
        PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Game);
        PauseMenu.AddPage(PageId, page);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(PageId);
    }

    // The Esc menu's way in, the pad's now the d-pad's down is the hands menu.
    void DrawEntry()
    {
        if (EventLog.TrackedCaster == null || !PauseMenu.Button("Spells", PauseMenu.ButtonStyle)) return;
        caster = EventLog.TrackedCaster;
        openedInPlay = false;
        said = null;
        Fill();
        tab = 0;
        PauseMenu.Instance.ShowPage(PageId);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    // Up, down and Enter are the menu's own (MenuControls); the tabs turn here.
    void Update()
    {
        if (!Open || openedFrame == Time.frameCount) return; // the Tab that opened it isn't one that closes it
        var keys = Keyboard.current;
        var pad = Gamepad.current;
        if (keys != null && keys.tabKey.wasPressedThisFrame) PauseMenu.Instance.SetOpen(false);
        else if ((keys != null && (keys.leftArrowKey.wasPressedThisFrame || keys.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame)) Turn(-1);
        else if ((keys != null && (keys.rightArrowKey.wasPressedThisFrame || keys.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame)) Turn(1);
    }

    // Back from the page goes to play if it was opened from there; else to the menu it came from.
    bool BackToPlay()
    {
        if (!openedInPlay) return false;
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

    World World => caster != null && caster.grid != null ? caster.grid.World : null;

    // The hands' tab, while the hands are what cast (not the debug layout).
    bool HandsShown => caster != null && caster.controls != null && caster.controls.HandPad && World != null && World.HandsOn(caster.CastPlayer);

    void Fill()
    {
        var rules = World?.Rules;
        if (rules == null || (rules == listedFor && HandsShown == listedHands)) return;
        listedFor = rules;
        listedHands = HandsShown;
        var all = new List<SpellTab>();
        if (listedHands) all.Add(new SpellTab(HandsName, "#ffd24a", new List<string>()));
        all.AddRange(SpellList.From(rules));
        tabs = all;
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
        var shown = tabs[Mathf.Min(tab, tabs.Count - 1)];
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(HudScale.Height - 280f, 240f, 640f), () => DrawTab(shown));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Label($"<color=#ffffff99>↑ ↓ choose  ·  ← → tab  ·  {PickHint(shown)}  ·  Tab closes</color>", hint);
    }

    void DrawTab(SpellTab shown)
    {
        if (shown.Name == HandsName && listedHands) DrawHands();
        else foreach (var id in shown.SpellIds) Row(id);
    }

    string PickHint(SpellTab shown)
    {
        if (!listedHands) return "Enter picks";
        return shown.Name == HandsName ? "Enter attunes" : "Enter picks for Cast in the F2 layout";
    }

    // ---------- the hands' tab ----------

    void DrawHands()
    {
        var world = World;
        string player = caster.CastPlayer;
        var hands = world.HandsOf(player);
        GUILayout.Label("<b>Your hands</b>", head);
        for (int hand = 0; hand < HandsDef.Count; hand++) HandRow(world, player, hands, hand);
        if (hands.AttuneLeft > 0) GUILayout.Label($"<color=#bfe3ff>Attuning your {(hands.AttuneHand == HandsDef.Left ? "left" : "right")} hand to {hands.AttuneTo ?? "nothing"}…</color>", cell);
        else if (said != null) GUILayout.Label($"<color=#ffb38a>{said}</color>", cell);
        else if (world.HoldRunning && world.TowerOf(player) == null) GUILayout.Label("<color=#ffffff99>Attune at a tower</color>", cell);
        GUILayout.Space(10);
        Table(world, player);
        GUILayout.Space(10);
        Kit(world, player);
        GUILayout.Space(10);
        Discoveries(world, player);
    }

    void HandRow(World world, string player, PlayerHands hands, int hand)
    {
        GUILayout.BeginHorizontal();
        string side = hand == HandsDef.Left ? "Left hand" : "Right hand";
        if (!world.HandOpen(player, hand))
        {
            GUILayout.Label($"<b>{side}</b>", cell, GUILayout.Width(HandLabel), GUILayout.Height(40));
            GUILayout.Label("<color=#ffffff88>locked</color>", cell, GUILayout.Height(40));
            GUILayout.EndHorizontal();
            return;
        }
        GUILayout.Label($"<b>{side}</b>", cell, GUILayout.Width(HandLabel), GUILayout.Height(40));
        var choices = world.Rules.Combos.Elements.Concat(new string[] { null });
        foreach (var element in choices)
        {
            string name = element ?? "nothing";
            string dot = element != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(HudDraw.ElementColor(element))}>●</color> " : "";
            if (!world.ElementOpen(player, element))
            {
                GUILayout.Label($"<color=#ffffff55>{name} (locked)</color>", cell, GUILayout.Width(ElementWidth), GUILayout.Height(40));
                continue;
            }
            bool holds = hands.Holds[hand] == element;
            string text = holds ? $"{dot}<b><color=#ffd24a>{name}</color></b>" : $"{dot}{name}";
            if (PauseMenu.Button(text, row, GUILayout.Width(ElementWidth), GUILayout.Height(40))) said = caster.AttuneTo(hand, element);
        }
        GUILayout.EndHorizontal();
    }

    // What the hands cast as they hold: each hand's own row and, where the table has one, the two together, gesture by gesture.
    void Table(World world, string player)
    {
        var table = world.Rules.Sim.Hold.Hands;
        string left = world.HandHolds(player, HandsDef.Left), right = world.HandHolds(player, HandsDef.Right);
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        GUILayout.Label("<b>What they cast</b>", head);
        string[] keys = pad
            ? new[] { PadGlyphs.Text("{LT} / {RT}"), PadGlyphs.Text("{LB} / {RB}"), "trigger + bumper" }
            : new[] { "left / right click", "Q / E", "click + Q / E" };
        GUILayout.BeginHorizontal();
        GUILayout.Label("", cell, GUILayout.Width(HandLabel));
        for (int g = 0; g < 3; g++) GUILayout.Label($"<b>{Capital(HandsDef.GestureName(g))}</b>\n<size=13><color=#ffffff88>{keys[g]}</color></size>", cell, GUILayout.Width(TableColumn));
        GUILayout.EndHorizontal();
        TableRow(world, "Left", g => table.One(left, g));
        TableRow(world, "Right", g => table.One(right, g));
        TableRow(world, "Both", g => table.Both(left, right, g));
    }

    void TableRow(World world, string what, System.Func<int, HandRowDef> rowOf)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"<b>{what}</b>", cell, GUILayout.Width(HandLabel));
        for (int g = 0; g < 3; g++)
        {
            var r = rowOf(g);
            string name = r?.Spell != null ? world.Rules.Spell(r.Spell)?.Name ?? r.Spell : r?.Act;
            GUILayout.Label(name ?? "<color=#ffffff55>each on its own</color>", cell, GUILayout.Width(TableColumn));
        }
        GUILayout.EndHorizontal();
    }

    void Kit(World world, string player)
    {
        var kit = world.DpadOf(player);
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        string keys = pad ? "d-pad ◀ ▶ pick, ▲ casts" : "[ ] pick, T casts";
        GUILayout.Label($"<b>The kit</b>   <color=#ffffff99>{keys}</color>", head);
        var selected = world.SelectedDpad(player);
        for (int i = 0; i < kit.Count; i++)
        {
            string name = kit[i] == selected ? $"<b><color=#ffd24a>{kit[i].Name}</color></b>" : kit[i].Name;
            if (PauseMenu.Button(name, row, GUILayout.Height(40))) caster.SelectKit(i);
        }
    }

    void Discoveries(World world, string player)
    {
        var all = world.Rules.Combos.All;
        var found = all.Where(c => world.KnowsCombo(player, c.Id)).ToList();
        GUILayout.Label($"<b>Discoveries</b>   <color=#ffffff99>{found.Count} of {all.Count} found</color>", head);
        if (found.Count == 0) GUILayout.Label("<color=#ffffff99>Nothing yet.</color>", cell);
        foreach (var combo in found)
        {
            var elements = combo.Id.Split('+').Select(e => $"<color=#{ColorUtility.ToHtmlStringRGB(HudDraw.ElementColor(e))}>● {e}</color>");
            GUILayout.Label($"{string.Join("  ", elements)}   <b>{combo.Name}</b>", cell);
        }
    }

    static string Capital(string word) => string.IsNullOrEmpty(word) ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);

    // ---------- every spell ----------

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
        cell = new GUIStyle(GUI.skin.label) { fontSize = 17, richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft };
        head = new GUIStyle(cell) { fontSize = 19 };
    }
}
