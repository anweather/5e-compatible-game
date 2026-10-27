// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Changed only to meet our analyzers (a parameter name, indented loops).
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Weather
{
    /// <summary>
    /// Generates the tiling 3D noise the volumetric clouds are carved from (the usual real-time cloud recipe):
    ///  r = "Perlin-Worley" shape noise: billowy Perlin shaped by inverted Worley cells, for puffy cloud masses;
    ///  g = higher-frequency Worley detail, used to erode cloud edges into cauliflower-like wisps.
    /// Every component repeats exactly across the texture, so it tiles seamlessly over the sky.
    /// </summary>
    public static class CloudNoise
    {
        public static Texture3D Create(int size = 64)
        {
            var voxels = new NativeArray<Color32>(size * size * size, Allocator.TempJob);
            new NoiseJob { Size = size, Voxels = voxels }.Schedule(voxels.Length, 256).Complete();
            var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            {
                name = "CloudNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            texture.SetPixelData(voxels, 0);
            texture.Apply(false, true);
            voxels.Dispose();
            return texture;
        }

        [BurstCompile]
        struct NoiseJob : IJobParallelFor
        {
            public int Size;
            [WriteOnly] public NativeArray<Color32> Voxels;

            public void Execute(int index)
            {
                int i = index;
                var p = new float3(i % Size, i / Size % Size, i / (Size * Size)) / Size;

                // Tiling Perlin fBm, 0..1.
                float perlin = 0f, amp = 0.5f, freq = 4f;
                for (int o = 0; o < 4; o++)
                {
                    perlin += amp * noise.pnoise(p * freq, new float3(freq));
                    amp *= 0.5f;
                    freq *= 2f;
                }
                perlin = math.saturate(perlin * 0.7f + 0.5f);

                var worleyLow = Worley(p, 4) * 0.625f + Worley(p, 8) * 0.25f + Worley(p, 16) * 0.125f;
                // Perlin-Worley: Perlin remapped so it only survives inside Worley's billows.
                var shape = math.saturate(Remap(perlin, worleyLow - 1f, 1f, 0f, 1f));
                var detail = Worley(p, 8) * 0.625f + Worley(p, 16) * 0.25f + Worley(p, 32) * 0.125f;

                Voxels[i] = new Color32((byte)(shape * 255f), (byte)(detail * 255f), 0, 255);
            }

            static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / (b - a) * (d - c);

            /// <summary>Inverted, tiling Worley noise: 1 at feature points, falling to 0 between them.</summary>
            static float Worley(float3 p, int cells)
            {
                var cell = (int3)math.floor(p * cells);
                var nearest = 1e9f;
                for (int z = -1; z <= 1; z++)
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            var n = cell + new int3(x, y, z);
                            var wrapped = (n % cells + cells) % cells;
                            var h = math.hash(new int4(wrapped, cells));
                            var jitter = new float3(h & 0x3FF, (h >> 10) & 0x3FF, (h >> 20) & 0x3FF) / 1023f;
                            var feature = ((float3)n + jitter) / cells;
                            nearest = math.min(nearest, math.distance(p, feature) * cells);
                        }
                return math.saturate(1f - nearest);
            }
        }
    }
}
