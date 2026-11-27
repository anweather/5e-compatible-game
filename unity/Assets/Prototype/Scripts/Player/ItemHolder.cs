using Chem;
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
[DefaultExecutionOrder(120)]
[RequireComponent(typeof(SpellCaster))]
public partial class ItemHolder : MonoBehaviour
{
    public PlayerControls controls;
    [Tooltip("The follow camera, for the spyglass.")]
    public CinemachineCamera view;
    [Tooltip("How far away an item can be picked up, in metres.")]
    public float reach = 2f;
    [Tooltip("Throw speeds in m/s: a tap, and the button held for windUp seconds.")]
    public float gentleThrow = 4f, hardThrow = 12f;
    public float windUp = 0.6f;

    const float Loft = 0.15f;

    Transform hand;
    CharacterController body;
    SpellRadialHud radial;
    Shapeshift shape;
    CastBeats beats;
    float throwPressedAt = -1f;
    float releaseAt = -1f, releaseSpeed;
    const float ReleaseDelay = 0.1f;
    GUIStyle style, shadow;

    public Carryable Held { get; private set; }
    public Carryable InReach { get; private set; }
    public Collider[] Colliders { get; private set; }
    public Vector3 HandPosition => hand != null ? hand.position + transform.forward * 0.08f : transform.position + Vector3.up * 1.1f + transform.forward * 0.45f;
    public Vector3 AimDirection => Aiming.Ray(controls).direction;
    public event System.Action<float> Thrown;

    public void Swing(float how) => Thrown?.Invoke(how);

    public float WindUp => throwPressedAt < 0f ? 0f : Mathf.Clamp01((Time.time - throwPressedAt) / windUp);

    void Awake()
    {
        Colliders = GetComponentsInChildren<Collider>();
        TryGetComponent(out body);
        TryGetComponent(out radial);
        TryGetComponent(out shape);
        TryGetComponent(out beats);
        if (TryGetComponent<Animator>(out var animator) && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

    void Update()
    {
        if (releaseAt >= 0f && Time.time >= releaseAt) LetFly();
        if (punchLandsAt >= 0f && Time.time >= punchLandsAt) PunchLands();
        if (controls == null) return;
        InReach = Held == null ? Nearest() : null;
        controls.ItemInReach = Held != null || InReach != null;
        if (Held == null && controls.Punch.WasPressedThisFrame()) TryPunch();

        if (controls.Interact.WasPressedThisFrame())
        {
            if (Held != null) Drop();
            else if (InReach != null) PickUp(InReach);
            else ChemEffects.SwingNear(FindAnyObjectByType<ChemGrid>(), transform.position);
        }
        if (Held != null) HandleHeld();
        else throwPressedAt = -1f;
    }

    void HandleHeld()
    {
        if (releaseAt >= 0f) return;
        if ((radial != null && radial.BusyThisFrame) || (TryGetComponent<SpellCaster>(out var casting) && casting.CastInProgress))
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

    void LateUpdate()
    {
        if (Held == null) return;
        HoldInHand();
        if (controls == null) return;
        bool useDown = controls.Use.IsPressed();
        foreach (var use in Held.Uses) use.WhileHeld(this, useDown);
    }

    public void HoldInHand()
    {
        if (Held == null) return;
        var aim = controls != null ? FrontArc(AimDirection) : transform.forward;
        Held.transform.SetPositionAndRotation(HandPosition, Quaternion.LookRotation(aim));
    }

    const float FrontArcDegrees = 70f;

    Vector3 FrontArc(Vector3 aim) => Vector3.RotateTowards(transform.forward, aim, FrontArcDegrees * Mathf.Deg2Rad, 0f);

    bool Animal => shape != null && shape.Shifted;

    public void PickUp(Carryable item)
    {
        if (Held != null || item.Holder != null || Animal) return;
        if (item.Online)
        {
            item.Net.RequestPickUp();
            return;
        }
        GrantSolo(item);
    }

    public void PickUpFromAfar(Carryable item, float reach)
    {
        if (Held != null || item.Holder != null) return;
        if (item.Online)
        {
            item.Net.RequestPickUp(reach);
            return;
        }
        GrantSolo(item);
    }

    void GrantSolo(Carryable item)
    {
        Grant(item);
        if (item.Prop.FromMod) item.Grid.QueueRoom(RoomCommand.Found(Peers.Solo.Me, item.Prop.Thing));
    }

    public void SetDownAt(Vector3 spot)
    {
        var item = Held;
        if (item == null) return;
        Held = null;
        throwPressedAt = -1f;
        if (item.Online) item.Net.RequestRelease(spot, Vector3.zero);
        else item.ReleaseAt(spot, Vector3.zero);
    }

    public void Grant(Carryable item)
    {
        Held = item;
        item.TakeInto(this);
        if (controls != null) EventLog.Add($"You pick up the {item.Name.ToLowerInvariant()}");
    }

    public void Forget(Carryable item)
    {
        if (Held == item) Held = null;
    }

    public void LetGoOnLeaving()
    {
        var item = Held;
        if (item == null) return;
        Held = null;
        item.Release(Vector3.zero);
    }

    public void Drop() => Let(body != null ? body.velocity : Vector3.zero, "drop");

    public void Throw(float speed)
    {
        if (Held == null) return;
        Thrown?.Invoke(Mathf.InverseLerp(gentleThrow, hardThrow, speed));
        throwPressedAt = -1f;
        releaseAt = Time.time + ReleaseDelay;
        releaseSpeed = speed;
    }

    void LetFly()
    {
        releaseAt = -1f;
        if (Held == null) return;
        var dir = (AimDirection + Vector3.up * Loft).normalized;
        var pace = body != null ? body.velocity : Vector3.zero;
        Let(pace + dir * releaseSpeed, releaseSpeed >= (gentleThrow + hardThrow) / 2f ? "hurl" : "toss");
    }

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

    void OnGUI()
    {
        HudScale.Apply();
        if (!EventLog.HudVisible || controls == null || (radial != null && radial.IsOpen)) return;
        string hint = Hint();
        if (hint == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        }
        var at = new Rect(0, HudScale.Height / 2f + 34, HudScale.Width, 28);
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
