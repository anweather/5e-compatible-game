using System.Collections.Generic;
using UnityEngine;

// Somewhere a wizard can sit (a bench, a lounger on the beach): where the
// sitter's hips go, in the seat's own frame (it faces +z), and whether it's
// for lying back. Sitter sits the local wizard down on the nearest; SitPose
// poses any wizard found sitting on one, so every machine sees it. Looks and
// play only: the chemistry never hears of seats.
public class Seat : MonoBehaviour
{
    [Tooltip("Where the sitter's hips go, in the seat's frame (it faces +z).")]
    public Vector3 seatAt = new(0f, 0.48f, 0f);
    [Tooltip("A lounger: lie back with the legs out, rather than sit up.")]
    public bool lounge;

    static readonly List<Seat> all = new();

    /// <summary>Every seat in the scene now (the villages' folk sit on them too: ValleyFauna.Folk).</summary>
    public static IReadOnlyList<Seat> All => all;

    /// <summary>Where the hips go, in the world.</summary>
    public Vector3 SeatPoint => transform.TransformPoint(seatAt);

    /// <summary>Where a sitter's feet stand: the ground under the seat point.</summary>
    public Vector3 Foot => transform.TransformPoint(new Vector3(seatAt.x, 0f, seatAt.z));

    /// <summary>The way a sitter faces, level.</summary>
    public Quaternion Facing
    {
        get
        {
            var f = transform.forward;
            f.y = 0f;
            return Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f : Vector3.forward);
        }
    }

    void OnEnable() => all.Add(this);

    void OnDisable() => all.Remove(this);

    /// <summary>The nearest seat whose seat point is within <paramref name="reach"/> of a point, or null.</summary>
    public static Seat Nearest(Vector3 at, float reach)
    {
        Seat best = null;
        float bestD = reach * reach;
        foreach (var seat in all)
        {
            float d = (seat.SeatPoint - at).sqrMagnitude;
            if (d >= bestD) continue;
            bestD = d;
            best = seat;
        }
        return best;
    }

    /// <summary>The seat a wizard standing at <paramref name="foot"/> is sitting on (its feet where a sitter's go), or null.</summary>
    public static Seat SittingAt(Vector3 foot)
    {
        foreach (var seat in all)
        {
            var d = seat.Foot - foot;
            if (Mathf.Abs(d.y) < 0.5f && new Vector2(d.x, d.z).sqrMagnitude < 0.3f * 0.3f) return seat;
        }
        return null;
    }
}
