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
    [Tooltip("Speed multiplier wading at the water's edge (in water at least wadingDepth units deep).")]
    public float shallowWadingSpeed = 0.92f;
    [Tooltip("Speed multiplier wading in water nearly deep enough to swim (swimDepth over the feet).")]
    public float wadingSpeed = 0.78f;
    public int wadingDepth = 2;
    [Tooltip("Highest ledge the wizard steps up without jumping: a little over one step of the grid.")]
    public float climbStep = 0.55f;
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

    static readonly int GroundedId = Animator.StringToHash("Grounded"), FreeFallId = Animator.StringToHash("FreeFall"), JumpId = Animator.StringToHash("Jump");
    const float StopSwimming = 1.1f, KickTime = 0.8f, Buoyancy = 4f, BobHeight = 0.04f;
    const float AboveSurface = 1f;
    const float HeadAt = 0.9f, IceGap = 0.15f;

    CharacterController body;
    Component controller, inputs;
    FieldInfo moveSpeed, sprintSpeed, gravity, fall, moveInput, sprintInput, analogInput;
    Animator animator;
    float baseMove, baseSprint, baseGravity, wizardMove, wizardSprint;
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
    float thornSpeed = 1f, thornsWalked;
    bool shocked;

    public bool InThorns { get; private set; }

    float groundSpeed = 1f, onHotMetal;

    public bool InMud { get; private set; }

    public bool OnHotMetal { get; private set; }

    public bool Slowed => Time.time < slowUntil;

    public float CastFactor { get; set; } = 1f;

    public bool Swimming => swimming;

    public bool Diving { get; private set; }

    public bool HoldDive { get; set; }

    public bool HeadUnder { get; private set; }

    public bool JustClimbedOut => Time.time < leftWaterAt + ClimbOutSeconds;

    public bool UnderIce { get; private set; }

    public int Skids { get; private set; }

    public void Kick() => kick = swimming;

    void OnJump(InputValue value)
    {
        if (value.isPressed) Kick();
    }

    public void Slow(float factor, float seconds)
    {
        if (!Slowed || factor < slowFactor) slowFactor = factor;
        slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
    }

    public void SetBaseSpeeds(float move, float sprint)
    {
        if (moveSpeed == null) return;
        baseMove = move;
        baseSprint = sprint;
        ApplySpeed();
    }

    public float SpeedStat
    {
        get => speedStat;
        set
        {
            speedStat = Mathf.Max(0.05f, value);
            ApplySpeed();
        }
    }

    float speedStat = 1f;

    void ApplySpeed()
    {
        if (moveSpeed == null) return;
        moveSpeed.SetValue(controller, baseMove * speedStat * appliedSpeed);
        sprintSpeed.SetValue(controller, baseSprint * speedStat * appliedSpeed);
    }

    public void RestoreBaseSpeeds() => SetBaseSpeeds(wizardMove, wizardSprint);

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

    void LateUpdate() => settled = transform.position;

    void Update()
    {
        if (grid == null || grid.World == null || !body.enabled) return;
        var cell = grid.CellAt(transform.position);
        var c = grid.World.At(cell.x, cell.y);
        float surface = grid.WaterSurfaceY(cell.x, cell.y);
        bool above = transform.position.y > surface + AboveSurface;
        bool slippery = c != null && !above && body.isGrounded && grid.World.IsSlippery(c);
        bool wading = c != null && !c.Frozen && c.Water >= wadingDepth && surface > transform.position.y;
        WadeDepth = wading ? surface - transform.position.y : 0f;

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
        if (!wasSlipping && momentum.sqrMagnitude > 1f) Skids++;
        wasSlipping = true;
        momentum = Glide(momentum, Wanted(moved), slipperyGrip, dt);
        transform.position = settled;
        Physics.SyncTransforms();
        body.Move(momentum * dt + Vector3.up * step.y);
    }

    public static Vector3 Glide(Vector3 momentum, Vector3 wanted, float grip, float dt) =>
        Vector3.Lerp(momentum, wanted, 1f - Mathf.Exp(-Mathf.Max(grip, 0f) * Mathf.Max(dt, 0f)));

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
            onHotMetal = every;
        }
        OnHotMetal = true;
        onHotMetal += Time.deltaTime;
        if (onHotMetal < every) return;
        onHotMetal -= every;
        if (vitals != null) vitals.TakeDamage(1f);
    }

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

    public float WadeDepth { get; private set; }

    public float WadingFactor(float depth) => Mathf.Lerp(shallowWadingSpeed, wadingSpeed, Mathf.InverseLerp(0f, swimDepth, depth));

    void SetSpeed(bool wading)
    {
        if (moveSpeed == null) return;
        wasWading = wading;
        float k = Slowed ? slowFactor : 1f;
        if (swimming) k *= HeadUnder ? underwaterSpeed : swimmingSpeed;
        else if (wading) k *= WadingFactor(WadeDepth);
        else k *= Mathf.Min(InThorns ? thornSpeed : 1f, groundSpeed);
        k *= CastFactor;
        if (Mathf.Approximately(k, appliedSpeed)) return;
        appliedSpeed = k;
        ApplySpeed();
    }

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
        bool iced = c != null && c.Frozen;
        float depth = c != null && c.Water > 0 && (!iced || swimming) ? grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y : 0f;
        bool afloat = Time.time >= kickedUntil && depth > (swimming ? StopSwimming : swimDepth);
        SetAfloat(afloat);
        if (afloat && iced && !UnderIce) EventLog.Add("Ice closes over you: break it or swim out from under it");
        UnderIce = afloat && iced;
        Diving = afloat && (HoldDive || (controls != null && controls.enabled && controls.Roll.IsPressed()));
        if (!afloat) return;

        Float(depth, cell);
        if (!kick) return;
        kick = false;
        if (HeadUnder || UnderIce) return;
        kickedUntil = Time.time + KickTime;
        SetAfloat(false);
        fall.SetValue(controller, Mathf.Sqrt(swimKick * -2f * baseGravity));
        if (animator != null) animator.SetBool(JumpId, true);
    }

    void Float(float depth, Vector2Int cell)
    {
        float y = transform.position.y;
        float target = y + depth + Swell(cell) - floatDepth + BobHeight * Mathf.Sin(Time.time * 2.4f);
        if (UnderIce) target = Mathf.Min(target, y + depth - body.height * transform.lossyScale.y - IceGap);
        fall.SetValue(controller, Diving ? -diveSpeed : Mathf.Min((target - y) * Buoyancy, riseSpeed));
        if (animator == null) return;
        animator.SetBool(GroundedId, true);
        animator.SetBool(FreeFallId, false);
        animator.SetBool(JumpId, false);
    }

    void Breathe(float surface)
    {
        var head = transform.position + Vector3.up * ((body.center.y + body.height * (HeadAt - 0.5f)) * transform.lossyScale.y);
        HeadUnder = head.y < UnderwaterView.WaterTop(grid, head) || (UnderIce && head.y < surface);
        if (vitals != null) vitals.Breathe(HeadUnder, Time.deltaTime);
    }

    public void Toss(float speed)
    {
        if (fall == null) return;
        kickedUntil = Time.time + KickTime;
        SetAfloat(false);
        fall.SetValue(controller, speed);
        if (animator != null) animator.SetBool(JumpId, true);
    }

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
