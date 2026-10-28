using UnityEngine;

// A spring bone for a bit of clothing that hangs and sways (a sleeve's cuff):
// a child of a rig bone that lags behind where the bone would put it, falls a
// little with gravity, and springs back, so the cloth skinned to it moves
// rather than holding still like a shell. View only, like the robe's cloth.
[DefaultExecutionOrder(305)]
public class ClothSpring : MonoBehaviour
{
    // How hard it springs back each step, how much of its motion it keeps, and how far it may lag.
    const float Stiffness = 0.18f, Damping = 0.86f, Reach = 0.07f, Sag = 0.02f;

    Vector3 rest, at, before;
    bool started;

    void Start()
    {
        rest = transform.localPosition;
        Snap();
    }

    void Snap()
    {
        at = before = transform.parent.TransformPoint(rest);
        started = true;
    }

    void LateUpdate()
    {
        if (!started) return;
        var target = transform.parent.TransformPoint(rest) + Vector3.down * Sag;
        if (Vector3.Distance(at, target) > 2f) Snap();
        var v = (at - before) * Damping;
        before = at;
        at += v + (target - at) * Stiffness;
        var off = at - target;
        if (off.magnitude > Reach) at = target + off.normalized * Reach;
        transform.position = at;
    }
}
