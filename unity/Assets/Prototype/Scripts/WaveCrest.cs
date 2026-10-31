using UnityEngine;

// The curled crest of a Wave (ChemGrid.Movers): one mesh, a side view of a
// breaking wave swept along its width and tapering at both ends, a metre wide,
// high and deep, sized by its transform. Its colour is a small texture along that
// side view: see-through green-blue water rising out of nothing behind it, paling
// to foam over the lip and fading out at both ends. Both faces.
public static class WaveCrest
{
    // The side view, from the flat water behind it, up over the top and curling down its front.
    static readonly Vector2[] Profile =
    {
        new(-1.5f, 0f), new(-0.95f, 0.22f), new(-0.45f, 0.62f), new(-0.05f, 0.95f), new(0.3f, 1.05f),
        new(0.55f, 0.92f), new(0.6f, 0.72f), new(0.42f, 0.56f), new(0.34f, 0.36f), new(0.46f, 0.14f), new(0.72f, 0f),
    };
    const int Across = 14;
    const int Skin = 32;
    // Along the side view from behind (0) to the toe (1): the water's colour and how much of it shows.
    static readonly (float at, Color colour)[] Colours =
    {
        (0f, new Color(0.1f, 0.42f, 0.5f, 0f)), (0.24f, new Color(0.11f, 0.45f, 0.55f, 0.72f)), (0.46f, new Color(0.3f, 0.68f, 0.75f, 0.82f)),
        (0.58f, new Color(0.92f, 0.98f, 1f, 0.95f)), (0.7f, new Color(0.85f, 0.95f, 0.98f, 0.92f)), (0.82f, new Color(0.14f, 0.5f, 0.58f, 0.82f)),
        (1f, new Color(0.85f, 0.95f, 0.98f, 0.45f)),
    };

    public static Mesh Make()
    {
        int rows = Across + 1, cols = Profile.Length;
        var points = new Vector3[rows * cols];
        var uvs = new Vector2[rows * cols];
        var triangles = new int[Across * (cols - 1) * 6];
        for (int r = 0; r < rows; r++)
        {
            float u = r / (float)Across, side = u * 2f - 1f;
            // Lower and further back toward its ends, as if the water fed it from behind.
            float taper = 1f - side * side * side * side;
            for (int c = 0; c < cols; c++)
            {
                points[r * cols + c] = new Vector3(u - 0.5f, Profile[c].y * taper, Profile[c].x * Mathf.Lerp(0.5f, 1f, taper));
                uvs[r * cols + c] = new Vector2(u, c / (cols - 1f));
            }
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
        var mesh = new Mesh { name = "Wave crest", vertices = points, uv = uvs, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Material Material()
    {
        var material = UrpMaterials.NewLit("Wave crest", Color.white, 0.9f);
        UrpMaterials.MakeTransparent(material);
        material.SetFloat("_Cull", 0f);
        material.SetTexture("_BaseMap", Colouring());
        return material;
    }

    static Texture2D Colouring()
    {
        var skin = new Texture2D(Skin, Skin, TextureFormat.RGBA32, false) { name = "Wave crest", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < Skin; y++)
            for (int x = 0; x < Skin; x++)
            {
                var colour = At(y / (Skin - 1f));
                colour.a *= Mathf.SmoothStep(0f, 1f, (1f - Mathf.Abs(x / (Skin - 1f) * 2f - 1f)) * 5f);
                skin.SetPixel(x, y, colour);
            }
        skin.Apply();
        return skin;
    }

    static Color At(float along)
    {
        for (int i = 1; i < Colours.Length; i++)
            if (along <= Colours[i].at)
                return Color.Lerp(Colours[i - 1].colour, Colours[i].colour, Mathf.InverseLerp(Colours[i - 1].at, Colours[i].at, along));
        return Colours[^1].colour;
    }
}
