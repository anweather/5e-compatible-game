using System.Reflection;
using UnityEngine;

// A dodge roll on the left trigger (or Left Ctrl): a quick tumble about 5 m
// the way the wizard is being steered (straight ahead standing still) in
// half a second, then a short wait before the next. It costs a little stamina
// (Vitals), and there's no roll while swimming, in the air or carrying
// something heavy. Like Knockback it runs after the Starter Assets controller
// has moved the wizard and replaces that move with the roll's, so the
// controller itself stays untouched. The body's tumble is RollPose, played
// here and, online, on every other machine from a message (NetWizard.Rolled),
// since the tumble is laid over the bones and isn't in the synced animation.
[DefaultExecutionOrder(105)]
[RequireComponent(typeof(CharacterController))]
public class DodgeRoll : MonoBehaviour
{
    /// <summary>How far a roll goes (m), how long it takes and the wait after it before the next (s).</summary>
    // 4 m in 0.45 s was a touch short (Andrew, 2026-10-27).
    public const float Distance = 5f, Duration = 0.5f, Cooldown = 0.35f;
    /// <summary>The stamina a roll costs (out of Vitals.StaminaMax).</summary>
    public const float StaminaCost = 15f;
    // Holding something heavier than this (kg), the wizard can't roll.
    const float HeavyKg = 10f;

    CharacterController body;
    PlayerControls controls;
    SurfaceEffects surface;
    ItemHolder hands;
    Vitals vitals;
    RollPose pose;
    NetWizard net;
    Component controller, inputs;
    FieldInfo grounded, move;
    Vector3 direction, lastPosition;
    float started = -10f, covered;

    /// <summary>True while a roll is under way.</summary>
    public bool Rolling { get; private set; }

    /// <summary>How many rolls this wizard has made.</summary>
    public int Count { get; private set; }

    /// <summary>When the next roll may start.</summary>
    public float ReadyAt => started + Duration + Cooldown;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        TryGetComponent(out controls);
        TryGetComponent(out surface);
        TryGetComponent(out hands);
        TryGetComponent(out vitals);
        TryGetComponent(out pose);
        TryGetComponent(out net);
        controller = GetComponent(WizardRig.ControllerComponent);
        inputs = GetComponent(WizardRig.InputsComponent);
        grounded = WizardRig.Field(controller, "Grounded", isPublic: true);
        move = WizardRig.Field(inputs, "move", isPublic: true);
    }

    void Update()
    {
        if (!body.enabled)
        {
            Rolling = false;
            return;
        }
        if (Rolling) Carry();
        else if (controls != null && controls.enabled && controls.Roll.WasPressedThisFrame()) TryRoll();
        lastPosition = transform.position;
    }

    /// <summary>Starts a roll if the wizard can roll now; whether it did.</summary>
    public bool TryRoll()
    {
        if (Rolling || Time.time < ReadyAt || !CanRoll()) return false;
        if (vitals != null && !vitals.TrySpendStamina(StaminaCost))
        {
            EventLog.Add("Too tired to roll");
            return false;
        }
        direction = Heading();
        transform.rotation = Quaternion.LookRotation(direction);
        Rolling = true;
        started = Time.time;
        covered = 0f;
        Count++;
        if (pose != null) pose.Play(Duration);
        if (net != null && net.IsSpawned) net.Rolled(Duration);
        return true;
    }

    bool CanRoll()
    {
        if (surface != null && surface.Swimming) return false;
        bool onGround = grounded != null ? (bool)grounded.GetValue(controller) : body.isGrounded;
        if (!onGround) return false;
        var held = hands != null ? hands.Held : null;
        return held == null || held.Def == null || held.Def.Mass <= HeavyKg;
    }

    // Where the stick or keys point, turned by the camera as the controller turns them; ahead when standing.
    Vector3 Heading()
    {
        var stick = move != null ? (Vector2)move.GetValue(inputs) : Vector2.zero;
        var cam = Camera.main;
        if (stick.sqrMagnitude > 0.01f && cam != null)
            return (Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f) * new Vector3(stick.x, 0f, stick.y)).normalized;
        var ahead = transform.forward;
        ahead.y = 0f;
        return ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.forward;
    }

    // This frame's stretch of the roll, in place of the controller's own move sideways (it keeps falling).
    void Carry()
    {
        float u = Mathf.Clamp01((Time.time - started) / Duration);
        float reach = Distance * Ease(u);
        var moved = transform.position - lastPosition;
        moved.y = 0f;
        body.Move(direction * (reach - covered) - moved);
        covered = reach;
        // A still last move, so the controller doesn't take the roll's speed as a run of its own.
        body.Move(Vector3.zero);
        transform.rotation = Quaternion.LookRotation(direction);
        if (u >= 1f) Rolling = false;
    }

    // Fast off the mark, easing to a stop: the distance covered by u (0 to 1) of the roll, 0 to 1.
    static float Ease(float u) => (4f * u - u * u * u * u) / 3f;
}
