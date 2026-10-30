using UnityEngine;
using UnityEngine.InputSystem;

// Sitting down (the local wizard only): near a Seat with nothing to pick up, E
// sits the wizard on it, facing the way it faces; E again, jump, moving or a
// hard push stands it up in front of the seat. While seated the Starter Assets
// controller is off and the wizard is held on the seat (its own colliders let
// it through), so another player's machine sees it there and SitPose bends it
// into the seat. The hint shows by the crosshair, like ItemHolder's.
[DefaultExecutionOrder(140)]
public class Sitter : MonoBehaviour
{
    const float Reach = 1.7f;
    static readonly int SpeedId = Animator.StringToHash("Speed"), GroundedId = Animator.StringToHash("Grounded");

    PlayerControls controls;
    ItemHolder hands;
    Knockback knockback;
    CharacterController body;
    Behaviour mover;
    Animator animator;
    InputAction move, jump;
    GUIStyle style, shadow;
    Seat inReach;

    /// <summary>The seat the wizard is sitting on, or null.</summary>
    public Seat Seated { get; private set; }

    void Awake()
    {
        TryGetComponent(out controls);
        TryGetComponent(out hands);
        TryGetComponent(out knockback);
        TryGetComponent(out body);
        TryGetComponent(out animator);
        mover = GetComponent("ThirdPersonController") as Behaviour;
        // Moving and jumping are the Starter Assets' own input actions, read through PlayerInput.
        if (TryGetComponent<PlayerInput>(out var input) && input.actions != null)
        {
            move = input.actions.FindAction("Move");
            jump = input.actions.FindAction("Jump");
        }
    }

    // The local, player-driven wizard (a puppet's controls are off).
    bool Driven => controls != null && controls.isActiveAndEnabled;

    void Update()
    {
        if (!Driven)
        {
            if (Seated != null) Stand();
            return;
        }
        if (Seated != null)
        {
            controls.SeatInReach = true;
            bool pushed = knockback != null && knockback.Velocity.sqrMagnitude > 1f;
            bool moving = move != null && move.ReadValue<Vector2>().sqrMagnitude > 0.25f;
            bool jumped = jump != null && jump.WasPressedThisFrame();
            if (controls.Interact.WasPressedThisFrame() || moving || jumped || pushed) Stand();
            return;
        }
        bool busy = hands != null && (hands.Held != null || hands.InReach != null);
        inReach = busy ? null : Seat.Nearest(transform.position + Vector3.up * 0.5f, Reach);
        controls.SeatInReach = inReach != null;
        if (inReach != null && controls.Interact.WasPressedThisFrame()) Sit(inReach);
    }

    // Held on the seat after everything else has moved the wizard this frame.
    void LateUpdate()
    {
        if (Seated == null) return;
        Place(Seated.Foot, Seated.Facing);
        if (animator != null)
        {
            animator.SetFloat(SpeedId, 0f);
            animator.SetBool(GroundedId, true);
        }
    }

    public void Sit(Seat seat)
    {
        Seated = seat;
        if (mover != null) mover.enabled = false;
        Ignore(seat, true);
        Place(seat.Foot, seat.Facing);
        EventLog.Add(seat.lounge ? "You lie back on the lounger" : "You sit down");
    }

    public void Stand()
    {
        var seat = Seated;
        Seated = null;
        if (seat == null) return;
        // Up and a step out in front of the seat, clear of it.
        Place(seat.Foot + seat.Facing * Vector3.forward * (seat.lounge ? 0.2f : 0.55f) + Vector3.up * 0.1f, seat.Facing);
        Ignore(seat, false);
        if (mover != null && Driven) mover.enabled = true;
        if (seat.lounge) Place(transform.position + seat.transform.right * 0.7f, transform.rotation);
    }

    void Place(Vector3 at, Quaternion facing)
    {
        transform.SetPositionAndRotation(at, facing);
        Physics.SyncTransforms();
    }

    void Ignore(Seat seat, bool ignore)
    {
        if (body == null) return;
        foreach (var c in seat.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(body, c, ignore);
    }

    void OnDisable()
    {
        if (Seated != null) Stand();
        if (controls != null) controls.SeatInReach = false;
    }

    void OnGUI()
    {
        HudScale.Apply();
        if (!EventLog.HudVisible || !Driven || (Seated == null && inReach == null)) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        }
        string key = controls.UsingGamepad ? PadGlyphs.Label(PadButton.West) : "E";
        string action = inReach != null && inReach.lounge ? "lie back" : "sit";
        string hint = Seated != null ? $"<b>{key}</b>: get up" : $"<b>{key}</b>: {action}";
        var at = new Rect(0, HudScale.Height / 2f + 34, HudScale.Width, 28);
        GUI.Label(new Rect(at.x + 1, at.y + 1, at.width, at.height), hint, shadow);
        GUI.Label(at, hint, style);
    }
}
