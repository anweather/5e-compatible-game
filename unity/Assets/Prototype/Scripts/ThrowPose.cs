using UnityEngine;

// Throwing, laid over the character's animation with LimbIK: while Throw is
// held the right hand draws back over the shoulder, further the longer it
// winds up (ItemHolder.WindUp), and on the throw the arm whips through,
// forward and down, then eases back; the item goes with the hand, and leaves
// it a moment into the whip (ItemHolder). Only the thrower's own machine knows
// the wind-up, so other players see the item fly without the arm.
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(Animator), typeof(ItemHolder))]
public class ThrowPose : MonoBehaviour
{
    const float FollowSeconds = 0.35f;

    ItemHolder hands;
    LimbIK.Limb arm;
    float thrownAt = -1f, thrownWith;

    void Awake()
    {
        hands = GetComponent<ItemHolder>();
        arm = LimbIK.Limb.Of(GetComponent<Animator>(), HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        hands.Thrown += wound =>
        {
            thrownAt = Time.time;
            thrownWith = wound;
        };
    }

    void LateUpdate()
    {
        if (!arm.Valid) return;
        var shoulder = arm.root.position;
        var forward = transform.forward;
        var right = transform.right;
        float wind = hands.Held != null ? hands.WindUp : 0f;
        if (wind > 0f)
        {
            // Drawn back over the shoulder, the elbow up and out.
            var back = shoulder - forward * (0.15f + 0.3f * wind) + Vector3.up * (0.1f + 0.2f * wind) + right * 0.15f;
            LimbIK.Solve(arm, back, shoulder + right * 0.4f + Vector3.up * 0.1f - forward * 0.1f, Mathf.Clamp01(wind * 3f));
            hands.HoldInHand();
            return;
        }
        if (thrownAt < 0f) return;
        float t = (Time.time - thrownAt) / FollowSeconds;
        if (t >= 1f)
        {
            thrownAt = -1f;
            return;
        }
        // Whipped through: up and forward at the release, then down across the body.
        var through = Vector3.Lerp(shoulder + forward * 0.6f + Vector3.up * 0.15f, shoulder + forward * 0.45f - Vector3.up * 0.35f - right * 0.2f, t);
        float weight = Mathf.Lerp(0.6f, 1f, thrownWith) * (1f - Mathf.SmoothStep(0.5f, 1f, t));
        LimbIK.Solve(arm, through, shoulder + right * 0.4f - Vector3.up * 0.2f, weight);
        // Still in the hand for the start of the whip (ItemHolder lets go a moment in).
        hands.HoldInHand();
    }
}
