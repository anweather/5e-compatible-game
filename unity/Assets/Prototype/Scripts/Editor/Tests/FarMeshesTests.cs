using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The far ring's meshes (FarMeshes), without a scene: a known chunk's land spans its chunk exactly so neighbours meet,
// stands at its heights and hangs a skirt; every shape world/horizon.json names is one FarMeshes draws, within its row's
// size; and the sea plane leaves the map's own rectangle open.
[Category("Unit")]
public class FarMeshesTests
{
    const float Cell = 1.5f;

    static KnownChunk Chunk(System.Func<int, int, int> height)
    {
        var k = new KnownChunk { X = 3, Y = 5, Biome = "tundra" };
        for (int j = 0; j < KnownChunk.Side; j++)
            for (int i = 0; i < KnownChunk.Side; i++) k.Heights[j * KnownChunk.Side + i] = height(i, j);
        return k;
    }

    [Test]
    public void AKnownChunkSpansItsChunkAtItsHeights()
    {
        var k = Chunk((i, j) => 8 * (i + 10 * j));
        var d = FarMeshes.Known(k, Cell);
        int grid = KnownChunk.Side * KnownChunk.Side, rim = 4 * (KnownChunk.Side - 1);
        Assert.AreEqual(grid + rim, d.Vertices.Count, "the 9 × 9 samples and a skirt vertex under each rim sample");
        float side = ChunkGen.Size * Cell;
        var top = d.Vertices.Take(grid).ToList();
        Assert.AreEqual(0f, top.Min(v => v.x), 1e-4f);
        Assert.AreEqual(side, top.Max(v => v.x), 1e-4f, "the east column on the next chunk's west edge, so they meet");
        Assert.AreEqual(-side, top.Min(v => v.z), 1e-4f, "rows run south (−z) to the next chunk's north edge");
        for (int j = 0; j < KnownChunk.Side; j++)
            for (int i = 0; i < KnownChunk.Side; i++)
                Assert.AreEqual(k.Heights[j * KnownChunk.Side + i] * (float)MapFile.LandUnitMetres, top[j * KnownChunk.Side + i].y, 1e-4f);
        Assert.That(d.Vertices.Skip(grid).All(v => top.Any(t => Mathf.Approximately(t.x, v.x) && Mathf.Approximately(t.z, v.z) &&
                                                               Mathf.Abs(t.y - FarMeshes.SkirtMetres - v.y) < 1e-3f)), "the skirt hangs under the rim");
        Assert.That(d.Triangles.Max(), Is.LessThan(d.Vertices.Count));
        Assert.That(d.Normals.Take(grid).All(n => n.y > 0f), "the land faces up");
    }

    [Test]
    public void AFlatKnownChunkIsItsBiomesColour()
    {
        var d = FarMeshes.Known(Chunk((_, _) => 40), Cell);
        var want = FarMeshes.BiomeColour("tundra");
        foreach (Color c in d.Colors.Take(81))
            Assert.That(Mathf.Abs(c.r - want.r) < 0.1f && Mathf.Abs(c.b - want.b) < 0.1f, $"{c} is near {want}");
        Assert.AreNotEqual(FarMeshes.BiomeColour("tundra"), FarMeshes.BiomeColour("ashlands"));
        Assert.AreEqual(FarMeshes.BiomeColour("temperate"), FarMeshes.BiomeColour("valley"), "the pinned map's name is grass");
    }

    [Test]
    public void EveryHorizonShapeIsDrawnWithinItsSize()
    {
        string world = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "world"));
        var table = HorizonTable.Load(world);
        Assert.That(table.Rows.Count, Is.GreaterThan(0));
        foreach (var row in table.Rows.Values)
        {
            Assert.That(FarMeshes.Shapes, Has.Member(row.Shape), $"{row.Type}'s shape {row.Shape}");
            var d = FarMeshes.Landmark(row.Type, row.Shape, row.Radius, row.Height, 8f);
            Assert.That(d.Vertices.Count, Is.GreaterThan(0), row.Type);
            Assert.AreEqual(row.Height, d.Vertices.Max(v => v.y), 1e-3f, $"{row.Type} stands its height");
            Assert.AreEqual(-8f, d.Vertices.Min(v => v.y), 1e-3f, $"{row.Type}'s foot sinks into the ground");
            Assert.That(d.Vertices.Max(v => new Vector2(v.x, v.z).magnitude), Is.LessThanOrEqualTo(row.Radius + 1e-3f), row.Type);
        }
    }

    [Test]
    public void TheSeaPlaneLeavesTheMapOpen()
    {
        var d = FarMeshes.SeaFrame(-100f, -50f, 300f, 250f, 5000f, -0.5f, Color.blue);
        for (int t = 0; t < d.Triangles.Count; t += 3)
        {
            var c = (d.Vertices[d.Triangles[t]] + d.Vertices[d.Triangles[t + 1]] + d.Vertices[d.Triangles[t + 2]]) / 3f;
            Assert.That(c.x < -100f || c.x > 300f || c.z < -50f || c.z > 250f, $"triangle {t / 3} is over the map at {c}");
            var n = Vector3.Cross(d.Vertices[d.Triangles[t + 1]] - d.Vertices[d.Triangles[t]], d.Vertices[d.Triangles[t + 2]] - d.Vertices[d.Triangles[t]]);
            Assert.That(n.y, Is.GreaterThan(0f), "it faces up (clockwise from above)");
        }
        Assert.That(d.Vertices.All(v => Mathf.Approximately(v.y, -0.5f)));
    }
}
