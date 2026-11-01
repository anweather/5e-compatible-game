using System.Collections.Generic;
using Chem;
using NUnit.Framework;
using UnityEngine;

// Generated meshes are right way out (issue #47): a cylinder wound backwards showed the
// far inside of every log. A face's front is the side its corners run clockwise from
// (Unity's), so each one must point the way its normal says, and out of the shape.
public class MeshKitTests
{
    static Mesh Log(bool caps)
    {
        var kit = new MeshKit { Occlusion = 0f };
        kit.Cylinder(new Vector3(-1f, 0.5f, 0.2f), new Vector3(1f, 0.7f, 0.4f), 0.2f, 7, Color.white, caps);
        return kit.ToMesh("log");
    }

    [Test]
    public void ACylindersFacesPointOutOfIt()
    {
        var mesh = Log(true);
        Vector3 a = new(-1f, 0.5f, 0.2f), b = new(1f, 0.7f, 0.4f), axis = (b - a).normalized;
        var points = mesh.vertices;
        var triangles = mesh.triangles;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 p = points[triangles[t]], q = points[triangles[t + 1]], r = points[triangles[t + 2]];
            var middle = (p + q + r) / 3f;
            float along = Vector3.Dot(middle - a, axis);
            var near = along <= 0f ? a : along >= (b - a).magnitude ? b : a + axis * along;
            var outward = (middle - near).normalized;
            // On a cap the middle of a face lies on the end, so outward is along the axis.
            if (Mathf.Abs(along) < 1e-4f) outward = -axis;
            else if (Mathf.Abs(along - (b - a).magnitude) < 1e-4f) outward = axis;
            Assert.That(Vector3.Dot(Vector3.Cross(q - p, r - p), outward), Is.GreaterThan(0f), $"triangle {t / 3}");
        }
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void ACappedCylinderHasNoHole()
    {
        var mesh = Log(true);
        var points = mesh.vertices;
        var triangles = mesh.triangles;
        // Welded by place: every edge of a closed shape runs once each way.
        var edges = new HashSet<(Vector3Int, Vector3Int)>();
        Vector3Int Key(int i) => Vector3Int.RoundToInt(points[i] * 10000f);
        for (int t = 0; t < triangles.Length; t += 3)
            for (int e = 0; e < 3; e++)
                Assert.That(edges.Add((Key(triangles[t + e]), Key(triangles[t + (e + 1) % 3]))), Is.True, "an edge twice the same way");
        foreach (var (from, to) in edges)
            Assert.That(edges.Contains((to, from)), Is.True, $"edge {from} to {to} is open");
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void AnOpenCylinderStaysRightWayOut()
    {
        var mesh = Log(false);
        Assert.That(MeshChecks.BackwardShare(mesh), Is.EqualTo(0f));
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void EveryBuildingIsRightWayOut()
    {
        foreach (WallKind walls in System.Enum.GetValues(typeof(WallKind)))
            foreach (RoofKind roof in System.Enum.GetValues(typeof(RoofKind)))
                foreach (RoofCover cover in System.Enum.GetValues(typeof(RoofCover)))
                {
                    var made = BuildingMesher.Make(new BuildingMesher.Spec
                    {
                        kind = BuildingKind.House, walls = walls, roof = roof, cover = cover, storeys = 1, chimney = true, seed = 77, width = 7.5f, depth = 6f,
                    });
                    Assert.That(MeshChecks.BackwardShare(made.body), Is.EqualTo(0f), $"{walls} {roof} {cover} body");
                    Assert.That(MeshChecks.BackwardShare(made.roof), Is.EqualTo(0f), $"{walls} {roof} {cover} roof");
                    Object.DestroyImmediate(made.body);
                    Object.DestroyImmediate(made.roof);
                }
    }

    [Test]
    public void EveryGeneratedPropIsRightWayOut()
    {
        foreach (GeneratedProp.Kind kind in System.Enum.GetValues(typeof(GeneratedProp.Kind)))
        {
            var mesh = GeneratedProp.Make(kind, 5, 6f, out _);
            Assert.That(MeshChecks.BackwardShare(mesh), Is.EqualTo(0f), kind.ToString());
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void AmbientLifeIsRightWayOut()
    {
        foreach (var mesh in new[] { CreatureMesher.Gull(0f), CreatureMesher.Crab(0), CreatureMesher.Fish(0f, Color.grey) })
        {
            Assert.That(MeshChecks.BackwardShare(mesh), Is.EqualTo(0f), mesh.name);
            Object.DestroyImmediate(mesh);
        }
    }
}
