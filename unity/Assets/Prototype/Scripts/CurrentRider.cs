using UnityEngine;

// The local wizard carried by the water (WaterCurrents): swept downstream in the
// river, shoved up the beach by the breakers. Fully when swimming, half when
// wading, not at all on a bridge, a dock or a seat. After the controller and
// SurfaceEffects have moved it this frame, like Knockback; only the wizard's
// owner moves it, and NetworkTransform carries it to everyone else.
[DefaultExecutionOrder(115)]
public class CurrentRider : MonoBehaviour
{
    const float WadeDepth = 0.35f;

    CharacterController body;
    PlayerControls controls;
    SurfaceEffects surface;
    Sitter sitter;
    ChemGrid grid;

    /// <summary>The push the water gave the wizard last frame (m/s).</summary>
    public Vector3 Current { get; private set; }

    void Awake()
    {
        TryGetComponent(out body);
        TryGetComponent(out controls);
        TryGetComponent(out surface);
        TryGetComponent(out sitter);
    }

    void Update()
    {
        Current = Vector3.zero;
        var currents = WaterCurrents.Instance;
        if (currents == null || body == null || !body.enabled || controls == null || !controls.isActiveAndEnabled) return;
        if (sitter != null && sitter.Seated != null) return;
        if (grid == null) grid = currents.grid;
        if (grid == null || grid.World == null) return;
        var cell = grid.CellAt(transform.position);
        float depth = grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y;
        bool swimming = surface != null && surface.Swimming;
        // On a bridge or a dock the feet are above the water: no push.
        if (!swimming && (depth < WadeDepth || depth > 3f)) return;
        Current = currents.At(transform.position) * (swimming ? 1f : 0.5f);
        if (Current.sqrMagnitude < 1e-4f) return;
        body.Move(Current * Time.deltaTime);
        // A zero move, so the controller doesn't read the carry as running (as Knockback does).
        body.Move(Vector3.zero);
    }
}
