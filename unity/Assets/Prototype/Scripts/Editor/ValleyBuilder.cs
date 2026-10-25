using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Sets the Chemistry Yard in a mountain valley: a generated terrain with the
// yard flush in a flat green valley floor, a ridged mountain to the north, a
// lake to the south, hills closing the sides, trees, grass and rocks from the
// terrain packs, the sample project's mountain skybox, haze, and a small log
// cabin. Everything is generated from code, so rebuilding gives the same valley.
// The valley is scenery only: the chemistry still lives in the yard's grid.
public static class ValleyBuilder
{
    const string Generated = "Assets/Prototype/Generated";
    const string TerrainPack = "Assets/TerrainSampleAssets";
    const string DemoPack = "Assets/TerrainDemoScene_URP/Prefabs";

    const float Size = 900f;        // terrain width and length, metres
    const float Depth = 22f;        // terrain base below the yard
    const float MaxHeight = 300f;
    const int HeightRes = 513;
    const int SplatRes = 512;
    const int DetailRes = 512;
    const float LakeLevel = -0.6f;

    // The yard's rectangle in world space and its centre, set by Dress.
    static Rect yard;
    static Vector2 centre;
    static Vector2 lakeCentre;
    static Vector2 cabin;
    static readonly List<Vector2> treeSpots = new();

    public static void Dress(ChemGrid grid, string[] yardRows)
    {
        int width = yardRows[0].Length, height = yardRows.Length;
        float half = grid.cellSize / 2f;
        yard = Rect.MinMaxRect(-half, -half, (width - 1) * grid.cellSize + half, (height - 1) * grid.cellSize + half);
        centre = yard.center;
        lakeCentre = centre + new Vector2(25f, -95f);
        cabin = new Vector2(yard.xMin - 16f, centre.y + 4f);

        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();

        var terrain = BuildTerrain();
        BuildLake();
        BuildSky();
        BuildCabin();
        ScatterRocks(terrain);
        grid.grassTint = new Color(0.95f, 1.05f, 0.85f);
        WireChemistry(grid, yardRows, terrain);
    }

    // --- terrain -------------------------------------------------------------

    static Terrain BuildTerrain()
    {
        // Order matters. Every asset the terrain uses (greener grass layers, repaired trees) is
        // created first; then the terrain asset is saved, and only then filled in. Creating other
        // assets after the terrain is saved reloads it from disk and throws away heights set in
        // memory (which left the valley flat, 22 m below the yard); filling it in before saving
        // loses the paint maps.
        var layers = LoadLayers();
        var trees = LoadTrees();

        var data = new TerrainData
        {
            heightmapResolution = HeightRes,
            alphamapResolution = SplatRes,
            baseMapResolution = 512,
        };
        data.SetDetailResolution(DetailRes, 16);
        data.size = new Vector3(Size, MaxHeight, Size);
        AssetDatabase.DeleteAsset(Generated + "/ValleyTerrain.asset");
        AssetDatabase.CreateAsset(data, Generated + "/ValleyTerrain.asset");

        var heights = new float[HeightRes, HeightRes];
        for (int iz = 0; iz < HeightRes; iz++)
        for (int ix = 0; ix < HeightRes; ix++)
        {
            var w = World(ix / (float)(HeightRes - 1), iz / (float)(HeightRes - 1));
            heights[iz, ix] = Mathf.Clamp01((HeightAt(w) + Depth) / MaxHeight);
        }
        data.SetHeights(0, 0, heights);

        PaintLayers(data, layers);
        PlantTrees(data, trees);
        PaintDetails(data);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Valley";
        go.transform.position = new Vector3(centre.x - Size / 2f, -Depth, centre.y - Size / 2f);
        var terrain = go.GetComponent<Terrain>();
        terrain.treeDistance = 900f;
        terrain.treeBillboardDistance = 160f;
        terrain.detailObjectDistance = 120f;
        terrain.heightmapPixelError = 4f;
        return terrain;
    }

    static Vector2 World(float u, float v) => new Vector2(centre.x - Size / 2f + u * Size, centre.y - Size / 2f + v * Size);

    // World height (metres above the yard) at a point on the ground plane.
    static float HeightAt(Vector2 p)
    {
        float d = DistanceToYard(p);
        if (d <= 2f) return -0.05f;                     // just under the yard's tiles
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 30f, d));

        var rel = p - centre;
        float meadow = 2.2f * Fbm(p * 0.012f, 3) * rise;
        // The mountain starts well back from the yard and climbs gradually, so it reads as a
        // mountain across the valley rather than a wall.
        float north = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 430f, rel.y)), 1.4f);
        float mountain = north * (110f + 120f * Ridged(p * 0.0045f));
        float sides = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(220f, 440f, Mathf.Abs(rel.x))) * (55f + 45f * Fbm(p * 0.008f + Vector2.one * 7f, 3));
        float south = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(260f, 440f, -rel.y)) * (40f + 35f * Fbm(p * 0.01f + Vector2.one * 3f, 3));

        float lake = LakeDepth(p);
        float h = Mathf.Max(meadow + mountain + sides + south, 0f) * rise - lake;
        // A level pad for the cabin, easing back into the meadow.
        float pad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, (p - cabin).magnitude));
        h = Mathf.Lerp(0f, h, pad);
        // Near the yard and the cabin the ground never dips below their base; heightmap samples
        // are 1.8 m apart, so a dip just outside the yard showed the sides of its tiles.
        if (d < 45f || (p - cabin).magnitude < 20f) h = Mathf.Max(h, 0f);
        return h;
    }

    static float LakeDepth(Vector2 p)
    {
        var q = p - lakeCentre;
        float e = Mathf.Sqrt(q.x * q.x / (95f * 95f) + q.y * q.y / (55f * 55f));
        e += 0.12f * (Mathf.PerlinNoise(p.x * 0.03f, p.y * 0.03f) - 0.5f);
        return 7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.05f, 0.55f, e));
    }

    static float DistanceToYard(Vector2 p)
    {
        float dx = Mathf.Max(yard.xMin - p.x, 0f, p.x - yard.xMax);
        float dy = Mathf.Max(yard.yMin - p.y, 0f, p.y - yard.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    static float Fbm(Vector2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < octaves; i++, p *= 2.03f, amp *= 0.5f) sum += amp * (Mathf.PerlinNoise(p.x, p.y) * 2f - 1f);
        return sum;
    }

    static float Ridged(Vector2 p)
    {
        float sum = 0f, amp = 0.55f;
        for (int i = 0; i < 5; i++, p *= 2.1f, amp *= 0.5f) sum += amp * (1f - Mathf.Abs(Mathf.PerlinNoise(p.x, p.y) * 2f - 1f));
        return sum;
    }

    // Grass on flat ground, moss in the meadows, rock on slopes, snow up high, sand at the lake.
    static readonly string[] LayerNames = { "Grass_A", "Grass_Moss", "Rock", "Snow", "Sand", "Soil_Rocks" };

    // The terrain layers in LayerNames order, with greener copies of the grass; null if the pack is missing.
    static TerrainLayer[] LoadLayers()
    {
        var layers = new List<TerrainLayer>();
        foreach (var n in LayerNames)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainPack}/TerrainLayers/{n}_TerrainLayer.terrainlayer");
            if (layer != null) layers.Add(n.StartsWith("Grass") ? Greener(layer) : layer);
        }
        if (layers.Count == LayerNames.Length) return layers.ToArray();
        Debug.LogWarning("Terrain Sample Asset Pack missing: the valley will be untextured. Run Prototype > Restore Asset Packs.");
        return null;
    }

    static void PaintLayers(TerrainData data, TerrainLayer[] layers)
    {
        if (layers == null) return;
        string[] names = LayerNames;
        data.terrainLayers = layers;

        var map = new float[SplatRes, SplatRes, names.Length];
        for (int iz = 0; iz < SplatRes; iz++)
        for (int ix = 0; ix < SplatRes; ix++)
        {
            float u = ix / (float)(SplatRes - 1), v = iz / (float)(SplatRes - 1);
            var w = World(u, v);
            float h = HeightAt(w);
            float slope = data.GetSteepness(u, v);
            float shore = Mathf.InverseLerp(1.5f, 0f, Mathf.Abs(h - LakeLevel)) * Mathf.InverseLerp(30f, 0f, DistanceToLake(w));
            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 38f, slope));
            float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 175f, h + 20f * Fbm(w * 0.02f, 2))) * (1f - rock * 0.6f);
            float moss = Mathf.Clamp01(0.5f + Fbm(w * 0.03f, 3)) * (1f - rock);
            float soil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 120f, h)) * (1f - rock) * (1f - snow);
            float sand = shore;
            float grass = Mathf.Max(0f, 1f - rock - snow - moss * 0.6f - sand - soil);
            float[] wts = { grass, moss * 0.6f, rock, snow, sand, soil };
            float total = 0f;
            foreach (var x in wts) total += x;
            for (int k = 0; k < wts.Length; k++) map[iz, ix, k] = total > 0f ? wts[k] / total : (k == 0 ? 1f : 0f);
        }
        data.SetAlphamaps(0, 0, map);
    }

    // The pack's grass textures are olive; a copy of the layer with a greener copy of its
    // texture gives a lush valley without editing the pack. (URP's terrain shader ignores the
    // layer's colour remap, so the colour has to be in the texture.)
    static TerrainLayer Greener(TerrainLayer source)
    {
        var layer = LoadOrCreate($"{Generated}/{source.name}_Green.terrainlayer", () => new TerrainLayer());
        layer.diffuseTexture = Tinted(source.diffuseTexture, new Color(0.78f, 1.12f, 0.6f));
        layer.normalMapTexture = source.normalMapTexture;
        layer.maskMapTexture = source.maskMapTexture;
        layer.tileSize = source.tileSize;
        layer.normalScale = source.normalScale;
        layer.smoothness = source.smoothness;
        layer.metallic = source.metallic;
        layer.diffuseRemapMin = source.diffuseRemapMin;
        layer.diffuseRemapMax = source.diffuseRemapMax;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static float DistanceToLake(Vector2 p) => Mathf.Max(0f, (p - lakeCentre).magnitude - 70f);

    static bool KeepClear(Vector2 p, float margin) =>
        DistanceToYard(p) < margin || (p - cabin).magnitude < 12f || LakeDepth(p) > 0.05f;

    // The tree species, repaired for URP; empty if the pack is missing.
    static List<TreePrototype> LoadTrees()
    {
        var conifer = RepairedTree(DemoPack + "/Trees/Conifer/Conifer.prefab", "Conifer");
        var cypress = RepairedTree(DemoPack + "/Trees/Cypress/Cypress_Forest_Desktop.prefab", "Cypress");
        var protos = new List<TreePrototype>();
        if (conifer != null) protos.Add(new TreePrototype { prefab = conifer });
        if (cypress != null) protos.Add(new TreePrototype { prefab = cypress });
        if (protos.Count == 0) Debug.LogWarning("URP Terrain sample trees missing: the valley has no trees. Run Prototype > Restore Asset Packs.");
        return protos;
    }

    static void PlantTrees(TerrainData data, List<TreePrototype> protos)
    {
        if (protos.Count == 0) return;
        data.treePrototypes = protos.ToArray();

        var rng = new System.Random(7);
        var trees = new List<TreeInstance>();
        treeSpots.Clear();
        for (int i = 0; i < 9000 && trees.Count < 2600; i++)
        {
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            var w = World(u, v);
            if (KeepClear(w, 14f)) continue;
            float h = HeightAt(w);
            float slope = data.GetSteepness(u, v);
            if (h > 150f || slope > 34f) continue;

            // Forest thickens toward the mountain and the edges; open meadow near the yard.
            float d = DistanceToYard(w);
            float chance = Mathf.InverseLerp(14f, 70f, d) * (0.35f + 0.65f * Mathf.InverseLerp(40f, 160f, (w - centre).magnitude));
            chance *= Mathf.InverseLerp(150f, 90f, h);
            if (rng.NextDouble() > chance) continue;

            float scale = 0.8f + 0.5f * (float)rng.NextDouble();
            treeSpots.Add(w);
            trees.Add(new TreeInstance
            {
                position = new Vector3(u, 0f, v),
                prototypeIndex = protos.Count > 1 && rng.NextDouble() < 0.35 ? 1 : 0,
                widthScale = scale,
                heightScale = scale,
                rotation = (float)(rng.NextDouble() * Mathf.PI * 2),
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
        data.SetTreeInstances(trees.ToArray(), true);
    }

    static void PaintDetails(TerrainData data)
    {
        string[] names = { "Grass_A", "Grass_B", "Grass_C", "Fern_A", "Plant_A", "Plant_C" };
        var protos = new List<DetailPrototype>();
        foreach (var n in names)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TerrainPack}/Prefabs/{n}.prefab");
            if (prefab == null) continue;
            protos.Add(new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = 0.8f, maxWidth = 1.3f, minHeight = 0.8f, maxHeight = 1.3f,
                noiseSpread = 0.3f,
            });
        }
        if (protos.Count == 0) return;
        data.detailPrototypes = protos.ToArray();

        var layers = new int[protos.Count][,];
        for (int k = 0; k < protos.Count; k++) layers[k] = new int[DetailRes, DetailRes];
        var rng = new System.Random(11);
        for (int iz = 0; iz < DetailRes; iz++)
        for (int ix = 0; ix < DetailRes; ix++)
        {
            float u = ix / (float)(DetailRes - 1), v = iz / (float)(DetailRes - 1);
            var w = World(u, v);
            if (KeepClear(w, 2.5f)) continue;
            float h = HeightAt(w);
            if (h > 70f || data.GetSteepness(u, v) > 28f) continue;
            float lush = Mathf.Clamp01(0.55f + Fbm(w * 0.05f, 2));
            for (int k = 0; k < protos.Count; k++)
            {
                bool grass = k < 3;
                double p = grass ? 0.55 * lush : 0.04 * (1f - lush);
                if (rng.NextDouble() < p) layers[k][iz, ix] = grass ? 1 + rng.Next(3) : 1;
            }
        }
        for (int k = 0; k < protos.Count; k++) data.SetDetailLayer(0, 0, k, layers[k]);
    }

    static void ScatterRocks(Terrain terrain)
    {
        var rocks = new List<GameObject>();
        foreach (var n in new[] { "Rock_A_01", "Rock_B_01", "Rock_C_01", "Rock_Overgrown_A", "Rock_Overgrown_C" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DemoPack}/Rocks/{n}.prefab");
            if (prefab != null) rocks.Add(prefab);
        }
        if (rocks.Count == 0) return;
        var parent = new GameObject("Rocks").transform;
        var rng = new System.Random(5);
        int placed = 0;
        for (int i = 0; i < 2000 && placed < 60; i++)
        {
            var w = new Vector2(centre.x + (float)(rng.NextDouble() - 0.5) * 420f, centre.y + (float)(rng.NextDouble() - 0.5) * 420f);
            if (KeepClear(w, 10f)) continue;
            float h = HeightAt(w);
            bool mountainFoot = h > 6f && h < 60f;
            bool shore = DistanceToLake(w) < 12f;
            if (!mountainFoot && !shore) continue;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[rng.Next(rocks.Count)], parent);
            rock.transform.position = new Vector3(w.x, terrain.SampleHeight(new Vector3(w.x, 0f, w.y)) + terrain.transform.position.y - 0.3f, w.y);
            rock.transform.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            rock.transform.localScale = Vector3.one * (0.6f + 1.2f * (float)rng.NextDouble());
            placed++;
        }
    }

    // --- lake ----------------------------------------------------------------

    static void BuildLake()
    {
        var ripplesPath = Generated + "/LakeRipples.png";
        File.WriteAllBytes(ripplesPath, ProceduralTextures.RippleNormals(256).EncodeToPNG());
        AssetDatabase.ImportAsset(ripplesPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ripplesPath);
        importer.textureType = TextureImporterType.NormalMap;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();

        var mat = LoadOrCreate(Generated + "/Lake.mat", () => new Material(Shader.Find("Universal Render Pipeline/Lit")));
        mat.SetColor("_BaseColor", new Color(0.12f, 0.32f, 0.42f, 0.78f));
        mat.SetFloat("_Smoothness", 0.96f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ripplesPath));
        mat.SetFloat("_BumpScale", 0.45f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTextureScale("_BaseMap", new Vector2(24f, 16f));
        EditorUtility.SetDirty(mat);

        var lake = GameObject.CreatePrimitive(PrimitiveType.Plane);
        lake.name = "Lake";
        Object.DestroyImmediate(lake.GetComponent<Collider>());
        lake.transform.position = new Vector3(lakeCentre.x, LakeLevel, lakeCentre.y);
        lake.transform.localScale = new Vector3(24f, 1f, 16f);   // a Plane is 10 m square
        var r = lake.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        lake.AddComponent<ScrollingTexture>().speed = new Vector2(0.004f, 0.003f);
    }

    // --- sky -----------------------------------------------------------------

    // The sample project's sky and its ring of distant mountains, plus haze that blends them in.
    static void BuildSky()
    {
        // A procedural sky: its sun follows the scene's directional light. (The sample's HDR
        // cubemap sky is tuned for HDRP's physical exposure and washes out in URP.)
        var sky = LoadOrCreate(Generated + "/Sky.mat", () => new Material(Shader.Find("Skybox/Procedural")));
        sky.shader = Shader.Find("Skybox/Procedural");
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_AtmosphereThickness", 0.9f);
        sky.SetColor("_SkyTint", new Color(0.45f, 0.55f, 0.75f));
        sky.SetColor("_GroundColor", new Color(0.35f, 0.38f, 0.33f));
        sky.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null) RenderSettings.sun = sun;
        var mountains = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Skybox/SkyboxMountains.prefab");
        if (mountains != null)
        {
            var ring = (GameObject)PrefabUtility.InstantiatePrefab(mountains);
            ring.transform.position = new Vector3(centre.x, -Depth, centre.y);
        }
        else Debug.LogWarning("Sample skybox missing: plain sky. Run Prototype > Restore Asset Packs.");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.84f);
        // The mountain ring stands about 9 km out; fog thins it to a hazy silhouette.
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 16000f;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        // Cinemachine's lens overrides the camera's own clip planes, so set both.
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) cam.farClipPlane = 15000f;
        foreach (var vcam in Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None))
        {
            var lens = vcam.Lens;
            lens.FarClipPlane = 15000f;
            vcam.Lens = lens;
        }
    }

    // --- cabin ---------------------------------------------------------------

    // A small log cabin from primitives: log walls, a pitched roof, a door, a lit window and a
    // smoking chimney. Local axes: x is the cabin's width, z its depth, the door is on the -z
    // side; the ridge runs along x.
    static void BuildCabin()
    {
        var root = new GameObject("Cabin").transform;
        root.position = new Vector3(cabin.x, HeightAt(cabin) - 0.1f, cabin.y);   // bottom logs bedded in
        root.rotation = Quaternion.LookRotation(Vector3.left);   // local -z (the door) faces the yard, east

        var log = CabinMaterial("Log", new Color(0.42f, 0.29f, 0.18f), 0.15f);
        var roof = CabinMaterial("Roof", new Color(0.24f, 0.2f, 0.18f), 0.1f);
        var stone = CabinMaterial("Stone", new Color(0.45f, 0.44f, 0.42f), 0.1f);
        var dark = CabinMaterial("Door", new Color(0.2f, 0.13f, 0.08f), 0.2f);
        var glow = CabinMaterial("Window", new Color(1f, 0.75f, 0.4f), 0.3f, new Color(1f, 0.6f, 0.25f) * 2.2f);

        const float w = 6f, l = 4.5f, r = 0.16f, pitch = 34f, overhang = 0.5f;
        const int courses = 9;
        float wallTop = courses * r * 2f;
        float rise = l / 2f * Mathf.Tan(pitch * Mathf.Deg2Rad);

        // Walls: courses of logs, front and back along x, the sides along z half a log higher,
        // with log ends running past the corners as on a real log cabin.
        for (int i = 0; i < courses; i++)
        {
            float y = r + i * r * 2f;
            Log(root, new Vector3(0f, y, l / 2f), w + 0.6f, Axis.X, log);
            Log(root, new Vector3(0f, y, -l / 2f), w + 0.6f, Axis.X, log);
            Log(root, new Vector3(w / 2f, y + r, 0f), l + 0.6f, Axis.Z, log);
            Log(root, new Vector3(-w / 2f, y + r, 0f), l + 0.6f, Axis.Z, log);
        }

        // Gable ends: logs along z, shortening with height so each log's top stays under the roof.
        for (float y = r; y + r * 2f < rise; y += r * 2f)
        {
            float span = l * (1f - (y + r * 2f) / rise);
            Log(root, new Vector3(w / 2f, wallTop + y + r, 0f), span, Axis.Z, log);
            Log(root, new Vector3(-w / 2f, wallTop + y + r, 0f), span, Axis.Z, log);
        }

        // Roof: two panels hanging from the ridge, each sloping down toward its own side.
        var ridge = new Vector3(0f, wallTop + rise + 0.1f, 0f);
        float panel = (l / 2f + overhang) / Mathf.Cos(pitch * Mathf.Deg2Rad);
        for (int side = -1; side <= 1; side += 2)
        {
            var downhill = new Vector3(0f, -Mathf.Sin(pitch * Mathf.Deg2Rad), side * Mathf.Cos(pitch * Mathf.Deg2Rad));
            var slab = Part(root, PrimitiveType.Cube, ridge + downhill * (panel / 2f), new Vector3(w + 1.2f, 0.12f, panel), roof);
            slab.transform.localRotation = Quaternion.LookRotation(downhill);
            slab.name = "Roof";
        }

        Part(root, PrimitiveType.Cube, new Vector3(0.8f, 1.0f, -l / 2f - 0.17f), new Vector3(1f, 2f, 0.08f), dark);           // door
        Part(root, PrimitiveType.Cube, new Vector3(-1.6f, 1.6f, -l / 2f - 0.17f), new Vector3(0.9f, 0.7f, 0.05f), glow);      // window
        var chimney = new Vector3(-w / 2f + 0.8f, wallTop + rise / 2f + 0.9f, l / 4f);
        Part(root, PrimitiveType.Cube, chimney, new Vector3(0.7f, rise + 1.8f, 0.7f), stone);

        var lamp = new GameObject("Window light").AddComponent<Light>();
        lamp.transform.SetParent(root, false);
        lamp.transform.localPosition = new Vector3(-1.6f, 1.6f, -l / 2f - 0.8f);
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, 0.7f, 0.4f);
        lamp.intensity = 2f;
        lamp.range = 5f;

        var smoke = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/SmokeEffect.prefab");
        if (smoke != null)
        {
            var puff = (GameObject)PrefabUtility.InstantiatePrefab(smoke, root);
            puff.transform.localPosition = chimney + Vector3.up * ((rise + 1.8f) / 2f + 0.2f);
            puff.transform.localScale = Vector3.one * 0.35f;
        }

        // One box keeps players out of the cabin.
        var block = root.gameObject.AddComponent<BoxCollider>();
        block.center = new Vector3(0f, (wallTop + rise) / 2f, 0f);
        block.size = new Vector3(w + 0.6f, wallTop + rise, l + 0.6f);
    }

    enum Axis { X, Z }

    // A log lying along the cabin's local x or z axis. (A cylinder's length runs along its own y.)
    static void Log(Transform parent, Vector3 at, float length, Axis axis, Material mat)
    {
        var log = Part(parent, PrimitiveType.Cylinder, at, new Vector3(0.32f, length / 2f, 0.32f), mat);
        log.transform.localRotation = axis == Axis.X ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material CabinMaterial(string name, Color color, float smoothness, Color? emission = null)
    {
        var mat = LoadOrCreate($"{Generated}/Cabin{name}.mat", () => new Material(Shader.Find("Universal Render Pipeline/Lit")));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // --- helpers -------------------------------------------------------------

    static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var made = create();
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    // The sample trees' leaf and billboard materials use a shader graph that fails to import
    // in Unity 6.3 (they render pink). This makes a variant of the tree prefab in Generated/
    // whose broken materials are swapped for copies on URP's own SpeedTree 8 shader, with the
    // same textures. The pack's own files stay untouched.
    static GameObject RepairedTree(string prefabPath, string name)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (source == null) return null;
        var shader = Shader.Find("Universal Render Pipeline/Nature/SpeedTree8_PBRLit");
        Directory.CreateDirectory(Generated + "/Trees");
        AssetDatabase.Refresh();

        var tree = (GameObject)PrefabUtility.InstantiatePrefab(source);
        foreach (var r in tree.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader.isSupported) continue;
                var broken = mats[i];
                mats[i] = LoadOrCreate($"{Generated}/Trees/{broken.name}.mat", () => new Material(shader));
                mats[i].shader = shader;
                foreach (var (prop, texture) in SavedTextures(broken))
                    if (mats[i].HasProperty(prop)) mats[i].SetTexture(prop, texture);
                bool billboard = broken.name.Contains("Billboard");
                if (billboard) mats[i].EnableKeyword("EFFECT_BILLBOARD");
                else mats[i].DisableKeyword("EFFECT_BILLBOARD");
                mats[i].SetFloat("_TwoSided", billboard ? 2f : 0f);
                // URP's SpeedTree shader tints leaves gold by default (hue variation).
                mats[i].SetFloat("_HueVariationKwToggle", 0f);
                mats[i].DisableKeyword("EFFECT_HUE_VARIATION");
                EditorUtility.SetDirty(mats[i]);
            }
            r.sharedMaterials = mats;
        }
        var variant = PrefabUtility.SaveAsPrefabAsset(tree, $"{Generated}/Trees/{name}.prefab");
        Object.DestroyImmediate(tree);
        return variant;
    }

    // Texture slots as saved in a material's file. A material whose shader failed to load
    // reports no properties through its shader, but its saved texture references remain.
    static IEnumerable<(string property, Texture texture)> SavedTextures(Material material)
    {
        var saved = new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
        for (int i = 0; i < saved.arraySize; i++)
        {
            var entry = saved.GetArrayElementAtIndex(i);
            var texture = entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            if (texture != null) yield return (entry.FindPropertyRelative("first").stringValue, texture);
        }
    }

    // A colour-multiplied copy of a texture, saved in Generated/ (which is gitignored: it is
    // derived from Asset Store content, which a public repo can't redistribute).
    static Texture2D Tinted(Texture2D source, Color multiply, int size = 1024)
    {
        string path = $"{Generated}/{source.name}_Tinted.png";
        var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = copy.GetPixels();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(pixels[i].r * multiply.r, pixels[i].g * multiply.g, pixels[i].b * multiply.b, pixels[i].a);
        copy.SetPixels(pixels);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        Object.DestroyImmediate(copy);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // --- chemistry ------------------------------------------------------------

    // The chemistry world around the yard: about 300 m x 260 m, from the lake's far shore to the
    // forest north of the yard. Bigger than the fire can reach (it fades after FireFadeCells).
    const float WestOfYard = 130f, EastOfYard = 130f, NorthOfYard = 88f, SouthOfYard = 150f;
    const int FireFadeCells = 50;
    const int LightningReach = 12000;

    // Builds the cell map around the yard from the same shapes the scenery came from, and
    // hooks up the scene's chemistry tuning and ValleyChemistry.
    static void WireChemistry(ChemGrid grid, string[] yardRows, Terrain terrain)
    {
        float cs = grid.cellSize;
        int left = Mathf.RoundToInt(WestOfYard / cs), right = Mathf.RoundToInt(EastOfYard / cs);
        int top = Mathf.RoundToInt(NorthOfYard / cs), bottom = Mathf.RoundToInt(SouthOfYard / cs);
        int yw = yardRows[0].Length, yh = yardRows.Length;
        int w = left + yw + right, h = top + yh + bottom;

        var treeCells = new HashSet<Vector2Int>();
        foreach (var t in treeSpots) treeCells.Add(CellOf(t, cs, left, top, yh));

        var rows = new System.Text.StringBuilder(w * h + h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int yx = x - left, yy = y - top;
                if (yx >= 0 && yx < yw && yy >= 0 && yy < yh)
                {
                    rows.Append(yardRows[yy][yx]);
                    continue;
                }
                // Cell centre on the ground plane (yard cell (0, 0) is at (0, (yh - 1) * cs)).
                var p = new Vector2(yx * cs, (yh - 1 - yy) * cs);
                rows.Append(CellKind(p, treeCells.Contains(new Vector2Int(x, y))));
            }
            if (y < h - 1) rows.Append('\n');
        }

        grid.map = rows.ToString();
        grid.yardCells = new RectInt(left, top, yw, yh);
        grid.fireFadeCells = FireFadeCells;
        grid.conductLimit = LightningReach;

        var valley = grid.gameObject.AddComponent<ValleyChemistry>();
        valley.grid = grid;
        valley.terrain = terrain;
        valley.cabin = GameObject.Find("Cabin")?.transform;
        valley.lakeLevel = LakeLevel;
        valley.scorchedLayer = 5;   // Soil_Rocks, see PaintLayers
        grid.valley = valley;
    }

    static Vector2Int CellOf(Vector2 p, float cs, int left, int top, int yh) =>
        new Vector2Int(Mathf.RoundToInt(p.x / cs) + left, top + yh - 1 - Mathf.RoundToInt(p.y / cs));

    // Lake water, cabin wall, a tree, bare shore, or meadow grass.
    static char CellKind(Vector2 p, bool tree)
    {
        if (LakeDepth(p) > 0.3f) return 'w';
        // The cabin faces east: its 6 m width runs north-south, its 4.5 m depth east-west.
        var q = p - cabin;
        if (Mathf.Abs(q.y) <= 3.3f && Mathf.Abs(q.x) <= 2.55f) return 'L';
        if (tree) return 'T';
        if (DistanceToLake(p) < 6f && HeightAt(p) < 0.6f) return '.';
        return ',';
    }
}
