using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Sets the Chemistry Yard in a mountain valley: chunk terrains (ValleyTerrain)
// shaped by ValleyShape, the old yard just meadow in a flat green valley floor
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
        // Raised walls are built of the demo pack's rocks: upright crags and slabs for their faces, flat stones on top.
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
        BuildLandmarks();
        BuildWaterfall();
        var valley = grid.gameObject.AddComponent<ValleyChemistry>();
        valley.grid = grid;
        valley.chunks = terrains;
        // Village buildings burn and cave in by the cabins' rules.
        valley.cabins = cabins.Concat(houses).ToArray();
        valley.lakeLevel = LakeLevel;
        valley.channelCells = channelCells.ToArray();
        valley.channelDepths = channelDepths.ToArray();
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

        // The cells painted as they are: the old yard's, the ball court's dirt floor (CellKind), and the
        // villages' squares, lanes and walls.
        var paintedCells = YardCells(grid.yardCells);
        paintedCells.AddRange(CourtCells());
        paintedCells.AddRange(PathCells());
        foreach (var plan in ValleyShape.Villages)
            foreach (var (vx, vy, _) in plan.Cells()) paintedCells.Add((vx, vy));
        ValleyTerrain.PlantTrees(assets.trees, AllTrees(valleyTrees, grid, world));
        ValleyTerrain.Paint(assets.layers, world, paintedCells);
        ValleyTerrain.PaintDetails(assets.details, world, paintedCells);
        return ValleyTerrain.PlaceTerrains(grid.yardCells.position);
    }

    static List<(int x, int y)> YardCells(RectInt yard)
    {
        var cells = new List<(int x, int y)>();
        for (int y = yard.yMin; y < yard.yMax; y++)
            for (int x = yard.xMin; x < yard.xMax; x++) cells.Add((x, y));
        return cells;
    }

    // The cells on and just round the court; only the ones that are dirt paint (the rest stay grass).
    // The path's cells, painted as the dirt they are.
    static IEnumerable<(int x, int y)> PathCells()
    {
        var layout = ValleyTerrain.Layout;
        foreach (var (x, y) in layout.Cells())
        {
            var (cx, cz) = layout.CellCenter(x, y);
            if (ValleyCoast.OnPath(new Vector2(cx, cz))) yield return (x, y);
        }
    }

    static IEnumerable<(int x, int y)> CourtCells()
    {
        var layout = ValleyTerrain.Layout;
        foreach (var (x, y) in layout.Cells())
        {
            var (cx, cz) = layout.CellCenter(x, y);
            if (ValleyShape.InCourt(new Vector2(cx, cz), -1f)) yield return (x, y);
        }
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
    // the yard, thickening toward the valley's edges; past them as thick as the generated land's
    // biome grows it (ValleySurroundings).
    static List<(Vector2 at, float scale, int species, float turn)> TreeSpots(bool any)
    {
        var spots = new List<(Vector2, float, int, float)>();
        if (!any) return spots;
        var layout = ValleyTerrain.Layout;
        float size = layout.ChunkSize * layout.ChunksX;
        var rng = new System.Random(7);
        for (int i = 0; i < 16000 && spots.Count < 3600; i++)
        {
            var w = new Vector2(layout.OriginX + (float)rng.NextDouble() * size, layout.OriginZ + (float)rng.NextDouble() * size);
            if (!TreeFits(w)) continue;
            float d = ValleyShape.DistanceToYard(w), h = ValleyShape.HeightAt(w);
            float chance = Mathf.InverseLerp(14f, 70f, d) * (0.35f + 0.65f * Mathf.InverseLerp(40f, 160f, (w - Centre).magnitude));
            chance = Mathf.Lerp(chance, ValleySurroundings.Trees(w), ValleySurroundings.Mask(w));
            chance *= Mathf.InverseLerp(150f, 90f, h);
            if (rng.NextDouble() > chance) continue;
            float scale = 0.8f + 0.5f * (float)rng.NextDouble();
            spots.Add((w, scale, rng.NextDouble() < 0.35 ? 1 : 0, (float)(rng.NextDouble() * Mathf.PI * 2)));
        }
        // None on the ball court, in a village, on the coast or by the path. Dropped afterwards, so every
        // other tree stands where it always has.
        spots.RemoveAll(s => ValleyShape.InCourt(s.Item1, -1.5f));
        spots.RemoveAll(s => ValleyShape.DistanceToVillage(s.Item1) < 4f);
        spots.RemoveAll(s => ValleyCoast.Coastal(s.Item1) || ValleyCoast.DistanceToPath(s.Item1) < 3.5f);
        return spots;
    }

    static bool TreeFits(Vector2 w) =>
        !ValleyShape.KeepClear(w, 14f) && (w - StonesAt).magnitude > 16f && (w - OutcropAt).magnitude > 14f && ValleyShape.HeightAt(w) <= 150f && ValleyTerrain.Steepness(w) <= 34f;

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

    // The demo pack's rocks by name, those that are there.
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
            // None in a village; removed after its draws, so the other rocks stay where they were.
            if (ValleyShape.DistanceToVillage(w) < 4f || ValleyCoast.DistanceToPath(w) < 3f) Object.DestroyImmediate(rock);
            placed++;
        }
    }

    // --- the ball court --------------------------------------------------------

    // A walled court on the flat meadow east of the yard, with a goal in each end wall and the
    // ball in the middle (world/things.json). Wooden rails a ball bounces off, low enough to step
    // over a corner of; the goal mouths are open. Its floor is dirt (CellKind). A gate in the
    // west rail, the yard's side, opens as a wizard comes up to it (CourtGate); a scoreboard
    // stands by the north rail, and BallCourt counts the goals and puts the ball back.
    static Vector2 CourtCentre => ValleyShape.CourtCentre;
    const float CourtLength = ValleyShape.CourtLength, CourtWidth = ValleyShape.CourtWidth;
    const float RailHeight = 0.9f, GoalWidth = 6f, GoalHeight = 2.2f, GoalDepth = 1.4f;
    // The gate: how wide, and where its middle is along the west rail north of the goal (z).
    const float GateWidth = 2.4f, GateZ = 10.5f;

    static void BuildCourt()
    {
        var parent = new GameObject("Ball court").transform;
        // Logs, as the cabins are built of.
        var wood = LogMaterial();
        float x0 = CourtCentre.x - CourtLength / 2f, x1 = CourtCentre.x + CourtLength / 2f;
        float z0 = CourtCentre.y - CourtWidth / 2f, z1 = CourtCentre.y + CourtWidth / 2f;
        // The long sides, whole; the ends, either side of a goal mouth, the west one's north part
        // either side of the gate.
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
        spot.y = ValleyTerrain.GroundAt(spot);
        var game = parent.gameObject.AddComponent<BallCourt>();
        game.centre = spot;
        game.length = CourtLength;
        game.width = CourtWidth;
        game.goalWidth = GoalWidth;
        game.goalHeight = GoalHeight;
        game.goalDepth = GoalDepth;
        game.board = Scoreboard(parent, wood, new Vector3(CourtCentre.x, 0f, z1 + 1.8f));
    }

    // The gate, hinged at its south post, shut along the rail (local +z) and swinging east into
    // the court: three logs like the rail's on two uprights, around an unseen box that stops the
    // ball and the wizard, with a post either side of the gateway.
    static void Gate(Transform parent, Material mat, Vector3 hinge)
    {
        hinge.y = ValleyTerrain.GroundAt(hinge);
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
        const int logs = 3;
        float r = RailHeight / logs / 2f * 0.85f;
        for (int i = 0; i < logs; i++)
            GatePart(gate.transform, mat, new Vector3(0f, RailHeight / logs * (i + 0.5f), leaf / 2f + 0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(2f * r, leaf / 2f, 2f * r));
        foreach (float z in new[] { 0.15f, leaf - 0.05f })
            GatePart(gate.transform, mat, new Vector3(0f, RailHeight / 2f, z), Quaternion.identity, new Vector3(0.12f, RailHeight / 2f, 0.12f));
        // The gateposts stand still, a little taller than the rail.
        foreach (float z in new[] { -0.05f, GateWidth + 0.05f })
            GatePart(parent, mat, hinge + new Vector3(0f, (RailHeight + 0.3f) / 2f, z), Quaternion.identity, new Vector3(0.2f, (RailHeight + 0.3f) / 2f, 0.2f), local: false);
        var body = gate.AddComponent<Rigidbody>();
        body.isKinematic = true;
        // Heavy, so a wizard walking into it never counts it as something to kick (Kicker).
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

    // Where each segment of a seven-segment figure sits (a to g, CourtScoreboard), in figure
    // heights from its middle, and whether it lies across (else upright).
    static readonly (float x, float y, bool across)[] Segments =
    {
        (0f, 0.5f, true), (0.25f, 0.25f, false), (0.25f, -0.25f, false), (0f, -0.5f, true),
        (-0.25f, -0.25f, false), (-0.25f, 0.25f, false), (0f, 0f, true),
    };

    // A dark board on two log posts, facing the court (south), with four lit figures: the west
    // end's goals on the left, the east end's on the right, a colon between.
    static CourtScoreboard Scoreboard(Transform parent, Material wood, Vector3 at)
    {
        at.y = ValleyTerrain.GroundAt(at);
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

    // A solid rail between two points on the ground: logs stacked to the rail's height, around an
    // unseen box that stops the ball.
    static void Rail(Transform parent, Material mat, Vector3 a, Vector3 b)
    {
        a.y = ValleyTerrain.GroundAt(a);
        b.y = ValleyTerrain.GroundAt(b);
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "Court rail";
        rail.transform.SetParent(parent, false);
        rail.transform.SetPositionAndRotation((a + b) / 2f + Vector3.up * (RailHeight / 2f), Quaternion.LookRotation(b - a));
        float length = Vector3.Distance(a, b) + 0.15f;
        rail.transform.localScale = new Vector3(0.15f, RailHeight, length);
        rail.GetComponent<Renderer>().enabled = false;
        const int logs = 3;
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

    // Two posts and a crossbar across a goal mouth, a bar along the back of the net to stop the ball.
    static void Goal(Transform parent, Material mat, Vector3 mouth, float outward)
    {
        mouth.y = ValleyTerrain.GroundAt(mouth);
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

    // --- places to go -----------------------------------------------------------

    // A few things to walk out to, well away from the start: a ring of standing stones on the
    // rise north-west of the yard, and a tall outcrop of rock to the east, past the river.
    static readonly Vector2 StonesAt = new Vector2(-120f, 175f), OutcropAt = new Vector2(215f, 110f);

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
        // The standing stones: nine tall stones in a ring 18 m across, each turned to face the middle.
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2f / 9f;
            var at = StonesAt + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f;
            Rock(parent, rocks[i % rocks.Count], at, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), new Vector3(1.6f, 5.5f, 1.3f));
        }
        Rock(parent, rocks[0], StonesAt, Quaternion.Euler(0f, 20f, 0f), new Vector3(2.2f, 0.7f, 1.6f)); // the altar stone
        // The outcrop: big rocks piled against each other, a climb (or a Misty Step) to a view.
        var rng = new System.Random(23);
        for (int i = 0; i < 6; i++)
        {
            var at = OutcropAt + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 14f;
            float s = 3f + 3f * (float)rng.NextDouble();
            Rock(parent, rocks[rng.Next(rocks.Count)], at, Quaternion.Euler(0f, rng.Next(360), 0f), new Vector3(s, s * (0.8f + 0.6f * (float)rng.NextDouble()), s));
        }
    }

    // --- the falls -------------------------------------------------------------------

    // The falls at the head of the river (ValleyShape.Falls): the cliff face clad in the demo pack's
    // big rocks, a stream over its lip (river squares, drawn by the cells) pouring down two sheets of
    // streaming water (one see-through, one of white foam, scrolled), spray falling down them, mist and
    // splashes where they land in the pond (WaterfallFx), which is river squares too and feeds the river.
    static void BuildWaterfall()
    {
        var parent = new GameObject("Falls").transform;
        var falls = ValleyShape.Falls;
        float top = ValleyShape.CliffTop, pond = ValleyShape.PondLevel, face = falls.y + ValleyShape.CliffNorth;
        CladCliff(parent, falls, face, pond, top);

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
        water.SetFloat("_Cull", 0f);   // both faces: seen from the pond and from the cliff top
        EditorUtility.SetDirty(water);
        var foam = EditorAssets.LoadOrCreate(Generated + "/FallsFoam.mat", () => UrpMaterials.NewUnlitParticles("FallsFoam", Color.white, additive: true));
        foam.SetTexture("_BaseMap", streaks);
        foam.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
        foam.SetTextureScale("_BaseMap", new Vector2(3f, 2f));
        foam.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(foam);

        // The water leaves the lip a little out from the face, so it falls clear of the rocks.
        var lip = new Vector3(falls.x, top + 0.3f, face - 2.4f);
        float drop = top + 0.3f - pond;
        Sheet(parent, "Falls water", lip, drop, (4.6f, 0f), water, new Vector2(0f, -1.4f));
        Sheet(parent, "Falls foam", lip + Vector3.back * 0.15f, drop, (4f, 0.1f), foam, new Vector2(0.03f, -2.3f));
        // The stream pours over the rocks to the lip: a short sheet from where its squares end.
        var streamEnd = ValleyShape.Stream[ValleyShape.Stream.Length - 1];
        Chute(parent, new Vector3(streamEnd.x, top + 0.12f, streamEnd.y), lip, 3.6f, water);

        var footAt = new Vector3(falls.x, pond, face - 2.4f - 2.6f);
        Spray(parent, lip, drop);
        Mist(parent, footAt);
        var fx = parent.gameObject.AddComponent<WaterfallFx>();
        fx.splash = EditorAssets.LoadPrefab(PackPaths.WaterFx + "/BigSplash.prefab");
        fx.foot = footAt;
    }

    // The cliff face built up of big rocks in staggered courses, like a crag: each rock its own shape
    // (only a little stretched), the courses stepping back a little as they rise, narrowing toward
    // the top and the ends where the shelf eases back into the mountain. The ones behind the falls
    // stand back so the water pours clear in front of them.
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
            float span = Mathf.Lerp(17f, 13f, rise);
            for (float x = -span + (k % 2) * 2.5f; x <= span; x += 5f)
            {
                // Behind the falls the rocks stop below the lip, so the stream goes over clear.
                if (Mathf.Abs(x) < 6f && y + course * 1.5f > top - 0.4f) continue;
                float behind = Mathf.Abs(x) < 4f ? 1.8f : 0f;
                float size = 7.5f + 2.5f * (float)rng.NextDouble();
                var at = new Vector3(falls.x + x + (float)rng.NextDouble() - 0.5f, y, face + 0.6f + behind + rise * 1.2f + 0.6f * (float)rng.NextDouble());
                FitRock(parent, rocks[rng.Next(rocks.Length)], at, new Vector3(size, course * 1.5f, size * 0.8f), Quaternion.Euler(0f, rng.Next(4) * 90f + (float)rng.NextDouble() * 25f, 0f));
            }
        }
    }

    // A rock prefab stood on <paramref name="foot"/>, stretched to about <paramref name="size"/>.
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
    }

    // A sheet of falling water: a strip of quads from the lip down to the pond, bowing out as it falls,
    // a little wider at the foot, its streaks scrolled down at the given speed.
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

    // A sheet of water sliding from <paramref name="from"/> over the edge to <paramref name="lip"/>,
    // widening toward the lip, its streaks running along it.
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

    // Spray tumbling down the face of the falls: the splash's droplets, let go along the lip.
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
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 140f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(4.6f, 0.2f, 0.4f);
        shape.rotation = new Vector3(0f, 180f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (droplets != null) r.sharedMaterial = droplets;
    }

    // A cloud of mist rolling off the foot of the falls: the smoke effect, turned white and slow.
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

    static void Rock(Transform parent, GameObject prefab, Vector2 at, Quaternion turn, Vector3 scale)
    {
        var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        var p = new Vector3(at.x, 0f, at.y);
        p.y = ValleyTerrain.GroundAt(p) - 0.3f;
        rock.transform.SetPositionAndRotation(p, turn);
        rock.transform.localScale = scale;
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
        // Punchy's sky (ThirdParty/Punchy): gradient, sun, moon, stars and volumetric clouds, all
        // from globals CloudSky sets from the clock and the weather. (The sample's HDR cubemap
        // sky is tuned for HDRP's physical exposure and washes out in URP.)
        var shader = Shader.Find("Punchy/Sky");
        var sky = EditorAssets.LoadOrCreate(Generated + "/Sky.mat", () => new Material(shader));
        sky.shader = shader;
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null) RenderSettings.sun = sun;
        var mountains = EditorAssets.LoadPrefab(DemoPack + "/Skybox/SkyboxMountains.prefab");
        if (mountains != null)
        {
            var ring = (GameObject)PrefabUtility.InstantiatePrefab(mountains);
            ring.transform.position = new Vector3(Centre.x, -22f, Centre.y);
            // The sea is to the south-east: no mountains on its horizon (the ring's 05 and 06
            // stand from east to south). Only this scene's copy; the pack is untouched.
            foreach (var name in new[] { "SkyboxMountains05", "SkyboxMountains06" })
            {
                var piece = ring.transform.Find(name);
                if (piece != null) piece.gameObject.SetActive(false);
            }
            // Their neighbours sink lower, so the ends they leave stand short and lost in the haze.
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

    // The cabins' materials, made once per build.
    sealed class CabinLook
    {
        public Material log, roof, stone, door, window;
    }

    // Every cabin of ValleyShape.Cabins, the first named "Cabin" and the rest numbered.
    static Transform[] BuildCabins()
    {
        var look = new CabinLook
        {
            log = LogMaterial(),
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

    // --- villages (D12, docs/WORLD_LIFE.md) ----------------------------------------------

    // Each village ValleyShape laid out (its cells are already in the map, WireChemistry): a root
    // with the well, and a VillageHouse per building, which builds its own meshes when the scene
    // loads, so only its plan is saved. A box keeps players out of each; chimneys smoke, and the
    // inn and the shop have a lamp by the door. Returns the buildings, which burn like cabins.
    static Transform[] BuildVillages()
    {
        var houses = new List<Transform>();
        if (ValleyShape.Villages.Length == 0) return houses.ToArray();
        var material = VillageMaterial();
        var smoke = EditorAssets.LoadPrefab(PackPaths.SmokeFx + "/SmokeEffect.prefab");
        float cs = ValleyTerrain.Layout.CellSize;
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
            village.enabled = false;
            village.enabled = true;

            // A bench on each side of the square facing the well, beside the middle (where a lane may leave).
            var square = ValleyShape.AreaOf(plan.Square);
            var rng = new System.Random(plan.Spec.Seed);
            foreach (var side in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
            {
                var along = new Vector2(side.y, -side.x);
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

    // What generated things are drawn with: their colours in their vertices (VillageLit).
    static Material VillageMaterial() =>
        EditorAssets.LoadOrCreate($"{Generated}/VillageLit.mat", () => new Material(Shader.Find("Prototype/VillageLit")));

    static Transform BuildHouse(Transform village, VillageBuilding b, string name, float level, float cs, Material material, GameObject smoke)
    {
        var go = new GameObject(name);
        go.transform.SetParent(village, false);
        var mid = ValleyShape.AreaOf(b.Footprint).center;
        var (dx, dy) = Sides.Step(b.Door);
        // Grid y grows south; world z grows north.
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
        house.Rebuild();

        var made = BuildingMesher.Make(house.Spec);
        var block = go.AddComponent<BoxCollider>();
        block.center = new Vector3(0f, made.size.y / 2f, 0f);
        block.size = made.size;
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

    // A small log cabin from primitives: log walls, a pitched roof, a door, a lit window and a
    // smoking chimney. Local axes: x is the cabin's width, z its depth, the door is on the -z
    // side; the ridge runs along x.
    static Transform BuildCabin(ValleyShape.CabinSite site, string name, CabinLook look)
    {
        var root = new GameObject(name).transform;
        // Bottom logs bedded in; local -z (the door) faces the site's way.
        root.SetPositionAndRotation(new Vector3(site.At.x, ValleyShape.HeightAt(site.At) - 0.1f, site.At.y), ValleyShape.CabinRotation(site));

        Material log = look.log, roof = look.roof, stone = look.stone, dark = look.door, glow = look.window;

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
        foreach (var palm in CoastBuilder.PalmSpots()) treeCells.Add(CellOf(palm, cs, left, top, yh));
        var villageCells = VillageCells();

        var rows = new System.Text.StringBuilder(w * h + h);
        channelCells.Clear();
        channelDepths.Clear();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int yx = x - left, yy = y - top;
                bool inYard = yx >= 0 && yx < yw && yy >= 0 && yy < yh;
                // Cell centre on the ground plane (yard cell (0, 0) is at (0, (yh - 1) * cs)).
                var p = new Vector2(yx * cs, (yh - 1 - yy) * cs);
                var sample = SampleOf(layout, x, y);
                char kind = inYard ? yardRows[yy][yx] : OutsideYard(x, y, p, sample, villageCells, treeCells);
                if (kind == 'r') NoteChannel(sample, y * w + x);
                rows.Append(kind);
            }
            if (y < h - 1) rows.Append('\n');
        }

        grid.map = rows.ToString();
        grid.yardCells = new RectInt(left, top, yw, yh);
        grid.fireFadeCells = FireFadeCells;
        grid.conductLimit = LightningReach;
    }

    // The river's squares whose ground is cut below their water (ValleyShape.ChannelDepth) and how deep,
    // as world cell indices, for ValleyChemistry: the water stands at the banks' level, over the channel.
    static readonly List<int> channelCells = new();
    static readonly List<float> channelDepths = new();

    // Where ValleyGround reads a cell's ground: its heightmap sample (the cell's centre on a
    // full-detail chunk, its south-west corner on a coarse one).
    static Vector2 SampleOf(TerrainGrid layout, int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        var (wx, wz) = layout.SamplePosition(i, j, sx, sz);
        return new Vector2(wx, wz);
    }

    // Notes a river square's channel (world cell <paramref name="index"/>), measured at its sample, so
    // the depth ValleyGround adds back is the depth that was cut there.
    static void NoteChannel(Vector2 sample, int index)
    {
        float depth = ValleyShape.ChannelDepth(sample);
        if (depth <= 0f) return;
        channelCells.Add(index);
        channelDepths.Add(depth);
    }

    static Vector2Int CellOf(Vector2 p, float cs, int left, int top, int yh) =>
        new Vector2Int(Mathf.RoundToInt(p.x / cs) + left, top + yh - 1 - Mathf.RoundToInt(p.y / cs));

    // Lake water (where the ground is under the lake's surface: the river's banks, not its channel's
    // bed, so the river runs on at its own level until it meets the lake), the generated land's lakes
    // and rivers (ValleySurroundings, from the world theme), cabin wall, a tree, bare shore, or meadow grass.
    // The river is decided at the cell's heightmap sample, where its channel is measured
    // (NoteChannel), so no dry cell's ground is cut into the channel.
    // Every village's cells (ValleyShape.Villages), by world cell.
    static Dictionary<Vector2Int, char> VillageCells()
    {
        var cells = new Dictionary<Vector2Int, char>();
        foreach (var plan in ValleyShape.Villages)
            foreach (var (x, y, c) in plan.Cells()) cells[new Vector2Int(x, y)] = c;
        return cells;
    }

    // A cell outside the old yard: a village's, else what the valley's shapes make it.
    static char OutsideYard(int x, int y, Vector2 p, Vector2 sample, Dictionary<Vector2Int, char> villageCells, HashSet<Vector2Int> treeCells)
    {
        var cell = new Vector2Int(x, y);
        return villageCells.TryGetValue(cell, out char v) ? v : CellKind(p, sample, treeCells.Contains(cell));
    }

    // The generated land's own lakes and rivers past the valley floor ('\0' for none); the coast
    // decides its own water.
    static char GeneratedWater(Vector2 p)
    {
        bool coast = ValleyCoast.Weight(p) > 0.05f || ValleyCoast.InIsland(p) > -40f;
        return !coast && ValleySurroundings.Mask(p) > 0.5f ? ValleySurroundings.Water(p) : '\0';
    }

    static char CellKind(Vector2 p, Vector2 sample, bool tree)
    {
        float ground = ValleyShape.BankHeightAt(p);
        // Under the lake's or the sea's surface (one level): water.
        if (ground < LakeLevel) return 'w';
        char water = GeneratedWater(p);
        if (water != '\0') return water;
        if (ValleyShape.InRiver(sample)) return 'r';
        if (ValleyShape.InCabin(p)) return 'L';
        // The ball court's floor is dirt: bare earth to look at, and nothing on it to burn.
        if (ValleyShape.InCourt(p, 0.2f)) return '.';
        // Trees (the palms on the sand too), then the path and the beaches: bare ground, nothing to burn.
        if (tree) return 'T';
        if (ValleyCoast.OnPath(p) || ValleyCoast.OnSand(p, ground)) return '.';
        if (ValleyShape.DistanceToLake(p) < 6f && ground < 0.6f) return '.';
        return ',';
    }
}
