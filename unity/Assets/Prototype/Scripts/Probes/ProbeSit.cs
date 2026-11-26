using System.Collections;
using UnityEngine;

// The probe's sit scenario (docs/FAST_LOOP.md): the wizard sits on a village
// bench, then lies back on a beach lounger (Seat, Sitter, SitPose), and gets up
// from each. Checks it's held on the seat with the pose on and its hips on the
// seat, that the seat is in reach before (so E goes to it, not the spell bar),
// and that standing frees it to walk again.
public class SitProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var sitter = wizard.GetComponent<Sitter>();
        var pose = wizard.GetComponent<SitPose>();
        var controls = wizard.GetComponent<PlayerControls>();
        var hips = wizard.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
        Check("parts", sitter != null && pose != null, "Sitter and SitPose on the wizard");
        if (sitter == null || pose == null) yield break;

        int seats = FindObjectsByType<Seat>(FindObjectsSortMode.None).Length;
        Metric("seats", seats);
        Check("seats_in_the_world", seats >= 10, $"{seats} seats (village benches, beach benches, loungers)");

        foreach (bool lounge in new[] { false, true })
        {
            var seat = Pick(lounge);
            string kind = lounge ? "lounger" : "bench";
            if (seat == null)
            {
                Check($"{kind}_found", false, "no seat of that kind");
                continue;
            }
            Teleport(seat.Foot + seat.Facing * Vector3.forward * 1.1f + Vector3.up * 0.2f);
            yield return Wait(1f);
            Check($"{kind}_in_reach", controls.SeatInReach, "PlayerControls.SeatInReach beside it");

            sitter.Sit(seat);
            Cut(seat.Foot + seat.Facing * V(1.6f, 1.4f, 2.4f), seat.SeatPoint);
            yield return Wait(1.5f);
            var foot = wizard.transform.position - seat.Foot;
            Check($"{kind}_seated", sitter.Seated == seat && foot.magnitude < 0.1f, $"feet {foot.magnitude:0.00} m from the seat's");
            Check($"{kind}_pose", pose.Weight > 0.95f, $"pose weight {pose.Weight:0.00}");
            float hipsAbove = hips.position.y - seat.SeatPoint.y;
            Metric($"{kind}_hips_above_seat_m", hipsAbove);
            Check($"{kind}_hips_on_seat", Mathf.Abs(hipsAbove) < 0.2f, $"hips {hipsAbove:0.00} m above the seat point");
            Shot(kind);

            sitter.Stand();
            yield return Wait(1f);
            var away = wizard.transform.position - seat.Foot;
            away.y = 0f;
            Check($"{kind}_stood_up", sitter.Seated == null && pose.Weight < 0.5f && away.magnitude > 0.15f, $"{away.magnitude:0.00} m from the seat, pose {pose.Weight:0.00}");
        }
    }

    // The bench or lounger nearest the yard.
    static Seat Pick(bool lounge)
    {
        Seat best = null;
        foreach (var seat in FindObjectsByType<Seat>(FindObjectsSortMode.None))
            if (seat.lounge == lounge && (best == null || seat.Foot.sqrMagnitude < best.Foot.sqrMagnitude)) best = seat;
        return best;
    }
}
