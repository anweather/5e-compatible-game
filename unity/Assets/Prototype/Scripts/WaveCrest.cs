using UnityEngine;

// The curled crest of a Wave (ChemGrid.Movers): one mesh, a side view of a
// breaking wave swept along its width and tapering at both ends, a metre wide,
// high and deep, sized by its transform. A glossy see-through blue, both faces.
public static class WaveCrest
{
    // The side view, from the flat water behind it, up over the top and curling down its front.
    static readonly Vector2[] Profile =
    {
        new(-1.5f, 0f), new(-0.95f, 0.22f), new(-0.45f, 0.62f), new(-0.05f, 0.95f), new(0.3f, 1.05f),
        new(0.55f, 0.92f), new(0.6f, 0.72f), new(0.42f, 0.56f), new(0.34f, 0.36f), new(0.46f, 0.14f), new(0.72f, 0f),
    };
    const int Across = 14;

    public static Mesh Make()
    {
        int rows = Across + 1, cols = Profile.Length;
        var points = new Vector3[rows * cols];
        var triangles = new int[Across * (cols - 1) * 6];
        for (int r = 0; r < rows; r++)
        {
            float u = r / (float)Across, side = u * 2f - 1f;
            // Lower and further back toward its ends, as if the water fed it from behind.
            float taper = 1f - side * side * side * side;
            for (int c = 0; c < cols; c++)
                points[r * cols + c] = new Vector3(u - 0.5f, Profile[c].y * taper, Profile[c].x * Mathf.Lerp(0.5f, 1f, taper));
        }
        int t = 0;
        for (int r = 0; r < Across; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = d;
                triangles[t++] = b;
                triangles[t++] = e;
                triangles[t++] = d;
            }
        var mesh = new Mesh { name = "Wave crest", vertices = points, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Material Material()
    {
        var material = UrpMaterials.NewLit("Wave crest", new Color(0.03f, 0.17f, 0.3f, 0.86f), 0.95f);
        UrpMaterials.MakeTransparent(material);
        material.SetFloat("_Cull", 0f);
        return material;
    }
}
