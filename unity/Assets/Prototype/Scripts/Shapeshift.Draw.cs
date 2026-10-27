using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// Shapeshift's look, on every machine: the wizard's own model hidden and the
// animal's body drawn where it stands, as ValleyFauna draws its animals
// (Punchy's CreatureAnimator, instanced with the creature material). The body
// follows the wizard's transform (the owner's movement, or the synced
// NetworkTransform on other screens): its heading, and a walk or run gait
// from how fast it goes, the stride advanced by the distance covered so feet
// don't slide. The ground it stands on is what's really under the wizard
// (rays down, so a bridge or a roof too), lifted with it when it jumps; in
// deep water it swims with its back at the surface.
public partial class Shapeshift : ICreatureGround
{
    const float RayUp = 0.7f, RayDown = 3f, SpeedSmoothing = 10f;
    static readonly RaycastHit[] Hits = new RaycastHit[16];

    readonly CreatureAnimator animator = new();
    CreatureDrawer drawer;
    SegmentInstance[] instances = new SegmentInstance[64];
    Renderer[] hidden = System.Array.Empty<Renderer>();
    CreatureState state;
    Vector3 lastPosition;
    float lastYaw, lift;

    /// <summary>Whether the wizard's own model is hidden (an animal is drawn instead).</summary>
    public bool ModelHidden { get; private set; }
    /// <summary>The animal body as last drawn: where, heading, speed and gait phase.</summary>
    public CreatureState Drawn => state;
    /// <summary>Frames the animal body has been drawn in this shape.</summary>
    public int DrawnFrames { get; private set; }

    void OnDestroy() => drawer?.Dispose();

    // The wizard's renderers off (not their enabled flag, which other parts may use) while an animal stands in.
    void HideModel(bool hide)
    {
        foreach (var r in hidden)
            if (r != null) r.forceRenderingOff = false;
        hidden = hide ? GetComponentsInChildren<Renderer>() : System.Array.Empty<Renderer>();
        foreach (var r in hidden) r.forceRenderingOff = true;
        ModelHidden = hide;
    }

    void StartDrawing()
    {
        DrawnFrames = 0;
        if (!Shifted) return;
        var b = Fauna.Body(Species);
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        state = new CreatureState
        {
            Position = transform.position,
            Yaw = lastYaw,
            Scale = (b.Size.x + b.Size.y) * 0.5f,
            Mode = CreatureMode.Puppet,
            Seed = 1u,
        };
        if (instances.Length < b.Segments.Length) instances = new SegmentInstance[Mathf.NextPowerOfTwo(b.Segments.Length)];
        drawer ??= new CreatureDrawer(Fauna.material);
    }

    // After everything has moved the wizard this frame: pose the animal there and draw it.
    void LateUpdate()
    {
        if (requested != Pack(Species, Coat)) Show(requested);
        if (!Shifted || Grid == null || Grid.World == null) return;
        var b = Fauna != null ? Fauna.Body(Species) : null;
        if (b == null) return;
        Follow(b, Mathf.Max(Time.deltaTime, 1e-4f));
        animator.Evaluate(b, state, Coat, this, instances, 0);
        drawer.Draw(instances, b.Segments.Length, transform.position);
        DrawnFrames++;
    }

    // The body's state from the wizard's transform: position, heading, speed, gait and tail sway.
    void Follow(CreatureBody b, float dt)
    {
        var at = transform.position;
        var moved = at - lastPosition;
        moved.y = 0f;
        // A blink or a respawn is a jump, not a sprint.
        float speed = moved.magnitude < 5f ? moved.magnitude / dt : 0f;
        float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        state.YawRate = Mathf.DeltaAngle(lastYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt;
        state.Yaw = yaw;
        state.Speed = Mathf.Lerp(state.Speed, speed, 1f - Mathf.Exp(-SpeedSmoothing * dt));
        state.Position = at;
        state.Clock += dt;
        float run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), state.Speed / state.Scale);
        float stride = math.max(math.lerp(b.WalkStride, b.RunStride, run) * state.Scale, 0.05f);
        state.Phase = math.frac(state.Phase + state.Speed * dt / stride);
        state.Sway += (-state.YawRate * 0.35f - state.Sway) * math.min(1f, dt * 4f);
        lastPosition = at;
        lastYaw = yaw;
        lift = Floor(b, at) - Under(at.x, at.z, at.y);
    }

    // Where the animal's feet go under the wizard: its feet, or swimming, low enough to keep its back at the water's surface.
    float Floor(CreatureBody b, Vector3 at)
    {
        var cell = Grid.CellAt(at);
        var c = Grid.World.At(cell.x, cell.y);
        if (c == null || c.Water <= 0 || c.Frozen) return at.y;
        float surface = Grid.WaterSurfaceY(cell.x, cell.y);
        float stand = b.StandHeight * state.Scale;
        return surface - at.y > stand ? surface - stand * 0.85f : at.y;
    }

    // What's under a point near the wizard's feet: the first thing a ray down meets (not the wizard), else the terrain.
    float Under(float x, float z, float feet)
    {
        var from = new Vector3(x, feet + RayUp, z);
        int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, RayUp + RayDown, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue, y = 0f;
        for (int i = 0; i < n; i++)
        {
            if (Hits[i].distance >= best || Hits[i].collider.transform.IsChildOf(transform)) continue;
            best = Hits[i].distance;
            y = Hits[i].point.y;
        }
        return best < float.MaxValue ? y : Grid.GroundAt(from);
    }

    // --- the ground the animator stands the body on (ICreatureGround) -------------------

    public float Height(float2 xz) => Under(xz.x, xz.y, transform.position.y) + lift;

    public bool Walkable(float2 xz) => true;

    public bool Water(float2 xz) => false;
}
