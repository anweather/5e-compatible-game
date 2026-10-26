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

    /// <summary>
    /// Falling water's streaks: white, see-through, in vertical strands of uneven brightness that
    /// thicken and thin down their length, tileable both ways (the falls under the mountain scrolls
    /// it down its sheets). A fixed seed, so every build is the same.
    /// </summary>
    public static Texture2D WaterStreaks(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Streaks", wrapMode = TextureWrapMode.Repeat };
        var rng = new System.Random(17);
        var strand = new float[width];
        var phase = new float[width];
        for (int x = 0; x < width; x++)
        {
            strand[x] = (float)rng.NextDouble();
            phase[x] = (float)rng.NextDouble() * Mathf.PI * 2f;
        }
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Neighbouring strands blur together a little; each pulses twice down the texture.
                float s = (strand[x] * 2f + strand[(x + 1) % width] + strand[(x + width - 1) % width]) / 4f;
                float pulse = 0.65f + 0.35f * Mathf.Sin(y * Mathf.PI * 4f / height + phase[x]);
                float a = Mathf.Clamp01(Mathf.Pow(s, 1.6f) * pulse * 1.3f);
                byte v = (byte)(200 + 55 * a);
                pixels[y * width + x] = new Color32(v, v, 255, (byte)(255 * a));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
