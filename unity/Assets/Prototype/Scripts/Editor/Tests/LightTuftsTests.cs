using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The terrains' grass is drawn with LightTufts' copies of the pack's tufts, every card kept as one or two quads: with the
// pack's own (Grass_B 1,136 triangles, Grass_C 1,002) the nine's start ran 21.3 ms a frame on the Mac, with these 18.0
// (docs/perf/2026-11-23-tufts.md). And the grass still fades out (the pack material's dither) before its reach ends.
public class LightTuftsTests
{
    const string Template = LightTufts.Template;

    static GameObject Pack(string name)
    {
        var pack = AssetDatabase.LoadAssetAtPath<GameObject>($"{PackPaths.TerrainSamples}/Prefabs/{name}.prefab");
        if (pack == null) Assert.Ignore("Terrain Sample Asset Pack missing");
        return pack;
    }

    [Test]
    public void EveryCardIsKeptAsOneOrTwoQuads()
    {
        foreach (var (name, segments) in LightTufts.Lightened)
        {
            var heavy = Pack(name).GetComponent<MeshFilter>().sharedMesh;
            var light = LightTufts.Lighten(heavy, segments);
            try
            {
                int cards = LightTufts.CardCount(heavy);
                Assert.That(LightTufts.CardCount(light), Is.EqualTo(cards), $"{name}: a card lost or split");
                Assert.That(light.triangles.Length / 3, Is.LessThanOrEqualTo(cards * 2 * segments), $"{name}: a card left heavy");
                Assert.That(light.triangles.Length / 3, Is.LessThan(heavy.triangles.Length * 0.35f), $"{name}: not lighter");
                Assert.That(light.vertexCount, Is.LessThan(heavy.vertexCount), $"{name}: unused vertices kept");
                // The same vertex layout, or the grass's pipeline states differ from the recorded PSO collections.
                Assert.That(light.GetVertexAttributes(), Is.EqualTo(heavy.GetVertexAttributes()), $"{name}: vertex layout changed");
            }
            finally
            {
                Object.DestroyImmediate(light);
            }
        }
    }

    [Test]
    public void TheGroundsGrassIsTheLightTufts()
    {
        var template = AssetDatabase.LoadAssetAtPath<TerrainData>(Template);
        if (template == null) Assert.Ignore("no ground template: build the Chemistry Yard");
        var light = template.detailPrototypes.Where(d => d.prototype != null && LightTufts.Lightened.ContainsKey(d.prototype.name)).ToList();
        Assert.That(light.Count, Is.EqualTo(LightTufts.Lightened.Count), "a lightened tuft isn't in the grass");
        Assert.That(LightTufts.GroundHasPackTufts(), Is.False, "YardAutoBuild would rebuild this yard");
        foreach (var d in light)
        {
            string name = d.prototype.name;
            Assert.That(AssetDatabase.GetAssetPath(d.prototype), Does.StartWith(LightTufts.Folder), $"{name} is the pack's own");
            var heavy = Pack(name).GetComponent<MeshFilter>().sharedMesh;
            int most = LightTufts.CardCount(heavy) * 2 * LightTufts.Lightened[name];
            Assert.That(d.prototype.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3, Is.LessThanOrEqualTo(most), $"{name} is heavy");
        }
    }

    [Test]
    public void TheGrassFadesOutBeforeItsReachEnds()
    {
        var template = AssetDatabase.LoadAssetAtPath<TerrainData>(Template);
        if (template == null) Assert.Ignore("no ground template: build the Chemistry Yard");
        var materials = template.detailPrototypes.Where(d => d.prototype != null)
            .SelectMany(d => d.prototype.GetComponent<Renderer>().sharedMaterials).Distinct().ToList();
        foreach (var m in materials)
        {
            Assert.That(m.HasProperty("Distance_Fade_End"), Is.True, $"{m.name} has no distance fade");
            float from = m.GetFloat("Distance_Fade_Start"), to = m.GetFloat("Distance_Fade_End");
            Assert.That(from, Is.GreaterThan(0f).And.LessThan(to), $"{m.name}: fade {from}-{to} m");
            Assert.That(to, Is.LessThanOrEqualTo(MapTerrains.DetailMetres), $"{m.name}: grass still showing where the terrain stops drawing it");
        }
    }
}
