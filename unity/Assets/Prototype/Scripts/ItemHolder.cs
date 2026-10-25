using Unity.Cinemachine;
using UnityEngine;

// The wizard's free hand (controls in PlayerControls). Picks up the nearest
// Carryable within reach, carries it in the right hand, drops it, throws it
// where the crosshair points (hold the throw button to throw harder) and
// passes "use" on to it. Shows what E would do next to the crosshair.
//
// Solo only for now: an online wizard has no ItemHolder, and items lie still
// on other machines (props aren't networked yet; see ChemEffects).
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
    float throwPressedAt = -1f;
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
    public float WindUp => throwPressedAt < 0f ? 0f : Mathf.Clamp01((Time.time - throwPressedAt) / windUp);

    void Awake()
    {
        Colliders = GetComponentsInChildren<Collider>();
        TryGetComponent(out body);
        if (TryGetComponent<Animator>(out var animator) && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

    void Update()
    {
        if (controls == null) return;
        InReach = Held == null ? Forces.Nearest<Carryable>(transform.position, reach) : null;
        if (InReach != null && !InReach.isActiveAndEnabled) InReach = null;
        controls.ItemInReach = Held != null || InReach != null;

        if (controls.Interact.WasPressedThisFrame())
        {
            if (Held != null) Drop();
            else if (InReach != null) PickUp(InReach);
        }
        if (Held != null) HandleHeld();
        else throwPressedAt = -1f;
    }

    // Throw (hold to wind up, release to let fly) and use.
    void HandleHeld()
    {
        if (controls.Throw.WasPressedThisFrame()) throwPressedAt = Time.time;
        if (controls.Throw.WasReleasedThisFrame() && throwPressedAt >= 0f)
        {
            Throw(Mathf.Lerp(gentleThrow, hardThrow, WindUp));
            return;
        }
        if (controls.Use.WasPressedThisFrame())
            foreach (var use in Held.Uses) use.Use(this);
    }

    // After the animation (and CastPose's gesture): the item rides in the hand, pointing where the wizard aims.
    void LateUpdate()
    {
        if (Held == null) return;
        var aim = AimDirection;
        Held.transform.SetPositionAndRotation(HandPosition, Quaternion.LookRotation(aim));
        bool useDown = controls.Use.IsPressed();
        foreach (var use in Held.Uses) use.WhileHeld(this, useDown);
    }

    public void PickUp(Carryable item)
    {
        if (Held != null || item.Holder != null) return;
        Held = item;
        item.TakeInto(this);
        EventLog.Add($"You pick up the {item.Name.ToLowerInvariant()}");
    }

    /// <summary>Lets go: it falls from the hand at the wizard's own pace.</summary>
    public void Drop() => Let(go => go.Release(body != null ? body.velocity : Vector3.zero), "drop");

    /// <summary>Throws the held item along the aim at <paramref name="speed"/> m/s, on top of the wizard's own pace.</summary>
    public void Throw(float speed)
    {
        var dir = (AimDirection + Vector3.up * Loft).normalized;
        var pace = body != null ? body.velocity : Vector3.zero;
        Let(go => go.Release(pace + dir * speed), speed >= (gentleThrow + hardThrow) / 2f ? "hurl" : "toss");
    }

    void Let(System.Action<Carryable> release, string verb)
    {
        var item = Held;
        Held = null;
        throwPressedAt = -1f;
        release(item);
        EventLog.Add($"You {verb} the {item.Name.ToLowerInvariant()}");
    }

    // --- hint by the crosshair -----------------------------------------------------

    void OnGUI()
    {
        if (!EventLog.HudVisible || controls == null) return;
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
        string pick = pad ? "X / □" : "E", use = pad ? "Y / △" : "F", toss = pad ? "B / ○" : "G / right-click";
        if (Held != null)
        {
            string wind = WindUp > 0f ? $"   ·   throw {Mathf.RoundToInt(WindUp * 100f)}%" : "";
            string useLabel = Held.Def?.Use != null ? $"<b>{use}</b> use   ·   " : "";
            return $"{Held.Name}:   {useLabel}<b>{pick}</b> drop   ·   <b>{toss}</b> throw (hold: harder){wind}";
        }
        return InReach != null ? $"<b>{pick}</b>: pick up {InReach.Name}" : null;
    }
}
