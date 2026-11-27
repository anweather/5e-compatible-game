using UnityEngine;

// Two-bone inverse kinematics for a humanoid limb (shoulder, elbow, hand or
// hip, knee, foot), solved on the bones' transforms after the Animator has
// posed them, so a pose can be laid over any animation without editing the
// animation controller (SwimPose, CastPose, ThrowPose, PunchPose). The elbow or knee
// bends in the plane of the hint, a point it should point toward. Weight 0
// leaves the animation as it was; 1 puts the end exactly on the target, or as
// near as the limb reaches.
public static class LimbIK
{
    public readonly struct Limb
    {
        public readonly Transform root, mid, end;

        public Limb(Transform root, Transform mid, Transform end)
        {
            this.root = root;
            this.mid = mid;
            this.end = end;
        }

        public bool Valid => root != null && mid != null && end != null;

        public static Limb Of(Animator animator, HumanBodyBones root, HumanBodyBones mid, HumanBodyBones end) =>
            new Limb(animator.GetBoneTransform(root), animator.GetBoneTransform(mid), animator.GetBoneTransform(end));
    }

    public static void Solve(Limb limb, Vector3 target, Vector3 hint, float weight)
    {
        if (!limb.Valid || weight <= 0f) return;
        weight = Mathf.Min(weight, 1f);
        var rootWas = limb.root.rotation;
        var midWas = limb.mid.rotation;

        Vector3 a = limb.root.position, b = limb.mid.position, c = limb.end.position;
        float ab = (b - a).magnitude, bc = (c - b).magnitude;
        if (ab < 1e-4f || bc < 1e-4f) return;
        var at = target - a;
        float reach = Mathf.Clamp(at.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);

        var axis = Vector3.Cross(b - a, c - b);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(b - a, hint - a);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(b - a, Vector3.up);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(b - a, Vector3.right);
        float now = Angle((c - a).magnitude, ab, bc), wanted = Angle(reach, ab, bc);
        limb.mid.rotation = Quaternion.AngleAxis((now - wanted) * Mathf.Rad2Deg, axis.normalized) * limb.mid.rotation;

        c = limb.end.position;
        limb.root.rotation = Quaternion.FromToRotation(c - a, at) * limb.root.rotation;

        b = limb.mid.position;
        var line = at.normalized;
        var bent = Vector3.ProjectOnPlane(b - a, line);
        var toward = Vector3.ProjectOnPlane(hint - a, line);
        if (bent.sqrMagnitude > 1e-6f && toward.sqrMagnitude > 1e-6f)
            limb.root.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(bent, toward, line), line) * limb.root.rotation;

        if (weight >= 1f) return;
        limb.root.rotation = Quaternion.Slerp(rootWas, limb.root.rotation, weight);
        limb.mid.rotation = Quaternion.Slerp(midWas, limb.mid.rotation, weight);
    }

    static float Angle(float opposite, float s1, float s2) =>
        Mathf.Acos(Mathf.Clamp((s1 * s1 + s2 * s2 - opposite * opposite) / (2f * s1 * s2), -1f, 1f));
}
