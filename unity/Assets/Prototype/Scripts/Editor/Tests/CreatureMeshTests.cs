using System.Linq;
using NUnit.Framework;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// What the creatures are made of (issue #47): one closed capsule, right way out however
// it's stretched, and every species' segments keep it so. The lens in one shows what's
// buried in it, so such a creature is drawn for its shadow alone.
public class CreatureMeshTests
{
    // As Creature.shader's SegmentVertex places a capsule's vertex.
    static Vector3 Posed(Vector3 shape, Vector2 cap, float length, float radiusStart, float radiusEnd, float flatten)
    {
        float r = Mathf.Lerp(radiusStart, radiusEnd, shape.z);
        return new Vector3(shape.x * r * cap.y, shape.y * r * cap.y * flatten, shape.z * length + cap.x * r);
    }

    [TestCase(1f, 0.2f, 0.19f, 1.15f)]
    [TestCase(0.3f, 0.09f, 0.03f, 1f)]
    [TestCase(0.05f, 0.1f, 0.02f, 0.5f)]
    public void TheCapsuleIsClosedAndRightWayOut(float length, float radiusStart, float radiusEnd, float flatten)
    {
        var mesh = CreatureCapsule.Build(12, 6, 4, readable: true);
        var uvs = mesh.uv;
        var points = mesh.vertices.Select((p, i) => Posed(p, uvs[i], length, radiusStart, radiusEnd, flatten)).ToArray();
        var triangles = mesh.triangles;
        var middle = new Vector3(0f, 0f, length / 2f);
        float volume = 0f;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = points[triangles[t]] - middle, b = points[triangles[t + 1]] - middle, c = points[triangles[t + 2]] - middle;
            // Clockwise from outside in Unity's left-handed space: the cross of two edges points out.
            volume += Vector3.Dot(a, Vector3.Cross(b - a, c - a)) / 6f;
        }
        Assert.That(volume, Is.GreaterThan(0f), "wound inside out");
        var edges = new System.Collections.Generic.HashSet<(Vector3Int, Vector3Int)>();
        Vector3Int Key(int i) => Vector3Int.RoundToInt(points[i] * 100000f);
        for (int t = 0; t < triangles.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                var from = Key(triangles[t + e]);
                var to = Key(triangles[t + (e + 1) % 3]);
                if (from != to) edges.Add((from, to));
            }
        foreach (var (from, to) in edges)
            Assert.That(edges.Contains((to, from)), Is.True, $"open edge {from} to {to}");
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void NoSpeciesTurnsACapsuleInsideOut()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:CreatureSpecies", new[] { "Assets/Prototype/ThirdParty/Punchy/Fauna" }))
        {
            var species = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var s in CreatureBody.Build(species).Segments)
            {
                Assert.That(s.Length, Is.GreaterThanOrEqualTo(0f), species.name);
                Assert.That(s.RadiusStart, Is.GreaterThanOrEqualTo(0f), species.name);
                Assert.That(s.RadiusEnd, Is.GreaterThanOrEqualTo(0f), species.name);
                Assert.That(s.Flatten, Is.GreaterThan(0f), species.name);
            }
        }
    }

    static SegmentInstance[] OneBody() => new[]
    {
        new SegmentInstance { Position = float3.zero, Rotation = quaternion.identity, Length = 1f, RadiusStart = 0.2f, RadiusEnd = 0.2f, Flatten = 1f },
        new SegmentInstance { Position = new float3(0f, -0.2f, 0.9f), Rotation = quaternion.RotateX(math.PI / 2f), Length = 0.6f, RadiusStart = 0.05f, RadiusEnd = 0.04f, Flatten = 1f },
    };

    [Test]
    public void ALensInACreatureIsAtIt()
    {
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0f, 0.05f, 0.5f), 0.25f), Is.True, "inside the body");
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0.4f, 0f, 0.5f), 0.25f), Is.True, "the near plane cuts it");
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0f, 0f, -0.4f), 0.25f), Is.True, "off the end of a segment, in its cap");
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0.1f, -0.6f, 0.9f), 0.25f), Is.True, "by a leg, the second segment");
    }

    [Test]
    public void ALensClearOfACreatureIsNot()
    {
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0.5f, 0f, 0.5f), 0.25f), Is.False, "just clear");
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0f, 0f, 3f), 0.25f), Is.False, "ahead of it");
        Assert.That(CreatureDrawer.AtLens(OneBody(), 0, 2, new float3(0f, 0f, 500f), 0.25f), Is.False, "far off");
    }

    [Test]
    public void TheLensReachesTheNearPlanesCorners()
    {
        var go = new GameObject("lens");
        var cam = go.AddComponent<Camera>();
        cam.nearClipPlane = 0.2f;
        cam.fieldOfView = 40f;
        cam.aspect = 16f / 9f;
        Assert.That(CreatureDrawer.LensReach(cam), Is.InRange(0.249f + 0.04f, 0.249f + 0.06f));
        Object.DestroyImmediate(go);
    }

    [Test]
    public void OnlyTheCreatureAtTheLensGoesToItsShadow()
    {
        var go = new GameObject("lens");
        var cam = go.AddComponent<Camera>();
        go.transform.position = new Vector3(0f, 0f, 0.5f);
        var two = OneBody().Concat(OneBody().Select(s => { s.Position.x += 5f; return s; })).ToArray();
        Assert.That(CreatureDrawer.HideAtLens(two, 0, 2, cam), Is.True);
        Assert.That(CreatureDrawer.HideAtLens(two, 2, 2, cam), Is.False);
        Assert.That(two.Select(s => s.Pad), Is.EqualTo(new[] { CreatureDrawer.ShadowOnly, CreatureDrawer.ShadowOnly, 0f, 0f }));
        Object.DestroyImmediate(go);
    }
}
