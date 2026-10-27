using System.IO;
using UnityEditor;
using UnityEngine;

// A map of the valley and the land the world theme lays round it (D11), like
// PocketTactics' theme:preview: one pixel per cell, coloured by its terrain
// layers and water, shaded by the height, with the valley floor's edge drawn
// in. Prototype > Preview World Theme writes Generated/ThemePreview.png; look
// at it after changing a theme or ValleySurroundings.
public static class ThemePreview
{
    const string PathOut = "Assets/Prototype/Generated/ThemePreview.png";

    // Grass, moss, rock, snow, sand, soil (ValleyBuilder.LayerNames order).
    static readonly Color[] LayerColours =
    {
        new Color(0.45f, 0.62f, 0.3f), new Color(0.25f, 0.42f, 0.2f), new Color(0.55f, 0.53f, 0.5f),
        new Color(0.95f, 0.96f, 0.98f), new Color(0.85f, 0.78f, 0.55f), new Color(0.48f, 0.38f, 0.28f),
    };

    [MenuItem("Prototype/Preview World Theme")]
    public static string Render()
    {
        var layout = ValleyTerrain.Layout;
        var rows = ChemYardBuilder.Map.Split('\n');
        ValleyShape.Plan(rows[0].Length, rows.Length, layout.CellSize);
        int n = layout.GridW;
        float cs = layout.ChunkSize * layout.ChunksX / n;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var pixels = new Color32[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(layout.OriginX + (i + 0.5f) * cs, layout.OriginZ + (j + 0.5f) * cs);
                pixels[j * n + i] = Colour(p, cs);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(PathOut));
        File.WriteAllBytes(PathOut, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log("World theme preview: " + PathOut);
        return PathOut;
    }

    static Color32 Colour(Vector2 p, float cs)
    {
        float h = ValleyShape.HeightAt(p);
        if (h < ValleyShape.LakeLevel || (ValleySurroundings.Mask(p) > 0.5f && ValleySurroundings.Water(p) != '\0')
            || ValleyShape.DistanceToRiver(p) < ValleyShape.RiverHalfWidth)
            return new Color(0.2f, 0.4f, 0.75f);
        // The slope from the neighbouring heights, for the layers and the hill shading.
        float dx = ValleyShape.HeightAt(p + Vector2.right * cs) - h, dz = ValleyShape.HeightAt(p + Vector2.up * cs) - h;
        float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / cs) * Mathf.Rad2Deg;
        var w = ValleyShape.LayerWeights(p, slope);
        var c = Color.black;
        for (int k = 0; k < w.Length && k < LayerColours.Length; k++) c += LayerColours[k] * w[k];
        float light = Mathf.Clamp(0.8f + (dx - dz) / cs * 0.6f, 0.45f, 1.2f) * Mathf.Lerp(0.85f, 1.1f, Mathf.InverseLerp(0f, 200f, h));
        c *= light;
        // The valley floor's edge, faintly.
        float m = ValleySurroundings.Mask(p);
        if (m > 0.02f && m < 0.06f) c = Color.Lerp(c, Color.yellow, 0.5f);
        c.a = 1f;
        return c;
    }
}
