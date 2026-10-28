using UnityEngine;

// The ball court's gate (ValleyBuilder.BuildCourt): a log gate in the rail on
// the yard's side that swings open when any wizard comes within a few metres of
// it, away from that wizard (the way they're walking: into the court from the
// yard, out of it from inside), and swings shut a couple of seconds after the
// last one has gone. Every machine swings its own from where the wizards stand (online
// the other players' wizards follow their owners), so nothing is sent; the
// ball only meets the host's gate, where its physics runs. A kinematic body,
// so it pushes the ball aside as it swings rather than passing through it.
[RequireComponent(typeof(Rigidbody))]
public class CourtGate : MonoBehaviour
{
    [Tooltip("How far it swings open, in degrees about its hinge (positive: into the court; it swings away from the wizard who opens it).")]
    public float openAngle = 95f;
    [Tooltip("How wide the gateway is (m), from the hinge along the shut gate.")]
    public float width = 2.4f;
    [Tooltip("A wizard this near the middle of the gateway (m) opens it.")]
    public float reach = 3f;
    [Tooltip("How long it stays open after the last wizard has gone (s).")]
    public float stayOpen = 2f;
    [Tooltip("How long a swing from shut to open takes (s).")]
    public float swingSeconds = 0.8f;

    const float LookEvery = 0.2f;

    Rigidbody body;
    Quaternion shut;
    Vector3 middle;
    float progress, openUntil = -1f, nextLook;
    // Which way it swings: 1 as openAngle says, -1 the other way. Chosen when it starts opening.
    float side = 1f;

    /// <summary>How far open it is: 0 shut, 1 wide open.</summary>
    public float Openness => Mathf.SmoothStep(0f, 1f, progress);

    /// <summary>The middle of the gateway, on the ground.</summary>
    public Vector3 Middle => middle;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        shut = transform.rotation;
        middle = transform.position + shut * Vector3.forward * (width / 2f);
    }

    void Update()
    {
        if (Time.time < nextLook) return;
        nextLook = Time.time + LookEvery;
        if (!NearestWizard(out var wizard)) return;
        if (progress <= 0f) side = AwayFrom(wizard);
        openUntil = Time.time + stayOpen;
    }

    // Eased both ways, so it starts and stops gently.
    void FixedUpdate()
    {
        float target = Time.time < openUntil ? 1f : 0f;
        float next = Mathf.MoveTowards(progress, target, Time.fixedDeltaTime / swingSeconds);
        if (Mathf.Approximately(next, progress)) return;
        progress = next;
        body.MoveRotation(shut * Quaternion.Euler(0f, side * openAngle * Openness, 0f));
    }

    // The nearest wizard within reach: the local player's, and online everyone else's too.
    bool NearestWizard(out Vector3 at)
    {
        at = default;
        float best = reach * reach;
        bool found = false;
        foreach (var wizard in FindObjectsByType<SpellCaster>(FindObjectsSortMode.None))
        {
            var to = wizard.transform.position - middle;
            to.y = 0f;
            if (to.sqrMagnitude >= best) continue;
            best = to.sqrMagnitude;
            at = wizard.transform.position;
            found = true;
        }
        return found;
    }

    // A positive angle swings the free end toward the shut gate's right; swing to the side the wizard isn't on.
    float AwayFrom(Vector3 wizard) => Vector3.Dot(wizard - middle, shut * Vector3.right) > 0f ? -1f : 1f;
}
