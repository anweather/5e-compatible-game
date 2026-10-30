using System.Linq;
using Chem;
using UnityEngine;

// The grimoire (docs/SPELL_COMBOS.md): a page of the Esc menu listing the combos
// this player has found, each with its elements in order, its name and shape.
// Combos start hidden; casting one adds it (World.KnowsCombo). Opened with the
// d-pad's down (SpellCaster) or from the menu; the menu's own back closes it.
public class GrimoireMenu : MonoBehaviour
{
    const string PageId = "grimoire";
    // Where its button sits in the menu, after Controls.
    const int SectionOrder = 45;
    const float ElementsWidth = 190f, RowHeight = 34f;

    static GrimoireMenu instance;
    PauseMenu.Page page;
    GUIStyle row, head;
    Vector2 scroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Grimoire");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GrimoireMenu>();
    }

    /// <summary>Opens the menu on the grimoire.</summary>
    public static void Show()
    {
        var menu = PauseMenu.Instance;
        if (menu == null) return;
        menu.SetOpen(true);
        menu.ShowPage(PageId);
    }

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Grimoire", Width = 520, Draw = DrawPage };
        PauseMenu.AddSection(DrawEntry, SectionOrder);
        PauseMenu.AddPage(PageId, page);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(PageId);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void DrawEntry()
    {
        if (PauseMenu.Button("Grimoire", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(PageId);
    }

    void DrawPage()
    {
        row ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleLeft };
        head ??= new GUIStyle(row) { fontSize = 20 };
        var caster = EventLog.TrackedCaster;
        var world = caster != null && caster.grid != null ? caster.grid.World : null;
        if (world == null)
        {
            GUILayout.Label("No wizard here yet.", row);
            return;
        }
        var all = world.Rules.Combos.All;
        var found = all.Where(c => world.KnowsCombo(caster.CastPlayer, c.Id)).ToList();
        GUILayout.Label($"<b>{found.Count} of {all.Count} found</b>", head);
        if (found.Count == 0) GUILayout.Label("Nothing yet. Load an element, then perhaps a second, and see what it casts.", row);
        scroll = PauseMenu.ScrollView(scroll, Mathf.Clamp(HudScale.Height - 300f, 200f, 520f), () =>
        {
            foreach (var combo in found) Row(combo);
        });
    }

    void Row(ComboDef combo)
    {
        var elements = combo.Id.Split('+').Select(e => $"<color=#{ColorUtility.ToHtmlStringRGB(HudDraw.ElementColor(e))}>● {e}</color>");
        GUILayout.BeginHorizontal();
        GUILayout.Label(string.Join("  ", elements), row, GUILayout.Width(ElementsWidth), GUILayout.Height(RowHeight));
        GUILayout.Label($"<b>{combo.Name}</b>   <color=#ffffff88>{combo.Shape}</color>", row, GUILayout.Height(RowHeight));
        GUILayout.EndHorizontal();
    }
}
