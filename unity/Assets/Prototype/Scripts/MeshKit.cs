using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The shape kit generated things are built from (D12, docs/WORLD_LIFE.md):
// flat-shaded quads, triangles, boxes and gable prisms, and smooth cylinders
// and cones, each one colour, into one mesh with the colour in its vertices
// (alpha is glow: a lit window). Parts go through Frame, so a builder can
// place, turn and tilt a group of them; faces low on the frame are darkened a
// little, a cheap ambient occlusion that sits a building on the ground.
// Drawn with the VillageLit shader. No textures and no imported models.
public sealed class MeshKit
{
    readonly List<Vector3> positions = new();
    readonly List<Vector3> normals = new();
    readonly List<Color> colors = new();
    readonly List<int> triangles = new();

    /// <summary>Where parts go: every point and normal is put through it.</summary>
    public Matrix4x4 Frame = Matrix4x4.identity;

    /// <summary>Faces below this height (after Frame) are darkened toward the ground, by up to Occlusion.</summary>
    public float OcclusionHeight = 1.2f;
    public float Occlusion = 0.35f;

    public int VertexCount => positions.Count;

    public void Clear()
    {
        positions.Clear();
        normals.Clear();
        colors.Clear();
        triangles.Clear();
        Frame = Matrix4x4.identity;
    }

    int Vertex(Vector3 p, Vector3 n, Color c)
    {
        var at = Frame.MultiplyPoint3x4(p);
        float shade = 1f - Occlusion * (1f - Mathf.Clamp01(at.y / OcclusionHeight));
        positions.Add(at);
        normals.Add(Frame.MultiplyVector(n).normalized);
        // Colours are picked in sRGB, as in a colour picker; the shader works in linear.
        var lit = new Color(c.r * shade, c.g * shade, c.b * shade, c.a);
        colors.Add(QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(lit.linear.r, lit.linear.g, lit.linear.b, c.a) : lit);
        return positions.Count - 1;
    }

    /// <summary>A flat triangle; its front is the side its corners run clockwise from.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var n = Vector3.Cross(b - a, c - a).normalized;
        int i = Vertex(a, n, color), j = Vertex(b, n, color), k = Vertex(c, n, color);
        triangles.Add(i);
        triangles.Add(j);
        triangles.Add(k);
    }

    /// <summary>A flat triangle facing <paramref name="outward"/>, whichever way its corners run.</summary>
    public void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Color color, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0f) Tri(p0, p2, p1, color);
        else Tri(p0, p1, p2, color);
    }

    /// <summary>A flat quad a b c d (in order round its edge) facing <paramref name="outward"/>.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
        {
            (b, d) = (d, b);
        }
        var n = outward.normalized;
        int i = Vertex(a, n, color), j = Vertex(b, n, color), k = Vertex(c, n, color), l = Vertex(d, n, color);
        triangles.Add(i);
        triangles.Add(j);
        triangles.Add(k);
        triangles.Add(i);
        triangles.Add(k);
        triangles.Add(l);
    }

    /// <summary>Both sides of a quad (an awning, a sign, a roof's underside).</summary>
    public void Sheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        var n = Vector3.Cross(b - a, c - a);
        Quad(a, b, c, d, color, n);
        Quad(a, b, c, d, color, -n);
    }

    /// <summary>A box about its centre, turned by <paramref name="turn"/>; <paramref name="top"/> colours its top face.</summary>
    public void Box(Vector3 centre, Vector3 size, Color color, Quaternion turn, Color? top = null)
    {
        var h = size * 0.5f;
        Vector3 P(float x, float y, float z) => centre + turn * new Vector3(x * h.x, y * h.y, z * h.z);
        Vector3 right = turn * Vector3.right, up = turn * Vector3.up, forward = turn * Vector3.forward;
        Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), color, right);
        Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), color, -right);
        Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), top ?? color, up);
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color, -up);
        Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color, forward);
        Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), color, -forward);
    }

    public void Box(Vector3 centre, Vector3 size, Color color) => Box(centre, size, color, Quaternion.identity);

    /// <summary>
    /// A gable's triangular prism: <paramref name="width"/> along x, its base <paramref name="depth"/>
    /// along z at y = <paramref name="baseY"/>, rising <paramref name="rise"/> to a ridge along x.
    /// Only the two end triangles: the roof covers the slopes.
    /// </summary>
    public void GableEnds(float width, float depth, float baseY, float rise, Color color)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * width / 2f;
            var front = new Vector3(x, baseY, -depth / 2f);
            var back = new Vector3(x, baseY, depth / 2f);
            var peak = new Vector3(x, baseY + rise, 0f);
            if (side > 0) Tri(front, peak, back, color);
            else Tri(front, back, peak, color);
        }
    }

    /// <summary>A smooth cylinder from a to b (a log, a post, a well's drum), capped.</summary>
    public void Cylinder(Vector3 a, Vector3 b, float radius, int sides, Color color, bool caps = true)
    {
        var axis = (b - a).normalized;
        var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var v = Vector3.Cross(axis, u);
        int first = positions.Count;
        for (int s = 0; s <= sides; s++)
        {
            float t = s * Mathf.PI * 2f / sides;
            var n = u * Mathf.Cos(t) + v * Mathf.Sin(t);
            Vertex(a + n * radius, n, color);
            Vertex(b + n * radius, n, color);
        }
        for (int s = 0; s < sides; s++)
        {
            int i = first + s * 2;
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 3);
            triangles.Add(i);
            triangles.Add(i + 3);
            triangles.Add(i + 2);
        }
        if (!caps) return;
        for (int s = 0; s < sides; s++)
        {
            float t0 = s * Mathf.PI * 2f / sides, t1 = (s + 1) * Mathf.PI * 2f / sides;
            Vector3 r0 = (u * Mathf.Cos(t0) + v * Mathf.Sin(t0)) * radius, r1 = (u * Mathf.Cos(t1) + v * Mathf.Sin(t1)) * radius;
            Tri(b, b + r1, b + r0, color);
            Tri(a, a + r0, a + r1, color);
        }
    }

    /// <summary>A smooth cone standing on <paramref name="foot"/> (a tower's roof, a hat).</summary>
    public void Cone(Vector3 foot, float radius, float height, int sides, Color color)
    {
        var tip = foot + Vector3.up * height;
        float slope = radius / height;
        for (int s = 0; s < sides; s++)
        {
            float t0 = s * Mathf.PI * 2f / sides, t1 = (s + 1) * Mathf.PI * 2f / sides, tm = (t0 + t1) / 2f;
            var r0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0));
            var r1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
            var nm = (new Vector3(Mathf.Cos(tm), 0f, Mathf.Sin(tm)) + Vector3.up * slope).normalized;
            int i = Vertex(foot + r0 * radius, (r0 + Vector3.up * slope).normalized, color);
            int j = Vertex(tip, nm, color);
            int k = Vertex(foot + r1 * radius, (r1 + Vector3.up * slope).normalized, color);
            triangles.Add(i);
            triangles.Add(j);
            triangles.Add(k);
        }
    }

    /// <summary>The parts so far as a mesh (32-bit indices if it needs them).</summary>
    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = name };
        if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
