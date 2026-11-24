using NUnit.Framework;
using UnityEngine;

// A brush cell's clumps stop drawing at about BrushDrawMetres (CellDressing.DrawBrushNear): on the nine they were drawn
// to the haze, ~730 m, 3.3 ms of the Mac's GPU frame at the start (docs/perf/2026-11-23-nine-gpu.md). The perf census
// (PERF CENSUS, Shader_Graphs/TerrainGrass) shows the clumps drawn in a player.
public class BrushLodTests
{
    [Test]
    public void BrushClumpsGetOneLodCulledAtTheirDistance()
    {
        var root = new GameObject("brush 1,1");
        try
        {
            for (int i = 0; i < 3; i++) Primitives.Visual(PrimitiveType.Cube).transform.SetParent(root.transform, false);
            CellDressing.DrawBrushNear(root.transform);
            Assert.That(root.TryGetComponent<LODGroup>(out var lod), Is.True, "no LOD group on the brush");
            var lods = lod.GetLODs();
            Assert.That(lods.Length, Is.EqualTo(1));
            Assert.That(lods[0].renderers.Length, Is.EqualTo(3), "every clump in the LOD");
            float fov = Camera.main != null ? Camera.main.fieldOfView : 60f;
            float culledAt = lod.size * QualitySettings.lodBias / (2f * lods[0].screenRelativeTransitionHeight * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            Assert.That(culledAt, Is.EqualTo(CellDressing.BrushDrawMetres).Within(1f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void BrushDrawsPastTheGrassButNotToTheHaze() =>
        Assert.That(CellDressing.BrushDrawMetres, Is.InRange(MapTerrains.DetailMetres, 300f));
}
