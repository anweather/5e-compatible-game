using UnityEngine;

// Swimming, laid over the character's animation with LimbIK (the Starter
// Assets have no swim animation). In water deep enough to float in (the same
// depth SurfaceEffects floats the wizard at), the body leans forward into a
// breaststroke while it moves, the arms reaching, sweeping out and pulling
// back and the legs drawing up and kicking out half a stroke behind; held
// still it treads water upright, sculling. The stroke follows the liquid's
// SwimStyle. Every machine works it out for every wizard from where the
// wizard is and the shared water, so other players are seen swimming too.
[DefaultExecutionOrder(150)]
[RequireComponent(typeof(Animator))]
public class SwimPose : MonoBehaviour
{
    const float SwimDepth = 1.15f;
    const float Ease = 3f;

    ChemGrid grid;
    Animator animator;
    Transform hips, chest;
    LimbIK.Limb leftArm, rightArm, leftLeg, rightLeg;
    Vector3 lastPosition;
    float weight, moving, phase;

    public float Weight => weight;

    void Awake()
    {
        animator = GetComponent<Animator>();
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        leftArm = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightArm = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        leftLeg = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        rightLeg = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (grid == null) grid = ChemGrid.Current?.Grid;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        moving = Mathf.MoveTowards(moving, Mathf.Clamp01(moved.magnitude / dt / 1.2f), dt * 2f);
        weight = Mathf.MoveTowards(weight, InDeepWater() ? 1f : 0f, dt * Ease);
        if (weight <= 0f || hips == null || chest == null) return;

        var style = SwimStyle.For("water");
        phase = (phase + dt / Mathf.Lerp(style.treadSeconds, style.strokeSeconds, moving)) % 1f;
        Lean(style);
        Arm(leftArm, -1f, style);
        Arm(rightArm, 1f, style);
        Leg(leftLeg, -1f, style);
        Leg(rightLeg, 1f, style);
    }

    bool InDeepWater()
    {
        if (grid == null || grid.World == null) return false;
        var cell = grid.CellAt(transform.position);
        var c = grid.World.At(cell.x, cell.y);
        return c != null && !c.Frozen && c.Water > 0 && grid.WaterSurfaceY(cell.x, cell.y) - transform.position.y > SwimDepth;
    }

    void Lean(SwimStyle style)
    {
        float lean = style.lean * moving * weight;
        hips.SetPositionAndRotation(hips.position + Vector3.up * (style.lift * moving * weight), Quaternion.AngleAxis(lean, transform.right) * hips.rotation);
    }

    void Arm(LimbIK.Limb arm, float side, SwimStyle style)
    {
        if (!arm.Valid) return;
        var up = chest.up;
        var right = transform.right * side;
        var front = Vector3.Cross(transform.right, up);
        var shoulder = arm.root.position;

        float t = phase * Mathf.PI * 2f;
        var stroke = shoulder
            + up * (style.reach * (0.45f + 0.55f * Mathf.Cos(t)))
            + right * (0.08f + style.sweep * Mathf.Max(0f, Mathf.Sin(t)))
            + front * 0.22f;
        var tread = shoulder
            + front * (0.3f + 0.08f * Mathf.Sin(t))
            + right * (0.3f + 0.1f * Mathf.Cos(t))
            - up * 0.25f;
        var target = Vector3.Lerp(tread, stroke, moving);
        var elbow = shoulder + right * 0.5f - up * 0.2f - front * 0.1f;
        LimbIK.Solve(arm, target, elbow, weight);
    }

    void Leg(LimbIK.Limb leg, float side, SwimStyle style)
    {
        if (!leg.Valid) return;
        var down = -hips.up;
        var right = transform.right * side;
        var front = Vector3.Cross(transform.right, hips.up);
        var hip = leg.root.position;
        float drawn = 0.5f + 0.5f * Mathf.Sin((phase + 0.5f) * Mathf.PI * 2f);
        float length = Vector3.Distance(leg.root.position, leg.mid.position) + Vector3.Distance(leg.mid.position, leg.end.position);
        var target = hip
            + down * (length * 0.97f - style.kick * drawn * Mathf.Lerp(0.4f, 1f, moving))
            + right * (0.1f + 0.22f * drawn * moving)
            - front * (0.12f * drawn);
        var knee = hip + down * 0.4f + right * 0.3f + front * 0.3f;
        LimbIK.Solve(leg, target, knee, weight);
    }
}
