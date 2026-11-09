using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Bake Map Data: rewrites the map's ground/ and cells.txt from what it's made of (its stamps, villages, rows,
// puzzles and golf, the valley's shapes), so an edit to one of those reaches the drawn ground (docs/design/maps-as-data.md,
// phase 7). Only the valley has a ground/ worked out from code (ValleyBake); a map without one is drawn from its land
// and needs no bake. The pack records the hash of the files it was baked from (MapInputs), and a core test fails when
// they've changed since. Commit what it writes; on unchanged inputs it writes the same bytes.
public static class MapBake
{
    const string Scene = "Assets/Prototype/Scenes/ChemistryYard.unity";
    const string TemplatePath = "Assets/Prototype/Generated/Ground/Template.asset";

    [MenuItem("Prototype/Bake Map Data")]
    public static void BakeMenu()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log("MAP BAKE: " + Bake());
    }

    /// <summary>Bakes the open map's folder; says what it wrote, or why it didn't.</summary>
    public static string Bake()
    {
        string dir = ChemGrid.Folders.World;
        var map = MapFile.Load(dir);
        if (map.Id != "valley") return $"{map.Id} has no ground worked out from code: it's drawn from its land, so there's nothing to bake";
        var template = AssetDatabase.LoadAssetAtPath<TerrainData>(TemplatePath);
        if (template == null) return "no ground template: run Prototype > Build Chemistry Yard first";
        EditorSceneManager.OpenScene(Scene);
        var grid = Object.FindFirstObjectByType<ChemGrid>();
        var rows = ChemYardBuilder.Map.Split('\n');
        ValleyShape.Plan(rows[0].Length, rows.Length, grid.cellSize);
        var bake = new ValleyBake(EditorGround.Layout, template.terrainLayers);
        try
        {
            var pack = BakeValley(bake, grid, rows, template);
            pack.Inputs = MapInputs.Hash(dir);
            pack.Save(dir);
            return $"{map.Id}: {pack.Chunks.Count} chunks, {pack.Chunks.Sum(c => c.Trees.Length)} trees, {pack.ChannelCells.Length} channel cells, inputs {pack.Inputs}";
        }
        finally
        {
            bake.Dispose();
        }
    }

    // The order the yard build baked in before phase 6: heights, trees, the cells (which know the trees), the cells'
    // own heights and paint, then the pack.
    static GroundPack BakeValley(ValleyBake bake, ChemGrid grid, string[] yardRows, TerrainData template)
    {
        var layout = bake.Layout;
        var trees = template.treePrototypes;
        var valleyTrees = TreeSpots(bake, trees.Length > 0);
        var (left, top) = layout.CellAt(0f, (yardRows.Length - 1) * grid.cellSize);
        var yard = new RectInt(left, top, yardRows[0].Length, yardRows.Length);
        ValleyBuilder.PlanCells(grid.cellSize, left, top, yardRows.Length);
        valleyTrees.RemoveAll(t => ValleyBuilder.OnPlayedGround(t.at, 1));
        var (cells, channelCells, channelDepths) = Cells(layout, grid.cellSize, yardRows, valleyTrees);
        MapFile.WriteCells(ChemGrid.Folders.World, cells);
        var world = ChemGrid.MapWorld(grid.LoadBaseRules(), ChemGrid.CellMap(), grid.seed, out _);
        bake.BakeCells(world, grid.stepHeight);
        // The map's own stamps (stamps.json): no tree on a crag's face or a canyon's floor.
        valleyTrees.RemoveAll(t =>
        {
            var c = ValleyBuilder.CellAt(t.at);
            return world.InBounds(c.x, c.y) && world.IsStampedRock(c.x, c.y);
        });

        // The cells painted as they are: the old yard's, the ball court's dirt floor (CellKind), the paths, the villages'
        // squares, lanes and walls, and what the stamps moved.
        var painted = new List<(int x, int y)>();
        for (int y = yard.yMin; y < yard.yMax; y++)
            for (int x = yard.xMin; x < yard.xMax; x++) painted.Add((x, y));
        painted.AddRange(CellsWhere(layout, p => ValleyShape.InCourt(p, -1f)));
        painted.AddRange(CellsWhere(layout, ValleyCoast.OnPath));
        painted.AddRange(DefenseFieldBuilder.PathCells());
        foreach (var plan in ValleyShape.Villages)
            foreach (var (vx, vy, _) in plan.Cells()) painted.Add((vx, vy));
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
                if (world.LandStampedAt(x, y) != 0) painted.Add((x, y));
        bake.PlantTrees(trees, AllTrees(valleyTrees, grid, yard, world));
        bake.Paint(template.terrainLayers, world, painted);
        bake.PaintDetails(template.detailPrototypes, world, painted);
        return Pack(bake, bake.Bottom(yard.position), channelCells, channelDepths);
    }

    static IEnumerable<(int x, int y)> CellsWhere(TerrainGrid layout, System.Func<Vector2, bool> on)
    {
        foreach (var (x, y) in layout.Cells())
        {
            var (cx, cz) = layout.CellCenter(x, y);
            if (on(new Vector2(cx, cz))) yield return (x, y);
        }
    }

    // --- trees -----------------------------------------------------------------

    // The valley's forest: world points with a scale, a species and a turn each. Open meadow near the yard, thickening
    // toward the valley's edges; past them as thick as the generated land's biome grows it (ValleySurroundings).
    static List<(Vector2 at, float scale, int species, float turn)> TreeSpots(ValleyBake bake, bool any)
    {
        var spots = new List<(Vector2, float, int, float)>();
        if (!any) return spots;
        var layout = bake.Layout;
        float size = layout.ChunkSize * layout.ChunksX;
        var rng = new System.Random(7);
        for (int i = 0; i < 16000 && spots.Count < 3600; i++)
        {
            var w = new Vector2(layout.OriginX + (float)rng.NextDouble() * size, layout.OriginZ + (float)rng.NextDouble() * size);
            if (!TreeFits(bake, w)) continue;
            float d = ValleyShape.DistanceToYard(w), h = ValleyShape.HeightAt(w);
            float chance = Mathf.InverseLerp(14f, 70f, d) * (0.35f + 0.65f * Mathf.InverseLerp(40f, 160f, (w - ValleyShape.Centre).magnitude));
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
        spots.RemoveAll(s => ValleyShape.NearTower(s.Item1, 4f));
        spots.RemoveAll(s => DefenseFieldBuilder.NearDefenseField(s.Item1, 4f));
        return spots;
    }

    static bool TreeFits(ValleyBake bake, Vector2 w) =>
        !ValleyShape.KeepClear(w, 14f) && (w - ValleyBuilder.StonesAt).magnitude > 16f && (w - ValleyBuilder.OutcropAt).magnitude > 14f &&
        ValleyShape.HeightAt(w) <= 150f && bake.Steepness(w) <= 34f;

    // The valley's trees and one smaller conifer on each of the yard map's tree cells.
    static IEnumerable<(Vector2 at, float scale, int species, float turn)> AllTrees(
        List<(Vector2 at, float scale, int species, float turn)> valley, ChemGrid grid, RectInt yard, World world)
    {
        foreach (var t in valley) yield return t;
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

    // --- cells -----------------------------------------------------------------

    // The cell map from the same shapes the ground comes from, the yard's rows in their corner; and the river's cells whose
    // ground is cut below their water (ValleyShape.ChannelDepth) and how deep, as world cell indices: the water stands at
    // the banks' level, over the channel.
    static (string cells, int[] channelCells, float[] channelDepths) Cells(TerrainGrid layout, float cs, string[] yardRows,
        List<(Vector2 at, float scale, int species, float turn)> trees)
    {
        int yw = yardRows[0].Length, yh = yardRows.Length;
        int w = layout.GridW, h = layout.GridH;
        var (left, top) = layout.CellAt(0f, (yh - 1) * cs);
        var treeCells = new HashSet<Vector2Int>();
        foreach (var t in trees) treeCells.Add(ValleyBuilder.CellAt(t.at));
        foreach (var palm in CoastBuilder.PalmSpots()) treeCells.Add(ValleyBuilder.CellAt(palm));
        var villageCells = new Dictionary<Vector2Int, char>();
        foreach (var plan in ValleyShape.Villages)
            foreach (var (vx, vy, c) in plan.Cells()) villageCells[new Vector2Int(vx, vy)] = c;

        var rows = new System.Text.StringBuilder(w * h + h);
        var channelCells = new List<int>();
        var channelDepths = new List<float>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int yx = x - left, yy = y - top;
                bool inYard = yx >= 0 && yx < yw && yy >= 0 && yy < yh;
                // Cell centre on the ground plane (yard cell (0, 0) is at (0, (yh - 1) * cs)).
                var p = new Vector2(yx * cs, (yh - 1 - yy) * cs);
                // Where ValleyGround reads a cell's ground: its heightmap sample (the cell's centre on a full-detail
                // chunk, its south-west corner on a coarse one); the river is decided and its channel measured there.
                var (i, j, sx, sz) = layout.CellSample(x, y);
                var (wx, wz) = layout.SamplePosition(i, j, sx, sz);
                var sample = new Vector2(wx, wz);
                var cell = new Vector2Int(x, y);
                char kind = inYard ? yardRows[yy][yx]
                    : villageCells.TryGetValue(cell, out char v) ? v : CellKind(p, sample, treeCells.Contains(cell));
                if (kind == 'r')
                {
                    float depth = ValleyShape.ChannelDepth(sample);
                    if (depth > 0f)
                    {
                        channelCells.Add(y * w + x);
                        channelDepths.Add(depth);
                    }
                }
                rows.Append(kind);
            }
            if (y < h - 1) rows.Append('\n');
        }
        return (rows.ToString(), channelCells.ToArray(), channelDepths.ToArray());
    }

    // The generated land's own lakes and rivers past the valley floor ('\0' for none); the coast decides its own water.
    static char GeneratedWater(Vector2 p)
    {
        bool coast = ValleyCoast.Weight(p) > 0.05f || ValleyCoast.InIsland(p) > -40f;
        return !coast && ValleySurroundings.Mask(p) > 0.5f ? ValleySurroundings.Water(p) : '\0';
    }

    // Lake water (where the ground is under the lake's surface: the river's banks, not its channel's bed, so the river
    // runs on at its own level until it meets the lake), the generated land's lakes and rivers, cabin wall, a tree, bare
    // shore, or meadow grass.
    static char CellKind(Vector2 p, Vector2 sample, bool tree)
    {
        float ground = ValleyShape.BankHeightAt(p);
        // Under the lake's or the sea's surface (one level): water.
        if (ground < ValleyShape.LakeLevel) return 'w';
        char water = GeneratedWater(p);
        if (water != '\0') return water;
        if (ValleyShape.InRiver(sample)) return 'r';
        if (ValleyShape.OnCabinWindow(p)) return 'P';
        if (ValleyShape.InCabin(p)) return 'L';
        // The ball court's floor is dirt: bare earth to look at, and nothing on it to burn.
        if (ValleyShape.InCourt(p, 0.2f)) return '.';
        if (DefenseFieldBuilder.OnPath(p)) return '.';
        // Trees (the palms on the sand too), then the path and the beaches: bare ground, nothing to burn.
        if (tree) return 'T';
        if (ValleyCoast.OnPath(p) || ValleyCoast.OnSand(p, ground)) return '.';
        if (ValleyShape.DistanceToLake(p) < 6f && ground < 0.6f) return '.';
        return ',';
    }

    // --- the pack --------------------------------------------------------------

    static GroundPack Pack(ValleyBake bake, float bottom, int[] channelCells, float[] channelDepths)
    {
        var first = bake.Chunks[0];
        var pack = new GroundPack
        {
            Bottom = bottom,
            Top = bottom + first.size.y,
            Layers = first.terrainLayers.Select(l => l.name).ToArray(),
            Grasses = first.detailPrototypes.Select(d => (d.prototype != null ? d.prototype : d.prototypeTexture as Object)?.name ?? "").ToArray(),
            TreeKinds = first.treePrototypes.Select(t => t.prefab.name).ToArray(),
            ChannelCells = channelCells,
            ChannelDepths = channelDepths,
        };
        var layout = bake.Layout;
        for (int j = 0; j < layout.ChunksZ; j++)
            for (int i = 0; i < layout.ChunksX; i++) pack.Chunks.Add(ChunkOf(bake.Chunks[j * layout.ChunksX + i], i, j));
        return pack;
    }

    static GroundChunk ChunkOf(TerrainData data, int i, int j)
    {
        int res = data.heightmapResolution, texels = data.alphamapResolution, detail = data.detailResolution;
        var heights = data.GetHeights(0, 0, res, res);
        var alpha = data.GetAlphamaps(0, 0, texels, texels);
        int layers = data.alphamapLayers, grasses = data.detailPrototypes.Length;
        var chunk = new GroundChunk
        {
            I = i,
            J = j,
            Resolution = res,
            Texels = texels,
            DetailResolution = detail,
            Heights = new ushort[res * res],
            Paint = new byte[texels * texels * layers],
            Details = new byte[detail * detail * grasses],
            Trees = data.treeInstances.Select(Tree).ToArray(),
        };
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) chunk.Heights[z * res + x] = (ushort)Mathf.RoundToInt(heights[z, x] * GroundPack.HeightSteps);
        for (int l = 0; l < layers; l++)
            for (int z = 0; z < texels; z++)
                for (int x = 0; x < texels; x++) chunk.Paint[(l * texels + z) * texels + x] = (byte)Mathf.RoundToInt(alpha[z, x, l] * 255f);
        for (int g = 0; g < grasses; g++)
        {
            var clumps = data.GetDetailLayer(0, 0, detail, detail, g);
            for (int z = 0; z < detail; z++)
                for (int x = 0; x < detail; x++) chunk.Details[(g * detail + z) * detail + x] = checked((byte)clumps[z, x]);
        }
        return chunk;
    }

    static GroundTree Tree(TreeInstance t) => new GroundTree
    {
        X = t.position.x,
        Y = t.position.y,
        Z = t.position.z,
        Width = t.widthScale,
        Height = t.heightScale,
        Rotation = t.rotation,
        Color = Packed(t.color),
        LightmapColor = Packed(t.lightmapColor),
        Prototype = t.prototypeIndex,
    };

    static uint Packed(Color32 c) => (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24);
}
