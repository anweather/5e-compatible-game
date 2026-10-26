using System.Reflection;
using UnityEngine;

// What the ground under the wizard does to movement, read from the chemistry
// grid: on a slippery cell (ice, oil, grease) the wizard keeps its momentum
// and skids; in unfrozen water deeper than a puddle it wades at reduced
// speed. Spells slow it too (Slow: frost chills, lightning jolts; SpellForces). Runs after the Starter Assets controller has moved the wizard and
// corrects that move, so the controller itself stays untouched.
//
// The ground is the valley's terrain everywhere, dug into pits, a pond and
// trenches by the cells. The wizard steps up one step of earth (a heap, a
// hill's terrace, a pit's edge) without jumping; two steps (out of a trench)
// need a jump, and a raised wall's blocker can't be climbed.
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
    [Tooltip("Highest ledge the wizard steps up without jumping: a little over one step of the grid.")]
    public float climbStep = 0.55f;

    CharacterController body;
    Component controller;
    FieldInfo moveSpeed, sprintSpeed;
    float baseMove, baseSprint;
    Vector3 lastPosition;
    Vector3 momentum;
    bool wasSlipping, wasWading;
    float slowUntil, slowFactor = 1f, appliedSpeed = 1f;

    /// <summary>True while a spell has the wizard slowed.</summary>
    public bool Slowed => Time.time < slowUntil;

    /// <summary>Slows the wizard to <paramref name="factor"/> of its speed for a while (the stronger slow wins while both last).</summary>
    public void Slow(float factor, float seconds)
    {
        if (!Slowed || factor < slowFactor) slowFactor = factor;
        slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
    }

    void Awake()
    {
        body = GetComponent<CharacterController>();
        body.stepOffset = Mathf.Max(body.stepOffset, climbStep);
        controller = GetComponent(WizardRig.ControllerComponent);
        if (controller == null) return;
        moveSpeed = WizardRig.Field(controller, "MoveSpeed", isPublic: true);
        sprintSpeed = WizardRig.Field(controller, "SprintSpeed", isPublic: true);
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
        SetSpeed(wading);
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

    // Wading and a spell's slow, together.
    void SetSpeed(bool wading)
    {
        if (moveSpeed == null) return;
        if (wading && wasWading != wading) EventLog.Add("You wade into the water");
        wasWading = wading;
        float k = (wading ? wadingSpeed : 1f) * (Slowed ? slowFactor : 1f);
        if (Mathf.Approximately(k, appliedSpeed)) return;
        appliedSpeed = k;
        moveSpeed.SetValue(controller, baseMove * k);
        sprintSpeed.SetValue(controller, baseSprint * k);
    }

}
