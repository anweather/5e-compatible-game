using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Sets the Chemistry Yard in a mountain valley: the ground's template and material (the chunk terrains themselves are made
// from the map's ground/ as the scene starts, MapTerrains), the old yard just meadow in a flat green valley floor
// with its pond, pits and oil slick in the ground, a ridged mountain to the
// north, a lake to the south, hills closing the sides, trees, grass and rocks
// from the terrain packs, the sample project's mountain skybox, haze, and a
// few small log cabins, and the villages of world/villages.json (VillageHouse).
// Everything is generated from code, so rebuilding gives the
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

    public static void Dress(ChemGrid grid, string[] yardRows)
    {
        playedGround = null;
        ValleyShape.Plan(yardRows[0].Length, yardRows.Length, grid.cellSize);
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();

        var terrains = BuildGround(grid, yardRows);
        try
        {
            DressGround(grid, terrains);
            MarkPlaces();
        }
        finally
        {
            EditorGround.Close();
        }
    }

    static readonly (string root, string place)[] Places =
    {
        ("Lake", "lake"), ("Rocks", "rocks"), ("River rocks", "river-rocks"), ("Ball court", "court"), ("Landmarks", "landmarks"),
        ("Falls", "falls"), ("Bridge", "bridge"), ("Tower", "tower"), ("Defense field", "defense-field"), ("Coast", "coast"),
        ("SkyboxMountains", "skyline"), ("Cabin*", "cabins"), ("Village *", "villages"), ("World edge", "world-edge"),
    };

    static void MarkPlaces()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var (name, place) in Places)
            foreach (var root in roots.Where(r => Named(r.name, name)))
                root.AddComponent<PlaceRoot>().place = place;
    }

    static bool Named(string root, string name) =>
        name.EndsWith("*") ? root.StartsWith(name.TrimEnd('*'), System.StringComparison.Ordinal) : root == name;

    static void DressGround(ChemGrid grid, MapTerrains terrains)
    {
        BuildLake();
        var cellWater = EditorAssets.LoadOrCreate($"{Generated}/CellWaterFlow.mat", () => new Material(Shader.Find("Prototype/CellWater")));
        cellWater.shader = Shader.Find("Prototype/CellWater");
        cellWater.SetColor("_Color", new Color(0.12f, 0.32f, 0.42f, 0.8f));
        EditorUtility.SetDirty(cellWater);
        grid.waterMaterial = cellWater;
        grid.lavaShader = Shader.Find("Prototype/CellLava");
        grid.aimShader = Shader.Find("Prototype/AimMarker");
        grid.farShader = Shader.Find("Prototype/FarLand");
        grid.slickMaterial = SoftMaterial("Slick", Color.white, UrpMaterials.FilmSmoothness, null, 0f);
        grid.wallRocks = LoadRocks("Rock_A_01", "Rock_A_02", "Rock_C_02");
        grid.wallStones = LoadRocks("Rock_B_01", "Rock_C_01");
        BuildSky();
        var cabins = BuildCabins();
        var houses = BuildVillages();
        CoastBuilder.Build(VillageMaterial());
        ScatterRocks();
        BuildWorldEdge();
        BuildCourt();
        ValleyBridge.Build(LogMaterial(), CabinMaterial("Plank", new Color(0.52f, 0.37f, 0.23f), 0.12f));
        RiverRocks();
        ValleyTower.Build(grid);
        DefenseFieldBuilder.Build(grid);
        BuildLandmarks();
        BuildWaterfall();
        var valley = grid.gameObject.AddComponent<ValleyChemistry>();
        valley.grid = grid;
        valley.terrains = terrains;
        valley.cabins = cabins.Concat(houses).ToArray();
        valley.lakeLevel = (float)MapFile.Load(ChemGrid.Folders.World).WaterMetres;
        grid.valley = valley;

        var currents = grid.gameObject.AddComponent<WaterCurrents>();
        currents.grid = grid;
        currents.river = ValleyShape.RiverLine;
        currents.riverHalfWidth = ValleyShape.RiverHalfWidth;
    }

    static MapTerrains BuildGround(ChemGrid grid, string[] yardRows)
    {
        AssetDatabase.DeleteAsset(OldBake);
        EditorAssets.EnsureFolderFor(TemplatePath);
        var details = LoadDetails(out var packTufts);
        var template = GroundTemplate(LoadLayers(), LoadTrees(), details);
        var material = RockMaterial();
        var (left, top) = EditorGround.Layout.CellAt(0f, (yardRows.Length - 1) * grid.cellSize);
        cellAt = at => CellOf(at, grid.cellSize, left, top, yardRows.Length);
        WireChemistry(grid, yardRows);
        var terrains = new GameObject("Ground").AddComponent<MapTerrains>();
        terrains.template = template;
        terrains.material = material;
        terrains.packTufts = packTufts;
        EditorGround.Open(template, material);
        return terrains;
    }

    const string OldBake = Generated + "/Valley", TemplatePath = Generated + "/Ground/Template.asset";
    const int DetailRes = TerrainGrid.ChunkCells;

    static TerrainData GroundTemplate(TerrainLayer[] layers, List<TreePrototype> trees, List<DetailPrototype> details)
    {
        var template = EditorAssets.LoadOrCreate(TemplatePath, () => new TerrainData { heightmapResolution = 33 });
        template.SetDetailResolution(DetailRes, 8);
        template.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        if (layers != null) template.terrainLayers = layers;
        template.treePrototypes = trees.ToArray();
        template.detailPrototypes = details.ToArray();
        EditorUtility.SetDirty(template);
        AssetDatabase.SaveAssets();
        return template;
    }

    static Material RockMaterial()
    {
        var material = EditorAssets.LoadOrCreate($"{Generated}/Ground/TerrainRock.mat", () =>
        {
            var made = new Material(Shader.Find("Prototype/TerrainRock"));
            made.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            made.EnableKeyword("_NORMALMAP");
            return made;
        });
        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }
        return material;
    }

    static readonly string[] LayerNames = { "Grass_A", "Grass_Moss", "Rock", "Snow", "Sand", "Soil_Rocks" };

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
            layers.Add(CopyLayer(pebbles, $"{Generated}/{CellPaint.Charred}_TerrainLayer.terrainlayer", new Color(0.42f, 0.4f, 0.38f), 2.5f));
            layers.Add(CopyLayer(mud, $"{Generated}/{CellPaint.Earth}_TerrainLayer.terrainlayer", new Color(1.12f, 1.05f, 0.98f), 3f));
            return layers.ToArray();
        }
        Debug.LogWarning("Terrain Sample Asset Pack missing: the valley will be untextured. Run Prototype > Restore Asset Packs.");
        return null;
    }

    static TerrainLayer PackLayer(string name) =>
        AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainPack}/TerrainLayers/{name}_TerrainLayer.terrainlayer");

    static TerrainLayer Greener(TerrainLayer source) =>
        CopyLayer(source, $"{Generated}/{source.name}_Green.terrainlayer", new Color(0.78f, 1.12f, 0.6f), source.tileSize.x);

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

    internal static bool OnPlayedGround(Vector2 at, int margin)
    {
        var cell = cellAt(at);
        return PlayedGround().Any(r => cell.x >= r.xMin - margin && cell.x < r.xMax + margin && cell.y >= r.yMin - margin && cell.y < r.yMax + margin);
    }

    static List<RectInt> PlayedGround()
    {
        if (playedGround != null) return playedGround;
        string dir = ChemGrid.Folders.World;
        playedGround = WorldPuzzles.Load(dir).All.Select(p => new RectInt(p.X, p.Y, p.Width, p.Height)).ToList();
        if (GolfCommand.Load(dir) is { } h)
            playedGround.Add(new RectInt(Mathf.Min(h.TeeX, h.HoleX) - FairwayHalfWide, Mathf.Min(h.TeeY, h.HoleY) - FairwayHalfWide,
                Mathf.Abs(h.HoleX - h.TeeX) + 1 + FairwayHalfWide * 2, Mathf.Abs(h.HoleY - h.TeeY) + 1 + FairwayHalfWide * 2));
        return playedGround;
    }

    static readonly string[] DetailNames = { "Grass_A", "Grass_B", "Grass_C", "Fern_A", "Plant_A", "Plant_C" };

    static List<DetailPrototype> LoadDetails(out Mesh[] packTufts)
    {
        var protos = new List<DetailPrototype>();
        var packs = new List<Mesh>();
        foreach (var n in DetailNames)
        {
            var pack = EditorAssets.LoadPrefab($"{TerrainPack}/Prefabs/{n}.prefab");
            if (pack == null) continue;
            var prefab = LightTufts.For(pack);
            if (LightTufts.Lightened.ContainsKey(n)) packs.Add(pack.GetComponent<MeshFilter>().sharedMesh);
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
        packTufts = packs.ToArray();
        return protos;
    }

    static GameObject[] LoadRocks(params string[] names)
    {
        var rocks = new List<GameObject>();
        foreach (var n in names)
        {
            var prefab = EditorAssets.LoadPrefab($"{DemoPack}/Rocks/{n}.prefab");
            if (prefab != null) rocks.Add(prefab);
        }
        return rocks.ToArray();
    }

    const int FairwayHalfWide = 4;
    static List<RectInt> playedGround;
    static System.Func<Vector2, Vector2Int> cellAt;

    internal static void PlanCells(float cellSize, int left, int top, int yardHeight)
    {
        playedGround = null;
        cellAt = at => CellOf(at, cellSize, left, top, yardHeight);
    }

    internal static Vector2Int CellAt(Vector2 at) => cellAt(at);

    static bool RockKeepsOff(Vector2 w) => ValleyShape.KeepClear(w, 10f) || OnPlayedGround(w, 2);

    static void ScatterRocks()
    {
        var rocks = new List<GameObject>(LoadRocks("Rock_A_01", "Rock_B_01", "Rock_C_01", "Rock_Overgrown_A", "Rock_Overgrown_C"));
        if (rocks.Count == 0) return;
        var parent = new GameObject("Rocks").transform;
        var rng = new System.Random(5);
        int placed = 0;
        for (int i = 0; i < 2000 && placed < 60; i++)
        {
            var w = new Vector2(Centre.x + (float)(rng.NextDouble() - 0.5) * 420f, Centre.y + (float)(rng.NextDouble() - 0.5) * 420f);
            if (RockKeepsOff(w)) continue;
            float h = ValleyShape.HeightAt(w);
            bool mountainFoot = h > 6f && h < 60f;
            bool shore = ValleyShape.DistanceToLake(w) < 12f;
            if (!mountainFoot && !shore) continue;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[rng.Next(rocks.Count)], parent);
            Solid(rock);
            var at = new Vector3(w.x, 0f, w.y);
            at.y = EditorGround.GroundAt(at) - 0.3f;
            rock.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, rng.Next(360), 0f));
            rock.transform.localScale = Vector3.one * (0.6f + 1.2f * (float)rng.NextDouble());
            if (ValleyShape.DistanceToVillage(w) < 4f || ValleyCoast.DistanceToPath(w) < 3f || ValleyShape.NearTower(w, 3f)) Object.DestroyImmediate(rock);
            if (rock != null && DefenseFieldBuilder.NearDefenseField(w, 3f)) Object.DestroyImmediate(rock);
            placed++;
        }
    }

    static void RiverRocks()
    {
        var rocks = LoadRocks("Rock_A_01", "Rock_B_01", "Rock_C_01");
        var line = ValleyShape.RiverLine;
        if (rocks.Length == 0 || line.Length < 2) return;
        var parent = new GameObject("River rocks").transform;
        var rng = new System.Random(33);
        float total = 0f;
        for (int i = 1; i < line.Length; i++) total += (line[i] - line[i - 1]).magnitude;
        for (int n = 0; n < RiverRockTries; n++)
        {
            var (along, way) = OnCourse(line, (float)rng.NextDouble() * total);
            var w = along + Vector2.Perpendicular(way) * ((float)(rng.NextDouble() * 2.0 - 1.0) * ValleyShape.RiverHalfWidth * 0.6f);
            var prefab = rocks[rng.Next(rocks.Length)];
            float turn = rng.Next(360), extra = (float)rng.NextDouble();
            if (!ValleyShape.InRiver(w) || ValleyShape.NearBridge(w, 6f) || ValleyShape.NearFalls(w)) continue;
            var at = new Vector3(w.x, 0f, w.y);
            float bed = EditorGround.GroundAt(at), water = bed + ValleyShape.ChannelDepth(w);
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Solid(rock);
            rock.transform.rotation = Quaternion.Euler(0f, turn, 0f);
            var size = BoundsOf(rock).size;
            rock.transform.localScale = Vector3.one * ((1.2f + extra) / Mathf.Max(Mathf.Max(size.x, size.z), 0.1f));
            var placed = BoundsOf(rock);
            float top = water + 0.3f + 0.3f * extra;
            rock.transform.position += new Vector3(w.x - placed.center.x, Mathf.Max(bed - 0.2f - placed.min.y, top - placed.max.y), w.y - placed.center.z);
            var wake = rock.AddComponent<RockWake>();
            wake.downstream = new Vector3(way.x, 0f, way.y);
            wake.waterY = water;
            wake.radius = Mathf.Max(0.3f, 0.45f * Mathf.Min(BoundsOf(rock).size.x, BoundsOf(rock).size.z));
        }
    }

    const int RiverRockTries = 16;

    static (Vector2 at, Vector2 way) OnCourse(Vector2[] line, float distance)
    {
        for (int i = 1; i < line.Length; i++)
        {
            var seg = line[i] - line[i - 1];
            if (distance <= seg.magnitude) return (line[i - 1] + seg.normalized * distance, seg.normalized);
            distance -= seg.magnitude;
        }
        return (line[^1], (line[^1] - line[^2]).normalized);
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static Vector2 CourtCentre => ValleyShape.CourtCentre;
    const float CourtLength = ValleyShape.CourtLength, CourtWidth = ValleyShape.CourtWidth;
    const float RailHeight = 1.2f, GoalWidth = 6f, GoalHeight = 2.2f, GoalDepth = 1.4f;
    const int RailLogs = 4;
    const float GateWidth = 2.4f, GateZ = 10.5f;

    static void BuildCourt()
    {
        var parent = new GameObject("Ball court").transform;
        var wood = LogMaterial();
        float x0 = CourtCentre.x - CourtLength / 2f, x1 = CourtCentre.x + CourtLength / 2f;
        float z0 = CourtCentre.y - CourtWidth / 2f, z1 = CourtCentre.y + CourtWidth / 2f;
        Rail(parent, wood, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0));
        Rail(parent, wood, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1));
        float gateFrom = GateZ - GateWidth / 2f, gateTo = GateZ + GateWidth / 2f;
        Rail(parent, wood, new Vector3(x0, 0f, CourtCentre.y + GoalWidth / 2f), new Vector3(x0, 0f, gateFrom - 0.15f));
        Rail(parent, wood, new Vector3(x0, 0f, gateTo + 0.15f), new Vector3(x0, 0f, z1));
        Gate(parent, wood, new Vector3(x0, 0f, gateFrom));
        Rail(parent, wood, new Vector3(x1, 0f, CourtCentre.y + GoalWidth / 2f), new Vector3(x1, 0f, z1));
        foreach (float x in new[] { x0, x1 })
        {
            Rail(parent, wood, new Vector3(x, 0f, z0), new Vector3(x, 0f, CourtCentre.y - GoalWidth / 2f));
            Goal(parent, wood, new Vector3(x, 0f, CourtCentre.y), x < CourtCentre.x ? -1f : 1f);
        }

        var spot = new Vector3(CourtCentre.x, 0f, CourtCentre.y);
        spot.y = EditorGround.GroundAt(spot);
        var game = parent.gameObject.AddComponent<BallCourt>();
        game.centre = spot;
        game.length = CourtLength;
        game.width = CourtWidth;
        game.goalWidth = GoalWidth;
        game.goalHeight = GoalHeight;
        game.goalDepth = GoalDepth;
        game.board = Scoreboard(parent, wood, new Vector3(CourtCentre.x, 0f, z1 + 1.8f));
    }

    static void Gate(Transform parent, Material mat, Vector3 hinge)
    {
        hinge.y = EditorGround.GroundAt(hinge);
        var gate = new GameObject("Court gate");
        gate.transform.SetParent(parent, false);
        gate.transform.position = hinge;
        float leaf = GateWidth - 0.1f;
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Gate leaf";
        box.transform.SetParent(gate.transform, false);
        box.transform.localPosition = new Vector3(0f, RailHeight / 2f, leaf / 2f + 0.05f);
        box.transform.localScale = new Vector3(0.15f, RailHeight, leaf);
        box.GetComponent<Renderer>().enabled = false;
        const int logs = RailLogs;
        float r = RailHeight / logs / 2f * 0.85f;
        for (int i = 0; i < logs; i++)
            GatePart(gate.transform, mat, new Vector3(0f, RailHeight / logs * (i + 0.5f), leaf / 2f + 0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(2f * r, leaf / 2f, 2f * r));
        foreach (float z in new[] { 0.15f, leaf - 0.05f })
            GatePart(gate.transform, mat, new Vector3(0f, RailHeight / 2f, z), Quaternion.identity, new Vector3(0.12f, RailHeight / 2f, 0.12f));
        foreach (float z in new[] { -0.05f, GateWidth + 0.05f })
            GatePart(parent, mat, hinge + new Vector3(0f, (RailHeight + 0.3f) / 2f, z), Quaternion.identity, new Vector3(0.2f, (RailHeight + 0.3f) / 2f, 0.2f), local: false);
        var body = gate.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = 200f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var swing = gate.AddComponent<CourtGate>();
        swing.width = GateWidth;
        swing.openAngle = 95f;
    }

    static void GatePart(Transform parent, Material mat, Vector3 at, Quaternion turn, Vector3 size, bool local = true)
    {
        var part = Primitives.Visual(PrimitiveType.Cylinder, "Gate log");
        part.transform.SetParent(parent, false);
        if (local) part.transform.SetLocalPositionAndRotation(at, turn);
        else part.transform.SetPositionAndRotation(at, turn);
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static readonly (float x, float y, bool across)[] Segments =
    {
        (0f, 0.5f, true), (0.25f, 0.25f, false), (0.25f, -0.25f, false), (0f, -0.5f, true),
        (-0.25f, -0.25f, false), (-0.25f, 0.25f, false), (0f, 0f, true),
    };

    static CourtScoreboard Scoreboard(Transform parent, Material wood, Vector3 at)
    {
        at.y = EditorGround.GroundAt(at);
        var root = new GameObject("Court scoreboard");
        root.transform.SetParent(parent, false);
        root.transform.position = at;
        var face = EditorAssets.LitMaterial($"{Generated}/CourtBoard.mat", new Color(0.07f, 0.09f, 0.08f), 0.2f);
        var lit = EditorAssets.LitMaterial($"{Generated}/CourtDigit.mat", new Color(1f, 0.78f, 0.35f), 0.3f, new Color(1f, 0.6f, 0.2f) * 2.5f);
        const float boardY = 2.6f, height = 1.0f, front = -0.09f;
        foreach (float x in new[] { -2.1f, 2.1f })
            BoardPart(root.transform, PrimitiveType.Cylinder, wood, new Vector3(x, boardY / 2f + 0.4f, 0f), new Vector3(0.2f, boardY / 2f + 0.4f, 0.2f));
        BoardPart(root.transform, PrimitiveType.Cube, face, new Vector3(0f, boardY, 0f), new Vector3(4.6f, 1.6f, 0.14f));
        foreach (float y in new[] { -0.2f, 0.2f })
            BoardPart(root.transform, PrimitiveType.Cube, lit, new Vector3(0f, boardY + y, front), new Vector3(0.1f, 0.1f, 0.04f));
        var digits = new[] { -1.55f, -0.85f, 0.85f, 1.55f };
        var segments = new Renderer[CourtScoreboard.DigitCount * CourtScoreboard.SegmentsPerDigit];
        for (int d = 0; d < digits.Length; d++)
        {
            for (int s = 0; s < Segments.Length; s++)
            {
                var (x, y, across) = Segments[s];
                var size = across ? new Vector3(height * 0.5f, 0.1f, 0.04f) : new Vector3(0.1f, height * 0.5f, 0.04f);
                var pos = new Vector3(digits[d] + x * height, boardY + y * height, front);
                segments[d * Segments.Length + s] = BoardPart(root.transform, PrimitiveType.Cube, lit, pos, size);
            }
        }
        var board = root.AddComponent<CourtScoreboard>();
        board.segments = segments;
        return board;
    }

    static Renderer BoardPart(Transform parent, PrimitiveType shape, Material mat, Vector3 at, Vector3 size)
    {
        var part = Primitives.Visual(shape, "Scoreboard part");
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        var look = part.GetComponent<Renderer>();
        look.sharedMaterial = mat;
        return look;
    }

    static void Rail(Transform parent, Material mat, Vector3 a, Vector3 b)
    {
        a.y = EditorGround.GroundAt(a);
        b.y = EditorGround.GroundAt(b);
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "Court rail";
        rail.transform.SetParent(parent, false);
        rail.transform.SetPositionAndRotation((a + b) / 2f + Vector3.up * (RailHeight / 2f), Quaternion.LookRotation(b - a));
        float length = Vector3.Distance(a, b) + 0.15f;
        rail.transform.localScale = new Vector3(0.15f, RailHeight, length);
        rail.GetComponent<Renderer>().enabled = false;
        const int logs = RailLogs;
        float r = RailHeight / logs / 2f;
        for (int i = 0; i < logs; i++)
        {
            var log = Primitives.Visual(PrimitiveType.Cylinder, "Court log");
            log.transform.SetParent(parent, false);
            log.transform.SetPositionAndRotation((a + b) / 2f + Vector3.up * (r + i * 2f * r), Quaternion.LookRotation(b - a) * Quaternion.Euler(90f, 0f, 0f));
            log.transform.localScale = new Vector3(2f * r, (length + 0.3f) / 2f, 2f * r);
            log.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static void Goal(Transform parent, Material mat, Vector3 mouth, float outward)
    {
        mouth.y = EditorGround.GroundAt(mouth);
        foreach (float side in new[] { -1f, 1f })
            Bar(parent, mat, mouth + new Vector3(0f, GoalHeight / 2f, side * GoalWidth / 2f), new Vector3(0.12f, GoalHeight, 0.12f));
        Bar(parent, mat, mouth + new Vector3(0f, GoalHeight, 0f), new Vector3(0.12f, 0.12f, GoalWidth + 0.12f));
        Bar(parent, mat, mouth + new Vector3(outward * GoalDepth, GoalHeight / 2f, 0f), new Vector3(0.1f, GoalHeight, GoalWidth));
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

    internal static readonly Vector2 StonesAt = new Vector2(-120f, 175f), OutcropAt = new Vector2(215f, 110f);

    static void BuildLandmarks()
    {
        var rocks = new List<GameObject>();
        foreach (var n in new[] { "Rock_A_01", "Rock_B_01", "Rock_C_01" })
        {
            var prefab = EditorAssets.LoadPrefab($"{DemoPack}/Rocks/{n}.prefab");
            if (prefab != null) rocks.Add(prefab);
        }
        if (rocks.Count == 0) return;
        var parent = new GameObject("Landmarks").transform;
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2f / 9f;
            var at = StonesAt + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f;
            Rock(parent, rocks[i % rocks.Count], at, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), new Vector3(1.6f, 5.5f, 1.3f));
        }
        Rock(parent, rocks[0], StonesAt, Quaternion.Euler(0f, 20f, 0f), new Vector3(2.2f, 0.7f, 1.6f));
        var rng = new System.Random(23);
        for (int i = 0; i < 6; i++)
        {
            var at = OutcropAt + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 14f;
            float s = 3f + 3f * (float)rng.NextDouble();
            Rock(parent, rocks[rng.Next(rocks.Count)], at, Quaternion.Euler(0f, rng.Next(360), 0f), new Vector3(s, s * (0.8f + 0.6f * (float)rng.NextDouble()), s));
        }
    }

    static void BuildWaterfall()
    {
        var parent = new GameObject("Falls").transform;
        var falls = ValleyShape.Falls;
        float top = ValleyShape.CliffTop, pond = ValleyShape.PondLevel, face = falls.y + ValleyShape.CliffNorth;
        CladCliff(parent, falls, face, pond, top);
        BuildScramble(parent, falls);

        var streaksPath = Generated + "/WaterStreaks.png";
        File.WriteAllBytes(streaksPath, ProceduralTextures.WaterStreaks(64, 256).EncodeToPNG());
        AssetDatabase.ImportAsset(streaksPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(streaksPath);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        var streaks = AssetDatabase.LoadAssetAtPath<Texture2D>(streaksPath);

        var water = EditorAssets.LitMaterial(Generated + "/FallsWater.mat", new Color(0.72f, 0.86f, 0.95f, 0.75f), 0.9f);
        UrpMaterials.MakeTransparent(water);
        water.SetTexture("_BaseMap", streaks);
        water.SetTextureScale("_BaseMap", new Vector2(2f, 1.5f));
        water.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(water);
        var foam = EditorAssets.LoadOrCreate(Generated + "/FallsFoam.mat", () => UrpMaterials.NewUnlitParticles("FallsFoam", Color.white, additive: true));
        foam.SetTexture("_BaseMap", streaks);
        foam.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
        foam.SetTextureScale("_BaseMap", new Vector2(3f, 2f));
        foam.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(foam);

        var lip = new Vector3(falls.x, top + 0.3f, face - 2.4f);
        float drop = top + 0.3f - pond;
        Sheet(parent, "Falls water", lip, drop, (8f, 0.8f), water, new Vector2(0f, -1.4f));
        Sheet(parent, "Falls foam", lip + Vector3.back * 0.15f, drop, (7f, 0.9f), foam, new Vector2(0.03f, -2.3f));
        var streamEnd = ValleyShape.Stream[ValleyShape.Stream.Length - 1];
        Chute(parent, new Vector3(streamEnd.x, top + 0.12f, streamEnd.y), lip, 6f, water);

        var footAt = new Vector3(falls.x, pond, face - 2.4f - 2.6f);
        Spray(parent, lip, drop);
        Mist(parent, footAt);
        var fx = parent.gameObject.AddComponent<WaterfallFx>();
        fx.splash = EditorAssets.LoadPrefab(PackPaths.WaterFx + "/BigSplash.prefab");
        fx.foot = footAt;
    }

    static void CladCliff(Transform parent, Vector2 falls, float face, float pond, float top)
    {
        var rocks = LoadRocks("Rock_A_02", "Rock_C_02");
        if (rocks.Length == 0) return;
        var rng = new System.Random(31);
        const float course = 4.5f;
        int courses = Mathf.CeilToInt((top - pond + 2f) / course);
        for (int k = 0; k < courses; k++)
        {
            float y = pond - 1.5f + k * course;
            float rise = k / (float)Mathf.Max(1, courses - 1);
            float span = Mathf.Lerp(22f, 17f, rise);
            for (float x = -span + (k % 2) * 2.5f; x <= span; x += 5f)
            {
                if (Mathf.Abs(x) < 8f && y + course * 1.5f > top - 0.4f) continue;
                float behind = Mathf.Abs(x) < 6f ? 1.8f : 0f;
                float size = 7.5f + 2.5f * (float)rng.NextDouble();
                var at = new Vector3(falls.x + x + (float)rng.NextDouble() - 0.5f, y, face + 0.6f + behind + rise * 1.2f + 0.6f * (float)rng.NextDouble());
                FitRock(parent, rocks[rng.Next(rocks.Length)], at, new Vector3(size, course * 1.5f, size * 0.8f), Quaternion.Euler(0f, rng.Next(4) * 90f + (float)rng.NextDouble() * 25f, 0f));
            }
        }
    }

    static void BuildScramble(Transform parent, Vector2 falls)
    {
        var rocks = LoadRocks("Rock_B_01", "Rock_C_01");
        if (rocks.Length == 0) return;
        var route = new GameObject("Scree ramp").transform;
        route.SetParent(parent, false);
        var rng = new System.Random(47);
        Vector2 foot = falls + ValleyShape.RampFoot, end = falls + ValleyShape.RampTop;
        var along = (end - foot).normalized;
        var side = Vector2.Perpendicular(along);
        float length = (end - foot).magnitude;
        for (float d = 2f; d < length - 1f; d += 3.5f + 1.5f * (float)rng.NextDouble())
            for (int s = -1; s <= 1; s += 2)
            {
                var at = foot + along * d + side * (s * (3.1f + 0.3f * (float)rng.NextDouble()));
                float ground = EditorGround.GroundAt(new Vector3(at.x, 0f, at.y));
                float size = 1.4f + 0.9f * (float)rng.NextDouble();
                FitRock(route, rocks[rng.Next(rocks.Length)], new Vector3(at.x, ground - 0.3f, at.y), new Vector3(size, 0.7f + 0.6f * (float)rng.NextDouble(), size), Quaternion.Euler(0f, rng.Next(360), 0f));
            }
    }

    static void FitRock(Transform parent, GameObject prefab, Vector3 foot, Vector3 size, Quaternion turn)
    {
        var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        rock.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var b = new Bounds();
        bool first = true;
        foreach (var r in rock.GetComponentsInChildren<Renderer>())
        {
            if (first) b = r.bounds;
            else b.Encapsulate(r.bounds);
            first = false;
        }
        if (first) return;
        var fit = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
        rock.transform.localScale = Vector3.Scale(rock.transform.localScale, fit);
        rock.transform.SetPositionAndRotation(foot - turn * Vector3.Scale(new Vector3(b.center.x, b.min.y, b.center.z), fit), turn);
        Solid(rock);
    }

    static void Sheet(Transform parent, string name, Vector3 lip, float drop, (float width, float flare) size, Material mat, Vector2 scroll)
    {
        const int rows = 18;
        var verts = new Vector3[(rows + 1) * 2];
        var uv = new Vector2[verts.Length];
        var tris = new int[rows * 6];
        for (int i = 0; i <= rows; i++)
        {
            float t = i / (float)rows;
            float half = size.width / 2f * (1f + (0.35f + size.flare) * t);
            float outward = 2.6f * Mathf.Sqrt(t);
            var c = new Vector3(0f, -drop * t, -outward);
            verts[i * 2] = c + Vector3.left * half;
            verts[i * 2 + 1] = c + Vector3.right * half;
            uv[i * 2] = new Vector2(0f, t * drop / 8f);
            uv[i * 2 + 1] = new Vector2(1f, t * drop / 8f);
            if (i == rows) break;
            int k = i * 6, v = i * 2;
            tris[k] = v; tris[k + 1] = v + 2; tris[k + 2] = v + 1;
            tris[k + 3] = v + 1; tris[k + 4] = v + 2; tris[k + 5] = v + 3;
        }
        var mesh = new Mesh { name = name, vertices = verts, uv = uv, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Generated}/{name.Replace(' ', '_')}.asset");
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = lip;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<ScrollingTexture>().speed = scroll;
    }

    static void Chute(Transform parent, Vector3 from, Vector3 lip, float width, Material mat)
    {
        var along = lip - from;
        var across = Vector3.Cross(Vector3.up, along).normalized;
        var verts = new[]
        {
            from - across * (width * 0.4f), from + across * (width * 0.4f),
            lip - across * (width * 0.5f), lip + across * (width * 0.5f),
        };
        float length = along.magnitude / 8f;
        var mesh = new Mesh
        {
            name = "Falls lip", vertices = verts,
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, length), new Vector2(1f, length) },
            triangles = new[] { 0, 2, 1, 1, 2, 3 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Generated + "/Falls_lip.asset");
        var go = new GameObject("Falls lip");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<ScrollingTexture>().speed = new Vector2(0f, -0.9f);
    }

    static void Spray(Transform parent, Vector3 lip, float drop)
    {
        var droplets = ParticleMaterialOf(EditorAssets.LoadPrefab(PackPaths.WaterFx + "/BigSplash.prefab"));
        var go = new GameObject("Falls spray");
        go.transform.SetParent(parent, false);
        go.transform.position = lip + Vector3.back * 0.6f;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = Mathf.Sqrt(2f * drop / 9.81f) + 0.2f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
        main.startColor = new Color(1f, 1f, 1f, 0.7f);
        main.gravityModifier = 1f;
        main.maxParticles = 1100;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 260f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(8f, 0.2f, 0.4f);
        shape.rotation = new Vector3(0f, 180f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (droplets != null) r.sharedMaterial = droplets;
    }

    static void Mist(Transform parent, Vector3 foot)
    {
        var smoke = EditorAssets.LoadPrefab(PackPaths.SmokeFx + "/SmokeEffect.prefab");
        if (smoke == null) return;
        var mist = (GameObject)PrefabUtility.InstantiatePrefab(smoke, parent);
        mist.name = "Falls mist";
        mist.transform.position = foot;
        mist.transform.localScale = Vector3.one * 3f;
        foreach (var ps in mist.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            main.startColor = new Color(0.92f, 0.95f, 1f, 0.35f);
            main.loop = true;
        }
    }

    static Material ParticleMaterialOf(GameObject prefab)
    {
        if (prefab == null) return null;
        foreach (var r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (r.sharedMaterial != null) return r.sharedMaterial;
        return null;
    }

    static void Solid(GameObject rock)
    {
        if (rock.GetComponentInChildren<Collider>() != null) return;
        IEnumerable<Renderer> renderers = rock.GetComponentsInChildren<Renderer>();
        if (rock.TryGetComponent<LODGroup>(out var lods) && lods.lodCount > 0) renderers = lods.GetLODs()[0].renderers;
        foreach (var r in renderers)
        {
            if (r == null || !r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
            r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
    }

    static void Rock(Transform parent, GameObject prefab, Vector2 at, Quaternion turn, Vector3 scale)
    {
        var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        var p = new Vector3(at.x, 0f, at.y);
        p.y = EditorGround.GroundAt(p) - 0.3f;
        rock.transform.SetPositionAndRotation(p, turn);
        rock.transform.localScale = scale;
        Solid(rock);
    }

    const float EdgeWallHeight = 400f, EdgeRockSpacing = 6f, EdgeInset = 3f;

    static void BuildWorldEdge()
    {
        var layout = EditorGround.Layout;
        var (x0, z0) = layout.ChunkCorner(0, 0);
        var (x1, z1) = layout.ChunkCorner(layout.ChunksX, layout.ChunksZ);
        var a = new Vector3(x0 + EdgeInset, 0f, z0 + EdgeInset);
        var b = new Vector3(x1 - EdgeInset, 0f, z1 - EdgeInset);
        var parent = new GameObject("World edge").transform;
        Wall(parent, "North", new Vector3((a.x + b.x) / 2f, 0f, b.z + 1f), new Vector3(b.x - a.x + 4f, EdgeWallHeight, 2f));
        Wall(parent, "South", new Vector3((a.x + b.x) / 2f, 0f, a.z - 1f), new Vector3(b.x - a.x + 4f, EdgeWallHeight, 2f));
        Wall(parent, "East", new Vector3(b.x + 1f, 0f, (a.z + b.z) / 2f), new Vector3(2f, EdgeWallHeight, b.z - a.z + 4f));
        Wall(parent, "West", new Vector3(a.x - 1f, 0f, (a.z + b.z) / 2f), new Vector3(2f, EdgeWallHeight, b.z - a.z + 4f));
        EdgeRocks(parent, a, b);
    }

    static void Wall(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name + " wall") { layer = 2 };
        go.transform.SetParent(parent, false);
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
                at.y = EditorGround.GroundAt(at);
                if (at.y < LakeLevel) continue;
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[rng.Next(rocks.Count)], parent);
                Solid(rock);
                rock.transform.SetPositionAndRotation(at - Vector3.up * 0.3f, Quaternion.Euler(0f, rng.Next(360), 0f));
                rock.transform.localScale = Vector3.one * (0.9f + 0.8f * (float)rng.NextDouble());
            }
        }
    }

    static void BuildLake()
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
        lake.transform.localScale = new Vector3(24f, 1f, 16f);
        var r = lake.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        lake.AddComponent<ScrollingTexture>().speed = new Vector2(0.004f, 0.003f);
    }

    static Material SoftMaterial(string name, Color color, float smoothness, Texture2D normals, float bump)
    {
        var mat = EditorAssets.LoadOrCreate($"{Generated}/{name}.mat", () => new Material(Shader.Find(UrpMaterials.LitShader)));
        UrpMaterials.ConfigureSoftSurface(mat, color, smoothness);
        if (normals != null) UrpMaterials.SetNormalMap(mat, normals, bump);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void BuildSky()
    {
        var shader = Shader.Find("Punchy/Sky");
        var sky = EditorAssets.LoadOrCreate(Generated + "/Sky.mat", () => new Material(shader));
        sky.shader = shader;
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        var sun = Object.FindAnyObjectByType<Light>();
        if (sun != null) RenderSettings.sun = sun;
        var mountains = EditorAssets.LoadPrefab(DemoPack + "/Skybox/SkyboxMountains.prefab");
        if (mountains != null)
        {
            var ring = (GameObject)PrefabUtility.InstantiatePrefab(mountains);
            ring.transform.position = new Vector3(Centre.x, -22f, Centre.y);
            foreach (var name in new[] { "SkyboxMountains05", "SkyboxMountains06" })
            {
                var piece = ring.transform.Find(name);
                if (piece != null) piece.gameObject.SetActive(false);
            }
            foreach (var name in new[] { "SkyboxMountains04", "SkyboxMountains07" })
            {
                var piece = ring.transform.Find(name);
                if (piece != null) piece.localPosition += Vector3.down * 550f;
            }
        }
        else Debug.LogWarning("Sample skybox missing: plain sky. Run Prototype > Restore Asset Packs.");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.84f);
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 16000f;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) cam.farClipPlane = 15000f;
        foreach (var vcam in Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None))
        {
            var lens = vcam.Lens;
            lens.FarClipPlane = 15000f;
            vcam.Lens = lens;
        }
    }

    sealed class CabinLook
    {
        public Material log, gable, roof, stone, door, window;
    }

    static Transform[] BuildCabins()
    {
        var look = new CabinLook
        {
            log = LogMaterial(),
            gable = CabinMaterial("Gable", new Color(0.25f, 0.17f, 0.11f), 0.1f),
            roof = CabinMaterial("Roof", new Color(0.24f, 0.2f, 0.18f), 0.1f),
            stone = CabinMaterial("Stone", new Color(0.45f, 0.44f, 0.42f), 0.1f),
            door = CabinMaterial("Door", new Color(0.2f, 0.13f, 0.08f), 0.2f),
            window = CabinMaterial("Window", new Color(1f, 0.75f, 0.4f), 0.3f, new Color(1f, 0.6f, 0.25f) * 2.2f),
        };
        var sites = ValleyShape.Cabins;
        var roots = new Transform[sites.Length];
        for (int i = 0; i < sites.Length; i++) roots[i] = BuildCabin(sites[i], i == 0 ? "Cabin" : $"Cabin {i + 1}", look);
        return roots;
    }

    static Transform[] BuildVillages()
    {
        var houses = new List<Transform>();
        if (ValleyShape.Villages.Length == 0) return houses.ToArray();
        var material = VillageMaterial();
        var smoke = EditorAssets.LoadPrefab(PackPaths.SmokeFx + "/SmokeEffect.prefab");
        float cs = EditorGround.Layout.CellSize;
        for (int v = 0; v < ValleyShape.Villages.Length; v++)
        {
            var plan = ValleyShape.Villages[v];
            float level = ValleyShape.VillageLevels[v];
            var well = ValleyShape.CellWorld(plan.Well.x, plan.Well.y);
            var root = new GameObject($"Village {plan.Spec.Name}");
            root.transform.position = new Vector3(well.x, level, well.y);
            var village = root.AddComponent<Village>();
            village.villageName = plan.Spec.Name;
            village.material = material;
            village.seed = plan.Spec.Seed;
            bool island = ValleyCoast.OnIsland(well);
            village.residents = island ? "elf" : "human";
            village.residentCount = island ? 4 : 7;
            village.wander = Mathf.Max(ValleyShape.AreaOf(plan.Bounds).size.x, ValleyShape.AreaOf(plan.Bounds).size.y) * 0.45f;
            village.enabled = false;
            village.enabled = true;

            var square = ValleyShape.AreaOf(plan.Square);
            var rng = new System.Random(plan.Spec.Seed);
            foreach (var side in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
            {
                var along = -Vector2.Perpendicular(side);
                var at = square.center + side * (square.size.x / 2f - 1.3f) + along * 3.2f;
                var bench = CoastBuilder.Bench(root.transform, at, -side, rng.Next(), material);
                bench.transform.position = new Vector3(at.x, level, at.y);
            }

            int n = 0;
            foreach (var b in plan.Buildings)
            {
                var name = b.Kind == BuildingKind.House ? $"House {++n}" : b.Kind.ToString();
                var house = BuildHouse(root.transform, b, name, level, cs, material, smoke);
                houses.Add(house);
            }
        }
        return houses.ToArray();
    }

    const string VillageMaterialGuid = "1c2f7c809517e4946a98211dcf83ad02";

    internal static Material VillageMaterial()
    {
        string path = $"{Generated}/VillageLit.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null && AssetDatabase.AssetPathToGUID(path) == VillageMaterialGuid) return existing;
        if (existing != null) AssetDatabase.DeleteAsset(path);
        EditorAssets.EnsureFolderFor(path);
        string scratch = $"{Generated}/VillageLit.scratch.mat";
        AssetDatabase.CreateAsset(new Material(Shader.Find("Prototype/VillageLit")), scratch);
        AssetDatabase.SaveAssets();
        File.WriteAllText(path, File.ReadAllText(scratch).Replace("m_Name: VillageLit.scratch", "m_Name: VillageLit"));
        File.WriteAllText(path + ".meta",
            "fileFormatVersion: 2\nguid: " + VillageMaterialGuid + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        AssetDatabase.DeleteAsset(scratch);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    internal static Material GlassMaterial()
    {
        var glass = EditorAssets.LoadOrCreate($"{Generated}/WindowGlass.mat", () => new Material(Shader.Find("Prototype/WindowGlass")));
        glass.shader = Shader.Find("Prototype/WindowGlass");
        return glass;
    }

    static Transform BuildHouse(Transform village, VillageBuilding b, string name, float level, float cs, Material material, GameObject smoke)
    {
        var go = new GameObject(name);
        go.transform.SetParent(village, false);
        var mid = ValleyShape.AreaOf(b.Footprint).center;
        var (dx, dy) = Sides.Step(b.Door);
        var door = new Vector3(dx, 0f, -dy);
        go.transform.SetPositionAndRotation(new Vector3(mid.x, level - 0.05f, mid.y), Quaternion.LookRotation(-door));

        var house = go.AddComponent<VillageHouse>();
        house.kind = b.Kind;
        house.walls = b.Walls;
        house.roof = b.Roof;
        house.cover = b.Cover;
        house.storeys = b.Storeys;
        house.chimney = b.Chimney;
        house.seed = b.Seed;
        house.width = b.Width * cs;
        house.depth = b.Depth * cs;
        house.material = material;
        house.glass = GlassMaterial();
        Vector3 Local((int x, int y) cell)
        {
            var at = ValleyShape.AreaOf(new CellRect(cell.x, cell.y, 1, 1)).center;
            return go.transform.InverseTransformPoint(new Vector3(at.x, go.transform.position.y, at.y));
        }
        house.doorAt = Local(b.DoorCell).x;
        house.windows = b.Windows.Select(w =>
        {
            var at = Local((w.X, w.Y));
            var (wx, wy) = Sides.Step(w.Face);
            var way = go.transform.InverseTransformDirection(new Vector3(wx, 0f, -wy));
            if (Mathf.Abs(way.x) > 0.5f) return new Vector2(way.x > 0f ? BuildingMesher.Spec.Right : BuildingMesher.Spec.Left, at.z);
            return new Vector2(way.z > 0f ? BuildingMesher.Spec.Back : BuildingMesher.Spec.Front, at.x);
        }).ToArray();
        house.Rebuild();

        var made = BuildingMesher.Make(house.Spec);
        var block = go.AddComponent<BoxCollider>();
        block.center = new Vector3(0f, made.eaves / 2f, 0f);
        block.size = new Vector3(made.size.x, made.eaves, made.size.z);
        Object.DestroyImmediate(made.body);
        Object.DestroyImmediate(made.roof);

        if (smoke != null && !float.IsNaN(made.chimneyTop.x))
        {
            var puff = (GameObject)PrefabUtility.InstantiatePrefab(smoke, go.transform);
            puff.transform.localPosition = made.chimneyTop;
            puff.transform.localScale = Vector3.one * 0.3f;
        }
        if (b.Kind != BuildingKind.House)
        {
            var lamp = new GameObject("Door light").AddComponent<Light>();
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = made.lamp;
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.7f, 0.4f);
            lamp.intensity = 2.5f;
            lamp.range = 7f;
        }
        return go.transform;
    }

    static Material LogMaterial() => CabinMaterial("Log", new Color(0.42f, 0.29f, 0.18f), 0.15f);

    static Transform BuildCabin(ValleyShape.CabinSite site, string name, CabinLook look)
    {
        var root = new GameObject(name).transform;
        root.SetPositionAndRotation(new Vector3(site.At.x, ValleyShape.HeightAt(site.At) - 0.1f, site.At.y), ValleyShape.CabinRotation(site));

        Material log = look.log, roof = look.roof, stone = look.stone, dark = look.door, glow = look.window;

        const float w = 6f, l = 4.5f, r = 0.16f, pitch = 34f, overhang = 0.5f;
        const int courses = 9;
        float wallTop = courses * r * 2f;
        float rise = l / 2f * Mathf.Tan(pitch * Mathf.Deg2Rad);

        for (int i = 0; i < courses; i++)
        {
            float y = r + i * r * 2f;
            Log(root, new Vector3(0f, y, l / 2f), w + 0.6f, Axis.X, log);
            Log(root, new Vector3(0f, y, -l / 2f), w + 0.6f, Axis.X, log);
            Log(root, new Vector3(w / 2f, y + r, 0f), l + 0.6f, Axis.Z, log);
            Log(root, new Vector3(-w / 2f, y + r, 0f), l + 0.6f, Axis.Z, log);
        }

        for (float y = r; y + r * 2f < rise; y += r * 2f)
        {
            float span = l * (1f - (y + r * 2f) / rise);
            Log(root, new Vector3(w / 2f, wallTop + y + r, 0f), span, Axis.Z, log);
            Log(root, new Vector3(-w / 2f, wallTop + y + r, 0f), span, Axis.Z, log);
        }

        var ridge = new Vector3(0f, wallTop + rise + 0.1f, 0f);
        float panel = (l / 2f + overhang) / Mathf.Cos(pitch * Mathf.Deg2Rad);
        for (int side = -1; side <= 1; side += 2)
        {
            var downhill = new Vector3(0f, -Mathf.Sin(pitch * Mathf.Deg2Rad), side * Mathf.Cos(pitch * Mathf.Deg2Rad));
            var slab = Part(root, PrimitiveType.Cube, ridge + downhill * (panel / 2f), new Vector3(w + 1.2f, 0.12f, panel), roof);
            slab.transform.localRotation = Quaternion.LookRotation(downhill);
            slab.name = "Roof";
        }

        float underRoof = wallTop + rise + 0.1f - 0.06f / Mathf.Cos(pitch * Mathf.Deg2Rad);
        float boardLength = (l / 2f + r) / Mathf.Cos(pitch * Mathf.Deg2Rad), boardDrop = l / 2f * Mathf.Sin(pitch * Mathf.Deg2Rad) + 0.05f;
        for (int end = -1; end <= 1; end += 2)
            for (int side = -1; side <= 1; side += 2)
            {
                var downhill = new Vector3(0f, -Mathf.Sin(pitch * Mathf.Deg2Rad), side * Mathf.Cos(pitch * Mathf.Deg2Rad));
                var turn = Quaternion.LookRotation(downhill);
                var middle = new Vector3(end * w / 2f, underRoof, 0f) + downhill * (boardLength / 2f) + turn * Vector3.down * (boardDrop / 2f);
                var board = Part(root, PrimitiveType.Cube, middle, new Vector3(0.1f, boardDrop, boardLength), look.gable);
                board.transform.localRotation = turn;
                board.name = "Gable wall";
            }

        Part(root, PrimitiveType.Cube, new Vector3(0.8f, 1.0f, -l / 2f - 0.17f), new Vector3(1f, 2f, 0.08f), dark);
        Part(root, PrimitiveType.Cube, new Vector3(-1.6f, 1.6f, -l / 2f - 0.17f), new Vector3(0.9f, 0.7f, 0.05f), glow);
        var chimney = new Vector3(-w / 2f + 0.8f, wallTop + rise / 2f + 0.9f, l / 4f);
        Part(root, PrimitiveType.Cube, chimney, new Vector3(0.7f, rise + 1.8f, 0.7f), stone);
        var glass = root.gameObject.AddComponent<WindowPanes>();
        glass.material = GlassMaterial();
        glass.panes = new[] { new WindowPanes.Pane { at = new Vector3(ValleyShape.CabinWindow.x, 1.6f, ValleyShape.CabinWindow.y), along = Vector3.right * 0.45f, up = Vector3.up * 0.35f, facing = Vector3.back } };

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

        var block = root.gameObject.AddComponent<BoxCollider>();
        block.center = new Vector3(0f, (wallTop + rise) / 2f, 0f);
        block.size = new Vector3(w + 0.6f, wallTop + rise, l + 0.6f);
        return root;
    }

    enum Axis { X, Z }

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
        mat.SetFloat("_HueVariationKwToggle", 0f);
        mat.DisableKeyword("EFFECT_HUE_VARIATION");
        EditorUtility.SetDirty(mat);
        return mat;
    }

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

    internal static Texture2D Tinted(Texture2D source, Color multiply, int size = 1024)
    {
        string path = $"{Generated}/{source.name}_Tinted.png";
        var copy = ReadableCopy(source, size);
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

    static Texture2D ReadableCopy(Texture2D source, int size)
    {
        string file = AssetDatabase.GetAssetPath(source);
        string temp = $"{Generated}/{source.name}_Readable{Path.GetExtension(file)}";
        if (!AssetDatabase.CopyAsset(file, temp)) throw new IOException($"Couldn't copy {file} to {temp}");
        try
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(temp);
            importer.ClearPlatformTextureSettings("Standalone");
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(temp);
            var copy = new Texture2D(imported.width, imported.height, TextureFormat.RGBA32, false);
            copy.SetPixels32(imported.GetPixels32());
            return copy;
        }
        finally
        {
            AssetDatabase.DeleteAsset(temp);
        }
    }

    const int FireFadeCells = 50;
    const int LightningReach = 12000;

    static void WireChemistry(ChemGrid grid, string[] yardRows)
    {
        var (x, y, w, h) = MapFile.Load(ChemGrid.Folders.World).Yard;
        grid.yardCells = new RectInt(x, y, w, h);
        var (left, top) = EditorGround.Layout.CellAt(0f, (yardRows.Length - 1) * grid.cellSize);
        if (new RectInt(left, top, yardRows[0].Length, yardRows.Length) != grid.yardCells)
            Debug.LogError($"map.json's yard {grid.yardCells} isn't where the yard's rows lie ({left}, {top})");
        grid.fireFadeCells = FireFadeCells;
        grid.conductLimit = LightningReach;
    }

    static Vector2Int CellOf(Vector2 p, float cs, int left, int top, int yh) =>
        new Vector2Int(Mathf.RoundToInt(p.x / cs) + left, top + yh - 1 - Mathf.RoundToInt(p.y / cs));

}
