using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The Hold's moments, offered in dev builds and the Editor only, never in a released player (docs/design/the-hold.md, phase 4):
// F8 lists them, and a click goes in. Going in is a command, so every peer goes with you; each wizard then gets up at the castle.
[RequireComponent(typeof(ChemGrid))]
public class HoldMoments : MonoBehaviour
{
    const float RowHeight = 30f, Width = 320f, Left = 16f, Top = 120f;

    ChemGrid grid;
    bool open;
    int seen;

    /// <summary>Whether this build offers moments at all.</summary>
    public static bool Offered => Application.isEditor || Debug.isDebugBuild;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        if (!Offered) enabled = false;
    }

    /// <summary>Goes into a moment by its id, as the list does; false if the core refused it. The probe starts one here.</summary>
    public bool Enter(string id)
    {
        var caster = EventLog.TrackedCaster;
        open = false;
        return caster != null && caster.Moment(id);
    }

    void Update()
    {
        var world = grid.World;
        if (world?.HoldSite == null) return;
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) open = !open;
        var hold = world.Hold;
        if (hold?.Moment == null || hold.MomentTick == seen) return;
        seen = hold.MomentTick;
        var caster = EventLog.TrackedCaster;
        if (caster != null && caster.TryGetComponent<WizardDown>(out var down)) down.ToTheCastle();
    }

    void OnGUI()
    {
        var world = grid.World;
        if (!open || world?.HoldSite == null) return;
        HudScale.Apply();
        var moments = world.Rules.Sim.Hold.Moments;
        for (int i = 0; i < moments.Count; i++)
            if (GUI.Button(new Rect(Left, Top + i * RowHeight, Width, RowHeight - 2f), moments[i].Name)) Enter(moments[i].Id);
    }
}
