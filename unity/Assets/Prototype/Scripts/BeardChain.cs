using UnityEngine;

// The beard's spring chains (WizardOutfit builds the beard's tufts skinned to
// them): a few short chains hanging from the chin, two joints each, moved by
// small position-based steps at 60 a second. Each joint falls with gravity, is
// pulled back toward where the head would hold it, keeps its length from the
// one above, and stays out of spheres on the chest, so the beard sways with a
// walk and lies on the chest when the wizard lies back, never inside the
// bodice. It snaps back after a jump of more than 2 m. View only, like the
// robe's cloth (RobeCloth).
[DefaultExecutionOrder(305)]
public class BeardChain : MonoBehaviour
{
    const float Step = 1f / 60f, Gravity = 9.81f;
    const int MaxStepsPerFrame = 3;
    // The velocity kept per step, how much of the way back to its rest place a joint goes each step,
    // and the hair's half-thickness against the chest.
    const float Damping = 0.88f, Keep = 0.1f, Skin = 0.02f;
    const float SnapDistance = 2f;

    Transform head;
    // Per chain: its root on the head (in the head's space), then its joints.
    Vector3[] roots;
    Transform[] joints;
    Vector3[] rest;          // each joint's place in the head's space
    float[] lengths;         // from the joint above (or the root)
    Vector3[] at, before;    // in the world
    (Transform bone, Vector3 offset, float radius)[] chest;
    float clock;
    Vector3 lastHead;

    /// <summary>
    /// Sets up the chains: for each, its root on the head and its joints (two each, in order; all in
    /// the head's space as built), and the spheres on the chest it keeps out of (a bone, an offset in
    /// the bone's space, a radius).
    /// </summary>
    public void Init(Transform head, Vector3[] roots, Transform[] joints, (Transform bone, Vector3 offset, float radius)[] chest)
    {
        this.head = head;
        this.roots = roots;
        this.joints = joints;
        this.chest = chest;
        rest = new Vector3[joints.Length];
        lengths = new float[joints.Length];
        at = new Vector3[joints.Length];
        before = new Vector3[joints.Length];
        for (int j = 0; j < joints.Length; j++)
        {
            rest[j] = head.InverseTransformPoint(joints[j].position);
            var above = j % 2 == 0 ? head.TransformPoint(roots[j / 2]) : joints[j - 1].position;
            lengths[j] = Vector3.Distance(above, joints[j].position);
        }
        Snap();
    }

    void Snap()
    {
        for (int j = 0; j < joints.Length; j++) at[j] = before[j] = head.TransformPoint(rest[j]);
        lastHead = head.position;
    }

    void LateUpdate()
    {
        if (head == null) return;
        if (Vector3.Distance(head.position, lastHead) > SnapDistance) Snap();
        lastHead = head.position;
        clock += Mathf.Min(Time.deltaTime, Step * MaxStepsPerFrame);
        while (clock >= Step)
        {
            clock -= Step;
            Simulate();
        }
        for (int j = 0; j < joints.Length; j++) joints[j].position = at[j];
    }

    void Simulate()
    {
        var fall = Vector3.down * (Gravity * Step * Step);
        for (int j = 0; j < joints.Length; j++)
        {
            var v = (at[j] - before[j]) * Damping;
            before[j] = at[j];
            at[j] += v + fall + (head.TransformPoint(rest[j]) - at[j]) * Keep;
            var above = j % 2 == 0 ? head.TransformPoint(roots[j / 2]) : at[j - 1];
            var d = at[j] - above;
            if (d.sqrMagnitude > 1e-8f) at[j] = above + d.normalized * lengths[j];
            foreach (var (bone, offset, radius) in chest)
            {
                var centre = bone.TransformPoint(offset);
                var away = at[j] - centre;
                float min = radius + Skin;
                if (away.sqrMagnitude < min * min && away.sqrMagnitude > 1e-8f) at[j] = centre + away.normalized * min;
            }
        }
    }
}
