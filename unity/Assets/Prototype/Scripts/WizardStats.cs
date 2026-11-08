using System.Reflection;
using Chem;
using UnityEngine;

// A player's stats on their wizard (docs/RULES_MCP.md "Player stats", PlayerStats.cs): how fast it
// moves, how big it is, how high it jumps and how many jumps it has before it lands, read from the
// rules in force for the wizard's slot (0 solo and the host, then each in join order) whenever they
// change, so a mod ("make me bigger", set_player) takes hold on every peer at the rule switch.
//
// - speed times every base speed SurfaceEffects keeps (the wizard's own and an animal's), so
//   wading, slows and Shapeshift stack on top;
// - size scales the whole wizard: its model, its CharacterController's capsule and the camera root
//   with it, the step it climbs without jumping and how deep the water must be to float it. It only
//   grows where there's room to stand, and tries again a moment later where there isn't;
// - jump times the Starter Assets JumpHeight and DoubleJump's height;
// - jumps - 1 is how many air jumps DoubleJump allows.
//
// A wizard's body is its own peer's (D4): nothing shared reads any of this. Every copy draws at its
// slot's size; only the copy that's driven (solo, or the owner online) has the rest to change. While
// shapeshifted, the jump, the step and the swimming depth wait for the wizard's own shape, since
// Shapeshift keeps and restores them.
[DefaultExecutionOrder(-10)]
public class WizardStats : MonoBehaviour
{
    // How long a size that doesn't fit waits before it tries again (s).
    const float RetryRoom = 0.5f;
    static readonly Collider[] Overlaps = new Collider[16];

    CharacterController body;
    SurfaceEffects surface;
    DoubleJump airJumps;
    Shapeshift shape;
    SpellCaster caster;
    Component mover;
    FieldInfo jumpHeight;
    Vector3 scale;
    float jump, airJump, step, swimDepth, floatDepth;
    bool known;

    Rules seen;
    int seenSlot = -1;
    float roomAt;
    bool jumpWaits;

    /// <summary>The stats this wizard has now (the size the last that fitted).</summary>
    public PlayerStats Applied { get; private set; } = PlayerStats.Normal;

    /// <summary>The stats the rules give this wizard's slot, which Applied catches up to.</summary>
    public PlayerStats Wanted { get; private set; } = PlayerStats.Normal;

    /// <summary>True while a bigger size waits for room to stand.</summary>
    public bool NoRoom { get; private set; }

    void Awake()
    {
        TryGetComponent(out body);
        TryGetComponent(out surface);
        TryGetComponent(out airJumps);
        TryGetComponent(out shape);
        TryGetComponent(out caster);
        mover = GetComponent(WizardRig.ControllerComponent);
        jumpHeight = WizardRig.Field(mover, "JumpHeight", isPublic: true);
    }

    // The wizard as it's made, read once its other parts have woken (SurfaceEffects raises the step).
    void Know()
    {
        if (known) return;
        known = true;
        scale = transform.localScale;
        if (jumpHeight != null) jump = (float)jumpHeight.GetValue(mover);
        if (airJumps != null) airJump = airJumps.height;
        if (body != null) step = body.stepOffset;
        if (surface != null) (swimDepth, floatDepth) = (surface.swimDepth, surface.floatDepth);
    }

    void Update()
    {
        var rules = ChemGrid.Current?.Rules;
        if (rules == null) return;
        int slot = ChemGrid.SlotOf(caster);
        if (!ReferenceEquals(rules, seen) || slot != seenSlot)
        {
            (seen, seenSlot) = (rules, slot);
            Wanted = rules.Sim.Player.Of(slot);
        }
        bool sizeLate = Wanted.Size.CompareTo(Applied.Size) != 0 && Time.time >= roomAt;
        bool restLate = Wanted.Speed.CompareTo(Applied.Speed) != 0 || Wanted.Jumps != Applied.Jumps || Wanted.Jump.CompareTo(Applied.Jump) != 0;
        if (sizeLate || restLate || (jumpWaits && !Shifted)) Apply(Wanted);
    }

    bool Shifted => shape != null && shape.Shifted;

    // A copy that isn't driven here (another player's, online) only draws at its size.
    bool Driven => !TryGetComponent<NetWizard>(out var net) || !net.IsSpawned || net.IsOwner;

    void Apply(PlayerStats want)
    {
        Know();
        float size = (float)want.Size;
        if (Driven && size > (float)Applied.Size && !RoomFor(size))
        {
            NoRoom = true;
            roomAt = Time.time + RetryRoom;
            size = (float)Applied.Size;
        }
        else NoRoom = false;

        transform.localScale = scale * size;
        Physics.SyncTransforms();
        if (surface != null) surface.SpeedStat = (float)want.Speed;
        if (airJumps != null) airJumps.AirJumps = Mathf.Max(0, want.Jumps - 1);
        jumpWaits = Shifted;
        if (!jumpWaits) Body(want, size);
        Applied = new PlayerStats(want.Speed, size, want.Jump, want.Jumps);
    }

    // What Shapeshift keeps of the wizard's own shape: the jump, the step and the swimming depth.
    void Body(PlayerStats want, float size)
    {
        float k = (float)want.Jump;
        if (jumpHeight != null) jumpHeight.SetValue(mover, jump * k);
        if (airJumps != null) airJumps.height = airJump * k;
        // The capsule scales with the transform; its step doesn't (it's in metres), so it's scaled here.
        if (body != null) body.stepOffset = Mathf.Min(step * size, body.height * transform.lossyScale.y - 0.05f);
        if (surface != null) (surface.swimDepth, surface.floatDepth) = (swimDepth * size, floatDepth * size);
    }

    // Room for the capsule at a size where the wizard stands: nothing but its own parts in it.
    bool RoomFor(float size)
    {
        if (body == null) return true;
        float k = scale.y * size;
        float r = body.radius * k;
        float feet = transform.position.y + (body.center.y - body.height * 0.5f) * k;
        var bottom = new Vector3(transform.position.x, feet + r + 0.05f, transform.position.z);
        var top = new Vector3(bottom.x, feet + Mathf.Max(r + 0.06f, body.height * k - r), bottom.z);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r * 0.9f, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!Overlaps[i].transform.IsChildOf(transform)) return false;
        return true;
    }
}
