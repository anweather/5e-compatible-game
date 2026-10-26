using UnityEngine;

// Textures generated in code, shared by the yard's water (at runtime) and
// the valley's lake (saved as an asset by the valley builder).
public static class ProceduralTextures
{
    // Ripple waves: how many, and the range of their wave numbers (whole periods across the texture).
    const int RippleWaves = 24, MinWaves = 2, MaxWaves = 14;

    /// <summary>
    /// A tileable normal map of gentle, irregular ripples: many sine waves in random directions and
    /// lengths (a fixed seed), each a whole number of periods across the texture so its edges match,
    /// the longer ones stronger. Enough of them that no pattern shows across a lake.
    /// </summary>
    public static Texture2D RippleNormals(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Ripples", wrapMode = TextureWrapMode.Repeat };
        var waves = RippleWaveSet();
        var pixels = new Color32[size * size];
        float tau = Mathf.PI * 2f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // The slope of h = sum of amplitude * sin(kx u + ky v + phase).
                float dx = 0f, dy = 0f;
                foreach (var (kx, ky, amp, phase) in waves)
                {
                    float c = amp * Mathf.Cos((kx * x + ky * y) * tau + phase);
                    dx += kx * c;
                    dy += ky * c;
                }
                var n = new Vector3(-dx * 0.08f, -dy * 0.08f, 1f).normalized;
                pixels[y * size + x] = new Color32(ToByte(n.x), ToByte(n.y), ToByte(n.z), 255);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(true);
        return tex;
    }

    static (int kx, int ky, float amp, float phase)[] RippleWaveSet()
    {
        var rng = new System.Random(11);
        var waves = new (int, int, float, float)[RippleWaves];
        for (int i = 0; i < RippleWaves; i++)
        {
            int kx, ky;
            do
            {
                kx = rng.Next(-MaxWaves, MaxWaves + 1);
                ky = rng.Next(-MaxWaves, MaxWaves + 1);
            } while (kx * kx + ky * ky < MinWaves * MinWaves);
            float k = Mathf.Sqrt(kx * kx + ky * ky);
            // Longer waves stronger, so the surface rolls rather than fizzes; scaled to the old range.
            waves[i] = (kx, ky, 1.6f / k, (float)rng.NextDouble() * Mathf.PI * 2f);
        }
        return waves;
    }

    // A normal component in -1..1 packed into a byte, as normal maps store it.
    static byte ToByte(float v) => (byte)((v * 0.5f + 0.5f) * 255);
}
