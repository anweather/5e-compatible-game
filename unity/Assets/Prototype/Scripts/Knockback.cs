using UnityEngine;

// Shoves the wizard: a Thunderwave or an exploding barrel knocks it back.
// The Starter Assets controller keeps steering; this adds a slide on top that
// dies away, moving the CharacterController after the controller has (as
// SurfaceEffects does), so the controller itself stays untouched. On slick
// ground SurfaceEffects then carries the slide on as momentum.
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(CharacterController))]
public class Knockback : MonoBehaviour, IPushable
{
    [Tooltip("For the wizard's mass in rules/entities.json; without it the mass is 1.")]
    public ChemGrid grid;
    [Tooltip("How quickly a shove dies away, per second (higher = shorter slide).")]
    public float decay = 2.5f;

    // Below this speed (m/s) the shove is over.
    const float StopSpeed = 0.05f;

    CharacterController body;

    /// <summary>The shove's current speed, in m/s.</summary>
    public Vector3 Velocity { get; private set; }

    void Awake() => body = GetComponent<CharacterController>();

    /// <summary>Adds an impulse (newton-seconds). Only the sideways part moves the wizard; the controller owns jumping and falling.</summary>
    public void Push(Vector3 impulse)
    {
        impulse.y = 0f;
        Velocity += impulse / MassKg;
    }

    float MassKg
    {
        get
        {
            var def = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
            return Forces.KgPerMass * (def != null ? def.Mass : 1);
        }
    }

    void Update()
    {
        if (Velocity.sqrMagnitude < StopSpeed * StopSpeed)
        {
            Velocity = Vector3.zero;
            return;
        }
        if (body.enabled)
        {
            body.Move(Velocity * Time.deltaTime);
            // The controller eases its speed from the CharacterController's last move and would
            // turn the shove into a run the way the wizard faces; a still last move stops that
            // (and staggers the wizard's own running while the shove lasts).
            body.Move(Vector3.zero);
        }
        Velocity *= Mathf.Exp(-decay * Time.deltaTime);
    }
}
