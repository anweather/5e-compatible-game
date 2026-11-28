using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The Hold's moments, offered in dev builds and the Editor only, never in a released player (docs/design/the-hold.md, phase 4):
// the Esc menu's Developer page lists them, F8 opens the menu, and a click goes in. Going in is a command, so every peer goes with
// you; each wizard then gets up at the castle.
[RequireComponent(typeof(ChemGrid))]
[GridAddOn(GridStage.Grid, 40)]
public class HoldMoments : MonoBehaviour
{
    const int MenuOrder = 50;
    const string PageId = "hold-moments";

    ChemGrid grid;
    int seen;
    string chosen;
    bool showPage;

    public static bool Offered => GridAddOns.DevBuild;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        if (!Offered) enabled = false;
        else
        {
            PauseMenu.AddSection(DrawInMenu, MenuOrder, PauseMenu.Submenu.Developer);
            PauseMenu.AddPage(PageId, new PauseMenu.Page { Title = "The Hold's moments", Draw = DrawPage });
        }
    }

    void OnDestroy()
    {
        PauseMenu.RemoveSection(DrawInMenu);
        PauseMenu.RemovePage(PageId);
    }

    void DrawInMenu()
    {
        if (PauseMenu.Button("The Hold's moments")) showPage = true;
    }

    void DrawPage()
    {
        var world = grid != null ? grid.World : null;
        foreach (var moment in world?.Rules.Sim.Hold.Moments ?? System.Array.Empty<MomentDef>())
            if (PauseMenu.Button(moment.Name)) chosen = moment.Id;
    }

    public bool Enter(string id)
    {
        var caster = WizardHud.TrackedCaster;
        return caster != null && caster.Moment(id);
    }

    void Update()
    {
        var world = grid.World;
        if (world?.HoldSite() == null) return;
        if (showPage)
        {
            showPage = false;
            PauseMenu.Instance.ShowPage(PageId);
        }
        if (chosen != null)
        {
            Enter(chosen);
            chosen = null;
            PauseMenu.Instance.SetOpen(false);
        }
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) PauseMenu.Instance.SetOpen(true);
        var hold = world.Hold();
        if (hold?.Moment == null || hold.MomentTick == seen) return;
        seen = hold.MomentTick;
        var caster = WizardHud.TrackedCaster;
        if (caster != null && caster.TryGetComponent<WizardDown>(out var down)) down.ToTheCastle();
    }

}
