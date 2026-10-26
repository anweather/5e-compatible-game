using NUnit.Framework;
using UnityEngine;

// The two-bone IK solver (LimbIK) on a bare chain of transforms: an arm
// hanging straight down, 0.3 m upper and 0.3 m lower.
public class LimbIKTests
{
    GameObject root;
    LimbIK.Limb limb;

    [SetUp]
    public void Chain()
    {
        root = new GameObject("Shoulder");
        var mid = new GameObject("Elbow").transform;
        mid.SetParent(root.transform, false);
        mid.localPosition = new Vector3(0f, -0.3f, 0f);
        var end = new GameObject("Hand").transform;
        end.SetParent(mid, false);
        end.localPosition = new Vector3(0f, -0.3f, 0f);
        limb = new LimbIK.Limb(root.transform, mid, end);
    }

    [TearDown]
    public void Clear() => Object.DestroyImmediate(root);

    [Test]
    public void TheHandReachesATargetInReachWithTheElbowTowardTheHint()
    {
        var target = new Vector3(0.35f, -0.2f, 0.1f);
        LimbIK.Solve(limb, target, new Vector3(0f, 0f, 1f), 1f);
        Assert.That(Vector3.Distance(limb.end.position, target), Is.LessThan(1e-3f), "on the target");
        Assert.That(Vector3.Distance(limb.root.position, limb.mid.position), Is.EqualTo(0.3f).Within(1e-4f), "the bones keep their length");
        Assert.That(limb.mid.position.z, Is.GreaterThan(0.1f), "the elbow bends toward the hint");
    }

    [Test]
    public void AFarTargetStraightensTheArmTowardIt()
    {
        LimbIK.Solve(limb, new Vector3(3f, 0f, 0f), new Vector3(0f, -1f, 0f), 1f);
        Assert.That(limb.end.position.x, Is.EqualTo(0.6f).Within(2e-3f), "as far as it reaches");
        Assert.That(Mathf.Abs(limb.end.position.y), Is.LessThan(0.03f), "along the line to the target");
    }

    [Test]
    public void WeightZeroLeavesThePoseAndHalfBlends()
    {
        var target = new Vector3(0.3f, -0.3f, 0f);
        LimbIK.Solve(limb, target, Vector3.forward, 0f);
        Assert.That(limb.end.position, Is.EqualTo(new Vector3(0f, -0.6f, 0f)));
        LimbIK.Solve(limb, target, Vector3.forward, 0.5f);
        float d = Vector3.Distance(limb.end.position, target);
        Assert.That(d, Is.GreaterThan(1e-3f).And.LessThan(Vector3.Distance(new Vector3(0f, -0.6f, 0f), target)), "part way there");
    }
}
