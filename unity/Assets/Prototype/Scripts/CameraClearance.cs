using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

// Keeps the over-the-shoulder camera out of the ground and out of the wizard
// (Andrew's playtest: the camera clipped through the ground; against a cliff
// it ended up inside the wizard). After the follow camera has placed itself
// (its own obstacle check only pulls it in along the line to the shoulder),
// it lifts the lens to a little over the ground under it, the highest of the
// cells round it (ChemGrid's ground, not the trees or houses standing on it,
// so it never jumps onto a canopy). Pulled in so close that it would be inside
// the wizard, the wizard's body and outfit are hidden and only cast their
// shadows until it backs out again.
public class CameraClearance : CinemachineExtension
{
    // How far over the ground the lens keeps, and how near the head hides the wizard.
    const float OverGround = 0.35f, InsideWizard = 0.55f;

    ChemGrid grid;
    Transform hiddenWizard;
    Renderer[] hidden;
    ShadowCastingMode[] was;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        var at = state.GetFinalPosition();
        float ground = GroundUnder(at);
        if (at.y < ground + OverGround) state.PositionCorrection += Vector3.up * (ground + OverGround - at.y);

        var target = vcam.Follow;
        bool inside = target != null && Vector3.Distance(state.GetFinalPosition(), target.position) < InsideWizard;
        Hide(inside ? target.root : null);
    }

    // The highest ground in the 3 × 3 cells round a point (the drawn ground is smoothed between
    // cells, so the lens keeps over the highest of them), or far below off the land.
    float GroundUnder(Vector3 at)
    {
        if (grid == null) grid = FindAnyObjectByType<ChemGrid>();
        if (grid == null || grid.World == null) return float.MinValue;
        var cell = grid.CellAt(at);
        float top = float.MinValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = cell.x + dx, y = cell.y + dy;
                if (!grid.World.InBounds(x, y)) continue;
                top = Mathf.Max(top, grid.GroundTop(x, y, grid.World.At(x, y)));
            }
        return top;
    }

    // Hides this wizard (shadows only), or shows the one hidden before.
    void Hide(Transform wizard)
    {
        if (wizard == hiddenWizard) return;
        if (hidden != null)
            for (int i = 0; i < hidden.Length; i++)
                if (hidden[i] != null) hidden[i].shadowCastingMode = was[i];
        hiddenWizard = wizard;
        hidden = wizard != null ? wizard.GetComponentsInChildren<Renderer>() : null;
        if (hidden == null) return;
        was = new ShadowCastingMode[hidden.Length];
        for (int i = 0; i < hidden.Length; i++)
        {
            was[i] = hidden[i].shadowCastingMode;
            hidden[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }
}
