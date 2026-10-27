// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's BiomeData.cs (BiomeSampling), BiomeBaking.cs
// and TerrainSource.ShapeHeight: the Entities blobs are plain classes, a surface
// stack gives per-surface weights (to paint Unity terrain layers) instead of
// blended material properties, and a biome's chance in a region can be biased
// by where the region is (CellBias). The maths is his. Editor-only: it bakes
// terrain, and its float noise is not lockstep-safe, so it stays out of Chem.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S1066, S3358
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    public enum TerrainShapeKind : byte
    {
        /// <summary>Rolling fractal hills.</summary>
        Hills,
        /// <summary>Sharp ridges and peaks, grouped into massifs.</summary>
        Ridged,
    }

    /// <summary>A rule layer of a biome's surface stack (Punchy's SurfaceLayer): where a surface covers the base.</summary>
    public sealed class SurfaceRule
    {
        public int Surface;
        public bool UseSlope;
        public float2 SlopeRange = new float2(35f, 90f);
        public float SlopeBlend = 5f;
        public bool UseHeight;
        public float2 HeightRange = new float2(80f, 10000f);
        public float HeightBlend = 8f;
        public bool UsePatches;
        public float PatchSize = 40f;
        public float PatchCoverage = 0.4f;
        public float EdgeNoise = 0.5f;
        public float Opacity = 1f;
    }

    /// <summary>A region type: its elevation and shape, and the surfaces covering it (Punchy's BiomeDefinition).</summary>
    public sealed class Biome
    {
        public string Name;
        public float Frequency = 1f;
        public float BaseElevation = 20f;
        public TerrainShapeKind Shape = TerrainShapeKind.Hills;
        public float Relief = 40f;
        public float FeatureSize = 400f;
        public int Octaves = 5;
        public float Sharpness = 1.5f;
        public int TerraceSteps;
        public int BaseSurface;
        public List<SurfaceRule> Rules = new List<SurfaceRule>();
        internal float2 NoiseOffset;
    }

    /// <summary>Procedurally placed biome regions: warped Voronoi cells, one biome per cell.</summary>
    public struct BiomeNoiseSettings
    {
        public uint Seed;
        /// <summary>Typical width of one biome region, in metres.</summary>
        public float CellSize;
        /// <summary>Width of the transition between neighbouring biomes' surfaces, in metres.</summary>
        public float BlendDistance;
        /// <summary>Width of the transition between neighbouring biomes' elevations and shapes, in metres.</summary>
        public float ShapeBlendDistance;
        /// <summary>How far (metres) region borders are pushed around to look organic.</summary>
        public float WarpStrength;
    }

    /// <summary>Up to four biomes and their weights at a point (weights sum to 1).</summary>
    public struct BiomeWeights
    {
        public int4 Ids;
        public float4 Weights;

        public void Add(int id, float weight)
        {
            if (weight <= 0f)
                return;
            for (int i = 0; i < 4; i++)
            {
                if (Weights[i] > 0f && Ids[i] == id)
                {
                    Weights[i] += weight;
                    return;
                }
            }

            // New biome: take an empty slot, or replace the weakest if this one is stronger.
            int slot = 0;
            for (int i = 1; i < 4; i++)
                if (Weights[i] < Weights[slot])
                    slot = i;
            if (weight > Weights[slot])
            {
                Ids[slot] = id;
                Weights[slot] = weight;
            }
        }

        public void Normalize()
        {
            var sum = math.csum(Weights);
            Weights = sum > 0f ? Weights / sum : new float4(1f, 0f, 0f, 0f);
        }
    }

    /// <summary>Biomes, the noise that places them, and sampling: heights and surface weights at a world point.</summary>
    public sealed class BiomeTerrain
    {
        readonly BiomeNoiseSettings settings;
        readonly List<Biome> biomes;
        readonly int surfaceCount;
        readonly Func<float2, int, float> cellBias;

        /// <param name="cellBias">
        /// How likely each biome is in a region, by the region's centre (world metres) and the biome's index:
        /// multiplies its Frequency there. Null for Punchy's plain random pick.
        /// </param>
        public BiomeTerrain(BiomeNoiseSettings settings, List<Biome> biomes, int surfaceCount, Func<float2, int, float> cellBias = null)
        {
            this.settings = settings;
            this.biomes = biomes;
            this.surfaceCount = surfaceCount;
            this.cellBias = cellBias;
            for (int i = 0; i < biomes.Count; i++)
            {
                // Each biome reads a different region of the noise field.
                var offsetHash = math.hash(new uint2(settings.Seed, (uint)i * 2654435761u));
                biomes[i].NoiseOffset = new float2(offsetHash & 0xFFFF, offsetHash >> 16) / 65535f * 500f;
            }
        }

        public int SurfaceCount => surfaceCount;

        /// <summary>Biome weights at a point: narrow (for surfaces) or wide (for heights).</summary>
        public BiomeWeights Weights(float2 xz, bool forShape)
        {
            var p = Warp(xz);
            var cell = (int2)math.floor(p);
            Span<float> distances = stackalloc float[9];
            Span<int> ids = stackalloc int[9];
            float nearest = float.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                var c = cell + new int2(i % 3 - 1, i / 3 - 1);
                var d = math.distance(p, c + CellJitter(c));
                distances[i] = d;
                ids[i] = CellBiome(c);
                nearest = math.min(nearest, d);
            }
            // Weights depend only on how much farther each point is than the nearest (continuous
            // everywhere), so blends never crease.
            var result = default(BiomeWeights);
            var blend = math.max(forShape ? settings.ShapeBlendDistance : settings.BlendDistance, 1e-3f) / settings.CellSize;
            for (int i = 0; i < 9; i++)
                result.Add(ids[i], math.smoothstep(0f, 1f, 1f - (distances[i] - nearest) / blend));
            result.Normalize();
            return result;
        }

        /// <summary>Biome base elevations plus each biome's relief, blended by shape weight (Punchy's ShapeHeight).</summary>
        public float Height(float2 xz)
        {
            var shape = Weights(xz, true);
            float height = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = shape.Weights[i];
                if (w <= 0f)
                    continue;
                var biome = biomes[shape.Ids[i]];
                height += w * (biome.BaseElevation + Relief(biome, xz));
            }
            return height;
        }

        /// <summary>
        /// Each surface's share at a point, summing to 1 (Punchy's EvaluateBiome, as weights: a rule laid
        /// over the stack with mask m scales what's under it by 1 - m and adds m to its surface).
        /// </summary>
        public float[] SurfaceWeights(float2 xz, float height, float slopeDegrees)
        {
            var result = new float[surfaceCount];
            var surface = Weights(xz, false);
            float edgeNoise = noise.snoise(xz * 0.045f) * 0.7f + noise.snoise(xz * 0.17f) * 0.3f;
            var stack = new float[surfaceCount];
            for (int k = 0; k < 4; k++)
            {
                var bw = surface.Weights[k];
                if (bw <= 0f)
                    continue;
                var biome = biomes[surface.Ids[k]];
                Array.Clear(stack, 0, stack.Length);
                stack[biome.BaseSurface] = 1f;
                foreach (var layer in biome.Rules)
                {
                    var mask = layer.Opacity;
                    var edge = edgeNoise * layer.EdgeNoise * 2f;
                    if (layer.UseSlope)
                        mask *= RangeMask(slopeDegrees + edge * layer.SlopeBlend, layer.SlopeRange, layer.SlopeBlend);
                    if (layer.UseHeight)
                        mask *= RangeMask(height + edge * layer.HeightBlend, layer.HeightRange, layer.HeightBlend);
                    if (layer.UsePatches && mask > 0f)
                    {
                        var n = noise.snoise(xz / math.max(layer.PatchSize, 1f) + layer.Surface * 13.7f) * 0.5f + 0.5f;
                        mask *= math.smoothstep(1f - layer.PatchCoverage - 0.08f, 1f - layer.PatchCoverage + 0.08f, n);
                    }
                    if (mask <= 0f)
                        continue;
                    for (int s = 0; s < stack.Length; s++)
                        stack[s] *= 1f - mask;
                    stack[layer.Surface] += mask;
                }
                for (int s = 0; s < stack.Length; s++)
                    result[s] += stack[s] * bw;
            }
            return result;
        }

        /// <summary>The biome with the most weight at a point (narrow blend).</summary>
        public Biome Dominant(float2 xz)
        {
            var w = Weights(xz, false);
            int best = 0;
            for (int i = 1; i < 4; i++)
                if (w.Weights[i] > w.Weights[best])
                    best = i;
            return biomes[w.Ids[best]];
        }

        /// <summary>World position → warped position in cell units.</summary>
        float2 Warp(float2 xz)
        {
            var s = settings;
            var seedOffset = new float2(s.Seed % 997u * 0.6180339f, s.Seed % 991u * 0.4142135f) * 1000f;
            var warpFreq = 2f / math.max(s.CellSize, 1f);
            var warp = new float2(
                noise.snoise((xz + seedOffset) * warpFreq),
                noise.snoise((xz - seedOffset) * warpFreq + 31.7f));
            return (xz + warp * s.WarpStrength) / s.CellSize;
        }

        float2 CellJitter(int2 cell)
        {
            var h = math.hash(new int3(cell, (int)settings.Seed));
            return 0.1f + 0.8f * new float2(h & 0xFFFF, h >> 16) / 65535f;
        }

        int CellBiome(int2 cell)
        {
            if (biomes.Count <= 1)
                return 0;
            var centre = ((float2)cell + 0.5f) * settings.CellSize;
            float total = 0f;
            for (int i = 0; i < biomes.Count; i++)
                total += Frequency(i, centre);
            var h = math.hash(new int3(cell, (int)(settings.Seed * 7919u + 17u)));
            var pick = h / (float)uint.MaxValue * total;
            for (int i = 0; i < biomes.Count; i++)
            {
                pick -= Frequency(i, centre);
                if (pick <= 0f)
                    return i;
            }
            return biomes.Count - 1;
        }

        float Frequency(int i, float2 centre) => biomes[i].Frequency * (cellBias != null ? math.max(0f, cellBias(centre, i)) : 1f);

        /// <summary>Height of one biome's relief above its base elevation, at xz (metres).</summary>
        static float Relief(Biome b, float2 xz)
        {
            if (b.Relief <= 0f)
                return 0f;

            // Offsets are added after scaling and kept small: float noise coordinates in the hundreds of
            // thousands lose enough precision to make the surface visibly jagged.
            var p = xz / math.max(b.FeatureSize, 1f);
            var o = b.NoiseOffset;
            float h;
            if (b.Shape == TerrainShapeKind.Ridged)
            {
                float sum = 0f, norm = 0f, amp = 1f, freq = 1f;
                for (int i = 0; i < b.Octaves; i++)
                {
                    var n = 1f - math.abs(noise.snoise(p * freq + o + i * 17.3f));
                    sum += n * n * amp;
                    norm += amp;
                    amp *= 0.5f;
                    freq *= 2.03f;
                }
                // Group ridges into massifs so the whole biome isn't uniformly crumpled.
                var massif = math.saturate(noise.snoise(p * 0.3f + o + 71.3f) * 0.6f + 0.6f);
                h = sum / norm * massif;
            }
            else
            {
                float sum = 0f, norm = 0f, amp = 1f, freq = 1f;
                for (int i = 0; i < b.Octaves; i++)
                {
                    sum += amp * noise.snoise(p * freq + o + i * 17.3f);
                    norm += amp;
                    amp *= 0.5f;
                    freq *= 2.03f;
                }
                h = sum / norm * 0.5f + 0.5f;
            }

            h = math.pow(math.saturate(h), b.Sharpness);
            if (b.TerraceSteps > 0)
            {
                var t = h * b.TerraceSteps;
                h = (math.floor(t) + math.smoothstep(0.35f, 0.65f, math.frac(t))) / b.TerraceSteps;
            }
            return h * b.Relief;
        }

        static float RangeMask(float x, float2 range, float blend) =>
            math.smoothstep(range.x - blend, range.x + blend, x) *
            (1f - math.smoothstep(range.y - blend, range.y + blend, x));
    }
}
