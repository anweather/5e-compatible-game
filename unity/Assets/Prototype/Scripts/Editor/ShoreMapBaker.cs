using System;
using UnityEditor;
using UnityEngine;

// Bakes the shore map (ShoreMap) for ValleyBuilder: which texels are land, a
// distance transform of those (Felzenszwalb and Huttenlocher's, in time linear
// in the texels) from each side of the shore into one signed distance, a blur
// so that it has no crease where two shores are about as near, and the way
// down that distance for the way to land. Near that crease the way to land
// shrinks toward nothing rather than flipping over.
public static class ShoreMapBaker
{
    /// <summary>Texels along a side, and a texel's side (m): 4 cells, out over 1.3 km past the world on the sea's side.</summary>
    public const int Texels = 512;
    public const float TexelMetres = 6f;

    /// <summary>The blur's width in texels (a standard deviation).</summary>
    public const float BlurTexels = 3f;

    // The map's middle: the valley's south-east corner, out where the sea is.
    static readonly Vector2 Middle = new(444f, -366f);

    /// <summary>Bakes the map of this land into the asset at the path (made if missing), keeping its id.</summary>
    public static ShoreMap Bake(string path, Func<Vector2, bool> isLand)
    {
        float side = Texels * TexelMetres;
        var origin = Middle - Vector2.one * (side / 2f);
        var land = new bool[Texels * Texels];
        for (int z = 0; z < Texels; z++)
            for (int x = 0; x < Texels; x++)
                land[z * Texels + x] = isLand(origin + new Vector2(x + 0.5f, z + 0.5f) * TexelMetres);
        var distance = Blur(SignedDistance(land, Texels, TexelMetres), Texels, BlurTexels);
        var pixels = Pixels(distance, Texels, TexelMetres);

        var map = EditorAssets.LoadOrCreate(path, ScriptableObject.CreateInstance<ShoreMap>);
        if (map.texture == null)
        {
            map.texture = new Texture2D(Texels, Texels, TextureFormat.RGBAHalf, false, true) { name = "Shore map" };
            AssetDatabase.AddObjectToAsset(map.texture, map);
        }
        map.texture.wrapMode = TextureWrapMode.Clamp;
        map.texture.filterMode = FilterMode.Bilinear;
        map.texture.SetPixels(pixels);
        map.texture.Apply(false, false); // stays readable: ShoreMap.At reads it
        map.origin = origin;
        map.size = side;
        EditorUtility.SetDirty(map.texture);
        EditorUtility.SetDirty(map);
        return map;
    }

    /// <summary>The distance from each texel to the shore (m): positive off the land, negative on it.</summary>
    public static float[] SignedDistance(bool[] land, int n, float texel)
    {
        var toLand = DistanceTo(land, true, n);
        var toSea = DistanceTo(land, false, n);
        var signed = new float[n * n];
        // The shore lies half a texel from the centres on either side of it.
        for (int i = 0; i < signed.Length; i++)
            signed[i] = land[i] ? (0.5f - Mathf.Sqrt(toSea[i])) * texel : (Mathf.Sqrt(toLand[i]) - 0.5f) * texel;
        return signed;
    }

    // Squared distance in texels to the nearest texel of this kind.
    static float[] DistanceTo(bool[] land, bool kind, int n)
    {
        const float Far = 1e12f;
        var d = new float[n * n];
        for (int i = 0; i < d.Length; i++) d[i] = land[i] == kind ? 0f : Far;
        var line = new float[n];
        var hull = new int[n];
        var from = new float[n + 1];
        for (int z = 0; z < n; z++)
            Transform(d, z * n, 1, n, line, hull, from);
        for (int x = 0; x < n; x++)
            Transform(d, x, n, n, line, hull, from);
        return d;
    }

    // One line's distance transform, in place: the lower envelope of a parabola on each texel.
    static void Transform(float[] d, int start, int step, int n, float[] line, int[] hull, float[] from)
    {
        for (int i = 0; i < n; i++) line[i] = d[start + i * step];
        int k = 0;
        hull[0] = 0;
        from[0] = float.NegativeInfinity;
        from[1] = float.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            float s = Crossing(line, q, hull[k]);
            while (s <= from[k]) s = Crossing(line, q, hull[--k]);
            hull[++k] = q;
            from[k] = s;
            from[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (from[k + 1] < q) k++;
            float gap = q - hull[k];
            d[start + q * step] = gap * gap + line[hull[k]];
        }
    }

    static float Crossing(float[] line, int q, int p) => (line[q] + q * q - (line[p] + p * p)) / (2f * (q - p));

    /// <summary>A Gaussian blur across a square field, the edges held.</summary>
    public static float[] Blur(float[] field, int n, float sigma)
    {
        int radius = Mathf.CeilToInt(sigma * 3f);
        var kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = Mathf.Exp(-0.5f * i * i / (sigma * sigma));
            sum += kernel[i + radius];
        }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        return Across(Across(field, n, 1, n, kernel), n, n, 1, kernel);
    }

    static float[] Across(float[] field, int n, int step, int rowStep, float[] kernel)
    {
        var blurred = new float[field.Length];
        int radius = kernel.Length / 2;
        for (int row = 0; row < n; row++)
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += kernel[k + radius] * field[row * rowStep + Mathf.Clamp(i + k, 0, n - 1) * step];
                blurred[row * rowStep + i * step] = sum;
            }
        return blurred;
    }

    /// <summary>The map's pixels from the distance: rg the way to land (down the distance), b the distance.</summary>
    public static Color[] Pixels(float[] distance, int n, float texel)
    {
        var pixels = new Color[distance.Length];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float At(int ix, int iz) => distance[Mathf.Clamp(iz, 0, n - 1) * n + Mathf.Clamp(ix, 0, n - 1)];
                var down = new Vector2(At(x - 1, z) - At(x + 1, z), At(x, z - 1) - At(x, z + 1)) / (2f * texel);
                down = Vector2.ClampMagnitude(down, 1f);
                pixels[z * n + x] = new Color(down.x, down.y, At(x, z), 1f);
            }
        return pixels;
    }
}
