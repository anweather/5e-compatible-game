using NUnit.Framework;
using UnityEngine;

// What can be checked of moving without Play mode: the slide on ice and who gets the pad's punch button.
public class MovingTests
{
    [Test]
    public void AGlideKeepsItsSpeedGoingOn()
    {
        var run = Vector3.forward * 6f;
        var after = SurfaceEffects.Glide(run, run, 1.2f, 0.016f);
        Assert.That(after.magnitude, Is.EqualTo(6f).Within(1e-4f));
    }

    [Test]
    public void AGlideIsSlowToStopAndToTurn()
    {
        var run = Vector3.forward * 6f;
        var stopping = run;
        for (int frame = 0; frame < 30; frame++) stopping = SurfaceEffects.Glide(stopping, Vector3.zero, 1.2f, 1f / 60f);
        Assert.That(stopping.magnitude, Is.EqualTo(6f * Mathf.Exp(-0.6f)).Within(0.01f), "half a second on, over half the speed is left");
        var turning = SurfaceEffects.Glide(run, Vector3.right * 6f, 1.2f, 1f / 60f);
        Assert.That(turning.z, Is.GreaterThan(5.8f), "still going the old way");
    }

    [Test]
    public void AGlideDoesntDependOnTheFrameRate()
    {
        var run = Vector3.forward * 6f;
        var slow = SurfaceEffects.Glide(run, Vector3.zero, 1.2f, 0.1f);
        var fast = run;
        for (int frame = 0; frame < 10; frame++) fast = SurfaceEffects.Glide(fast, Vector3.zero, 1.2f, 0.01f);
        Assert.That(fast.magnitude, Is.EqualTo(slow.magnitude).Within(1e-3f));
    }

    [Test]
    public void ThePunchIsOnCircleAndSquareOnlyPicksUp()
    {
        Assert.That(PlayerControls.PadPunch, Is.EqualTo("<Gamepad>/buttonEast"));
        Assert.That(PlayerControls.PadInteract, Is.EqualTo("<Gamepad>/buttonWest"));
    }

    [Test]
    public void CirclePunchesUnlessItHasACastToCancel()
    {
        Assert.That(ItemHolder.MayPunch(cancelling: false, animal: false, swimming: false, seated: false), Is.True);
        Assert.That(ItemHolder.MayPunch(cancelling: true, animal: false, swimming: false, seated: false), Is.False);
        Assert.That(ItemHolder.MayPunch(cancelling: false, animal: true, swimming: false, seated: false), Is.False);
        Assert.That(ItemHolder.MayPunch(cancelling: false, animal: false, swimming: true, seated: false), Is.False);
        Assert.That(ItemHolder.MayPunch(cancelling: false, animal: false, swimming: false, seated: true), Is.False);
    }
}
