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
    public void ThePadsPunchGivesWayToAnItemOrASeat()
    {
        Assert.That(PlayerControls.PunchGivesWay(onPad: true, itemInReach: true, seatInReach: false), Is.True);
        Assert.That(PlayerControls.PunchGivesWay(onPad: true, itemInReach: false, seatInReach: true), Is.True);
        Assert.That(PlayerControls.PunchGivesWay(onPad: true, itemInReach: false, seatInReach: false), Is.False);
        Assert.That(PlayerControls.PunchGivesWay(onPad: false, itemInReach: true, seatInReach: true), Is.False, "F is the punch's alone");
    }
}
