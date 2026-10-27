using UnityEngine;

// One platform of the climbing tower south-west of the yard (TowerCourse,
// built by ValleyTower). Its root holds the box the wizard stands on; the
// child "look" is what is drawn, so a collapsing platform can shake and fall
// without moving its box first.
//
// - Static: stands still.
// - Moving: slides back and forth along the ring, rides up and down, or goes
//   round in a small circle. Where it is is a function of the course's shared
//   clock (the chemistry's tick), so every player sees it in the same place.
//   The local wizard standing on it is carried along.
// - Collapsing: half a second after a wizard lands on it, it shakes, drops
//   away (its box gone) and comes back four seconds later. Started by whoever
//   lands on it as each machine sees them, so not tied to the shared clock.
// - Phasing: solid for part of its cycle on the shared clock, then a ghost
//   that can't be stood on; it flickers just before it fades.
[DefaultExecutionOrder(-50)]
public class TowerPlatform : MonoBehaviour
{
    public enum Kind { Static, Moving, Collapsing, Phasing }
    public enum Motion { Slide, Lift, Round }

    public TowerCourse course;
    public Kind kind;
    public Motion motion;
    [Tooltip("The drawn block (the root's child).")]
    public Transform look;
    [Tooltip("How far a moving platform goes from its home (m), along the axis, up, or round.")]
    public float amplitude = 1.2f;
    [Tooltip("A whole cycle (back and forth, round, or solid and ghost), in seconds of the shared clock.")]
    public float period = 4f;
    [Tooltip("Where in its cycle the platform starts (0 to 1), so neighbours don't move together.")]
    public float phase;
    [Tooltip("A sliding platform's way, on the ground plane (along the ring).")]
    public Vector3 axis = Vector3.right;
    [Tooltip("A phasing platform: the part of its cycle it is solid.")]
    public float solidPart = 0.6f;
    public Material solid, ghost;

    /// <summary>How long a collapsing platform shakes before it falls, and how long it is gone (s).</summary>
    public const float ShakeSeconds = 0.5f, GoneSeconds = 4f;
    const float FallSpeed = 9f, FlickerBefore = 0.6f;

    BoxCollider box;
    Renderer drawn;
    Vector3 home;
    // A collapse: when it started (local time), or below 0 while it stands.
    float collapsedAt = -1f;

    /// <summary>Whether it can be stood on now (its box is on).</summary>
    public bool Solid => box != null && box.enabled;

    /// <summary>A collapsing platform that has dropped away and not come back yet.</summary>
    public bool Fallen => collapsedAt >= 0f && Time.time - collapsedAt >= ShakeSeconds;

    /// <summary>The top of its box, in the world.</summary>
    public float Top => transform.position.y + (box != null ? box.size.y / 2f : 0f);

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        home = transform.position;
        if (look != null) drawn = look.GetComponent<Renderer>();
    }

    void Update()
    {
        if (course == null) return;
        switch (kind)
        {
            case Kind.Moving: Move(); break;
            case Kind.Collapsing: Collapse(); break;
            case Kind.Phasing: Phase(); break;
            default: break;
        }
    }

    // Where in its cycle (0 to 1) the shared clock has it.
    float Cycle() => (float)((course.Now / period + phase) % 1.0);

    void Move()
    {
        float a = Cycle() * Mathf.PI * 2f;
        var offset = motion switch
        {
            Motion.Lift => Vector3.up * (amplitude * Mathf.Sin(a)),
            Motion.Round => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * amplitude,
            _ => axis.normalized * (amplitude * Mathf.Sin(a)),
        };
        var delta = home + offset - transform.position;
        if (delta.sqrMagnitude < 1e-10f) return;
        var rider = course.LocalRider;
        bool carry = rider != null && rider.enabled && StandingOn(rider.transform.position);
        // Going up, the wizard is lifted first so the box doesn't come up through its feet; going down, the box goes first.
        if (carry && delta.y > 0f) rider.Move(delta);
        transform.position = home + offset;
        Physics.SyncTransforms();
        if (carry && delta.y <= 0f) rider.Move(delta);
    }

    void Collapse()
    {
        if (collapsedAt < 0f)
        {
            if (course.AnyWizardOn(this)) collapsedAt = Time.time;
            return;
        }
        float t = Time.time - collapsedAt;
        if (t < ShakeSeconds)
        {
            // A shake that grows toward the fall.
            float k = 0.06f * t / ShakeSeconds;
            look.localPosition = new Vector3(Random.Range(-k, k), Random.Range(-k, k), Random.Range(-k, k));
            return;
        }
        float down = t - ShakeSeconds;
        if (down < GoneSeconds)
        {
            box.enabled = false;
            look.localPosition = Vector3.down * (0.5f * FallSpeed * down * down);
            drawn.enabled = down < 1.5f;
            return;
        }
        collapsedAt = -1f;
        look.localPosition = Vector3.zero;
        drawn.enabled = true;
        box.enabled = true;
    }

    void Phase()
    {
        float c = Cycle();
        bool on = c < solidPart;
        if (box.enabled != on) box.enabled = on;
        // Flickers in its last moments before it fades.
        float left = (solidPart - c) * period;
        bool flicker = on && left < FlickerBefore && Mathf.Repeat(Time.time * 8f, 1f) < 0.5f;
        var mat = on && !flicker ? solid : ghost;
        if (drawn.sharedMaterial != mat) drawn.sharedMaterial = mat;
    }

    /// <summary>Whether feet at <paramref name="feet"/> stand on this platform's top (its box, a little either way).</summary>
    public bool StandingOn(Vector3 feet)
    {
        if (box == null || !box.enabled) return false;
        var local = transform.InverseTransformPoint(feet) - box.center;
        var half = box.size / 2f;
        const float reach = 0.3f;
        return Mathf.Abs(local.x) < half.x + reach && Mathf.Abs(local.z) < half.z + reach
            && local.y > half.y - 0.25f && local.y < half.y + 0.35f;
    }
}
