using NUnit.Framework;
using UnityEngine;

/// <summary>How a body goes with moving ground (ChemGrid.CarryBody): the rule a land stamp and a terraform spell share.</summary>
public class LandCarryTests
{
    GameObject go;
    Rigidbody body;

    [SetUp]
    public void Make()
    {
        go = new GameObject("Crate");
        body = go.AddComponent<Rigidbody>();
        body.position = new Vector3(0f, 10.5f, 0f);
    }

    [TearDown]
    public void Clear() => Object.DestroyImmediate(go);

    [Test]
    public void OnRisingGroundItGoesUpWithIt()
    {
        body.linearVelocity = new Vector3(1f, -2f, 0f);
        ChemGrid.CarryBody(body, 10f, 10f, 10.25f);
        Assert.That(body.position.y, Is.EqualTo(10.75f).Within(1e-4f));
        Assert.That(body.linearVelocity, Is.EqualTo(new Vector3(1f, 0f, 0f)), "it stops falling, and a push along the ground stays");
    }

    [Test]
    public void OverSinkingGroundPhysicsDropsIt()
    {
        ChemGrid.CarryBody(body, 10f, 10f, 9.75f);
        Assert.That(body.position.y, Is.EqualTo(10.5f).Within(1e-4f), "it falls by itself");
        Assert.That(body.IsSleeping(), Is.False);
    }

    [Test]
    public void InTheAirItIsLeftToFly()
    {
        body.linearVelocity = Vector3.up * 3f;
        ChemGrid.CarryBody(body, 12f, 10f, 10.25f);
        Assert.That(body.position.y, Is.EqualTo(10.5f).Within(1e-4f));
        Assert.That(body.linearVelocity, Is.EqualTo(Vector3.up * 3f));
    }

    [TestCase(10.25f)]
    [TestCase(9.75f)]
    public void AsleepWithItsChunkItMovesWithTheGroundBothWays(float now)
    {
        body.isKinematic = true;
        ChemGrid.CarryBody(body, 10f, 10f, now, asleep: true);
        Assert.That(body.position.y, Is.EqualTo(10.5f + now - 10f).Within(1e-4f));
    }

    [Test]
    public void AsleepInTheAirItStays()
    {
        body.isKinematic = true;
        ChemGrid.CarryBody(body, 12f, 10f, 9.75f, asleep: true);
        Assert.That(body.position.y, Is.EqualTo(10.5f).Within(1e-4f));
    }
}
