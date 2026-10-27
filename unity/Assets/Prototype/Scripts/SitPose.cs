using UnityEngine;

// Sitting, laid over the animation with LimbIK (the Starter Assets have no
// sitting animation). A wizard whose feet are where a Seat's sitter's go is
// sitting on it: the hips drop onto the seat, the legs bend with the feet on
// the ground (or stretch out along a lounger, the back lying against it) and
// the hands rest on the thighs (or go behind the head on a lounger). Every
// machine works it out from where the wizard is, like SwimPose, so other
// players are seen sitting too.
[DefaultExecutionOrder(160)]
[RequireComponent(typeof(Animator))]
public class SitPose : MonoBehaviour
{
    const float Ease = 4f;

    Animator animator;
    Transform hips, head;
    LimbIK.Limb leftArm, rightArm, leftLeg, rightLeg;
    Seat seat;
    float weight;

    /// <summary>How far into the sitting pose the body is, 0 to 1.</summary>
    public float Weight => weight;

    void Awake()
    {
        animator = GetComponent<Animator>();
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        leftArm = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightArm = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        leftLeg = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        rightLeg = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
    }

    void LateUpdate()
    {
        var on = Seat.SittingAt(transform.position);
        if (on != null) seat = on;
        weight = Mathf.MoveTowards(weight, on != null ? 1f : 0f, Time.deltaTime * Ease);
        if (weight <= 0f || seat == null || hips == null) return;

        // The hips down onto the seat, tipped back to lie on a lounger.
        var right = transform.right;
        var forward = transform.forward;
        var seatHips = seat.SeatPoint + Vector3.up * 0.06f;
        // A lounger's back leans 55 degrees up from flat (PropMesher.Lounger): lie against it.
        float recline = seat.lounge ? -35f : -6f;
        hips.SetPositionAndRotation(
            Vector3.Lerp(hips.position, seatHips, weight),
            Quaternion.Slerp(hips.rotation, Quaternion.AngleAxis(recline, right) * hips.rotation, weight));

        Leg(leftLeg, -1f, forward, right);
        Leg(rightLeg, 1f, forward, right);
        Arm(leftArm, -1f, forward, right);
        Arm(rightArm, 1f, forward, right);
    }

    // Knees bent with the feet on the ground in front of the seat; on a lounger, straight out along it.
    void Leg(LimbIK.Limb leg, float side, Vector3 forward, Vector3 right)
    {
        if (!leg.Valid) return;
        var hip = leg.root.position;
        Vector3 foot, knee;
        if (seat.lounge)
        {
            foot = hip + forward * 0.82f + right * (side * 0.08f) - Vector3.up * 0.04f;
            knee = hip + forward * 0.4f + Vector3.up * 0.25f;
        }
        else
        {
            foot = new Vector3(0f, seat.Foot.y + 0.08f, 0f) + Flat(hip + forward * 0.45f + right * (side * 0.06f));
            knee = hip + forward * 0.5f + Vector3.up * 0.1f;
        }
        LimbIK.Solve(leg, foot, knee, weight);
    }

    // Hands on the thighs; on a lounger, clasped behind the head with the elbows out.
    void Arm(LimbIK.Limb arm, float side, Vector3 forward, Vector3 right)
    {
        if (!arm.Valid) return;
        var shoulder = arm.root.position;
        Vector3 hand, elbow;
        if (seat.lounge && head != null)
        {
            hand = head.position - hips.up * 0.02f + right * (side * 0.06f) - forward * 0.1f;
            elbow = shoulder + right * (side * 0.35f) + hips.up * 0.25f;
        }
        else
        {
            hand = hips.position + forward * 0.32f + right * (side * 0.16f) + Vector3.up * 0.06f;
            elbow = shoulder + right * (side * 0.25f) - Vector3.up * 0.2f;
        }
        LimbIK.Solve(arm, hand, elbow, weight);
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
