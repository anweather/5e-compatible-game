using NUnit.Framework;

// BuildContents: a headless Windows build only counts with its PSO collection and shaders inside.
public class BuildContentsTests
{
    const string Pso = "Assets/Prototype/Scripts/Perf/Resources/PsoDirect3D12.graphicsstate";
    const string OtherPso = "Assets/Prototype/Scripts/Perf/Resources/PsoMetal.graphicsstate";

    [Test]
    public void CompleteWithTheCollectionAndShaders()
    {
        var summary = BuildContents.Of("PsoDirect3D12", new[] { (Pso, "GraphicsStateCollection", 500UL), ("a.shader", "Shader", 1048576UL) });
        Assert.That(summary.Complete, Is.True);
        Assert.That(summary.Line, Does.Contain("PsoDirect3D12 500 bytes").And.Contain("1 shaders 1.0 MB"));
    }

    [Test]
    public void IncompleteWithAnotherPlatformsCollection()
    {
        var summary = BuildContents.Of("PsoDirect3D12", new[] { (OtherPso, "GraphicsStateCollection", 500UL), ("a.shader", "Shader", 10UL) });
        Assert.That(summary.Complete, Is.False);
        Assert.That(summary.Line, Does.Contain("PsoDirect3D12 MISSING"));
    }

    [Test]
    public void IncompleteWithNoShaders() =>
        Assert.That(BuildContents.Of("PsoDirect3D12", new[] { (Pso, "GraphicsStateCollection", 500UL) }).Complete, Is.False);
}
