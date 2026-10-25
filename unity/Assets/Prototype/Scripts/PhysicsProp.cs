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
    // Angular damping in air and in water (so a floating crate settles rather than spins).
    const float AirSpin = 0.05f, WaterSpin = 1.5f;

    static PhysicsMaterial slick;

    public PropState State { get; private set; }

    Rigidbody body;
    // Its colliders (a body and the feet PropBuilder gives it), and its look, for its size.
    Collider[] shapes;
    Renderer look;
    PhysicsMaterial ownMaterial;
    bool onSlick;
    float halfHeight;
    Collider valleyGround;
    bool inYard;
    GameObject flames;
    int lastTick;
    bool broken;
    NetProp net;

    /// <summary>Online on a client: the host simulates this prop, and this copy only follows it.</summary>
    public bool Puppet => net != null && net.Puppet;

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
        if (grid.valley != null && grid.valley.terrain != null) valleyGround = grid.valley.terrain.GetComponent<TerrainCollider>();
    }

    void OnDestroy()
    {
        if (flames != null) Destroy(flames);
        if (grid != null) grid.RulesChanged -= ReadRules;
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
        var at = grid.CellAt(transform.position);
        var cell = grid.World.At(at.x, at.y);
        StandOnYard(at);
        ReadGround(at, cell);
        RunTicks(at, cell);
        if (flames != null) flames.transform.position = TopCentre();
    }

    // --- what the cell does to it ------------------------------------------------

    // The valley's terrain runs flush under the whole yard (ValleyBuilder), which would floor the
    // pond and the pits; inside the yard the cells' own tiles are the ground, so pass through it.
    void StandOnYard(Vector2Int at)
    {
        bool yard = grid.InYard(at.x, at.y);
        if (valleyGround == null || yard == inYard) return;
        inYard = yard;
        foreach (var s in shapes) Physics.IgnoreCollision(s, valleyGround, yard);
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
        if (Has(events, PropEvents.Burned)) ChemEffects.Request(grid, ChemEffects.At(EffectKind.Burn, at, kind));
        if (Has(events, PropEvents.Landed)) EventLog.Add($"The {Label} drifts back down");
        if (Has(events, PropEvents.Destroyed)) Break(at);
    }

    static bool Has(PropEvents events, PropEvents flag) => (events & flag) != 0;

    // --- what spells and blasts do to it -------------------------------------------

    // As fast as its rules mass says (impulse / mass), whatever its body weighs: a carried
    // item weighs grams but flies off like the lightest crate, not like a bullet.
    public void Push(Vector3 impulse)
    {
        if (State == null || Puppet) return;
        body.AddForce(impulse / (State.Def.Mass * Forces.KgPerMass), ForceMode.VelocityChange);
    }

    /// <summary>Floats for <paramref name="ticks"/> sim ticks, rising a little, then falls.</summary>
    public void Levitate(int ticks)
    {
        // A kinematic body is in someone's hand (Carryable), or a puppet of the host's.
        if (State == null || body.isKinematic || Puppet) return;
        State.Levitate(grid.World.Tick, ticks);
        body.useGravity = false;
        body.linearVelocity += Vector3.up * levitateRise;
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
        else if (State.Burning) ChemEffects.Request(grid, ChemEffects.At(EffectKind.Embers, at, kind));
        ShowBreak(exploded);
        if (net != null && net.Hosting) net.Broke(exploded);
        else Destroy(gameObject);
    }

    /// <summary>Online: it broke on the host. Hidden rather than destroyed, as Netcode wants for objects placed in the scene.</summary>
    public void Vanish()
    {
        broken = true;
        if (flames != null) Destroy(flames);
        flames = null;
        gameObject.SetActive(false);
    }

    /// <summary>How it goes: a blast for a barrel, a line in the log otherwise. Its cells are the chemistry's.</summary>
    public void ShowBreak(bool exploded)
    {
        if (exploded && explosionFx != null) Destroy(Instantiate(explosionFx, transform.position, Quaternion.identity), 4f);
        else if (!exploded && (State == null || !State.Burning) && !(net != null && net.Burning)) EventLog.Add($"The {Label} breaks apart");
    }

    // The blast's cells go to the chemistry; its shove and its damage are physics, so they stay here.
    void Explode(Vector2Int at, ExplosionDef blast)
    {
        ChemEffects.Request(grid, ChemEffects.At(EffectKind.Explode, at, kind));
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
