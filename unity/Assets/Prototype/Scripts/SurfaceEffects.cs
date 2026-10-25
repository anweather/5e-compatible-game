using System.Reflection;
using UnityEngine;

// What the ground under the wizard does to movement, read from the chemistry
// grid: on a slippery cell (ice, oil, grease) the wizard keeps its momentum
// and skids; in unfrozen water deeper than a puddle it wades at reduced
// speed. Runs after the Starter Assets controller has moved the wizard and
// corrects that move, so the controller itself stays untouched.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CharacterController))]
public class SurfaceEffects : MonoBehaviour
{
    public ChemGrid grid;
    [Tooltip("How quickly the wizard regains control on slippery ground (higher = less slide).")]
    public float slipperyGrip = 1.2f;
    [Tooltip("Speed multiplier in water at least this many units deep.")]
    public float wadingSpeed = 0.55f;
    public int wadingDepth = 2;

    CharacterController body;
    Component controller;
    FieldInfo moveSpeed, sprintSpeed;
    float baseMove, baseSprint;
    Vector3 lastPosition;
    Vector3 momentum;
    bool wasSlipping, wasWading;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        controller = GetComponent("ThirdPersonController");
        if (controller == null) return;
        moveSpeed = controller.GetType().GetField("MoveSpeed", BindingFlags.Instance | BindingFlags.Public);
        sprintSpeed = controller.GetType().GetField("SprintSpeed", BindingFlags.Instance | BindingFlags.Public);
        baseMove = (float)moveSpeed.GetValue(controller);
        baseSprint = (float)sprintSpeed.GetValue(controller);
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        momentum = Vector3.zero;
    }

    void Update()
    {
        if (grid == null || grid.World == null || !body.enabled) return;
        var cell = grid.CellAt(transform.position);
        var c = grid.World.At(cell.x, cell.y);
        bool slippery = c != null && body.isGrounded && grid.World.IsSlippery(c);
        bool wading = c != null && !c.Frozen && c.Water >= wadingDepth;

        Slide(slippery);
        Wade(wading);
        lastPosition = transform.position;
    }

    // Blend the controller's move this frame with the momentum carried from before.
    void Slide(bool slippery)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var moved = transform.position - lastPosition;
        moved.y = 0f;
        var intended = moved / dt;
        if (!slippery)
        {
            momentum = intended;
            wasSlipping = false;
            return;
        }
        if (!wasSlipping && momentum.sqrMagnitude > 1f) EventLog.Add("You skid across the slick ground");
        wasSlipping = true;
        momentum = Vector3.Lerp(momentum, intended, 1f - Mathf.Exp(-slipperyGrip * dt));
        body.Move((momentum - intended) * dt);
    }

    void Wade(bool wading)
    {
        if (moveSpeed == null || wading == wasWading) return;
        wasWading = wading;
        float k = wading ? wadingSpeed : 1f;
        moveSpeed.SetValue(controller, baseMove * k);
        sprintSpeed.SetValue(controller, baseSprint * k);
        if (wading) EventLog.Add("You wade into the water");
    }
}
