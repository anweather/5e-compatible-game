using UnityEngine;

// A short casting gesture laid over the character's animation: the right
// arm swings up to point at the target and the hand glows in the spell's
// colour, then the arm settles back. Runs after the Animator (LateUpdate),
// so it works on any humanoid without editing the animation controller.
// Every machine plays it for every caster, from the cast announcement.
[RequireComponent(typeof(Animator))]
public class CastPose : MonoBehaviour
{
    const float Duration = 0.7f;
    const float Rise = 0.12f;

    Animator animator;
    Transform upperArm, lowerArm, hand;
    Light glow;
    Vector3 target;
    float started = -1f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        glow = new GameObject("Cast glow").AddComponent<Light>();
        glow.transform.SetParent(hand, false);
        glow.type = LightType.Point;
        glow.range = 3f;
        glow.enabled = false;
    }

    /// <summary>The hand's position, where projectiles start; the chest if the rig has no hand.</summary>
    public Vector3 HandPosition => hand != null ? hand.position : transform.position + Vector3.up * 1.3f;

    public void Play(Vector3 at, Color color)
    {
        target = at;
        started = Time.time;
        if (glow == null) return;
        glow.color = color;
        glow.enabled = true;
    }

    void LateUpdate()
    {
        if (started < 0f || upperArm == null || lowerArm == null || hand == null) return;
        float t = Time.time - started;
        if (t > Duration)
        {
            started = -1f;
            glow.enabled = false;
            return;
        }

        // Up quickly, hold, ease back down.
        float weight = t < Rise ? t / Rise : 1f - Mathf.SmoothStep(0f, 1f, (t - Rise) / (Duration - Rise));
        PointAt(upperArm, lowerArm.position, weight);
        PointAt(lowerArm, hand.position, weight);
        glow.intensity = 4f * weight;
    }

    // Rotate a bone so the segment from it to its child points at the target, blended by weight.
    void PointAt(Transform bone, Vector3 childPosition, float weight)
    {
        var current = childPosition - bone.position;
        var wanted = target - bone.position;
        if (current.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f) return;
        var turn = Quaternion.FromToRotation(current, wanted);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * bone.rotation;
    }
}
