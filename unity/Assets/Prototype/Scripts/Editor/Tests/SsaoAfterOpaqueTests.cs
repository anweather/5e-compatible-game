using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

// SSAO is worked out after the opaques (docs/perf/2026-11-23-nine-gpu.md, fix 2): before them it costs a depth prepass of
// everything opaque, ~5 ms of the Mac's GPU frame at the nine's start. A change back would also want the pipeline
// collections recorded again (docs/RELEASING.md).
public class SsaoAfterOpaqueTests
{
    [Test]
    public void ThePcRenderersSsaoRunsAfterTheOpaques()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        Assert.That(renderer, Is.Not.Null, "PC_Renderer.asset");
        var ssao = renderer.rendererFeatures.FirstOrDefault(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion");
        Assert.That(ssao, Is.Not.Null, "no SSAO feature");
        var after = new SerializedObject(ssao).FindProperty("m_Settings.AfterOpaque");
        Assert.That(after, Is.Not.Null, "m_Settings.AfterOpaque");
        Assert.That(after.boolValue, Is.True, "SSAO before the opaques costs a depth prepass of everything");
    }
}
