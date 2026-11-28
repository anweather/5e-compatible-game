using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

// The tree impostors' trial (docs/design/tree-impostors.md, J-090): the views' directions the baker and the shader share,
// the tree placement the ground and the impostors share, and that it's off unless asked for.
public class TreeImpostorTests
{
    [Test]
    public void EveryViewEncodesBackToItsGridPoint()
    {
        const int frames = 12;
        for (int j = 0; j < frames; j++)
        {
            for (int i = 0; i < frames; i++)
            {
                var c = TreeImpostorMath.Encode(TreeImpostorMath.FrameDir(i, j, frames));
                Assert.That(c.x, Is.EqualTo(i / (frames - 1f) * 2f - 1f).Within(1e-4f), $"view ({i}, {j})");
                Assert.That(c.y, Is.EqualTo(j / (frames - 1f) * 2f - 1f).Within(1e-4f), $"view ({i}, {j})");
            }
        }
    }

    [Test]
    public void TheGridsEdgeIsTheHorizonAndItsMiddleStraightUp()
    {
        Assert.That(TreeImpostorMath.Decode(Vector2.zero).y, Is.EqualTo(1f).Within(1e-5f));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, 0) })
            Assert.That(TreeImpostorMath.Decode(corner).y, Is.EqualTo(0f).Within(1e-5f), corner.ToString());
        Assert.That(TreeImpostorMath.Encode(new Vector3(1f, -0.5f, 0f)), Is.EqualTo(TreeImpostorMath.Encode(Vector3.right)));
    }

    [Test]
    public void ATreeCellsSpotIsTheSameEveryTimeAndInsideItsCell()
    {
        for (int x = 0; x < 50; x++)
        {
            var a = GroundFromLand.Spot(1234, x, 7, 2);
            var b = GroundFromLand.Spot(1234, x, 7, 2);
            Assert.That(a.Inset, Is.EqualTo(b.Inset));
            Assert.That(a.Kind, Is.EqualTo(b.Kind));
            Assert.That(a.Inset, Is.InRange(0.2f, 0.8f));
            Assert.That(a.Across, Is.InRange(0.2f, 0.8f));
            Assert.That(a.Size, Is.InRange(0.85f, 1.35f));
            Assert.That(a.Kind, Is.InRange(0, 1));
        }
    }

    [Test]
    public void ImpostorsAreOnUnlessTurnedOff()
    {
        TreeImpostors.Reset();
        Assert.That(TreeImpostors.On, Is.True);
        Assert.That(TreeImpostors.ShadowFar, Is.EqualTo(256f));
        Assert.That(MapTerrains.TreeDistance, Is.EqualTo(900f));
    }

    [Test]
    public void WithNoGpuAMissingImpostorIsRefusedByName()
    {
        string refused = TreeImpostorBaker.NoGpuRefusal(new[] { "Conifer", "Cypress" }, GraphicsDeviceType.Null);
        Assert.That(refused, Does.StartWith("TREE IMPOSTORS: FAILED"));
        Assert.That(refused, Does.Contain("Conifer, Cypress"));
    }

    [Test]
    public void WithNoGpuButNothingToBakeThereIsNoRefusal() =>
        Assert.That(TreeImpostorBaker.NoGpuRefusal(new string[0], GraphicsDeviceType.Null), Is.Null);

    [Test]
    public void WithAGpuAMissingImpostorIsBaked() =>
        Assert.That(TreeImpostorBaker.NoGpuRefusal(new[] { "Conifer" }, GraphicsDeviceType.Metal), Is.Null);

    [Test]
    public void ThePlayerKeepsTheBudget()
    {
        Assert.That(TreeImpostors.Budget, Is.True, "the tree budget is on unless -no-tree-budget");
    }
}
