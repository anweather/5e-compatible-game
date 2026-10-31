using Chem;
using UnityEngine;

// A crate, barrel or boulder that Unity's physics moves and the chemistry
// acts on. Its numbers come from rules/entities.json (kind), or, for a thing
// to carry (Carryable), from rules/items.json, which runs as a light,
// unbreakable entity (ItemDef.AsEntity) with its own mass in kilograms. Every physics
// step it reads the cell under it: slick ground (ice, oil, grease) takes its
// friction away, and water holds up what floats and slows everything down.
// Every sim tick goes to its PropState, which lights it from fire, douses it
// in water and burns its hit points down. At 0 HP it breaks, burns away or,
// for a barrel, explodes.
//
// What it does to the cells (fire it spreads, a blast of burning oil) goes
// through ChemEffects, the one door from physics into the chemistry.
//
// In an inactive chunk it sleeps (kinematic, its ticks held) and wakes as the
// chunk activates (ChemGrid.ChunkSwitched): the view only, the world isn't touched.
//
// Online (NetProp) only the host runs all this; a client's copy is a puppet
// that follows the host's and only shows its flames and its end.
[RequireComponent(typeof(Rigidbody), typeof(Collider), typeof(Renderer))]
public class PhysicsProp : MonoBehaviour, IPushable
{
    public ChemGrid grid;
    [Tooltip("Its id in rules/entities.json (or rules/items.json for a Carryable).")]
    public string kind = EntityIds.Crate;
    [Tooltip("Optional: shown while it burns. A cube stands in.")]
    public GameObject flamesFx;
    [Tooltip("Optional: plays where it explodes.")]
    public GameObject explosionFx;
    [Tooltip("Speed it rises at when levitated, m/s.")]
    public float levitateRise = 1.2f;

    // Linear damping: in air, in water, and while levitating (so it rises and then hovers).
    const float AirDrag = 0.05f, WaterDrag = 2.5f, HoverDrag = 1.2f;
    // How hard the water pulls a floating prop toward the current's speed (per second).
    const float CurrentGrip = 1.5f;
    // Angular damping in air and in water (so a floating crate settles rather than spins).
    const float AirSpin = 0.05f, WaterSpin = 1.5f;

    static PhysicsMaterial slick;

    public PropState State { get; private set; }

    Rigidbody body;
    // Its colliders (a body and the feet PropBuilder gives its prefab), and its look, for its size.
    Collider[] shapes;
    Renderer look;
    PhysicsMaterial ownMaterial;
    bool onSlick;
    float halfHeight;
    GameObject flames;
    int lastTick;
    bool broken;
    NetProp net;
    // Asleep with its chunk: kinematic, holding the velocity it had. Home is the chunk it was last seen in.
    bool asleep;
    Vector3 heldVelocity, heldSpin;
    Vector2Int home;

    /// <summary>Online on a client: the host simulates this prop, and this copy only follows it.</summary>
    public bool Puppet => net != null && net.Puppet;

    /// <summary>Its stable id from world/things.json (ThingSpawner), or None for a prop made some other way.</summary>
    public ThingId Thing => net != null ? net.Id : ThingId.None;

    /// <summary>Its chunk is inactive, so it's held where it is until the chunk activates.</summary>
    public bool Asleep => asleep;

    string Label => State.Def.Name.ToLowerInvariant();

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        shapes = GetComponents<Collider>();
        look = GetComponent<Renderer>();
        ownMaterial = shapes[0].sharedMaterial;
        TryGetComponent(out net);
    }

    void Start()
    {
        var rules = grid != null && grid.World != null ? grid.World.Rules : null;
        var item = rules?.Item(kind);
        var def = rules?.Entity(kind) ?? item?.AsEntity();
        if (def == null)
        {
            Debug.LogWarning($"{name}: no entity or item '{kind}' in rules/, or no grid");
            enabled = false;
            return;
        }
        State = new PropState(def);
        body.mass = item != null ? (float)item.Mass : def.Mass * Forces.KgPerMass;
        halfHeight = look.bounds.extents.y;
        lastTick = grid.World.Tick;
        grid.RulesChanged += ReadRules;
        grid.ChunkSwitched += ChunkSwitched;
        grid.WorldReset += Wake;
        home = grid.ChunkAt(transform.position);
        if (InInactiveChunk(home)) Sleep();
    }

    void OnDestroy()
    {
        if (flames != null) Destroy(flames);
        if (grid == null) return;
        grid.RulesChanged -= ReadRules;
        grid.ChunkSwitched -= ChunkSwitched;
        grid.WorldReset -= Wake;
    }

    // --- sleeping with its chunk ----------------------------------------------------------

    void ChunkSwitched(ChunkSwitch s)
    {
        if (State == null || s.CX != home.x || s.CY != home.y) return;
        if (s.Active) Wake();
        else Sleep();
    }

    // Off the edge of the world counts as active: a prop falling off it shouldn't hang in the air.
    bool InInactiveChunk(Vector2Int chunk) =>
        grid.World.HasChunk(chunk.x, chunk.y) && !grid.IsChunkActive(chunk.x, chunk.y);

    // A body already kinematic is in a hand (Carryable) or a puppet: someone else moves it.
    void Sleep()
    {
        if (asleep || Puppet || body.isKinematic) return;
        PerfCounts.Add("prop.sleep");
        asleep = true;
        heldVelocity = body.linearVelocity;
        heldSpin = body.angularVelocity;
        body.isKinematic = true;
    }

    // Its ticks were held too, so it picks up from now rather than burning through the gap.
    void Wake()
    {
        if (!asleep) return;
        PerfCounts.Add("prop.wake");
        asleep = false;
        body.isKinematic = false;
        body.linearVelocity = heldVelocity;
        body.angularVelocity = heldSpin;
        if (grid.World != null) lastTick = grid.World.Tick;
    }

    // A live mod changed the rules: take this kind's new numbers (hit points, mass, what it does
    // when it breaks), keeping the damage it has taken. A kind the new rules dropped keeps its old ones.
    void ReadRules()
    {
        var rules = grid.World.Rules;
        var item = rules.Item(kind);
        var def = rules.Entity(kind) ?? item?.AsEntity();
        if (def == null || State == null) return;
        State.UseDef(def);
        body.mass = item != null ? (float)item.Mass : def.Mass * Forces.KgPerMass;
    }

    void FixedUpdate()
    {
        if (State == null || grid.World == null) return;
        if (Puppet)
        {
            if (flames != null) flames.transform.position = TopCentre();
            return;
        }
        if (asleep)
        {
            // Something else took it in hand (Carryable) and let it go: it's awake again.
            if (body.isKinematic) return;
            asleep = false;
        }
        // Pushed or rolled into an inactive chunk: it sleeps there.
        var chunk = grid.ChunkAt(transform.position);
        if (chunk != home)
        {
            home = chunk;
            if (InInactiveChunk(home))
            {
                Sleep();
                return;
            }
        }
        var at = grid.CellAt(transform.position);
        var cell = grid.World.At(at.x, at.y);
        ReadGround(at, cell);
        RunTicks(at, cell);
        if (flames != null) flames.transform.position = TopCentre();
    }

    // --- what the cell does to it ------------------------------------------------

    void ReadGround(Vector2Int at, Cell cell)
    {
        var world = grid.World;
        bool hovering = State.Levitating(world.Tick);
        body.useGravity = !hovering;
        SetSlick(!hovering && cell != null && world.IsSlippery(cell));
        if (hovering)
        {
            body.linearDamping = HoverDrag;
            return;
        }
        bool wet = cell != null && cell.Water > 0 && !cell.Frozen && Buoy(at);
        body.linearDamping = wet ? WaterDrag : AirDrag;
        body.angularDamping = wet ? WaterSpin : AirSpin;
        if (wet) Drift();
    }

    // Afloat in a current (WaterCurrents): pulled along until it moves with the water, so a crate
    // floats down the river and the surf washes things up the beach.
    void Drift()
    {
        var currents = WaterCurrents.Instance;
        if (currents == null) return;
        var flow = currents.At(transform.position);
        if (flow.sqrMagnitude < 1e-4f) return;
        var v = body.linearVelocity;
        var slip = flow - new Vector3(v.x, 0f, v.z);
        body.AddForce(slip * (body.mass * CurrentGrip));
    }

    void SetSlick(bool slick)
    {
        if (slick == onSlick) return;
        onSlick = slick;
        foreach (var s in shapes) s.sharedMaterial = slick ? Slick : ownMaterial;
    }

    // Lift in proportion to how much of it is under the surface; floaters bob, sinkers settle slowly.
    bool Buoy(Vector2Int at)
    {
        // Measured upright (see Start), so a tumbling crate's growing bounding box doesn't add lift.
        float bottom = transform.position.y - halfHeight;
        float under = (float)PropState.Submerged(bottom, 2f * halfHeight, grid.WaterSurfaceY(at.x, at.y));
        if (under <= 0f) return false;
        body.AddForce(-Physics.gravity * (body.mass * (float)State.Lift * under));
        return true;
    }

    // Each sim tick since the last physics step goes to the rules once.
    void RunTicks(Vector2Int at, Cell cell)
    {
        var world = grid.World;
        // Going online starts a fresh world at tick 0 (NetChem); count from there.
        if (lastTick > world.Tick) lastTick = world.Tick;
        while (lastTick < world.Tick && !broken)
        {
            lastTick++;
            Handle(State.Step(cell, lastTick, world.Rules.Sim.BurnDamageEvery), at);
        }
    }

    void Handle(PropEvents events, Vector2Int at)
    {
        if (Has(events, PropEvents.Ignited)) ShowBurning(true, announce: true);
        if (Has(events, PropEvents.Doused)) ShowBurning(false, announce: true);
        if (Has(events, PropEvents.Burned)) ChemEffects.Request(grid, ChemEffects.By(this, EffectKind.Burn, at));
        if (Has(events, PropEvents.Landed)) EventLog.Add($"The {Label} drifts back down");
        if (Has(events, PropEvents.Destroyed)) Break(at);
    }

    static bool Has(PropEvents events, PropEvents flag) => (events & flag) != 0;

    // --- what spells and blasts do to it -------------------------------------------

    // As fast as its rules mass says (impulse / mass), whatever its body weighs: a carried
    // item weighs grams but flies off like the lightest crate, not like a bullet.
    public void Push(Vector3 impulse)
    {
        if (State == null || Puppet || asleep) return;
        body.AddForce(impulse / (State.Def.Mass * Forces.KgPerMass), ForceMode.VelocityChange);
    }

    /// <summary>
    /// Carried along by something rolling under it (a wave, on the host, as Push): it gets only the speed it
    /// lacks along the way, so a prop that's already riding isn't thrown on ever faster.
    /// </summary>
    public void Ride(Vector3 velocity)
    {
        if (State == null || Puppet || asleep) return;
        var way = velocity.normalized;
        float lacks = velocity.magnitude - Vector3.Dot(body.linearVelocity, way);
        if (lacks > 0f) body.AddForce(Forces.Thrown(way) * lacks, ForceMode.VelocityChange);
    }

    /// <summary>
    /// Pushed by one wizard (a punch): only that wizard's machine knows, so online it goes to the host's
    /// physics as a kick does (NetProp), at the same speed as Push.
    /// </summary>
    public void Shove(Vector3 impulse)
    {
        if (net == null || !net.IsSpawned || State == null) Push(impulse);
        else net.Kick(impulse / (State.Def.Mass * Forces.KgPerMass) * body.mass);
    }

    /// <summary>Floats for <paramref name="ticks"/> sim ticks, rising a little (<paramref name="rise"/> times as fast for a charged cast), then falls.</summary>
    public void Levitate(int ticks, float rise = 1f)
    {
        // A kinematic body is in someone's hand (Carryable), or a puppet of the host's.
        if (State == null || body.isKinematic || Puppet) return;
        State.Levitate(grid.World.Tick, ticks);
        body.useGravity = false;
        body.linearVelocity += Vector3.up * (levitateRise * rise);
        EventLog.Add($"The {Label} floats up");
    }

    /// <summary>Takes damage; at 0 HP it breaks (or explodes).</summary>
    public void Hurt(int amount)
    {
        if (State != null && !Puppet && State.Damage(amount)) Break(grid.CellAt(transform.position));
    }

    void Break(Vector2Int at)
    {
        if (broken) return;
        broken = true;
        bool exploded = State.Def.Explodes != null;
        if (exploded) Explode(at, State.Def.Explodes);
        else if (State.Burning) ChemEffects.Request(grid, ChemEffects.By(this, EffectKind.Embers, at));
        ShowBreak(exploded);
        if (net != null && net.Hosting) net.Broke(exploded);
        else Destroy(gameObject);
    }

    /// <summary>Online: it broke on the host. Hidden at once; Netcode destroys it as it despawns.</summary>
    public void Vanish()
    {
        broken = true;
        if (flames != null) Destroy(flames);
        flames = null;
        gameObject.SetActive(false);
    }

    void OnCollisionEnter(Collision hit)
    {
        if (!Puppet) WindowPanes.Struck(grid, hit);
    }

    /// <summary>
    /// How it goes: a blast for a barrel, a spray of shards for something brittle (glass: a glow ball,
    /// ShardBurst), a line in the log otherwise. Its cells are the chemistry's.
    /// </summary>
    public void ShowBreak(bool exploded)
    {
        GameSounds.Play(exploded ? SoundIds.Explosion : SoundIds.Break, transform.position);
        if (exploded && explosionFx != null) Destroy(Instantiate(explosionFx, transform.position, Quaternion.identity), 4f);
        if (!exploded && Shatters(out var tint))
        {
            ShardBurst.At(transform.position, tint);
            EventLog.Add($"The {Label} shatters");
        }
        else if (!exploded && (State == null || !State.Burning) && !(net != null && net.Burning)) EventLog.Add($"The {Label} breaks apart");
    }

    /// <summary>
    /// Whether it's brittle (its material shatters: glass), and the tint of its shards: its light's
    /// colour for a glow ball, else white.
    /// </summary>
    public bool Shatters(out Color tint)
    {
        tint = Color.white;
        var rules = grid != null && grid.World != null ? grid.World.Rules : null;
        if (rules == null || !rules.ItemHas(kind, "brittle")) return false;
        var light = rules.Item(kind).Light;
        if (light != null) ColorUtility.TryParseHtmlString(light.Color, out tint);
        return true;
    }

    // The blast's cells go to the chemistry; its shove and its damage are physics, so they stay here.
    void Explode(Vector2Int at, ExplosionDef blast)
    {
        ChemEffects.Request(grid, ChemEffects.By(this, EffectKind.Explode, at));
        float reach = (blast.Radius + 1) * grid.cellSize;
        Forces.Radial(transform.position, reach, blast.Push * Forces.ImpulsePerPush, transform);
        foreach (var other in Forces.InSphere<PhysicsProp>(transform.position, reach, transform)) other.Hurt(Roll(blast.Damage));
    }

    // [dice, sides], as the rules write damage. Physics is local (D4), so Unity's random is fine here.
    static int Roll(int[] dice)
    {
        if (dice.Length < 2) return 0;
        int total = 0;
        for (int i = 0; i < dice[0]; i++) total += Random.Range(1, dice[1] + 1);
        return total;
    }

    // --- looks ---------------------------------------------------------------------

    /// <summary>Flames on or off; <paramref name="announce"/> says so in the log.</summary>
    public void ShowBurning(bool on, bool announce)
    {
        if (on == (flames != null)) return;
        ShowFlames(on);
        if (announce && State != null) EventLog.Add(on ? $"The {Label} catches fire" : $"The {Label} hisses out in the water");
    }

    void ShowFlames(bool on)
    {
        if (on == (flames != null)) return;
        if (!on)
        {
            Destroy(flames);
            flames = null;
            return;
        }
        // Not parented: it follows the top of the prop (FixedUpdate) but doesn't tumble with it.
        flames = flamesFx != null ? Instantiate(flamesFx) : FallbackFlames();
        flames.transform.position = TopCentre();
    }

    Vector3 TopCentre()
    {
        var bounds = look.bounds;
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    static GameObject FallbackFlames()
    {
        var cube = Primitives.Visual(PrimitiveType.Cube, "Flames");
        cube.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f);
        UrpMaterials.SetColor(cube.GetComponent<Renderer>(), new MaterialPropertyBlock(), new Color(1f, 0.4f, 0.05f));
        cube.AddComponent<Flicker>();
        return cube;
    }

    // Almost no friction, and the lower of the two surfaces wins, so it slides on anything.
    static PhysicsMaterial Slick
    {
        get
        {
            if (slick == null)
                slick = new PhysicsMaterial("Slick")
                {
                    dynamicFriction = 0.02f,
                    staticFriction = 0.02f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            return slick;
        }
    }
}
