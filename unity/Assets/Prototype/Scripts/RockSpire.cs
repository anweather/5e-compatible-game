using System.Collections.Generic;
using UnityEngine;

// Jagged shapes made in code, faceted and lopsided, a metre tall and a metre
// across at the base so a caller scales them: a spire of rock (Earth Spires'
// pillars, CellDressing.Spires.cs) tapering to a crooked point, and a chunk of
// ice or a shard of it (the Iceberg's wave and the pack ice it leaves,
// ChemGrid.Ice.cs). A few of each are made once and shared; a seed picks one.
public static class RockSpire
{
    const int Variants = 6;
    static readonly Mesh[] spires = new Mesh[Variants], chunks = new Mesh[Variants], slabs = new Mesh[Variants];

    /// <summary>A spire: a tapered, leaning, jagged column, its base at the origin and its point at y = 1.</summary>
    public static Mesh Spire(int seed) => Pick(spires, seed, s => Column(s, new Shape(7, 6, 0.28f, 0.22f, 1.35f, true, 1f)));

    /// <summary>A chunk of ice: a squat, broken block about a metre each way, centred on the origin.</summary>
    public static Mesh Chunk(int seed) => Pick(chunks, seed, s => Centred(Column(s + 101, new Shape(5, 2, 0.3f, 0.05f, 0.35f, false, 1f))));

    /// <summary>A slab of pack ice: a flat broken plate a metre across and a fifth as thick, its underside at the origin.</summary>
    public static Mesh Slab(int seed) => Pick(slabs, seed, s => Column(s + 211, new Shape(6, 1, 0.35f, 0f, 0.15f, false, 0.2f)));

    static Mesh Pick(Mesh[] made, int seed, System.Func<int, Mesh> make)
    {
        int i = ((seed % Variants) + Variants) % Variants;
        if (made[i] == null) made[i] = make(i * 7919 + 13);
        return made[i];
    }

    // A column of rings round a crooked middle: each ring Sides points, jittered in and out by Rough,
    // narrowing to nothing by the top when Pointed (by Taper otherwise), its middle drifting off by Lean.
    readonly struct Shape
    {
        public readonly int Sides, Rings;
        public readonly float Rough, Lean, Taper, Height;
        public readonly bool Pointed;

        public Shape(int sides, int rings, float rough, float lean, float taper, bool pointed, float height)
        {
            Sides = sides;
            Rings = rings;
            Rough = rough;
            Lean = lean;
            Taper = taper;
            Pointed = pointed;
            Height = height;
        }
    }

    // Flat-shaded: every face its own vertices, so it reads as broken stone or ice, not a smooth cone.
    static Mesh Column(int seed, Shape shape)
    {
        var rng = new System.Random(seed);
        var rows = MakeRings(rng, shape, out var leanWay);
        var tip = shape.Pointed
            ? new Vector3(leanWay.x + ((float)rng.NextDouble() - 0.5f) * 0.08f, shape.Height, leanWay.y + ((float)rng.NextDouble() - 0.5f) * 0.08f)
            : Mean(rows[shape.Rings]);
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Face(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            tris.Add(verts.Count); verts.Add(p0);
            tris.Add(verts.Count); verts.Add(p1);
            tris.Add(verts.Count); verts.Add(p2);
        }
        int sides = shape.Sides;
        for (int k = 0; k < shape.Rings; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Face(rows[k][i], rows[k + 1][i], rows[k][j]);
                Face(rows[k][j], rows[k + 1][i], rows[k + 1][j]);
            }
        var top = rows[shape.Rings];
        for (int i = 0; i < sides; i++) Face(top[i], tip, top[(i + 1) % sides]);
        var bottom = Mean(rows[0]);
        for (int i = 0; i < sides; i++) Face(rows[0][(i + 1) % sides], bottom, rows[0][i]);
        var mesh = new Mesh { name = shape.Pointed ? "Rock spire" : "Ice piece" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static List<Vector3[]> MakeRings(System.Random rng, Shape shape, out Vector2 leanWay)
    {
        float R() => (float)rng.NextDouble();
        leanWay = new Vector2(R() - 0.5f, R() - 0.5f).normalized * shape.Lean;
        float turn = R() * Mathf.PI * 2f, twist = (R() - 0.5f) * 0.6f;
        var rows = new List<Vector3[]>();
        for (int k = 0; k <= shape.Rings; k++)
        {
            float up = k / (float)shape.Rings;
            float radius = shape.Pointed ? 0.5f * Mathf.Pow(1f - up, shape.Taper) : 0.5f * (1f - up * shape.Taper);
            var middle = new Vector3(leanWay.x * up * up, up * shape.Height, leanWay.y * up * up);
            float bump = k == 0 ? 0f : 0.4f * shape.Height / Mathf.Max(1, shape.Rings);
            var ring = new Vector3[shape.Sides];
            for (int i = 0; i < shape.Sides; i++)
            {
                float a = turn + twist * up + (i + (R() - 0.5f) * 0.5f) * Mathf.PI * 2f / shape.Sides;
                float r = radius * (1f - shape.Rough * 0.5f + shape.Rough * R());
                ring[i] = middle + new Vector3(Mathf.Cos(a) * r, (R() - 0.5f) * bump, Mathf.Sin(a) * r);
            }
            rows.Add(ring);
        }
        return rows;
    }

    static Vector3 Mean(Vector3[] ring)
    {
        var sum = Vector3.zero;
        foreach (var p in ring) sum += p;
        return sum / ring.Length;
    }

    static Mesh Centred(Mesh mesh)
    {
        var verts = mesh.vertices;
        var middle = mesh.bounds.center;
        for (int i = 0; i < verts.Length; i++) verts[i] -= middle;
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        return mesh;
    }
}
