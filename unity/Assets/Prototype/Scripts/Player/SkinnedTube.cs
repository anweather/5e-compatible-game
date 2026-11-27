using System;
using System.Collections.Generic;
using UnityEngine;

// Smooth tubes of clothing or hair skinned to a rig (the wizard's bodice,
// sleeves and beard: WizardOutfit): rings round a path, each ring an ellipse
// across its own axis, sharing their corners so the normals come out smooth,
// coloured ring by ring (vertex colours for VillageLit, picked in sRGB as
// MeshKit's are), each corner given its bones by a weigh function. Several
// tubes can go into one mesh (a beard's tufts), so they cost one draw. Built in
// a pose (the idle) in the space of the object they're put on, which the bind
// poses remember.
public static class SkinnedTube
{
    public struct Ring
    {
        public Vector3 at, along;
        public float half, depth;
        public Color colour;
    }

    public static GameObject Make(Transform host, string name, Ring[] rings, int sides, Transform[] bones,
        Func<Vector3, int, BoneWeight> weigh, Material material) =>
        MakeMany(host, name, new List<Ring[]> { rings }, sides, bones, (p, _, ring) => weigh(p, ring), material);

    public static GameObject MakeMany(Transform host, string name, List<Ring[]> tubes, int sides, Transform[] bones,
        Func<Vector3, int, int, BoneWeight> weigh, Material material)
    {
        var vertices = new List<Vector3>();
        var colours = new List<Color>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int>();
        for (int tube = 0; tube < tubes.Count; tube++)
        {
            int first = vertices.Count, which = tube;
            AddRings(tubes[tube], sides, (p, ring) => weigh(p, which, ring), vertices, colours, weights);
            AddFaces(first, tubes[tube].Length, sides, triangles);
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetColors(colours);
        mesh.SetTriangles(triangles, 0);
        mesh.boneWeights = weights.ToArray();
        mesh.RecalculateNormals();
        var binds = new Matrix4x4[bones.Length];
        for (int j = 0; j < bones.Length; j++) binds[j] = bones[j].worldToLocalMatrix * host.localToWorldMatrix;
        mesh.bindposes = binds;
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(host, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.bones = bones;
        skin.rootBone = bones[0];
        skin.sharedMaterial = material;
        return go;
    }

    static void AddRings(Ring[] rings, int sides, Func<Vector3, int, BoneWeight> weigh, List<Vector3> vertices, List<Color> colours, List<BoneWeight> weights)
    {
        for (int i = 0; i < rings.Length; i++)
        {
            var r = rings[i];
            var axis = r.along.normalized;
            var right = Vector3.Cross(axis, Vector3.forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            var front = Vector3.Cross(right, axis);
            var colour = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(r.colour.linear.r, r.colour.linear.g, r.colour.linear.b, r.colour.a) : r.colour;
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * Mathf.PI * k / sides;
                var p = r.at + right * (Mathf.Cos(a) * r.half) + front * (Mathf.Sin(a) * r.half * r.depth);
                vertices.Add(p);
                colours.Add(colour);
                weights.Add(weigh(p, i));
            }
        }
    }

    static void AddFaces(int first, int rings, int sides, List<int> triangles)
    {
        for (int i = 0; i + 1 < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = first + i * sides + k, b = first + i * sides + (k + 1) % sides,
                    c = first + (i + 1) * sides + (k + 1) % sides, d = first + (i + 1) * sides + k;
                triangles.AddRange(new[] { a, d, c, a, c, b });
            }
    }

    public static BoneWeight Blend(int a, int b, float t) =>
        new() { boneIndex0 = a, weight0 = 1f - t, boneIndex1 = b, weight1 = t };
}
