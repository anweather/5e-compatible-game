using UnityEngine;

// A dodge roll's tumble, laid over the character's animation (the Starter
// Assets have no roll clip): the body tucks into a ball (spine and neck bent
// forward, knees drawn up with LimbIK, hands to the shins), drops so the ball
// sits on the ground, and turns once head over heels about the wizard's side,
// quickest in the middle, then opens out upright again. DodgeRoll plays it on
// the roller's machine, and NetWizard on everyone else's, so other players see
// it roll too. Runs after the Animator and before the held item follows the
// hand (ItemHolder).
[DefaultExecutionOrder(112)]
[RequireComponent(typeof(Animator))]
public class RollPose : MonoBehaviour
{
    // One turn; the tuck takes this long at each end (s); the ball's radius (m).
    const float Turn = 360f, TuckSeconds = 0.09f, Radius = 0.5f;
    // How far the spine, chest and neck each bend forward when tucked (degrees).
    const float Bend = 30f;

    Transform hips, spine, chest, neck, head;
    LimbIK.Limb leftArm, rightArm, leftLeg, rightLeg;
    float started = -10f, duration = 0.45f;

    /// <summary>True while the tumble plays.</summary>
    public bool Playing => Time.time - started < duration;

    /// <summary>How far round the body has turned this roll (degrees, 0 to 360).</summary>
    public float Angle { get; private set; }

    void Awake()
    {
        var animator = GetComponent<Animator>();
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        leftArm = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightArm = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        leftLeg = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        rightLeg = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
    }

    /// <summary>Plays the tumble over <paramref name="seconds"/>.</summary>
    public void Play(float seconds)
    {
        duration = Mathf.Max(0.1f, seconds);
        started = Time.time;
    }

    void LateUpdate()
    {
        float t = Time.time - started;
        if (t >= duration || t < 0f || hips == null)
        {
            Angle = 0f;
            return;
        }
        float u = t / duration;
        float tuck = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, duration - t) / TuckSeconds);
        // Slow in, quick through the middle, slow out: a whole turn by the end.
        Angle = Turn * (u - Mathf.Sin(2f * Mathf.PI * u) / (2f * Mathf.PI));
        Curl(tuck);
        Tumble(tuck);
    }

    void Curl(float tuck)
    {
        var side = transform.right;
        BendForward(spine, side, tuck);
        BendForward(chest, side, tuck);
        BendForward(neck, side, tuck);
        Leg(leftLeg, tuck);
        Leg(rightLeg, tuck);
        Arm(leftArm, -1f, tuck);
        Arm(rightArm, 1f, tuck);
    }

    static void BendForward(Transform bone, Vector3 side, float tuck)
    {
        if (bone != null) bone.rotation = Quaternion.AngleAxis(Bend * tuck, side) * bone.rotation;
    }

    // Knees drawn up toward the chest.
    void Leg(LimbIK.Limb leg, float tuck)
    {
        if (!leg.Valid) return;
        var hip = leg.root.position;
        var target = hip + transform.forward * 0.25f - transform.up * 0.3f;
        var knee = hip + transform.forward * 0.6f + transform.up * 0.1f;
        LimbIK.Solve(leg, target, knee, tuck);
    }

    // Hands to the shins, elbows out. side is -1 left, 1 right.
    void Arm(LimbIK.Limb arm, float side, float tuck)
    {
        if (!arm.Valid) return;
        var shoulder = arm.root.position;
        var target = shoulder + transform.forward * 0.35f - transform.up * 0.45f + transform.right * (side * 0.05f);
        var elbow = shoulder + transform.right * (side * 0.35f) - transform.up * 0.2f + transform.forward * 0.1f;
        LimbIK.Solve(arm, target, elbow, tuck * 0.9f);
    }

    // The tucked body, lowered to sit on the ground as a ball and turned about its middle.
    void Tumble(float tuck)
    {
        var up = transform.up;
        var top = head != null ? head.position : hips.position + up * 0.6f;
        var middle = (hips.position + top) * 0.5f;
        float height = Vector3.Dot(middle - transform.position, up);
        var lowered = middle + up * (Mathf.Lerp(height, Radius, tuck) - height);
        var spin = Quaternion.AngleAxis(Angle, transform.right);
        hips.SetPositionAndRotation(lowered + spin * (hips.position - middle), spin * hips.rotation);
    }
}
