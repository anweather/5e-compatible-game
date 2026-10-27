using System.Collections.Generic;
using Chem;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

// The land around the valley, laid out by a world theme (D11) and shaped by
// Punchy's biome terrain (ThirdParty/Punchy, BiomeTerrain). The home-valley
// theme (world/themes/home-valley.json) says where things go: mountains far
// north with a tarn in their foothills, a brook down the west, woods to the
// sides, fields to the south, groves and meadows between. Its blueprint
// (ThemeBlueprint, from PocketTactics) gives each spot a look, and
// world/looks.json says which of Punchy's lands draws it (grassland, forest,
// mountains, fells); where the theme says nothing, Punchy's own regions fill
// in. The lands' weights are blurred, so heights and surfaces blend; the
// theme's lakes and rivers are carved in. The valley floor (the yard, the
// cabins, the lake, the river and the falls) stays as ValleyShape draws it;
// past it the generated land rises over a blend (Mask). Editor-only: it bakes
// the terrain and the cell map, which every machine then loads the same.
public static class ValleySurroundings
{
    // Surfaces, in ValleyBuilder.LayerNames order (the biomes' surfaces are the valley's layers).
    const int Grass = 0, Moss = 1, Rock = 2, Snow = 3, Sand = 4, Soil = 5, Surfaces = 6;
    static readonly string[] Lands = { "Grassland", "Temperate", "Mountains", "Fells" };
    static readonly float[] LandTrees = { 0.12f, 0.75f, 0.3f, 0.05f };

    const string ThemeId = "home-valley";
    const int ThemeSeed = 1;
    /// <summary>A theme region, in cells: a 3×3 core is then 576 cells across, the valley's size.</summary>
    const double RegionCells = 192;
    /// <summary>How far past the valley floor the generated land takes over fully, in metres.</summary>
    const float BlendMetres = 90f;
    /// <summary>The theme's layout is sampled this far apart (metres), and the lands' weights blurred over these.</summary>
    const float Step = 3f, ShapeBlur = 140f, SurfaceBlur = 45f;

    static BiomeTerrain land;
    static Blueprint blueprint;
    static ThemeField field;
    static Vector2 centre, origin;
    static float cellSize;
    static (Vector2 at, float radius)[] floor;
    // The sampled layout: per land a blurred weight map (for heights, for surfaces), and per sample
    // the look's tree factor and what fills it (ThemeField.Mat*).
    static float[][] shapeMaps, surfaceMaps;
    static float[] treeFactor;
    static int[] material;
    static int size;
    static float[] lakeLevels;

    /// <summary>Sets the land up around the valley as ValleyShape.Plan laid it out.</summary>
    public static void Plan(Vector2 valleyCentre, Vector2 lakeCentre, Vector2 falls, IEnumerable<Vector2> cabins, float cells)
    {
        centre = valleyCentre;
        cellSize = cells;
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
        }, Biomes(), Surfaces);

        var registry = ThemeRegistry.Load(ChemGrid.FindWorldDir());
        blueprint = ThemeBlueprint.Solve(ThemeSeed, registry.Themes[ThemeId], RegionCells);
        field = new ThemeField(blueprint, new GeoBox(-600, -600, 600, 600));
        SampleLayout(registry);
    }

    // Punchy's temperate biomes (Grassland, Temperate, Mountains; Tundra as the fells), scaled to a
    // valley under a kilometre across: lower relief and smaller features than his 8 km world. In
    // Lands order.
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
            Rules =
            {
                Patches(Moss, 0.5f), Slope(Rock, 30f),
                new SurfaceRule { Surface = Snow, UsePatches = true, PatchSize = 45f, PatchCoverage = 0.35f, UseHeight = true, HeightRange = new float2(110f, 10000f) },
            },
        },
    };

    static SurfaceRule Slope(int surface, float from) =>
        new() { Surface = surface, UseSlope = true, SlopeRange = new float2(from, 90f) };

    static SurfaceRule High(int surface, float from, float maxSlope) =>
        new() { Surface = surface, UseSlope = true, SlopeRange = new float2(-90f, maxSlope), UseHeight = true, HeightRange = new float2(from, 10000f) };

    static SurfaceRule Patches(int surface, float coverage) =>
        new() { Surface = surface, UsePatches = true, PatchSize = 45f, PatchCoverage = coverage };

    // ---------- the theme's layout, sampled ----------

    // The continent cell (x east, y south, the yard's centre at 0) of a point on the ground plane.
    static (int x, int y) Cell(Vector2 p) =>
        (Mathf.RoundToInt((p.x - centre.x) / cellSize), Mathf.RoundToInt(-(p.y - centre.y) / cellSize));

    static void SampleLayout(ThemeRegistry registry)
    {
        var layout = ValleyTerrain.Layout;
        float extent = layout.ChunkSize * layout.ChunksX + 2 * ShapeBlur;
        origin = new Vector2(layout.OriginX - ShapeBlur, layout.OriginZ - ShapeBlur);
        size = Mathf.CeilToInt(extent / Step) + 1;
        var landOf = new Dictionary<string, int>();
        var treesOf = new Dictionary<string, float>();
        foreach (var kv in registry.Looks)
        {
            landOf[kv.Key] = System.Array.IndexOf(Lands, (string)kv.Value["land"]);
            treesOf[kv.Key] = (float?)kv.Value["trees"] ?? 1f;
        }
        var weights = new float[Lands.Length][];
        for (int k = 0; k < Lands.Length; k++) weights[k] = new float[size * size];
        treeFactor = new float[size * size];
        material = new int[size * size];
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                var p = origin + new Vector2(i, j) * Step;
                var (cx, cy) = Cell(p);
                int at = j * size + i;
                string look = field.Look(cx, cy);
                material[at] = field.Material(cx, cy);
                treeFactor[at] = look != null && treesOf.TryGetValue(look, out var t) ? t : 1f;
                if (look != null && landOf.TryGetValue(look, out int l) && l >= 0) weights[l][at] = 1f;
                else Filler(p, weights, at);
            }
        shapeMaps = new float[Lands.Length][];
        surfaceMaps = new float[Lands.Length][];
        for (int k = 0; k < Lands.Length; k++)
        {
            shapeMaps[k] = Blurred(weights[k], Mathf.RoundToInt(ShapeBlur / Step / 2));
            surfaceMaps[k] = Blurred(weights[k], Mathf.RoundToInt(SurfaceBlur / Step / 2));
        }
        LevelLakes();
    }

    // Where the theme says nothing: Punchy's own random regions.
    static void Filler(Vector2 p, float[][] weights, int at)
    {
        var w = land.Weights(new float2(p.x, p.y), false);
        for (int s = 0; s < 4; s++)
            if (w.Weights[s] > 0f) weights[w.Ids[s]][at] += w.Weights[s];
    }

    // Two passes of a box blur each way (close to a gaussian), radius in samples.
    static float[] Blurred(float[] map, int radius)
    {
        var a = (float[])map.Clone();
        var b = new float[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxBlur(a, b, radius, true);
            BoxBlur(b, a, radius, false);
        }
        return a;
    }

    static void BoxBlur(float[] from, float[] to, int r, bool rows)
    {
        for (int line = 0; line < size; line++)
        {
            float sum = 0f;
            int count = 0;
            for (int k = 0; k <= r && k < size; k++)
            {
                sum += from[Index(line, k, rows)];
                count++;
            }
            for (int k = 0; k < size; k++)
            {
                to[Index(line, k, rows)] = sum / count;
                int drop = k - r, add = k + r + 1;
                if (drop >= 0)
                {
                    sum -= from[Index(line, drop, rows)];
                    count--;
                }
                if (add < size)
                {
                    sum += from[Index(line, add, rows)];
                    count++;
                }
            }
        }
    }

    static int Index(int line, int k, bool rows) => rows ? line * size + k : k * size + line;

    // Each theme lake's water level: the lowest generated ground inside it, so it never spills.
    static void LevelLakes()
    {
        lakeLevels = new float[blueprint.Lakes.Count];
        for (int l = 0; l < blueprint.Lakes.Count; l++)
        {
            var blob = blueprint.Lakes[l].Blob;
            float lowest = float.MaxValue;
            for (int k = 0; k < 64; k++)
            {
                float a = k / 64f * Mathf.PI * 2f, rr = (k % 4 + 1) / 4f;
                float cx = (float)blob.Cx + Mathf.Cos(a) * (float)blob.Rx * rr, cy = (float)blob.Cy + Mathf.Sin(a) * (float)blob.Ry * rr;
                lowest = Mathf.Min(lowest, Dry(new Vector2(centre.x + cx * cellSize, centre.y - cy * cellSize)));
            }
            lakeLevels[l] = lowest;
        }
    }

    // Bilinear sample of a sampled map at a point.
    static float Sample(float[] map, Vector2 p)
    {
        var q = (p - origin) / Step;
        int i = Mathf.Clamp(Mathf.FloorToInt(q.x), 0, size - 2), j = Mathf.Clamp(Mathf.FloorToInt(q.y), 0, size - 2);
        float fx = Mathf.Clamp01(q.x - i), fy = Mathf.Clamp01(q.y - j);
        float top = Mathf.Lerp(map[j * size + i], map[j * size + i + 1], fx);
        float bottom = Mathf.Lerp(map[(j + 1) * size + i], map[(j + 1) * size + i + 1], fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    static int Nearest(Vector2 p)
    {
        var q = (p - origin) / Step;
        int i = Mathf.Clamp(Mathf.RoundToInt(q.x), 0, size - 1), j = Mathf.Clamp(Mathf.RoundToInt(q.y), 0, size - 1);
        return j * size + i;
    }

    static BiomeWeights WeightsAt(float[][] maps, Vector2 p)
    {
        var w = new BiomeWeights();
        for (int k = 0; k < Lands.Length; k++)
        {
            w.Ids[k] = k;
            w.Weights[k] = Mathf.Max(0f, Sample(maps[k], p));
        }
        w.Normalize();
        return w;
    }

    // ---------- what ValleyShape and the builder ask ----------

    /// <summary>0 on the valley floor, rising to 1 where the generated land takes over.</summary>
    public static float Mask(Vector2 p)
    {
        float outside = float.MaxValue;
        foreach (var (at, radius) in floor) outside = Mathf.Min(outside, (p - at).magnitude - radius);
        outside = Mathf.Min(outside, ValleyShape.DistanceToCourse(p, ValleyShape.River) - 35f);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, BlendMetres, outside));
    }

    // The lands' height at a point, before lakes and rivers are carved in.
    static float Dry(Vector2 p) => land.Height(new float2(p.x, p.y), WeightsAt(shapeMaps, p));

    /// <summary>The generated land's height at a point (metres above the yard), lakes and rivers carved in.</summary>
    public static float Height(Vector2 p)
    {
        float h = Dry(p);
        var (cx, cy) = Cell(p);
        for (int l = 0; l < blueprint.Lakes.Count; l++)
        {
            var lake = blueprint.Lakes[l];
            if (lake.Material != "water") continue;
            // A flat bed two metres under the lake's level, rising to the shore.
            float rho = (float)ThemeGeometry.Rho(lake.Blob, cx + 0.5, cy + 0.5);
            float inside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.25f, 0.9f, rho));
            h = Mathf.Lerp(h, Mathf.Min(h, lakeLevels[l] - 2f), inside);
        }
        // A channel a metre and a half deep along each river, easing out over a few cells.
        float bank = (float)field.RiverBankDistance(cx, cy);
        if (bank < 4f) h -= 1.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 0f, bank));
        return h;
    }

    /// <summary>The theme's water at a point: 'w' a lake, 'r' a river, or '\0' (dry).</summary>
    public static char Water(Vector2 p)
    {
        var (cx, cy) = Cell(p);
        if (field.Material(cx, cy) != ThemeField.MatWater) return '\0';
        return field.RiverBankDistance(cx, cy) <= 0 ? 'r' : 'w';
    }

    /// <summary>The generated land's layers at a point (LayerNames order, summing to 1); sand on the theme's lake shores.</summary>
    public static float[] LayerWeights(Vector2 p, float height, float slope)
    {
        var w = land.SurfaceWeights(new float2(p.x, p.y), height, slope, WeightsAt(surfaceMaps, p));
        if (material[Nearest(p)] == ThemeField.MatShoreSand)
        {
            for (int k = 0; k < w.Length; k++) w[k] *= 0.2f;
            w[Sand] += 0.8f;
        }
        return w;
    }

    /// <summary>How thick the forest grows at a point on the generated land, 0..1: the land's own, scaled by the theme's look; none in water.</summary>
    public static float Trees(Vector2 p)
    {
        int at = Nearest(p);
        if (material[at] != ThemeField.MatNone) return 0f;
        var w = WeightsAt(surfaceMaps, p);
        float density = 0f;
        for (int k = 0; k < 4; k++) density += w.Weights[k] * LandTrees[w.Ids[k]];
        return Mathf.Clamp01(density * treeFactor[at]);
    }
}
