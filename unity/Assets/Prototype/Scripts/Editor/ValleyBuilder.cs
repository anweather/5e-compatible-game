using System.Collections.Generic;
using System.IO;
using Chem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Sets the Chemistry Yard in a mountain valley: chunk terrains (ValleyTerrain)
// shaped by ValleyShape, the old yard just meadow in a flat green valley floor
// with its pond, pits and oil slick in the ground, a ridged mountain to the
// north, a lake to the south, hills closing the sides, trees, grass and rocks
// from the terrain packs, the sample project's mountain skybox, haze, and a
// small log cabin. Everything is generated from code, so rebuilding gives the
// same valley. The chemistry map covers the yard and about 300 m × 260 m of
// valley around it; every cell is on the terrain.
public static class ValleyBuilder
{
    const string Generated = "Assets/Prototype/Generated";
    const string TerrainPack = PackPaths.TerrainSamples;
    const string DemoPack = PackPaths.TerrainDemo;
    const float LakeLevel = ValleyShape.LakeLevel;

    static Vector2 Centre => ValleyShape.Centre;
    static Vector2 LakeCentre => ValleyShape.LakeCentre;
    static Vector2 CabinSpot => ValleyShape.Cabin;

    public static void Dress(ChemGrid grid, string[] yardRows)
    {
        ValleyShape.Plan(yardRows[0].Length, yardRows.Length, grid.cellSize);
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();

        var terrains = BuildTerrain(grid, yardRows);
        var ripples = BuildLake();
        // The cells' water (the pond, trenches, puddles) and their oil and grease: films whose
        // meshes fade their own edges (vertex alpha). The water has the lake's colour and
        // ripples, gentler, so a small pool doesn't glint in rings.
        grid.waterMaterial = SoftMaterial("CellWater", new Color(0.12f, 0.32f, 0.42f, 0.8f), 0.93f, ripples, 0.12f);
        grid.slickMaterial = SoftMaterial("Slick", Color.white, 0.92f, null, 0f);
        BuildSky();
        var cabinRoot = BuildCabin();
        ScatterRocks();
        BuildWorldEdge();
        BuildCourt();
        var valley = grid.gameObject.AddComponent<ValleyChemistry>();
        valley.grid = grid;
        valley.chunks = terrains;
        valley.cabin = cabinRoot;
        valley.lakeLevel = LakeLevel;
        grid.valley = valley;
    }

    // --- terrain -------------------------------------------------------------

    // Order matters. Every asset the terrain uses (greener grass layers, repaired trees) is
    // created first; then the chunk assets, and only then are they filled in (ValleyTerrain).
    static Terrain[] BuildTerrain(ChemGrid grid, string[] yardRows)
    {
        var assets = new ValleyTerrain.Assets { layers = LoadLayers(), trees = LoadTrees(), details = LoadDetails() };
        ValleyTerrain.CreateChunks(assets);

        var valleyTrees = TreeSpots(assets.trees.Count > 0);
        WireChemistry(grid, yardRows, valleyTrees);
        var world = ChemGrid.MapWorld(grid.LoadBaseRules(), grid.map, grid.seed, out _);
        ValleyTerrain.BakeCells(world, grid.stepHeight);

        var yardCells = YardCells(grid.yardCells);
        ValleyTerrain.PlantTrees(assets.trees, AllTrees(valleyTrees, grid, world));
        ValleyTerrain.Paint(assets.layers, world, yardCells);
        ValleyTerrain.PaintDetails(assets.details, world, yardCells);
        return ValleyTerrain.PlaceTerrains(grid.yardCells.position);
    }

    static List<(int x, int y)> YardCells(RectInt yard)
    {
        var cells = new List<(int x, int y)>();
        for (int y = yard.yMin; y < yard.yMax; y++)
            for (int x = yard.xMin; x < yard.xMax; x++) cells.Add((x, y));
        return cells;
    }

    // Grass on flat ground, moss in the meadows, rock on slopes, snow up high, sand at the lake.
    // ValleyShape.LayerWeights paints these; the cells also paint charred and bare earth
    // (CellPaint.Charred, CellPaint.Earth: copies of pack layers, after these).
    static readonly string[] LayerNames = { "Grass_A", "Grass_Moss", "Rock", "Snow", "Sand", "Soil_Rocks" };

    // The terrain layers in LayerNames order, with greener copies of the grass, then the cells'
    // own; null if the pack is missing.
    static TerrainLayer[] LoadLayers()
    {
        var layers = new List<TerrainLayer>();
        foreach (var n in LayerNames)
        {
            var layer = PackLayer(n);
            if (layer != null) layers.Add(n.StartsWith("Grass", System.StringComparison.Ordinal) ? Greener(layer) : layer);
        }
        var pebbles = PackLayer("Pebbles_A");
        var mud = PackLayer("Muddy");
        if (layers.Count == LayerNames.Length && pebbles != null && mud != null)
        {
            // Burnt ground: the pack's dark pebbles, charred nearly black, tiled wide so no repeat shows.
            layers.Add(CopyLayer(pebbles, $"{Generated}/{CellPaint.Charred}_TerrainLayer.terrainlayer", new Color(0.42f, 0.4f, 0.38f), 2.5f));
            // Worn bare earth: the pack's mud, a touch lighter.
            layers.Add(CopyLayer(mud, $"{Generated}/{CellPaint.Earth}_TerrainLayer.terrainlayer", new Color(1.12f, 1.05f, 0.98f), 3f));
            return layers.ToArray();
        }
        Debug.LogWarning("Terrain Sample Asset Pack missing: the valley will be untextured. Run Prototype > Restore Asset Packs.");
        return null;
    }

    static TerrainLayer PackLayer(string name) =>
        AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainPack}/TerrainLayers/{name}_TerrainLayer.terrainlayer");

    // The pack's grass textures are olive; a copy of the layer with a greener copy of its
    // texture gives a lush valley without editing the pack.
    static TerrainLayer Greener(TerrainLayer source) =>
        CopyLayer(source, $"{Generated}/{source.name}_Green.terrainlayer", new Color(0.78f, 1.12f, 0.6f), source.tileSize.x);

    // A copy of a pack layer at a path, its texture colour-multiplied (URP's terrain shader ignores
    // the layer's colour remap, so the colour has to be in the texture) and tiled every tileSize metres.
    static TerrainLayer CopyLayer(TerrainLayer source, string path, Color tint, float tileSize)
    {
        var layer = EditorAssets.LoadOrCreate(path, () => new TerrainLayer());
        layer.diffuseTexture = Tinted(source.diffuseTexture, tint);
        layer.normalMapTexture = source.normalMapTexture;
        layer.maskMapTexture = source.maskMapTexture;
        layer.tileSize = new Vector2(tileSize, tileSize);
        layer.normalScale = source.normalScale;
        layer.smoothness = source.smoothness;
        layer.metallic = source.metallic;
        layer.diffuseRemapMin = source.diffuseRemapMin;
        layer.diffuseRemapMax = source.diffuseRemapMax;
        EditorUtility.SetDirty(layer);
        return layer;
    }

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

    // The valley's forest: world points with a scale, a species and a turn each. Open meadow near
    // the yard, thickening toward the mountain and the edges.
    static List<(Vector2 at, float scale, int species, float turn)> TreeSpots(bool any)
    {
        var spots = new List<(Vector2, float, int, float)>();
        if (!any) return spots;
        var layout = ValleyTerrain.Layout;
        float size = layout.ChunkSize * layout.ChunksX;
        var rng = new System.Random(7);
        for (int i = 0; i < 9000 && spots.Count < 2600; i++)
        {
            var w = new Vector2(layout.OriginX + (float)rng.NextDouble() * size, layout.OriginZ + (float)rng.NextDouble() * size);
            if (!TreeFits(w)) continue;
            float d = ValleyShape.DistanceToYard(w), h = ValleyShape.HeightAt(w);
            float chance = Mathf.InverseLerp(14f, 70f, d) * (0.35f + 0.65f * Mathf.InverseLerp(40f, 160f, (w - Centre).magnitude));
            chance *= Mathf.InverseLerp(150f, 90f, h);
            if (rng.NextDouble() > chance) continue;
            float scale = 0.8f + 0.5f * (float)rng.NextDouble();
            spots.Add((w, scale, rng.NextDouble() < 0.35 ? 1 : 0, (float)(rng.NextDouble() * Mathf.PI * 2)));
        }
        return spots;
    }

    static bool TreeFits(Vector2 w) =>
        !ValleyShape.KeepClear(w, 14f) && ValleyShape.HeightAt(w) <= 150f && ValleyTerrain.Steepness(w) <= 34f;

    // The valley's trees and one smaller conifer on each of the yard map's tree cells.
    static IEnumerable<(Vector2 at, float scale, int species, float turn)> AllTrees(
        List<(Vector2 at, float scale, int species, float turn)> valley, ChemGrid grid, Chem.World world)
    {
        foreach (var t in valley) yield return t;
        var yard = grid.yardCells;
        for (int y = yard.yMin; y < yard.yMax; y++)
        {
            for (int x = yard.xMin; x < yard.xMax; x++)
            {
                if (world.At(x, y).T != TerrainIds.Tree) continue;
                var c = grid.CellCenter(x, y);
                yield return (new Vector2(c.x, c.z), 0.4f + 0.04f * ((x * 7 + y * 3) % 5), 0, x * 1.7f + y);
            }
        }
    }

    // Grass (the first three) and ferns and plants (the rest), from the terrain pack.
    static readonly string[] DetailNames = { "Grass_A", "Grass_B", "Grass_C", "Fern_A", "Plant_A", "Plant_C" };

    static List<DetailPrototype> LoadDetails()
    {
        var protos = new List<DetailPrototype>();
        foreach (var n in DetailNames)
        {
            var prefab = EditorAssets.LoadPrefab($"{TerrainPack}/Prefabs/{n}.prefab");
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
        return protos;
    }

    static void ScatterRocks()
    {
        var rocks = new List<GameObject>();
        foreach (var n in new[] { "Rock_A_01", "Rock_B_01", "Rock_C_01", "Rock_Overgrown_A", "Rock_Overgrown_C" })
        {
            var prefab = EditorAssets.LoadPrefab($"{DemoPack}/Rocks/{n}.prefab");
            if (prefab != null) rocks.Add(prefab);
        }
        if (rocks.Count == 0) return;
        var parent = new GameObject("Rocks").transform;
        var rng = new System.Random(5);
        int placed = 0;
        for (int i = 0; i < 2000 && placed < 60; i++)
        {
            var w = new Vector2(Centre.x + (float)(rng.NextDouble() - 0.5) * 420f, Centre.y + (float)(rng.NextDouble() - 0.5) * 420f);
            if (ValleyShape.KeepClear(w, 10f)) continue;
            float h = ValleyShape.HeightAt(w);
            bool mountainFoot = h > 6f && h < 60f;
            bool shore = ValleyShape.DistanceToLake(w) < 12f;
            if (!mountainFoot && !shore) continue;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[rng.Next(rocks.Count)], parent);
            var at = new Vector3(w.x, 0f, w.y);
            at.y = ValleyTerrain.GroundAt(at) - 0.3f;
            rock.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, rng.Next(360), 0f));
            rock.transform.localScale = Vector3.one * (0.6f + 1.2f * (float)rng.NextDouble());
            placed++;
        }
    }

    // --- the ball court --------------------------------------------------------

    // A walled court on the flat meadow east of the yard, with a goal in each end wall and the
    // ball in the middle (world/things.json). Wooden rails a ball bounces off, low enough to step
    // over a corner of; the goal mouths are open.
    static readonly Vector2 CourtCentre = new Vector2(61f, 5f);
    const float CourtLength = 26f, CourtWidth = 18f, RailHeight = 0.9f, GoalWidth = 4f, GoalHeight = 1.8f;

    static void BuildCourt()
    {
        var parent = new GameObject("Ball court").transform;
        var wood = EditorAssets.LitMaterial(Generated + "/CourtWood.mat", new Color(0.55f, 0.4f, 0.24f), 0.2f);
        var paint = EditorAssets.LitMaterial(Generated + "/CourtGoal.mat", new Color(0.92f, 0.92f, 0.9f), 0.4f);
        float x0 = CourtCentre.x - CourtLength / 2f, x1 = CourtCentre.x + CourtLength / 2f;
        float z0 = CourtCentre.y - CourtWidth / 2f, z1 = CourtCentre.y + CourtWidth / 2f;
        // The long sides, whole; the ends, either side of a goal mouth.
        Rail(parent, wood, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0));
        Rail(parent, wood, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1));
        foreach (float x in new[] { x0, x1 })
        {
            Rail(parent, wood, new Vector3(x, 0f, z0), new Vector3(x, 0f, CourtCentre.y - GoalWidth / 2f));
            Rail(parent, wood, new Vector3(x, 0f, CourtCentre.y + GoalWidth / 2f), new Vector3(x, 0f, z1));
            Goal(parent, paint, new Vector3(x, 0f, CourtCentre.y), x < CourtCentre.x ? -1f : 1f);
        }
    }

    // A solid rail between two points on the ground, on posts.
    static void Rail(Transform parent, Material mat, Vector3 a, Vector3 b)
    {
        a.y = ValleyTerrain.GroundAt(a);
        b.y = ValleyTerrain.GroundAt(b);
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "Court rail";
        rail.transform.SetParent(parent, false);
        rail.transform.SetPositionAndRotation((a + b) / 2f + Vector3.up * (RailHeight / 2f), Quaternion.LookRotation(b - a));
        rail.transform.localScale = new Vector3(0.15f, RailHeight, Vector3.Distance(a, b) + 0.15f);
        rail.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // Two posts and a crossbar across a goal mouth, a bar along the back of the net to stop the ball.
    static void Goal(Transform parent, Material mat, Vector3 mouth, float outward)
    {
        mouth.y = ValleyTerrain.GroundAt(mouth);
        foreach (float side in new[] { -1f, 1f })
            Bar(parent, mat, mouth + new Vector3(0f, GoalHeight / 2f, side * GoalWidth / 2f), new Vector3(0.12f, GoalHeight, 0.12f));
        Bar(parent, mat, mouth + new Vector3(0f, GoalHeight, 0f), new Vector3(0.12f, 0.12f, GoalWidth + 0.12f));
        Bar(parent, mat, mouth + new Vector3(outward * 1.2f, GoalHeight / 2f, 0f), new Vector3(0.1f, GoalHeight, GoalWidth));
    }

    static void Bar(Transform parent, Material mat, Vector3 at, Vector3 size)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Goal";
        bar.transform.SetParent(parent, false);
        bar.transform.position = at;
        bar.transform.localScale = size;
        bar.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // --- the world's edge ------------------------------------------------------

    const float EdgeWallHeight = 400f, EdgeRockSpacing = 6f, EdgeInset = 3f;

    // The end of the land: the valley's chunk terrains stop, and past them is nothing to stand on.
    // Walls no ray sees (Ignore Raycast, so aiming and the camera pass through) stand just inside
    // the last terrain's edge, with a line of standing rocks along them so the edge shows (none
    // out in the lake).
    static void BuildWorldEdge()
    {
        var layout = ValleyTerrain.Layout;
        var (x0, z0) = layout.ChunkCorner(0, 0);
        var (x1, z1) = layout.ChunkCorner(layout.ChunksX, layout.ChunksZ);
        var a = new Vector3(x0 + EdgeInset, 0f, z0 + EdgeInset);   // south-west corner
        var b = new Vector3(x1 - EdgeInset, 0f, z1 - EdgeInset);   // north-east corner
        var parent = new GameObject("World edge").transform;
        Wall(parent, "North", new Vector3((a.x + b.x) / 2f, 0f, b.z + 1f), new Vector3(b.x - a.x + 4f, EdgeWallHeight, 2f));
        Wall(parent, "South", new Vector3((a.x + b.x) / 2f, 0f, a.z - 1f), new Vector3(b.x - a.x + 4f, EdgeWallHeight, 2f));
        Wall(parent, "East", new Vector3(b.x + 1f, 0f, (a.z + b.z) / 2f), new Vector3(2f, EdgeWallHeight, b.z - a.z + 4f));
        Wall(parent, "West", new Vector3(a.x - 1f, 0f, (a.z + b.z) / 2f), new Vector3(2f, EdgeWallHeight, b.z - a.z + 4f));
        EdgeRocks(parent, a, b);
    }

    static void Wall(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name + " wall") { layer = 2 };   // Ignore Raycast
        go.transform.SetParent(parent, false);
        // From well under the lake bed to well over the hills.
        go.transform.position = centre + Vector3.up * (EdgeWallHeight / 2f - 60f);
        go.AddComponent<BoxCollider>().size = size;
    }

    static void EdgeRocks(Transform parent, Vector3 a, Vector3 b)
    {
        var rocks = new List<GameObject>();
        foreach (var n in new[] { "Rock_A_01", "Rock_B_01", "Rock_C_01" })
        {
            var prefab = EditorAssets.LoadPrefab($"{DemoPack}/Rocks/{n}.prefab");
            if (prefab != null) rocks.Add(prefab);
        }
        if (rocks.Count == 0) return;
        var rng = new System.Random(11);
        var corners = new[] { new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, a.z), new Vector3(b.x, 0f, b.z), new Vector3(a.x, 0f, b.z) };
        for (int side = 0; side < 4; side++)
        {
            var from = corners[side];
            var to = corners[(side + 1) % 4];
            int count = Mathf.CeilToInt(Vector3.Distance(from, to) / EdgeRockSpacing);
            for (int i = 0; i < count; i++)
            {
                var at = Vector3.Lerp(from, to, (i + (float)rng.NextDouble() * 0.5f) / count);
                at.y = ValleyTerrain.GroundAt(at);
                if (at.y < LakeLevel) continue;
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[rng.Next(rocks.Count)], parent);
                rock.transform.SetPositionAndRotation(at - Vector3.up * 0.3f, Quaternion.Euler(0f, rng.Next(360), 0f));
                rock.transform.localScale = Vector3.one * (0.9f + 0.8f * (float)rng.NextDouble());
            }
        }
    }

    // --- lake ----------------------------------------------------------------

    // Builds the lake; returns its ripples (a normal map).
    static Texture2D BuildLake()
    {
        var ripplesPath = Generated + "/LakeRipples.png";
        File.WriteAllBytes(ripplesPath, ProceduralTextures.RippleNormals(512).EncodeToPNG());
        AssetDatabase.ImportAsset(ripplesPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ripplesPath);
        importer.textureType = TextureImporterType.NormalMap;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();

        var mat = EditorAssets.LitMaterial(Generated + "/Lake.mat", new Color(0.12f, 0.32f, 0.42f, 0.78f), 0.96f);
        UrpMaterials.MakeTransparent(mat);
        UrpMaterials.SetNormalMap(mat, AssetDatabase.LoadAssetAtPath<Texture2D>(ripplesPath), 0.45f);
        mat.SetTextureScale("_BaseMap", new Vector2(12f, 8f));
        EditorUtility.SetDirty(mat);

        var lake = Primitives.Visual(PrimitiveType.Plane, "Lake");
        lake.transform.position = new Vector3(LakeCentre.x, LakeLevel, LakeCentre.y);
        lake.transform.localScale = new Vector3(24f, 1f, 16f);   // a Plane is 10 m square
        var r = lake.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        lake.AddComponent<ScrollingTexture>().speed = new Vector2(0.004f, 0.003f);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ripplesPath);
    }

    // A see-through material tinted by its mesh's vertex colours (UrpMaterials.ConfigureSoftSurface).
    static Material SoftMaterial(string name, Color color, float smoothness, Texture2D normals, float bump)
    {
        var mat = EditorAssets.LoadOrCreate($"{Generated}/{name}.mat", () => new Material(Shader.Find(UrpMaterials.LitShader)));
        UrpMaterials.ConfigureSoftSurface(mat, color, smoothness);
        if (normals != null) UrpMaterials.SetNormalMap(mat, normals, bump);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // --- sky -----------------------------------------------------------------

    // The sample project's sky and its ring of distant mountains, plus haze that blends them in.
    static void BuildSky()
    {
        // A procedural sky: its sun follows the scene's directional light. (The sample's HDR
        // cubemap sky is tuned for HDRP's physical exposure and washes out in URP.)
        var sky = EditorAssets.LoadOrCreate(Generated + "/Sky.mat", () => new Material(Shader.Find("Skybox/Procedural")));
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
        var mountains = EditorAssets.LoadPrefab(DemoPack + "/Skybox/SkyboxMountains.prefab");
        if (mountains != null)
        {
            var ring = (GameObject)PrefabUtility.InstantiatePrefab(mountains);
            ring.transform.position = new Vector3(Centre.x, -22f, Centre.y);
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
    static Transform BuildCabin()
    {
        var root = new GameObject("Cabin").transform;
        // Bottom logs bedded in; local -z (the door) faces the yard, east.
        root.SetPositionAndRotation(new Vector3(CabinSpot.x, ValleyShape.HeightAt(CabinSpot) - 0.1f, CabinSpot.y), Quaternion.LookRotation(Vector3.left));

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

        var smoke = EditorAssets.LoadPrefab(PackPaths.SmokeFx + "/SmokeEffect.prefab");
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
        return root;
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
        var go = Primitives.Visual(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material CabinMaterial(string name, Color color, float smoothness, Color? emission = null) =>
        EditorAssets.LitMaterial($"{Generated}/Cabin{name}.mat", color, smoothness, emission);

    // --- helpers -------------------------------------------------------------

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
                if (mats[i] != null && !mats[i].shader.isSupported) mats[i] = RepairedTreeMaterial(mats[i], shader);
            }
            r.sharedMaterials = mats;
        }
        var variant = PrefabUtility.SaveAsPrefabAsset(tree, $"{Generated}/Trees/{name}.prefab");
        Object.DestroyImmediate(tree);
        return variant;
    }

    // A copy of a broken tree material on the given SpeedTree shader, with the same textures.
    static Material RepairedTreeMaterial(Material broken, Shader shader)
    {
        var mat = EditorAssets.LoadOrCreate($"{Generated}/Trees/{broken.name}.mat", () => new Material(shader));
        mat.shader = shader;
        foreach (var (prop, texture) in SavedTextures(broken))
        {
            if (mat.HasProperty(prop)) mat.SetTexture(prop, texture);
        }
        bool billboard = broken.name.Contains("Billboard");
        if (billboard) mat.EnableKeyword("EFFECT_BILLBOARD");
        else mat.DisableKeyword("EFFECT_BILLBOARD");
        mat.SetFloat("_TwoSided", billboard ? 2f : 0f);
        // URP's SpeedTree shader tints leaves gold by default (hue variation).
        mat.SetFloat("_HueVariationKwToggle", 0f);
        mat.DisableKeyword("EFFECT_HUE_VARIATION");
        EditorUtility.SetDirty(mat);
        return mat;
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
        var pixels = copy.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            pixels[i] = new Color(c.r * multiply.r, c.g * multiply.g, c.b * multiply.b, c.a);
        }
        copy.SetPixels32(pixels);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        Object.DestroyImmediate(copy);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // --- chemistry ------------------------------------------------------------

    // The chemistry world: the whole valley, every chunk terrain's cells (TerrainGrid.Valley()), so
    // spells work wherever the players go; only the chunks around them are simulated
    // (ChunkStreaming). The yard's cell (0, 0) stands at world (0, (yard rows - 1) * cell size).
    const int FireFadeCells = 50;
    const int LightningReach = 12000;

    // Builds the cell map around the yard from the same shapes the scenery comes from, and the
    // scene's chemistry tuning.
    static void WireChemistry(ChemGrid grid, string[] yardRows, List<(Vector2 at, float scale, int species, float turn)> trees)
    {
        float cs = grid.cellSize;
        int yw = yardRows[0].Length, yh = yardRows.Length;
        var layout = ValleyTerrain.Layout;
        int w = layout.GridW, h = layout.GridH;
        var (left, top) = layout.CellAt(0f, (yh - 1) * cs);

        var treeCells = new HashSet<Vector2Int>();
        foreach (var t in trees) treeCells.Add(CellOf(t.at, cs, left, top, yh));

        var rows = new System.Text.StringBuilder(w * h + h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int yx = x - left, yy = y - top;
                bool inYard = yx >= 0 && yx < yw && yy >= 0 && yy < yh;
                // Cell centre on the ground plane (yard cell (0, 0) is at (0, (yh - 1) * cs)).
                var p = new Vector2(yx * cs, (yh - 1 - yy) * cs);
                rows.Append(inYard ? yardRows[yy][yx] : CellKind(p, treeCells.Contains(new Vector2Int(x, y))));
            }
            if (y < h - 1) rows.Append('\n');
        }

        grid.map = rows.ToString();
        grid.yardCells = new RectInt(left, top, yw, yh);
        grid.fireFadeCells = FireFadeCells;
        grid.conductLimit = LightningReach;
    }

    static Vector2Int CellOf(Vector2 p, float cs, int left, int top, int yh) =>
        new Vector2Int(Mathf.RoundToInt(p.x / cs) + left, top + yh - 1 - Mathf.RoundToInt(p.y / cs));

    // Lake water (where the ground is under the lake's surface), cabin wall, a tree, bare shore,
    // or meadow grass.
    static char CellKind(Vector2 p, bool tree)
    {
        float ground = ValleyShape.HeightAt(p);
        if (ground < LakeLevel) return 'w';
        // The cabin faces east: its 6 m width runs north-south, its 4.5 m depth east-west.
        var q = p - CabinSpot;
        if (Mathf.Abs(q.y) <= 3.3f && Mathf.Abs(q.x) <= 2.55f) return 'L';
        if (tree) return 'T';
        if (ValleyShape.DistanceToLake(p) < 6f && ground < 0.6f) return '.';
        return ',';
    }
}
