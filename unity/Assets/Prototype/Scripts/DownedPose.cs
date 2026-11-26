using UnityEngine;

// A wizard down at 0 health (WizardDown), laid over the character's animation
// as RollPose is: the body tips over backwards about its side and comes to lie
// flat on the ground, and gets up the same way. Set on the wizard's own machine
// by WizardDown and on every other by NetWizard.Downed. Runs after the Animator
// and before the held item follows the hand (ItemHolder).
[DefaultExecutionOrder(112)]
[RequireComponent(typeof(Animator))]
public class DownedPose : MonoBehaviour
{
    const float FallSeconds = 0.35f, Over = -88f, LyingHeight = 0.2f;

    Transform hips, head;
    float weight;

    public bool Lying { get; set; }

    public float Weight => weight;

    void Awake()
    {
        var animator = GetComponent<Animator>();
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void LateUpdate()
    {
        weight = Mathf.MoveTowards(weight, Lying ? 1f : 0f, Time.deltaTime / FallSeconds);
        if (weight <= 0f || hips == null) return;
        float w = Mathf.SmoothStep(0f, 1f, weight);
        var up = transform.up;
        var top = head != null ? head.position : hips.position + up * 0.6f;
        var middle = (hips.position + top) * 0.5f;
        float height = Vector3.Dot(middle - transform.position, up);
        var lowered = middle + up * (Mathf.Lerp(height, LyingHeight, w) - height);
        var tip = Quaternion.AngleAxis(Over * w, transform.right);
        hips.SetPositionAndRotation(lowered + tip * (hips.position - middle), tip * hips.rotation);
    }
}
