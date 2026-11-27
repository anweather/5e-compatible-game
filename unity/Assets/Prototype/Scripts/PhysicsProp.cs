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

    const float AirDrag = 0.05f, WaterDrag = 2.5f, HoverDrag = 1.2f;
    const float CurrentGrip = 1.5f;
    const float AirSpin = 0.05f, WaterSpin = 1.5f;

    static PhysicsMaterial slick;
    static readonly Registry<PhysicsProp> registry = new();

    public static PhysicsProp[] All => registry.All();

    public static PhysicsProp[] Near(Vector2Int from, Vector2Int to) => registry.Near(from, to);

    public PropState State { get; private set; }

    Rigidbody body;
    Collider[] shapes;
    Renderer look;
    PhysicsMaterial ownMaterial;
    bool onSlick;
    float halfHeight;
    GameObject flames;
    int lastTick;
    bool broken;
    NetProp net;
    bool asleep;
    Vector3 heldVelocity, heldSpin;
    Vector2Int home;

    public bool Puppet => net != null && net.Puppet;

    public ThingId Thing => net != null ? net.Id : ThingId.None;

    public bool FromMod => grid.World.ThingsNow.Find(Thing)?.FromMod == true;

    public bool Asleep => asleep;

    public bool Held => body.isKinematic && !asleep && !Puppet;

    public void BackAsBuilt(Vector3 at, Quaternion turn)
    {
        if (State != null) State = new PropState(State.Def);
        ShowFlames(false);
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        heldVelocity = heldSpin = Vector3.zero;
        transform.SetPositionAndRotation(at, turn);
        body.position = at;
        body.rotation = turn;
        Physics.SyncTransforms();
        Place();
        if (State == null) return;
        home = grid.ChunkAt(at);
        if (InInactiveChunk(home)) Sleep();
        else Wake();
    }

    string Label => State.Def.Name.ToLowerInvariant();

    void Awake()
    {
        registry.Join(this);
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
        Place();
        grid.RulesChanged += ReadRules;
        grid.ChunkSwitched += ChunkSwitched;
        grid.WorldReset += Wake;
        home = grid.ChunkAt(transform.position);
        if (InInactiveChunk(home)) Sleep();
    }

    void OnDestroy()
    {
        registry.Leave(this);
        if (flames != null) Destroy(flames);
        if (grid == null) return;
        grid.RulesChanged -= ReadRules;
        grid.ChunkSwitched -= ChunkSwitched;
        grid.WorldReset -= Wake;
    }

    void ChunkSwitched(ChunkSwitch s)
    {
        if (State == null || s.CX != home.x || s.CY != home.y) return;
        if (s.Active) Wake();
        else Sleep();
    }

    bool InInactiveChunk(Vector2Int chunk) =>
        grid.World.HasChunk(chunk.x, chunk.y) && !grid.IsChunkActive(chunk.x, chunk.y);

    void Sleep()
    {
        if (asleep || Puppet || body.isKinematic) return;
        PerfCounts.Add("prop.sleep");
        asleep = true;
        heldVelocity = body.linearVelocity;
        heldSpin = body.angularVelocity;
        body.isKinematic = true;
    }

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

    void ReadRules()
    {
        var rules = grid.World.Rules;
        var item = rules.Item(kind);
        var def = rules.Entity(kind) ?? item?.AsEntity();
        if (def == null || State == null) return;
        State.UseDef(def);
        body.mass = item != null ? (float)item.Mass : def.Mass * Forces.KgPerMass;
    }

    void Place()
    {
        if (grid != null) registry.Place(this, grid.ChunkAt(transform.position));
    }

    void OnEnable() => Place();

    void OnDisable() => registry.Unplace(this);

    void FixedUpdate()
    {
        Place();
        if (State == null || grid.World == null) return;
        if (Puppet)
        {
            if (flames != null) flames.transform.position = TopCentre();
            return;
        }
        if (asleep)
        {
            if (body.isKinematic) return;
            asleep = false;
        }
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

    void Drift()
    {
        var currents = LandFaces.Currents;
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

    bool Buoy(Vector2Int at)
    {
        float bottom = transform.position.y - halfHeight;
        float under = (float)PropState.Submerged(bottom, 2f * halfHeight, grid.WaterSurfaceY(at.x, at.y));
        if (under <= 0f) return false;
        body.AddForce(-Physics.gravity * (body.mass * (float)State.Lift * under));
        return true;
    }

    void RunTicks(Vector2Int at, Cell cell)
    {
        var world = grid.World;
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

    public void Push(Vector3 impulse)
    {
        if (State == null || Puppet || asleep) return;
        body.AddForce(impulse / (State.Def.Mass * Forces.KgPerMass), ForceMode.VelocityChange);
    }

    public void Ride(Vector3 velocity)
    {
        if (State == null || Puppet || asleep) return;
        var way = velocity.normalized;
        float lacks = velocity.magnitude - Vector3.Dot(body.linearVelocity, way);
        if (lacks > 0f) body.AddForce(Forces.Thrown(way) * lacks, ForceMode.VelocityChange);
    }

    public void Shove(Vector3 impulse)
    {
        if (net == null || !net.IsSpawned || State == null) Push(impulse);
        else net.Kick(impulse / (State.Def.Mass * Forces.KgPerMass) * body.mass);
    }

    public void Levitate(int ticks, float rise = 1f)
    {
        if (State == null || body.isKinematic || Puppet) return;
        State.Levitate(grid.World.Tick, ticks);
        body.useGravity = false;
        body.linearVelocity += Vector3.up * (levitateRise * rise);
        EventLog.Add($"The {Label} floats up");
    }

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

    public bool Shatters(out Color tint)
    {
        tint = Color.white;
        var rules = grid != null && grid.World != null ? grid.World.Rules : null;
        if (rules == null || !rules.ItemHas(kind, "brittle")) return false;
        var light = rules.Item(kind).Light;
        if (light != null) ColorUtility.TryParseHtmlString(light.Color, out tint);
        return true;
    }

    void Explode(Vector2Int at, ExplosionDef blast)
    {
        ChemEffects.Request(grid, ChemEffects.By(this, EffectKind.Explode, at));
        float reach = (blast.Radius + 1) * grid.cellSize;
        Forces.Radial(transform.position, reach, blast.Push * Forces.ImpulsePerPush, transform);
        foreach (var other in Forces.InSphere<PhysicsProp>(transform.position, reach, transform)) other.Hurt(Roll(blast.Damage));
    }

    static int Roll(int[] dice)
    {
        if (dice.Length < 2) return 0;
        int total = 0;
        for (int i = 0; i < dice[0]; i++) total += Random.Range(1, dice[1] + 1);
        return total;
    }

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
        flames = flamesFx != null ? Instantiate(flamesFx) : FallbackFlames();
        flames.transform.position = TopCentre();
        if (grid != null && grid.World != null)
        {
            var cell = grid.CellAt(transform.position);
            grid.FireTint(cell.x, cell.y).Dress(flames, null);
        }
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
