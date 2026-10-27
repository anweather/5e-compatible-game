using System.Reflection;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// What the ground under the wizard does to movement, read from the chemistry
// grid: on a slippery cell (ice, oil, grease) the wizard keeps its momentum
// and skids; in unfrozen water deeper than a puddle it wades at reduced
// speed; deeper than the wizard's chest it swims: it floats with its head
// above the surface, slower still, and jump kicks it up out of the water onto
// a bank. Over the water (crossing the bridge) it's dry. Spells slow it too
// (Slow: frost chills, lightning jolts; SpellForces). Runs after the Starter Assets controller has moved the wizard
// and corrects that move, so the controller itself stays untouched (its
// gravity is switched off while afloat and its animator told it's standing).
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
    [Tooltip("Speed multiplier while swimming.")]
    public float swimmingSpeed = 0.45f;
    [Tooltip("Water this deep over the wizard's feet (metres) floats it; it floats with its feet this far down.")]
    public float swimDepth = 1.3f, floatDepth = 1.45f;
    [Tooltip("How high a kick out of the water lifts the wizard (metres).")]
    public float swimKick = 2.2f;

    // Afloat until the water under the feet is shallower than this (metres), so a wave doesn't flicker it.
    static readonly int GroundedId = Animator.StringToHash("Grounded"), FreeFallId = Animator.StringToHash("FreeFall"), JumpId = Animator.StringToHash("Jump");
    const float StopSwimming = 1.1f, KickTime = 0.8f, Buoyancy = 4f, BobHeight = 0.04f;
    // Feet this far over a cell's surface are on something built over it (the bridge), not on the cell.
    const float AboveSurface = 1f;

    CharacterController body;
    Component controller;
    FieldInfo moveSpeed, sprintSpeed, gravity, fall;
    Animator animator;
    float baseMove, baseSprint, baseGravity, wizardMove, wizardSprint;
    Vector3 lastPosition;
    Vector3 momentum;
    bool wasSlipping, wasWading, swimming;
    float kickedUntil;
    bool kick;
    float slowUntil, slowFactor = 1f, appliedSpeed = 1f;

    /// <summary>True while a spell has the wizard slowed.</summary>
    public bool Slowed => Time.time < slowUntil;

    /// <summary>True while the wizard is afloat in deep water.</summary>
    public bool Swimming => swimming;

    /// <summary>Kicks up out of the water, if swimming (what jump does afloat).</summary>
    public void Kick() => kick = swimming;

    // Jump, from PlayerInput's messages: the controller drops a jump made off the ground, so it's
    // caught here as it comes in.
    void OnJump(InputValue value)
    {
        if (value.isPressed) Kick();
    }

    /// <summary>Slows the wizard to <paramref name="factor"/> of its speed for a while (the stronger slow wins while both last).</summary>
    public void Slow(float factor, float seconds)
    {
        if (!Slowed || factor < slowFactor) slowFactor = factor;
        slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
    }

    /// <summary>The speeds wading, swimming and slows scale (the controller's MoveSpeed and SprintSpeed): an animal's, while shapeshifted.</summary>
    public void SetBaseSpeeds(float move, float sprint)
    {
        if (moveSpeed == null) return;
        baseMove = move;
        baseSprint = sprint;
        moveSpeed.SetValue(controller, baseMove * appliedSpeed);
        sprintSpeed.SetValue(controller, baseSprint * appliedSpeed);
    }

    /// <summary>Back to the wizard's own speeds.</summary>
    public void RestoreBaseSpeeds() => SetBaseSpeeds(wizardMove, wizardSprint);

    /// <summary>The wizard's own speeds, walking and sprinting (before any shape, wading or slow).</summary>
    public (float move, float sprint) WizardSpeeds => (wizardMove, wizardSprint);

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
        (wizardMove, wizardSprint) = (baseMove, baseSprint);
        gravity = WizardRig.Field(controller, "Gravity", isPublic: true);
        fall = WizardRig.Field(controller, "_verticalVelocity", isPublic: false);
        if (gravity != null) baseGravity = (float)gravity.GetValue(controller);
        TryGetComponent(out animator);
    }

    void OnDisable() => SetAfloat(false);

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
        // The cell's water (or its ground, dry): on the bridge over the river the wizard stands well
        // above it, out of its water and off its ice.
        float surface = grid.WaterSurfaceY(cell.x, cell.y);
        bool above = transform.position.y > surface + AboveSurface;
        bool slippery = c != null && !above && body.isGrounded && grid.World.IsSlippery(c);
        bool wading = c != null && !c.Frozen && c.Water >= wadingDepth && surface > transform.position.y;

        Swim(c, cell);
        Slide(slippery && !swimming);
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
        float k = Slowed ? slowFactor : 1f;
        if (swimming) k *= swimmingSpeed;
        else if (wading) k *= wadingSpeed;
        if (Mathf.Approximately(k, appliedSpeed)) return;
        appliedSpeed = k;
        moveSpeed.SetValue(controller, baseMove * k);
        sprintSpeed.SetValue(controller, baseSprint * k);
    }

    // Afloat in water deeper than the wizard's chest (unfrozen): held at the surface with a
    // gentle bob, until the ground under it rises (walking out) or it kicks up out of the water.
    void Swim(Cell c, Vector2Int cell)
    {
        if (gravity == null || fall == null) return;
        float depth = c != null && !c.Frozen && c.Water > 0 ? grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y : 0f;
        bool afloat = Time.time >= kickedUntil && depth > (swimming ? StopSwimming : swimDepth);
        if (afloat && !swimming) EventLog.Add("You swim");
        SetAfloat(afloat);
        if (!afloat) return;

        // Rise or sink toward floating height through the controller's own fall speed (gravity is off),
        // so its move each frame stays one move and its run speed isn't disturbed.
        float target = transform.position.y + depth - floatDepth + BobHeight * Mathf.Sin(Time.time * 2.4f);
        fall.SetValue(controller, (target - transform.position.y) * Buoyancy);
        if (animator != null)
        {
            animator.SetBool(GroundedId, true);
            animator.SetBool(FreeFallId, false);
            animator.SetBool(JumpId, false);
        }
        if (!kick) return;
        // A kick up out of the water, with gravity back on so it lands on a bank or falls back in.
        kick = false;
        kickedUntil = Time.time + KickTime;
        SetAfloat(false);
        fall.SetValue(controller, Mathf.Sqrt(swimKick * -2f * baseGravity));
        if (animator != null) animator.SetBool(JumpId, true);
    }

    void SetAfloat(bool on)
    {
        if (swimming == on || gravity == null) return;
        swimming = on;
        kick = false;
        gravity.SetValue(controller, on ? 0f : baseGravity);
        if (on) fall.SetValue(controller, 0f);
    }

}
