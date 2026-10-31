using System.Reflection;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// What the ground under the wizard does to movement, read from the chemistry
// grid: on a slippery cell (ice, oil, grease) the wizard keeps its momentum
// and skids; in unfrozen water deeper than a puddle it wades at reduced
// speed; deeper than the wizard's chest it swims: it floats with its head
// above the surface, slower still, and jump kicks it up out of the water onto
// a bank. Holding the dodge roll's button (Left Ctrl, the crouch key; there's
// no roll afloat) dives: it sinks toward the bottom, slower to move, and
// letting go swims it back up. With its head under it holds its breath
// (Vitals.Breathe) and drowns once that runs out. Water frozen over it holds
// it under the ice, so Frost over a swimmer is a trap (on purpose).
// Over the water (crossing the bridge) it's dry. Thorns (Spike Growth's spiky
// cover) slow it to the cover's speed and cut it (Vitals) every square it walks. Heavy ground slows
// it to its terrain's speed (mud: the move cost raiders pay too, World.Thorns), and hot metal (Cell.Heat)
// burns it 1 fire damage every sim.burnDamageEvery ticks, as it does a raider. Spells slow it too
// (Slow: frost chills, lightning jolts; SpellForces). Lightning in the water it stands in shocks it
// (shock-water: the charge runs through all the connected water), twice the zap, as the core does a
// wet creature. Snow slows it as it does a raider (World.WalkSpeed), and ice, oil and Frost's glaze,
// snow glazed included, cost it no speed at all: it slides. Runs after the Starter Assets controller has moved the wizard
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
    [Tooltip("Speed multiplier with the head under water (instead of swimmingSpeed).")]
    public float underwaterSpeed = 0.4f;
    [Tooltip("How fast a dive sinks, and how fast letting go swims back up (metres a second).")]
    public float diveSpeed = 1.6f, riseSpeed = 2f;

    // Afloat until the water under the feet is shallower than this (metres), so a wave doesn't flicker it.
    static readonly int GroundedId = Animator.StringToHash("Grounded"), FreeFallId = Animator.StringToHash("FreeFall"), JumpId = Animator.StringToHash("Jump");
    const float StopSwimming = 1.1f, KickTime = 0.8f, Buoyancy = 4f, BobHeight = 0.04f;
    // Feet this far over a cell's surface are on something built over it (the bridge), not on the cell.
    const float AboveSurface = 1f;
    // The head's height up the wizard's capsule (a share of its height), and how far under ice the top of the head stays.
    const float HeadAt = 0.9f, IceGap = 0.15f;

    CharacterController body;
    Component controller, inputs;
    FieldInfo moveSpeed, sprintSpeed, gravity, fall, moveInput, sprintInput, analogInput;
    Animator animator;
    float baseMove, baseSprint, baseGravity, wizardMove, wizardSprint;
    // Where the wizard stood after this script last ran, and after everything had moved it last frame.
    Vector3 lastPosition, settled;
    Vector3 momentum;
    bool wasSlipping, wasWading, swimming;
    float kickedUntil;
    bool kick;
    const float ClimbReach = 1f, ClimbSpeed = 2.6f, ClimbOutSeconds = 0.6f;
    float leftWaterAt = float.NegativeInfinity;
    float slowUntil, slowFactor = 1f, appliedSpeed = 1f;
    Vitals vitals;
    PlayerControls controls;
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

    /// <summary>True while the wizard dives (the dive held while swimming).</summary>
    public bool Diving { get; private set; }

    /// <summary>Holds the dive as the button does (the probe and the playtest bot).</summary>
    public bool HoldDive { get; set; }

    /// <summary>True while the wizard's head is under water (or under ice).</summary>
    public bool HeadUnder { get; private set; }

    /// <summary>Just out of the water: the dive's button doesn't turn into a dodge roll on the bank.</summary>
    public bool JustClimbedOut => Time.time < leftWaterAt + ClimbOutSeconds;

    /// <summary>True while the water over the swimming wizard is frozen: it can't come up.</summary>
    public bool UnderIce { get; private set; }

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
        TryGetComponent(out controls);
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
        inputs = GetComponent(WizardRig.InputsComponent);
        moveInput = WizardRig.Field(inputs, "move", isPublic: true);
        sprintInput = WizardRig.Field(inputs, "sprint", isPublic: true);
        analogInput = WizardRig.Field(inputs, "analogMovement", isPublic: true);
        TryGetComponent(out animator);
    }

    void OnDisable()
    {
        SetAfloat(false);
        Diving = HeadUnder = UnderIce = false;
    }

    void OnEnable()
    {
        lastPosition = settled = transform.position;
        momentum = Vector3.zero;
    }

    // After the roll, a knock and the river (which run after this script): the controller's own step
    // next frame is what's gone on from here.
    void LateUpdate() => settled = transform.position;

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
        Breathe(surface);
        Climb(cell);
        Slide(slippery && !swimming);
        bool underfoot = c != null && !above && body.isGrounded && !swimming;
        Thorns(underfoot && grid.World.IsSpiky(c) ? grid.World.CoverOf(c) : null);
        Ground(underfoot ? c : null);
        Shock(c != null && !above && !c.Frozen && c.Water > 0 && surface > transform.position.y && c.Charge > 0);
        SetSpeed(wading);
        lastPosition = transform.position;
    }

    // On slippery ground the wizard keeps the momentum it came with: it eases toward the way the player
    // wants to go (Glide) at slipperyGrip, so it's slow to turn and to stop, and keeps its speed going on.
    // The controller's own step this frame is taken back and the glide made as one move, so the
    // controller's velocity, which sets its next frame's speed, is the glide's. (Correcting its step with
    // a second move left it measuring only the correction, so it started again from a crawl each frame
    // and ice slowed the wizard down: GitHub #12.)
    void Slide(bool slippery)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var step = transform.position - settled;
        var moved = new Vector3(step.x, 0f, step.z);
        if (!slippery)
        {
            var whole = transform.position - lastPosition;
            momentum = new Vector3(whole.x, 0f, whole.z) / dt;
            wasSlipping = false;
            return;
        }
        if (!wasSlipping && momentum.sqrMagnitude > 1f) EventLog.Add("You skid across the slick ground");
        wasSlipping = true;
        momentum = Glide(momentum, Wanted(moved), slipperyGrip, dt);
        transform.position = settled;
        Physics.SyncTransforms();
        body.Move(momentum * dt + Vector3.up * step.y);
    }

    /// <summary>
    /// The momentum on slippery ground after <paramref name="dt"/> seconds: eased toward the
    /// <paramref name="wanted"/> velocity at <paramref name="grip"/> (the share of the difference left
    /// falls by e each 1/grip seconds, whatever the frame rate), never faster than the faster of the two.
    /// </summary>
    public static Vector3 Glide(Vector3 momentum, Vector3 wanted, float grip, float dt) =>
        Vector3.Lerp(momentum, wanted, 1f - Mathf.Exp(-Mathf.Max(grip, 0f) * Mathf.Max(dt, 0f)));

    // The velocity the player asks for: the controller's way, at its walk or sprint speed (the move
    // stick's tilt for a pad), or none with the stick and keys let go.
    Vector3 Wanted(Vector3 moved)
    {
        if (moveSpeed == null || inputs == null || moveInput == null) return moved / Mathf.Max(Time.deltaTime, 1e-4f);
        var stick = (Vector2)moveInput.GetValue(inputs);
        if (stick == Vector2.zero || moved.sqrMagnitude < 1e-8f) return Vector3.zero;
        bool sprint = sprintInput != null && (bool)sprintInput.GetValue(inputs);
        float tilt = analogInput != null && (bool)analogInput.GetValue(inputs) ? Mathf.Clamp01(stick.magnitude) : 1f;
        float speed = (float)(sprint ? sprintSpeed : moveSpeed).GetValue(controller);
        return moved.normalized * (speed * tilt);
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
    // (mud) and snow slow the wizard to their speed (World.WalkSpeed), not while it slides on anything
    // slippery laid over them (Frost's glaze keeps its speed), and hot metal burns it 1 fire damage every
    // sim.burnDamageEvery ticks' worth of seconds while it stands there, the raiders' rate
    // (World.Hazards). Health is the wizard's own (Vitals), so the timing is Unity's, not the world's.
    void Ground(Cell c)
    {
        var world = grid.World;
        float speed = c != null && !world.IsSlippery(c) ? Mathf.Clamp((float)world.WalkSpeed(c), 0.05f, 1f) : 1f;
        bool mud = speed < 1f && world.TerrainOf(c).Speed < 1;
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
        if (swimming) k *= HeadUnder ? underwaterSpeed : swimmingSpeed;
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
        // Ice is walked on; only a swimmer already in the water stays in it under the ice.
        bool iced = c != null && c.Frozen;
        float depth = c != null && c.Water > 0 && (!iced || swimming) ? grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y : 0f;
        bool afloat = Time.time >= kickedUntil && depth > (swimming ? StopSwimming : swimDepth);
        if (afloat && !swimming) EventLog.Add("You swim");
        SetAfloat(afloat);
        if (afloat && iced && !UnderIce) EventLog.Add("Ice closes over you: break it or swim out from under it");
        UnderIce = afloat && iced;
        Diving = afloat && (HoldDive || (controls != null && controls.enabled && controls.Roll.IsPressed()));
        if (!afloat) return;

        Float(depth, cell);
        if (!kick) return;
        // A kick up out of the water, with gravity back on so it lands on a bank or falls back in;
        // not from under the water or the ice.
        kick = false;
        if (HeadUnder || UnderIce) return;
        kickedUntil = Time.time + KickTime;
        SetAfloat(false);
        fall.SetValue(controller, Mathf.Sqrt(swimKick * -2f * baseGravity));
        if (animator != null) animator.SetBool(JumpId, true);
    }

    // Rise or sink toward floating height through the controller's own fall speed (gravity is off),
    // so its move each frame stays one move and its run speed isn't disturbed. At sea it rides the
    // swell (Ocean.WaveOffset); whether it swims at all is still judged on the calm level. A dive
    // sinks instead, and from under the water it swims back up at riseSpeed; ice over it keeps the
    // top of its head under the ice.
    void Float(float depth, Vector2Int cell)
    {
        float y = transform.position.y;
        float target = y + depth + Swell(cell) - floatDepth + BobHeight * Mathf.Sin(Time.time * 2.4f);
        if (UnderIce) target = Mathf.Min(target, y + depth - body.height - IceGap);
        fall.SetValue(controller, Diving ? -diveSpeed : Mathf.Min((target - y) * Buoyancy, riseSpeed));
        if (animator == null) return;
        animator.SetBool(GroundedId, true);
        animator.SetBool(FreeFallId, false);
        animator.SetBool(JumpId, false);
    }

    // The head under the water (a cell's drawn water or the sea over it, or ice over a swimmer):
    // the wizard holds its breath, and drowns once it runs out (Vitals).
    void Breathe(float surface)
    {
        var head = transform.position + Vector3.up * (body.center.y + body.height * (HeadAt - 0.5f));
        HeadUnder = head.y < UnderwaterView.WaterTop(grid, head) || (UnderIce && head.y < surface);
        if (vitals != null) vitals.Breathe(HeadUnder, Time.deltaTime);
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
        if (!on) leftWaterAt = Time.time;
        kick = false;
        gravity.SetValue(controller, on ? 0f : baseGravity);
        if (on) fall.SetValue(controller, 0f);
    }

}
