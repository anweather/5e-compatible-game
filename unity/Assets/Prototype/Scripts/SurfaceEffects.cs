using System.Reflection;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// What the ground under the wizard does to movement, read from the chemistry
// grid: on a slippery cell (ice, oil, grease) the wizard keeps its momentum
// and skids; in unfrozen water deeper than a puddle it wades at reduced
// speed; deeper than the wizard's chest it swims: it floats with its head
// above the surface, slower still, and jump kicks it up out of the water onto
// a bank. Over the water (crossing the bridge) it's dry. Thorns (Spike Growth's spiky
// cover) slow it to the cover's speed and cut it (Vitals) every square it walks. Heavy ground slows
// it to its terrain's speed (mud: the move cost raiders pay too, World.Thorns), and hot metal (Cell.Heat)
// burns it 1 fire damage every sim.burnDamageEvery ticks, as it does a raider. Spells slow it too
// (Slow: frost chills, lightning jolts; SpellForces). Lightning in the water it stands in shocks it
// (shock-water: the charge runs through all the connected water), twice the zap, as the core does a
// wet creature. Runs after the Starter Assets controller has moved the wizard
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
    // 0.45 was too slow once walking got faster (Andrew, 2026-10-27).
    [Tooltip("Speed multiplier while swimming.")]
    public float swimmingSpeed = 0.65f;
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
    const float ClimbReach = 1f, ClimbSpeed = 2.6f;
    float slowUntil, slowFactor = 1f, appliedSpeed = 1f;
    Vitals vitals;
    // Thorns underfoot (Spike Growth): the cover's speed, and how far the wizard has walked through them since the last cut.
    float thornSpeed = 1f, thornsWalked;
    // In charged water since the charge came (one shock a strike, not one a frame).
    bool shocked;

    /// <summary>True while the wizard walks on thorns (a spiky cover): slowed, and cut every square walked.</summary>
    public bool InThorns { get; private set; }

    // The ground underfoot's speed (mud: 0.5; 1 for most), and how long the wizard has stood on hot metal since its last burn.
    float groundSpeed = 1f, onHotMetal;

    /// <summary>True while heavy ground (mud) slows the wizard.</summary>
    public bool InMud { get; private set; }

    /// <summary>True while the wizard stands on hot metal, burning.</summary>
    public bool OnHotMetal { get; private set; }

    /// <summary>True while a spell has the wizard slowed.</summary>
    public bool Slowed => Time.time < slowUntil;

    /// <summary>What the cast meter leaves of the wizard's speed (World.MoveFactorOf); SpellCaster sets it.</summary>
    public float CastFactor { get; set; } = 1f;

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
        TryGetComponent(out vitals);
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
        Climb(cell);
        Slide(slippery && !swimming);
        bool underfoot = c != null && !above && body.isGrounded && !swimming;
        Thorns(underfoot && grid.World.IsSpiky(c) ? grid.World.CoverOf(c) : null);
        Ground(underfoot ? c : null);
        Shock(c != null && !above && !c.Frozen && c.Water > 0 && surface > transform.position.y && c.Charge > 0);
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

    // Thorns underfoot (the cover, or null off them): they slow the wizard to the cover's speed and,
    // like the SRD's Spike Growth, cut it for every square (cellSize metres) it walks through them,
    // with the cover's damage dice. Health is the wizard's own (Vitals), not shared state, so the
    // dice are Unity's, not the world's.
    void Thorns(CoverDef thorns)
    {
        if (thorns == null)
        {
            InThorns = false;
            thornsWalked = 0f;
            return;
        }
        if (!InThorns) EventLog.Add($"The {thorns.Name.ToLowerInvariant()} tear at you");
        InThorns = true;
        thornSpeed = Mathf.Clamp((float)thorns.Speed, 0.05f, 1f);
        var moved = transform.position - lastPosition;
        moved.y = 0f;
        thornsWalked += moved.magnitude;
        if (thornsWalked < grid.cellSize) return;
        thornsWalked -= grid.cellSize;
        if (vitals != null && thorns.Damage.Length == 2) vitals.Hurt(Roll(thorns.Damage));
    }

    // The ground underfoot (its cell, or null off it: afloat, in the air, on the bridge): heavy ground
    // (mud) slows the wizard to its terrain's speed, and hot metal burns it 1 fire damage every
    // sim.burnDamageEvery ticks' worth of seconds while it stands there, the raiders' rate
    // (World.Hazards). Health is the wizard's own (Vitals), so the timing is Unity's, not the world's.
    void Ground(Cell c)
    {
        var world = grid.World;
        float speed = c != null ? Mathf.Clamp((float)world.TerrainOf(c).Speed, 0.05f, 1f) : 1f;
        bool mud = speed < 1f;
        if (mud && !InMud) EventLog.Add($"You slog into the {world.TerrainOf(c).Name.ToLowerInvariant()}");
        InMud = mud;
        groundSpeed = speed;

        bool hot = c != null && c.Heat > 0;
        if (!hot)
        {
            OnHotMetal = false;
            onHotMetal = 0f;
            return;
        }
        float every = world.Rules.Sim.BurnDamageEvery / (float)world.Rules.Sim.TicksPerSecond;
        if (!OnHotMetal)
        {
            EventLog.Add("The hot metal burns you");
            onHotMetal = every; // the first touch burns at once
        }
        OnHotMetal = true;
        onHotMetal += Time.deltaTime;
        if (onHotMetal < every) return;
        onHotMetal -= every;
        if (vitals != null) vitals.TakeDamage(1f);
    }

    // Freshly charged water round the wizard's feet: one shock a strike, wet so twice the zap, and a jolt.
    // Health is the wizard's own (Vitals), as with the thorns.
    void Shock(bool charged)
    {
        if (charged && !shocked)
        {
            EventLog.Add("The charged water shocks you");
            if (vitals != null) vitals.Hurt(grid.World.Rules.Sim.ZapDamage * 2);
            Slow(0.35f, 0.8f);
        }
        shocked = charged;
    }

    static int Roll(int[] dice)
    {
        int total = 0;
        for (int i = 0; i < dice[0]; i++) total += Random.Range(1, dice[1] + 1);
        return total;
    }

    // Wading, thorns, heavy ground, a spell's slow and casting, together.
    void SetSpeed(bool wading)
    {
        if (moveSpeed == null) return;
        if (wading && wasWading != wading) EventLog.Add("You wade into the water");
        wasWading = wading;
        float k = Slowed ? slowFactor : 1f;
        if (swimming) k *= swimmingSpeed;
        else if (wading) k *= wadingSpeed;
        else k *= Mathf.Min(InThorns ? thornSpeed : 1f, groundSpeed);
        k *= CastFactor;
        if (Mathf.Approximately(k, appliedSpeed)) return;
        appliedSpeed = k;
        moveSpeed.SetValue(controller, baseMove * k);
        sprintSpeed.SetValue(controller, baseSprint * k);
    }

    // Afloat in water deeper than the wizard's chest (unfrozen): held at the surface with a
    // gentle bob, until the ground under it rises (walking out) or it kicks up out of the water.
    // The sea's waves under the wizard (0 off the sea: the lake and the river lie still).
    float Swell(Vector2Int cell)
    {
        var ocean = Ocean.Instance;
        var xz = new Vector2(transform.position.x, transform.position.z);
        if (ocean == null || !ocean.Covers(xz) || grid.valley == null) return 0f;
        var c = grid.World.At(cell.x, cell.y);
        return grid.valley.IsLake(c, cell.x, cell.y) ? ocean.WaveOffset(xz) : 0f;
    }

    void Swim(Cell c, Vector2Int cell)
    {
        if (gravity == null || fall == null) return;
        float depth = c != null && !c.Frozen && c.Water > 0 ? grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y : 0f;
        bool afloat = Time.time >= kickedUntil && depth > (swimming ? StopSwimming : swimDepth);
        if (afloat && !swimming) EventLog.Add("You swim");
        SetAfloat(afloat);
        if (!afloat) return;

        // Rise or sink toward floating height through the controller's own fall speed (gravity is off),
        // so its move each frame stays one move and its run speed isn't disturbed. At sea it rides the
        // swell (Ocean.WaveOffset); whether it swims at all is still judged on the calm level.
        float target = transform.position.y + depth + Swell(cell) - floatDepth + BobHeight * Mathf.Sin(Time.time * 2.4f);
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

    /// <summary>Thrown straight up at this speed (a Geyser's jet), out of the water if afloat.</summary>
    public void Toss(float speed)
    {
        if (fall == null) return;
        kickedUntil = Time.time + KickTime;
        SetAfloat(false);
        fall.SetValue(controller, speed);
        if (animator != null) animator.SetBool(JumpId, true);
    }

    // Walking into vines on what stands ahead climbs them, up over the top.
    void Climb(Vector2Int cell)
    {
        if (fall == null || ((int)body.collisionFlags & (int)CollisionFlags.Sides) == 0) return;
        var ahead = grid.CellAt(transform.position + transform.forward * ClimbReach);
        var c = grid.World.At(ahead.x, ahead.y);
        if (ahead == cell || c == null || c.Cover != CoverIds.Vines || !grid.World.IsBlocking(c)) return;
        fall.SetValue(controller, ClimbSpeed);
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
