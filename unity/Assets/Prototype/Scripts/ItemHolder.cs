using Unity.Cinemachine;
using UnityEngine;

// The wizard's free hand (controls in PlayerControls). Picks up the nearest
// Carryable within reach, carries it in the right hand, drops it, throws it
// where the crosshair points (hold the throw button to throw harder) and
// passes "use" on to it. Shows what E would do next to the crosshair.
//
// Online every wizard has one, so a held item rides in its holder's hand on
// every screen, but only the local player's has controls. Picking up and
// letting go are asked of the host (NetItem), which grants them; Grant and
// Forget are how the answer arrives, on every machine.
[DefaultExecutionOrder(120)] // after CastPose and Knockback have moved the arm and body
public class ItemHolder : MonoBehaviour
{
    public PlayerControls controls;
    [Tooltip("The follow camera, for the spyglass.")]
    public CinemachineCamera view;
    [Tooltip("How far away an item can be picked up, in metres.")]
    public float reach = 2f;
    [Tooltip("Throw speeds in m/s: a tap, and the button held for windUp seconds.")]
    public float gentleThrow = 4f, hardThrow = 12f;
    public float windUp = 0.6f;

    // A throw goes a little above the crosshair, so a level aim still lobs.
    const float Loft = 0.15f;

    Transform hand;
    CharacterController body;
    SpellRadialHud radial;
    Shapeshift shape;
    float throwPressedAt = -1f;
    // A throw under way: when it leaves the hand (the arm part way through its whip), and how fast.
    float releaseAt = -1f, releaseSpeed;
    const float ReleaseDelay = 0.1f;
    GUIStyle style, shadow;

    public Carryable Held { get; private set; }
    /// <summary>The item E would pick up, or null.</summary>
    public Carryable InReach { get; private set; }
    /// <summary>The wizard's colliders, which a held item ignores.</summary>
    public Collider[] Colliders { get; private set; }
    /// <summary>Where the held item sits: the right hand, or in front of the chest.</summary>
    public Vector3 HandPosition => hand != null ? hand.position + transform.forward * 0.08f : transform.position + Vector3.up * 1.1f + transform.forward * 0.45f;
    /// <summary>The way the wizard is aiming (through the crosshair).</summary>
    public Vector3 AimDirection => Aiming.Ray(controls).direction;
    /// <summary>How far the current throw has wound up, 0..1 (0 when not throwing).</summary>
    /// <summary>A throw just left the hand: how hard, 0 (a toss) to 1 (a full wind-up), for the arm's follow-through (ThrowPose).</summary>
    public event System.Action<float> Thrown;

    public float WindUp => throwPressedAt < 0f ? 0f : Mathf.Clamp01((Time.time - throwPressedAt) / windUp);

    void Awake()
    {
        Colliders = GetComponentsInChildren<Collider>();
        TryGetComponent(out body);
        TryGetComponent(out radial);
        TryGetComponent(out shape);
        if (TryGetComponent<Animator>(out var animator) && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

    void Update()
    {
        if (releaseAt >= 0f && Time.time >= releaseAt) LetFly();
        if (controls == null) return;
        InReach = Held == null ? Nearest() : null;
        controls.ItemInReach = Held != null || InReach != null;

        if (controls.Interact.WasPressedThisFrame())
        {
            if (Held != null) Drop();
            else if (InReach != null) PickUp(InReach);
        }
        if (Held != null) HandleHeld();
        else throwPressedAt = -1f;
    }

    // Throw (hold to wind up, release to let fly) and use. B is also the spell radial's cancel:
    // while the radial has it (open, or closed by it this frame) it doesn't throw.
    void HandleHeld()
    {
        // Mid-throw: the item is on its way out of the hand.
        if (releaseAt >= 0f) return;
        if (radial != null && radial.BusyThisFrame)
        {
            throwPressedAt = -1f;
            return;
        }
        if (controls.Throw.WasPressedThisFrame()) throwPressedAt = Time.time;
        if (controls.Throw.WasReleasedThisFrame() && throwPressedAt >= 0f)
        {
            Throw(Mathf.Lerp(gentleThrow, hardThrow, WindUp));
            return;
        }
        if (controls.Use.WasPressedThisFrame())
            foreach (var use in Held.Uses) use.Use(this);
    }

    // The nearest item lying about (not in anyone's hand) within reach. An animal has no hands (Shapeshift).
    Carryable Nearest()
    {
        if (Animal) return null;
        Carryable best = null;
        float bestDistance = float.MaxValue;
        foreach (var item in Forces.InSphere<Carryable>(transform.position, reach, null))
        {
            if (item.Holder != null || !item.isActiveAndEnabled) continue;
            float d = (item.transform.position - transform.position).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = item;
        }
        return best;
    }

    // After the animation (and CastPose's gesture): the item rides in the hand, pointing where the
    // wizard aims (someone else's wizard: the way it faces). Only the local player uses it.
    void LateUpdate()
    {
        if (Held == null) return;
        HoldInHand();
        if (controls == null) return;
        bool useDown = controls.Use.IsPressed();
        foreach (var use in Held.Uses) use.WhileHeld(this, useDown);
    }

    /// <summary>
    /// Puts the held item in the hand where it is now. Again after a pose has moved the arm (ThrowPose
    /// draws it back and whips it through), so the item goes with the hand, not where it held still.
    /// </summary>
    public void HoldInHand()
    {
        if (Held == null) return;
        var aim = controls != null ? FrontArc(AimDirection) : transform.forward;
        Held.transform.SetPositionAndRotation(HandPosition, Quaternion.LookRotation(aim));
    }

    // How far from straight ahead a held item may point (Andrew: the lantern pointed where the camera
    // looked, back through the wizard when the camera came round in front).
    const float FrontArcDegrees = 70f;

    // An aim kept within the front arc: turned toward the wizard's front where it points further round.
    Vector3 FrontArc(Vector3 aim) => Vector3.RotateTowards(transform.forward, aim, FrontArcDegrees * Mathf.Deg2Rad, 0f);

    // In an animal's shape (Shapeshift): no picking things up.
    bool Animal => shape != null && shape.Shifted;

    public void PickUp(Carryable item)
    {
        if (Held != null || item.Holder != null || Animal) return;
        if (item.Online)
        {
            item.Net.RequestPickUp();
            return;
        }
        Grant(item);
    }

    /// <summary>
    /// Mage Hand: picks up <paramref name="item"/> from up to <paramref name="reach"/> metres away
    /// (online the host allows a spell's reach, within limits; see NetItem).
    /// </summary>
    public void PickUpFromAfar(Carryable item, float reach)
    {
        if (Held != null || item.Holder != null) return;
        if (item.Online)
        {
            item.Net.RequestPickUp(reach);
            return;
        }
        Grant(item);
    }

    /// <summary>Mage Hand: lets go of what this hand holds at <paramref name="spot"/>, at rest.</summary>
    public void SetDownAt(Vector3 spot)
    {
        var item = Held;
        if (item == null) return;
        Held = null;
        throwPressedAt = -1f;
        if (item.Online) item.Net.RequestRelease(spot, Vector3.zero);
        else item.ReleaseAt(spot, Vector3.zero);
    }

    /// <summary>Puts <paramref name="item"/> in this hand (solo at once; online when the host says so, on every machine).</summary>
    public void Grant(Carryable item)
    {
        Held = item;
        item.TakeInto(this);
        if (controls != null) EventLog.Add($"You pick up the {item.Name.ToLowerInvariant()}");
    }

    /// <summary>Online: <paramref name="item"/> has left this hand (the host let it go).</summary>
    public void Forget(Carryable item)
    {
        if (Held == item) Held = null;
    }

    /// <summary>The wizard is leaving the game: whatever it holds drops where it is.</summary>
    public void LetGoOnLeaving()
    {
        var item = Held;
        if (item == null) return;
        Held = null;
        item.Release(Vector3.zero);
    }

    /// <summary>Lets go: it falls from the hand at the wizard's own pace.</summary>
    public void Drop() => Let(body != null ? body.velocity : Vector3.zero, "drop");

    /// <summary>Throws the held item along the aim at <paramref name="speed"/> m/s, on top of the wizard's own pace.</summary>
    public void Throw(float speed)
    {
        if (Held == null) return;
        Thrown?.Invoke(Mathf.InverseLerp(gentleThrow, hardThrow, speed));
        throwPressedAt = -1f;
        releaseAt = Time.time + ReleaseDelay;
        releaseSpeed = speed;
    }

    // The throw's release, from the hand as the arm whips through (ThrowPose), along the aim then.
    void LetFly()
    {
        releaseAt = -1f;
        if (Held == null) return;
        var dir = (AimDirection + Vector3.up * Loft).normalized;
        var pace = body != null ? body.velocity : Vector3.zero;
        Let(pace + dir * releaseSpeed, releaseSpeed >= (gentleThrow + hardThrow) / 2f ? "hurl" : "toss");
    }

    // Solo the item leaves the hand at once; online the host lets it go, from where this screen
    // has the hand, and every machine follows (it stays in the hand until the host's answer arrives).
    void Let(Vector3 velocity, string verb)
    {
        var item = Held;
        Held = null;
        throwPressedAt = -1f;
        releaseAt = -1f;
        if (item.Online) item.Net.RequestRelease(HandPosition, velocity);
        else item.Release(velocity);
        EventLog.Add($"You {verb} the {item.Name.ToLowerInvariant()}");
    }

    // --- hint by the crosshair -----------------------------------------------------

    void OnGUI()
    {
        if (!EventLog.HudVisible || controls == null || (radial != null && radial.IsOpen)) return; // the spell radial covers the middle
        string hint = Hint();
        if (hint == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        }
        // Below the crosshair, with a dark shadow so it reads against the sky and the grass.
        var at = new Rect(0, Screen.height / 2f + 34, Screen.width, 28);
        GUI.Label(new Rect(at.x + 1, at.y + 1, at.width, at.height), hint, shadow);
        GUI.Label(at, hint, style);
    }

    string Hint()
    {
        bool pad = controls.UsingGamepad;
        string pick = pad ? PadGlyphs.Label(PadButton.West) : "E", use = pad ? PadGlyphs.Label(PadButton.North) : "F", toss = pad ? PadGlyphs.Label(PadButton.East) : "G / right-click";
        if (Held != null)
        {
            string wind = WindUp > 0f ? $"   ·   throw {Mathf.RoundToInt(WindUp * 100f)}%" : "";
            string useLabel = Held.Def?.Use != null ? $"<b>{use}</b> use   ·   " : "";
            return $"{Held.Name}:   {useLabel}<b>{pick}</b> drop   ·   <b>{toss}</b> throw (hold: harder){wind}";
        }
        return InReach != null ? $"<b>{pick}</b>: pick up {InReach.Name}" : null;
    }
}
