using System;
using NUnit.Framework;

// A probe doesn't enter Play mode while an EditMode run is going (#174): a run that timed out at the CLI goes on, and
// when it ends it restores its scene, which throws in Play mode and leaves an empty scene. These tests are such a run.
public class ProbeStartTests
{
    [Test]
    public void TheProbeSeesTheTestRunItIsIn() => Assert.That(ProbeCommands.TestRunActive(), Is.True);

    [Test]
    public void AProbeWontStartDuringATestRun()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ProbeCommands.Run("smoke"));
        Assert.That(refused.Message, Does.Contain("test run"));
    }
}
