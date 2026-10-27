using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

// The land around the valley: Punchy's generated biomes (ThirdParty/Punchy,
// BiomeTerrain) instead of the hand-shaped ridges that used to close it in.
// The valley floor (the yard, the cabins, the lake, the river and the falls)
// stays as ValleyShape draws it; past it the generated land rises over a
// blend (Mask), grassland and forest to the sides and south, mountains and
// fells to the north behind the falls (a regional bias on the biome picks,
// like a world theme). The biomes' surfaces map straight onto the valley's
// terrain layers, and each biome says how thick its forest grows.
// Editor-only: it bakes the terrain and the cell map, which every machine then
// loads the same, so the float noise never touches the lockstep chemistry.
public static class ValleySurroundings
{
    // Surfaces, in ValleyBuilder.LayerNames order (the biomes' surfaces are the valley's layers).
    // (Sand, 4, is the valley floor's own lake shore: no biome here has water to put it by.)
    const int Grass = 0, Moss = 1, Rock = 2, Snow = 3, Soil = 5, Surfaces = 6;

    /// <summary>How far past the valley floor the generated land takes over fully, in metres.</summary>
    const float BlendMetres = 90f;

    static readonly Dictionary<string, float> TreeDensity = new()
    {
        ["Grassland"] = 0.12f, ["Temperate"] = 0.75f, ["Mountains"] = 0.3f, ["Fells"] = 0.05f,
    };

    static BiomeTerrain land;
    static Vector2 centre;
    static (Vector2 at, float radius)[] floor;

    /// <summary>Sets the land up around the valley as ValleyShape.Plan laid it out.</summary>
    public static void Plan(Vector2 valleyCentre, Vector2 lakeCentre, Vector2 falls, IEnumerable<Vector2> cabins)
    {
        centre = valleyCentre;
        var shapes = new List<(Vector2, float)> { (valleyCentre, 150f), (lakeCentre, 130f), (falls, 45f) };
        foreach (var c in cabins) shapes.Add((c, 28f));
        floor = shapes.ToArray();
        land = new BiomeTerrain(new BiomeNoiseSettings
        {
            Seed = 7,
            CellSize = 260f,
            BlendDistance = 50f,
            ShapeBlendDistance = 140f,
            WarpStrength = 70f,
        }, Biomes(), Surfaces, Bias);
    }

    // Punchy's temperate biomes (Grassland, Temperate, Mountains; Tundra as the fells), scaled to a
    // valley under a kilometre across: lower relief and smaller features than his 8 km world.
    static List<Biome> Biomes() => new()
    {
        new Biome
        {
            Name = "Grassland", Frequency = 1.1f, BaseElevation = 14f, Relief = 12f, FeatureSize = 500f, Octaves = 4, Sharpness = 1f,
            BaseSurface = Grass,
            Rules = { Slope(Soil, 22f), Slope(Rock, 32f), Patches(Moss, 0.3f) },
        },
        new Biome
        {
            Name = "Temperate", Frequency = 1f, BaseElevation = 22f, Relief = 60f, FeatureSize = 350f, Octaves = 5, Sharpness = 1.6f,
            BaseSurface = Grass,
            Rules = { Patches(Moss, 0.45f), Slope(Soil, 22f), Slope(Rock, 32f), High(Snow, 95f, 45f) },
        },
        new Biome
        {
            Name = "Mountains", Frequency = 0.8f, BaseElevation = 45f, Shape = TerrainShapeKind.Ridged, Relief = 170f, FeatureSize = 600f,
            Octaves = 6, Sharpness = 1.3f, BaseSurface = Rock,
            Rules =
            {
                new SurfaceRule { Surface = Grass, UseSlope = true, SlopeRange = new float2(-90f, 28f), UseHeight = true, HeightRange = new float2(-1000f, 90f) },
                High(Snow, 140f, 50f),
            },
        },
        new Biome
        {
            Name = "Fells", Frequency = 0.8f, BaseElevation = 85f, Relief = 25f, FeatureSize = 400f, Octaves = 4, Sharpness = 1f,
            BaseSurface = Soil,
            Rules = { Patches(Moss, 0.5f), Slope(Rock, 30f), Patches(Snow, 0.35f) },
        },
    };

    static SurfaceRule Slope(int surface, float from) =>
        new() { Surface = surface, UseSlope = true, SlopeRange = new float2(from, 90f) };

    static SurfaceRule High(int surface, float from, float maxSlope) =>
        new() { Surface = surface, UseSlope = true, SlopeRange = new float2(-90f, maxSlope), UseHeight = true, HeightRange = new float2(from, 10000f) };

    static SurfaceRule Patches(int surface, float coverage) =>
        new() { Surface = surface, UsePatches = true, PatchSize = 45f, PatchCoverage = coverage };

    // Mountains and fells north of the valley (behind the falls), grassland to the south, forest
    // to the sides: how much more likely each biome is in a region, by where it lies.
    static float Bias(float2 regionCentre, int biome)
    {
        float north = math.saturate((regionCentre.y - centre.y - 80f) / 220f);
        float south = math.saturate((centre.y - regionCentre.y - 60f) / 200f);
        return biome switch
        {
            0 => 1f + 2f * south - 0.9f * north,   // grassland
            1 => 1.5f - north,                     // temperate forest
            2 => 0.2f + 5f * north,                // mountains
            _ => 0.1f + 2f * north,                // fells
        };
    }

    /// <summary>0 on the valley floor, rising to 1 where the generated land takes over.</summary>
    public static float Mask(Vector2 p)
    {
        float outside = float.MaxValue;
        foreach (var (at, radius) in floor) outside = Mathf.Min(outside, (p - at).magnitude - radius);
        outside = Mathf.Min(outside, ValleyShape.DistanceToCourse(p, ValleyShape.River) - 35f);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, BlendMetres, outside));
    }

    /// <summary>The generated land's height at a point (metres above the yard), before the blend.</summary>
    public static float Height(Vector2 p) => land.Height(new float2(p.x, p.y));

    /// <summary>The generated land's layers at a point (LayerNames order, summing to 1).</summary>
    public static float[] LayerWeights(Vector2 p, float height, float slope) => land.SurfaceWeights(new float2(p.x, p.y), height, slope);

    /// <summary>How thick the forest grows at a point on the generated land, 0..1.</summary>
    public static float Trees(Vector2 p) => TreeDensity.TryGetValue(land.Dominant(new float2(p.x, p.y)).Name, out var d) ? d : 0.3f;
}
