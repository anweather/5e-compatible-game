using System.Collections.Generic;
using UnityEngine;

// Shoves the wizard: a Thunderwave or an exploding barrel knocks it back.
// The Starter Assets controller keeps steering; this adds a slide on top that
// dies away, moving the CharacterController after the controller has (as
// SurfaceEffects does), so the controller itself stays untouched. On slick
// ground SurfaceEffects then carries the slide on as momentum.
//
// Online, a wizard moves only on its owner's machine: a shove that lands on
// someone else's wizard here (a barrel the host's physics blew up) is handed
// on to its owner (NetWizard), who applies it.
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

    static readonly List<Knockback> all = new();

    CharacterController body;
    NetWizard net;
    Vector3 shuntTo;
    float shuntLeft;

    /// <summary>Every wizard in the scene, local or not (Forces finds other players' wizards by distance).</summary>
    public static IReadOnlyList<Knockback> All => all;

    /// <summary>The shove's current speed, in m/s.</summary>
    public Vector3 Velocity { get; private set; }

    void Awake()
    {
        body = GetComponent<CharacterController>();
        TryGetComponent(out net);
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    /// <summary>
    /// Moves it along the ground to a point over this many seconds (a strike's knock-back), through the
    /// controller, so what stands in the way stops it. The owner's machine only.
    /// </summary>
    public void Shunt(Vector3 to, float seconds)
    {
        shuntTo = to;
        shuntLeft = Mathf.Max(seconds, 0.01f);
    }

    /// <summary>Adds an impulse (newton-seconds). Only the sideways part moves the wizard; the controller owns jumping and falling.</summary>
    public void Push(Vector3 impulse)
    {
        if (net != null && net.IsSpawned && !net.IsOwner)
        {
            net.PushOwner(impulse);
            return;
        }
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

    void Shunting()
    {
        var left = shuntTo - transform.position;
        left.y = 0f;
        shuntLeft -= Time.deltaTime;
        if (!body.enabled) return;
        body.Move(left * Mathf.Min(1f, Time.deltaTime / (shuntLeft + Time.deltaTime)));
        body.Move(Vector3.zero); // as in Update: no run from this move
    }

    void Update()
    {
        if (shuntLeft > 0f) Shunting();
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
