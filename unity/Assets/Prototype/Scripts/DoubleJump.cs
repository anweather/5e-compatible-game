using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

// A second jump in mid-air, once each time the wizard leaves the ground: the
// same jump button pressed while airborne kicks the wizard up again. Like
// SurfaceEffects, a correction on top of the Starter Assets controller, which
// stays untouched: it reads the controller's Grounded flag and sets its
// vertical speed (_verticalVelocity) by reflection (WizardRig), the way the
// swim kick does. Works everywhere (the climbing tower is laid out for it).
// Not while swimming: there jump is the swim kick.
[DefaultExecutionOrder(101)]
[RequireComponent(typeof(CharacterController))]
public class DoubleJump : MonoBehaviour
{
    [Tooltip("How high the second jump lifts the wizard from where it is pressed (m).")]
    public float height = 1.2f;
    [Tooltip("Airborne at least this long (s) before the second jump answers, so one press is never two jumps.")]
    public float after = 0.08f;

    static readonly int JumpId = Animator.StringToHash("Jump");

    CharacterController body;
    Behaviour controller;
    FieldInfo grounded, fall, gravity;
    SurfaceEffects surface;
    Animator animator;
    bool used;
    float airborneSince = -1f;

    /// <summary>Whether the second jump has been used since the wizard last stood on something.</summary>
    public bool Used => used;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        controller = GetComponent(WizardRig.ControllerComponent) as Behaviour;
        grounded = WizardRig.Field(controller, "Grounded", isPublic: true);
        gravity = WizardRig.Field(controller, "Gravity", isPublic: true);
        fall = WizardRig.Field(controller, "_verticalVelocity", isPublic: false);
        TryGetComponent(out surface);
        TryGetComponent(out animator);
    }

    // Jump, from PlayerInput's messages (as SurfaceEffects gets it): the controller ignores a press off the ground.
    void OnJump(InputValue value)
    {
        if (value.isPressed) AirJump();
    }

    void Update()
    {
        if (!OnGround())
        {
            if (airborneSince < 0f) airborneSince = Time.time;
            return;
        }
        used = false;
        airborneSince = -1f;
    }

    bool OnGround() => grounded == null || (bool)grounded.GetValue(controller);

    /// <summary>The second jump, if the wizard is in the air and hasn't used it yet; returns whether it jumped.</summary>
    public bool AirJump()
    {
        if (controller == null || !controller.enabled || !body.enabled || fall == null || gravity == null) return false;
        if (used || OnGround() || airborneSince < 0f || Time.time - airborneSince < after) return false;
        if (surface != null && surface.Swimming) return false;
        used = true;
        float g = (float)gravity.GetValue(controller);
        fall.SetValue(controller, Mathf.Sqrt(height * -2f * g));
        if (animator != null) animator.SetBool(JumpId, true);
        return true;
    }
}
