using UnityEngine;

// Textures generated in code, shared by the yard's water (at runtime) and
// the valley's lake (saved as an asset by the valley builder).
public static class ProceduralTextures
{
    /// <summary>
    /// A tileable normal map of gentle ripples, from a few crossing sine waves
    /// with whole periods across the texture so its edges match.
    /// </summary>
    public static Texture2D RippleNormals(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Ripples", wrapMode = TextureWrapMode.Repeat };
        var pixels = new Color32[size * size];
        float tau = Mathf.PI * 2f / size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Slope of h = sin(3u + 1v) + 0.6 sin(-2u + 5v) + 0.35 sin(7u - 4v + 1).
            float a = 3 * x * tau + 1 * y * tau, b = -2 * x * tau + 5 * y * tau, c = 7 * x * tau - 4 * y * tau + 1f;
            float dx = 3 * Mathf.Cos(a) - 1.2f * Mathf.Cos(b) + 2.45f * Mathf.Cos(c);
            float dy = 1 * Mathf.Cos(a) + 3f * Mathf.Cos(b) - 1.4f * Mathf.Cos(c);
            var n = new Vector3(-dx * 0.08f, -dy * 0.08f, 1f).normalized;
            pixels[y * size + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
        }
        tex.SetPixels32(pixels);
        tex.Apply(true);
        return tex;
    }
}
