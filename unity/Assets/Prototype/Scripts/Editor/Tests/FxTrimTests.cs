using NUnit.Framework;
using UnityEngine;

// A pooled effect's template is trimmed before any copy of it is made (ChemGrid.TrimFx): prewarm off, and no mesh left
// on a renderer that draws billboards. The pack's smoke carries a sphere it never draws, and waking a renderer with one
// cost 5-10 ms a puff in a player on the nine (#178).
[Category("Unit")]
public class FxTrimTests
{
    GameObject fx;

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(fx);

    static Mesh Sphere()
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = ball.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(ball);
        return mesh;
    }

    ParticleSystemRenderer Effect(ParticleSystemRenderMode mode)
    {
        fx = new GameObject("Effect");
        fx.SetActive(false);
        var ps = fx.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        var drawn = fx.GetComponent<ParticleSystemRenderer>();
        drawn.renderMode = mode;
        drawn.mesh = Sphere();
        return drawn;
    }

    [Test]
    public void ABillboardEffectLosesItsPrewarmAndTheMeshItNeverDraws()
    {
        var drawn = Effect(ParticleSystemRenderMode.Billboard);
        Assert.That(drawn.meshCount, Is.EqualTo(1), "the effect as the pack ships it");
        ChemGrid.TrimFx(fx);
        Assert.That(fx.GetComponent<ParticleSystem>().main.prewarm, Is.False);
        Assert.That(drawn.meshCount, Is.EqualTo(0));
        Assert.That(drawn.renderMode, Is.EqualTo(ParticleSystemRenderMode.Billboard));
    }

    [Test]
    public void AnEffectDrawnAsMeshesKeepsItsMesh()
    {
        var drawn = Effect(ParticleSystemRenderMode.Mesh);
        ChemGrid.TrimFx(fx);
        Assert.That(drawn.meshCount, Is.EqualTo(1));
        Assert.That(drawn.mesh, Is.Not.Null);
    }
}
