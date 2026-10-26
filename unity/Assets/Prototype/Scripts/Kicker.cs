using UnityEngine;

// Running into a light thing kicks it: the Starter Assets controller is a
// CharacterController, which stops at a rigidbody instead of pushing it, so
// this gives what the wizard runs into a shove away from where they touched,
// harder the faster it ran, with a little lift. Only things light enough to
// kick (a ball, a lantern on the ground), never one in a hand. Online the
// host's physics moves props, so a client's kick goes to the host (NetProp).
[RequireComponent(typeof(CharacterController))]
public class Kicker : MonoBehaviour
{
    /// <summary>Things heavier than this (kg) are walked into, not kicked.</summary>
    public const float MaxKickKg = 5f;
    /// <summary>The ball's speed from a kick: this much, plus the runner's speed times KickPerSpeed (m/s).</summary>
    const float KickBase = 2f, KickPerSpeed = 1.3f, Lift = 0.25f, Cooldown = 0.2f, MinRunSpeed = 0.5f;

    CharacterController body;
    float nextKick;

    void Awake() => body = GetComponent<CharacterController>();

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!enabled || Time.time < nextKick) return;
        var thing = hit.rigidbody;
        if (thing == null || thing.mass > MaxKickKg) return;
        if (thing.TryGetComponent<Carryable>(out var carried) && carried.Holder != null) return;
        // Away from where they touched (the move that hit may be a step's fall, straight down),
        // and only while the wizard is on the move: standing against it doesn't kick.
        var along = -hit.normal;
        along.y = 0f;
        var run = body.velocity;
        run.y = 0f;
        if (along.sqrMagnitude < 0.01f || run.sqrMagnitude < MinRunSpeed * MinRunSpeed) return;
        nextKick = Time.time + Cooldown;
        float speed = KickBase + run.magnitude * KickPerSpeed;
        var impulse = thing.mass * speed * (along.normalized + Vector3.up * Lift);
        if (thing.TryGetComponent<NetProp>(out var net) && net.IsSpawned) net.Kick(impulse);
        else thing.AddForce(impulse, ForceMode.Impulse);
    }
}
