using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// Shows the chemistry of the cells on the valley's chunk terrains (ValleyGround
// does their heights and paint): trees that burn down to charred stumps, burnt
// grass leaving scorched ground with its grass cleared, a cabin or a village
// house (VillageHouse) charring and losing its roof once half its walls have
// burned (or caving in once they're broken), and earth shaping raising or
// sinking the ground under reshaped cells, and rain darkening the ground and making
// it shine (WetGround). Every cell is on the
// terrain, the old yard included; the lake's cells are pits under the lake's
// own surface.
// Works on runtime copies of the terrain data, so playing never changes the
// saved valley.
public partial class ValleyChemistry : MonoBehaviour
{
    public ChemGrid grid;
    [Tooltip("The chunk terrains, made from the map's folder as the scene starts (the scene holds none).")]
    public MapTerrains terrains;
    [Tooltip("The cabins and village buildings; a wall cell belongs to the one whose box holds it, else a log wall to the nearest.")]
    public Transform[] cabins;
    [Tooltip("Height of the lake's surface, the map's water level (MapLand); lake cells' effects sit on it.")]
    public float lakeLevel = -0.6f;

    static readonly Registry<ValleyChemistry> registry = new();

    public static ValleyChemistry InPlay => registry.First;

    void Awake() => registry.Join(this);

    TerrainGrid layout = TerrainGrid.Valley();
    ValleyGround ground;
    WetGround wet;
    LandRocks rocks;

    static LookDef LayerLook(Rules rules, string layer)
    {
        foreach (var (name, table, id) in CellPaint.LayerLooks)
        {
            if (!layer.StartsWith(name, System.StringComparison.Ordinal)) continue;
            if (table == "covers") return rules.Covers.TryGetValue(id, out var cover) ? cover.Look : null;
            return rules.Terrain.TryGetValue(id, out var terrain) ? terrain.Look : null;
        }
        return null;
    }

    public void RecolourGround(Rules was, Rules now)
    {
        wet?.Tint(layer => ChemGrid.TintBetween(LayerLook(was, layer), LayerLook(now, layer)));
        var tint = ChemGrid.TintBetween(LayerLook(was, CellPaint.Grass), LayerLook(now, CellPaint.Grass));
        if (ground == null || (tint == Color.white && grassTint == Color.white)) return;
        grassTint = tint;
        grassToTint.Clear();
        grassQueued.Clear();
        grid.DrawnChunksNearestFirst(drawnOrder);
        foreach (var c in drawnOrder) QueueGrass(c.x, c.y);
    }

    void QueueGrass(int cx, int cy)
    {
        var w = grid.World;
        if (ground == null || w == null) return;
        int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
        int x1 = Mathf.Min(w.W - 1, x0 + Chunks.Size - 1), y1 = Mathf.Min(w.H - 1, y0 + Chunks.Size - 1);
        QueueGrassAt(layout.ChunkOfCell(x0, y0));
        QueueGrassAt(layout.ChunkOfCell(x1, y0));
        QueueGrassAt(layout.ChunkOfCell(x0, y1));
        QueueGrassAt(layout.ChunkOfCell(x1, y1));
    }

    void QueueGrassAt((int i, int j) at)
    {
        if (!ground.HasChunk(at) || !ground.IsReady(at) || TintedAlready(ground.Data(at)) || !grassQueued.Add(at)) return;
        grassToTint.Enqueue(at);
    }

    bool TintedAlready(TerrainData data) => tintedWith.TryGetValue(data, out var was) ? was == grassTint : grassTint == Color.white;

    void TintSomeGrass()
    {
        while (grassToTint.Count > 0)
        {
            var at = grassToTint.Dequeue();
            grassQueued.Remove(at);
            if (!ground.IsReady(at)) continue;
            var data = ground.Data(at);
            if (TintedAlready(data)) continue;
            var kinds = data.detailPrototypes;
            if (!grassColors.TryGetValue(data, out var own)) grassColors[data] = own = System.Array.ConvertAll(kinds, k => (k.healthyColor, k.dryColor));
            for (int i = 0; i < kinds.Length; i++)
            {
                kinds[i].healthyColor = own[i].healthyColor * grassTint;
                kinds[i].dryColor = own[i].dryColor * grassTint;
            }
            data.detailPrototypes = kinds;
            tintedWith[data] = grassTint;
            return;
        }
    }

    readonly Queue<(int i, int j)> grassToTint = new();
    readonly HashSet<(int i, int j)> grassQueued = new();
    readonly List<Vector2Int> drawnOrder = new();
    Color grassTint = Color.white;
    readonly Dictionary<TerrainData, (Color healthyColor, Color dryColor)[]> grassColors = new();
    readonly Dictionary<TerrainData, Color> tintedWith = new();

    public bool Recolouring => grassToTint.Count > 0;

    public void RecolourWater(Color tint)
    {
        if (!lakeSought)
        {
            lakeSought = true;
            var lake = GameObject.Find("Lake");
            lakeRenderer = lake != null && lake.TryGetComponent<Renderer>(out var found) ? found : null;
        }
        var r = lakeRenderer;
        if (r == null) return;
        var own = r.sharedMaterial.color;
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, own * new Color(tint.r, tint.g, tint.b, 1f));
        r.SetPropertyBlock(block);
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    Renderer lakeRenderer;
    bool lakeSought;

    static IEnumerable<TerrainData> DataOf(ValleyGround g)
    {
        foreach (var (_, data) in g.Chunks()) yield return data;
    }
    readonly Dictionary<(int i, int j), TreeInstance[]> trees = new();
    readonly Dictionary<(int i, int j), TreeInstance[]> bakedTrees = new();
    readonly Dictionary<int, List<((int i, int j) chunk, int index)>> treesByCell = new();
    readonly HashSet<(int i, int j)> treesChanged = new();
    readonly HashSet<int> scorched = new();
    readonly Dictionary<int, List<GeneratedProp>> palmsByCell = new();
    readonly HashSet<int> grown = new();
    StartTrees startTrees;
    readonly Dictionary<int, WindowPanes> panesByCell = new();
    int[] cabinCells, cabinBurnt, cabinWrecked;
    bool[] cabinCharred;
    Material charred;

    public bool FlatUnderWalls => grid != null && grid.wallRocks != null && grid.wallRocks.Length > 0 && grid.wallRocks[0] != null;

    public ValleyGround Ground => ground;

    GroundBuilder builder;

    static int Lowest(LandChunks land)
    {
        int least = int.MaxValue;
        var cells = new int[Chunks.Area];
        for (int cy = 0; cy * Chunks.Size < land.H; cy++)
            for (int cx = 0; cx * Chunks.Size < land.W; cx++)
            {
                land.ChunkInto(cx, cy, cells);
                int rows = System.Math.Min(Chunks.Size, land.H - cy * Chunks.Size), cols = System.Math.Min(Chunks.Size, land.W - cx * Chunks.Size);
                for (int ly = 0; ly < rows; ly++)
                    for (int lx = 0; lx < cols; lx++) least = System.Math.Min(least, cells[ly * Chunks.Size + lx]);
            }
        return least;
    }

    GroundBuilder BuiltGround(World world)
    {
        if (terrains.Builder != null) return terrains.Builder;
        var template = terrains.template;
        var land = LandOf(world) ?? LandChunks.Read(File.ReadAllText(Path.Combine(ChemGrid.Folders.World, MapFile.LandName)), world.W, world.H);
        var source = new GroundFromLand.Source
        {
            World = world,
            Land = land,
            Layout = layout,
            StepHeight = grid.stepHeight,
            Unit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep,
            Lowest = land.IsFromSource ? MapLand.Current.Map.Lowest : Lowest(land),
            Layers = template.terrainLayers.Select(l => l.name).ToArray(),
            Grasses = template.detailPrototypes.Select(d => (d.prototype != null ? d.prototype : d.prototypeTexture as Object)?.name ?? "").ToArray(),
            TreeKinds = template.treePrototypes.Select(t => t.prefab.name).ToArray(),
            DetailResolution = template.detailResolution,
            Biomes = BiomeGround.For(ChemGrid.MapSource),
        };
        if (terrains.Lazy) return LazyGround(source, template);
        foreach (var (cx, cy) in world.ChunkCoords) world.At(cx << Chem.Chunks.Shift, cy << Chem.Chunks.Shift);
        var pack = GroundFromLand.Build(source);
        if (GroundSwitch.Mode == GroundSwitch.On)
        {
            int laid = PinnedGround.Lay(pack, layout, ChemGrid.MapSource);
            if (laid > 0) Debug.Log($"PINNED GROUND: {laid} chunks from the pinned map's ground/, the seam eased over {PinnedGround.SeamCells} cells");
        }
        return new GroundBuilder(pack, template, layout.ChunkSize);
    }

    GroundBuilder LazyGround(GroundFromLand.Source source, TerrainData template)
    {
        lazySource = source;
        var (pack, make) = GroundFromLand.Lazy(source);
        IReadOnlyDictionary<(int i, int j), GroundChunk> pinned = null;
        if (GroundSwitch.Mode == GroundSwitch.On)
        {
            int laid;
            (laid, make, pinned) = PinnedGround.LayLazily(pack, layout, ChemGrid.MapSource, make);
            if (laid > 0) Debug.Log($"PINNED GROUND: {laid} chunks from the pinned map's ground/, the seam eased over {PinnedGround.SeamCells} cells, made as they're wanted");
        }
        var shape = layout;
        return new GroundBuilder(pack, template, layout.ChunkSize, make, at => shape.HasChunk(at.i, at.j)) { Pinned = pinned };
    }

    GroundFromLand.Source lazySource;

    public LandRocks Rocks => rocks;

    public float Wetness => wet != null ? wet.Wetness : 0f;

    public void Bind(World world)
    {
        var steps = new StepTimes("valley bind");
        lakeSought = false;
        if (ground == null && MapLand.Current is { } map)
        {
            layout = map.Layout;
            lakeLevel = map.WaterLevel;
        }
        hillsRivers = PinnedValley.InGenerated ? LoadedMaps.Get(ChemGrid.MapSource).Hills?.Rivers : null;
        if (world.W != layout.GridW || world.H != layout.GridH)
            Debug.LogError($"The valley's chunk layout is for a {layout.GridW}×{layout.GridH} world, not {world.W}×{world.H}");
        if (ground == null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            builder = BuiltGround(world);
            steps.Mark("ground_builder");
            if (builder.Lazy) Debug.Log($"GROUND BUILDER {GroundSwitch.Mode}: arrays from the land made a chunk at a time, as the terrains are");
            else if (builder != terrains.Builder) Debug.Log($"GROUND BUILDER {GroundSwitch.Mode}: arrays from the land ready in {timer.Elapsed.TotalMilliseconds:0} ms");
            ground = builder.Lazy ? new ValleyGround(layout, builder) : new ValleyGround(layout, terrains.Chunks, builder, built: builder == terrains.Builder);
            ground.ChunkReady += ChunkMade;
            ground.ChunkGone += ChunkGone;
            steps.Mark("ground_new");
            if (!builder.Lazy && builder != terrains.Builder && Ocean.Instance != null) Ocean.Instance.Build();
            steps.Mark("sea_remeasured");
            rocks = new LandRocks(grid);
            rocks.Lay(ChemGrid.Folders.World);
            grid.DetailDistance = ground.DetailDistance;
            grid.DetailChunksChanged += ground.DrawDetails;
            if (grid.DetailChunks.Count > 0) ground.DrawDetails(grid.DetailChunks);
            steps.Mark("ground_made");
        }
        grid.ChunkFreshened -= BuildingsBack;
        grid.ChunkFreshened += BuildingsBack;
        grid.ChunkDrawn -= QueueGrass;
        grid.ChunkDrawn += QueueGrass;
        if (lazySource != null) lazySource.World = world;
        if (ground.Lazy) ground.Land = LandOf(world);
        binding = true;
        ground.Bind(world, grid.stepHeight, grid.stepHeight / Mathf.Max(0.05f, grid.shapeSeconds), FlatUnderWalls);
        binding = false;
        steps.Mark("ground_bind");
        var (channelCells, channelDepths) = builder.Channel;
        ground.RaiseBase(channelCells, channelDepths);
        steps.Mark("raise_base");
        if (!grid.WorldLoaded) SetLand(world);
        steps.Mark("set_land");
        CheckGround(world);
        steps.Mark("land");
        wet?.Destroy();
        wet = new WetGround(DataOf(ground));
        if (grid.CurrentRules != null) RecolourGround(grid.CurrentRules.Base, grid.CurrentRules.Effective);
        scorched.Clear();
        grown.Clear();
        steps.Mark("wet");
        IndexTrees();
        IndexPalms();
        steps.Mark("trees");
        int n = cabins?.Length ?? 0;
        cabinCells = new int[n];
        cabinBurnt = new int[n];
        cabinWrecked = new int[n];
        cabinCharred = new bool[n];
        BindRoofs(n);
        HookRows(world);
        BindStartTrees(world);
        if (n > 0) CountBuildingWalls(world);
        steps.Mark("cells");
        ClearLaidRows(world);
        steps.Mark("clear_rows");
        BindStampedLand(world);
        steps.Mark("rows_stamps");
        if (charred == null) charred = UrpMaterials.NewLit("Charred", new Color(0.07f, 0.06f, 0.055f), 0.05f);
        panesByCell.Clear();
        foreach (var panes in FindObjectsByType<WindowPanes>(FindObjectsSortMode.None)) panes.Bind(grid, panesByCell);
        if (ground.Lazy && !served)
        {
            served = true;
            terrains.Serve(ground);
            steps.Mark("terrains_served");
            if (Ocean.Instance != null) Ocean.Instance.Build();
        }
        steps.Mark("panes_terrains");
        Debug.Log($"VALLEY BIND steps (ms): {steps}; heightfield chunks worked {ground.HeightfieldChunks}, start trees read {startTrees.Read()}");
    }

    bool served, binding;

    void BindStartTrees(World world)
    {
        startTrees = new StartTrees(world, grid.WorldLoaded);
        grid.ChunkSwitched -= ReadStartTrees;
        grid.ChunkSwitched += ReadStartTrees;
        if (!ground.Lazy)
        {
            foreach (var (cx, cy) in world.ChunkCoords) startTrees.Read(cx, cy);
            return;
        }
        foreach (var (cx, cy) in world.ActiveChunks) startTrees.Read(cx, cy);
    }

    void ReadStartTrees(ChunkSwitch s)
    {
        if (s.Active) startTrees?.Read(s.CX, s.CY);
    }

    void CountBuildingWalls(World world)
    {
        if (!ground.Lazy)
        {
            for (int y = 0; y < world.H; y++)
                for (int x = 0; x < world.W; x++) CountWall(world, x, y);
            return;
        }
        foreach (var (cy, cx) in ChunksNearHouses(world))
        {
            int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
            for (int y = y0; y < Mathf.Min(world.H, y0 + Chunks.Size); y++)
                for (int x = x0; x < Mathf.Min(world.W, x0 + Chunks.Size); x++) CountWall(world, x, y);
        }
    }

    SortedSet<(int cy, int cx)> ChunksNearHouses(World world)
    {
        var near = new SortedSet<(int cy, int cx)>();
        foreach (var cabin in cabins)
        {
            if (cabin == null) continue;
            var c = grid.CellAt(cabin.position);
            for (int y = c.y - HouseReach; y <= c.y + HouseReach; y += Chunks.Size / 2)
                for (int x = c.x - HouseReach; x <= c.x + HouseReach; x += Chunks.Size / 2)
                    if (world.InBounds(x, y)) near.Add((y >> Chunks.Shift, x >> Chunks.Shift));
        }
        return near;
    }

    const int HouseReach = 32;

    void CountWall(World world, int x, int y)
    {
        int key = world.Idx(x, y);
        string t = world.At(x, y).T;
        if (t != TerrainIds.LogWall && t != TerrainIds.Stone) return;
        int k = BuildingWallAt(t, x, y);
        if (k < 0) return;
        cabinCells[k]++;
        buildingCells[k].Add(key);
    }

    void ChunkMade((int i, int j) at, TerrainData data)
    {
        IndexTrees(at, data);
        if (!binding) wet?.Add(data);
        if (grassTint != Color.white) QueueGrassAt(at);
    }

    void ChunkGone((int i, int j) at, TerrainData data)
    {
        trees.Remove(at);
        bakedTrees.Remove(at);
        treesChanged.Remove(at);
        foreach (var (x, y) in layout.CellsIn(at.i, at.j)) treesByCell.Remove(grid.World.Idx(x, y));
        tintedWith.Remove(data);
        grassColors.Remove(data);
    }

    void ClearLaidRows(World world)
    {
        foreach (var row in world.RowsDrawn().Where(r => world.IsBaseBuilding(r.Id)))
        {
            foreach (var c in world.BuildingCellsOf(row))
            {
                BurnTrees(c.X, c.Y);
                if (c.Terrain != TerrainIds.Grass) ground.PaintAnew(c.X, c.Y);
            }
            foreach (var a in WorldBuildings.PadAreas(row).Where(a => a.Clear))
                for (int y = a.Y0 - a.Bank; y <= a.Y1 + a.Bank; y++)
                    for (int x = a.X0 - a.Bank; x <= a.X1 + a.Bank; x++)
                        if (world.InBounds(x, y)) BurnTrees(x, y);
        }
    }

    void BindStampedLand(World world)
    {
        if (!world.LandHasMoved) return;
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
            {
                if (world.LandStampedAt(x, y) == 0) continue;
                ground.Repaint(x, y);
                if (world.At(x, y).T != TerrainIds.Tree && (world.IsStampedRock(x, y) || world.IsStampedBare(x, y))) BurnTrees(x, y);
            }
        foreach (var s in world.MapStampsLaid) rocks?.Moved(s.X, s.Y, s.Bound);
        foreach (var (x, y) in world.LandCragCells()) rocks?.Moved(x, y, 2);
    }

    void IndexTrees()
    {
        trees.Clear();
        bakedTrees.Clear();
        treesByCell.Clear();
        treesChanged.Clear();
        foreach (var (at, data) in ground.Chunks()) IndexTrees(at, data);
    }

    void IndexTrees((int i, int j) at, TerrainData data)
    {
        var list = data.treeInstances;
        trees[at] = list;
        bakedTrees[at] = (TreeInstance[])list.Clone();
        var (cx, cz) = layout.ChunkCorner(at.i, at.j);
        for (int n = 0; n < list.Length; n++)
        {
            var p = list[n].position;
            var (x, y) = layout.CellAt(cx + p.x * data.size.x, cz + p.z * data.size.z);
            if (!layout.InGrid(x, y)) continue;
            int key = grid.World.Idx(x, y);
            if (!treesByCell.TryGetValue(key, out var mine)) treesByCell[key] = mine = new();
            mine.Add((at, n));
        }
    }

    public bool TrunkAt(int x, int y, out Vector3 at)
    {
        at = default;
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) || list.Count == 0) return false;
        var (chunk, n) = list[0];
        var p = trees[chunk][n].position;
        var size = ground.Data(chunk).size;
        var (cx, cz) = layout.ChunkCorner(chunk.i, chunk.j);
        at = new Vector3(cx + p.x * size.x, 0f, cz + p.z * size.z);
        return true;
    }

    public bool FelledTree(int x, int y, out GameObject prefab, out Vector3 scale)
    {
        prefab = null;
        scale = Vector3.one;
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) || list.Count == 0) return false;
        var (chunk, n) = list[0];
        var t = bakedTrees[chunk][n];
        var protos = ground.Data(chunk).treePrototypes;
        if (t.prototypeIndex < 0 || t.prototypeIndex >= protos.Length) return false;
        prefab = protos[t.prototypeIndex].prefab;
        scale = new Vector3(t.widthScale, t.heightScale, t.widthScale);
        return prefab != null;
    }

    public float BaseHeight(int x, int y) => ground != null ? ground.BaseHeight(x, y) : 0f;

    public float ShownGround(int x, int y) => ground != null ? ground.ShownGround(x, y) : 0f;

    public float GroundAt(Vector3 p) => ground != null ? ground.GroundAt(p.x, p.z) : 0f;

    public bool GroundMoving => ground != null && ground.Shaping;

    public void AdvanceShaping(float dt) => ground?.Advance(dt);

    public bool IsLake(Cell c, int x, int y) => LakeLevelOf(c, x, y) > float.NegativeInfinity;

    public float LakeLevelOf(Cell c, int x, int y)
    {
        if (c.T != TerrainIds.Pit) return float.NegativeInfinity;
        if (hillsRivers != null)
        {
            int level = hillsRivers.LakeNear(grid.World.FoldX(x), grid.World.FoldY(y));
            return level == HillsRivers.NoLake ? float.NegativeInfinity : level * (float)MapFile.LandUnitMetres;
        }
        return BaseHeight(x, y) < lakeLevel ? lakeLevel : float.NegativeInfinity;
    }

    HillsRivers hillsRivers;

    static LandChunks landFromFile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLand()
    {
        landFromFile = null;
    }

    public static void ForgetLand() => landFromFile = null;

    void CheckGround(World world)
    {
        if (landFromFile == null || world.Source is IGeneratedSource) return;
        float unit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        var apart = LandCheck.Disagreements((x, y) => ground.BaseHeight(x, y) - world.LandMovedAt(x, y) * unit, landFromFile, unit, unit);
        if (apart.Count > 0) Debug.LogError($"The drawn ground isn't this map's land on {apart.Count} of {LandCheck.Cells} cells: {string.Join("; ", apart.Take(4))}");
    }

    static void SetLand(World world)
    {
        if (world.Source is IGeneratedSource) return;
        var land = LandOf(world);
        if (land == null) world.SetLand((int[])null);
        else world.SetLandByChunk(land);
    }

    public static LandChunks LandOf(World world)
    {
        if (landFromFile == null || landFromFile.W != world.W || landFromFile.H != world.H)
        {
            string path = Path.Combine(ChemGrid.Folders.World, "land.json");
            var map = MapLand.Current;
            landFromFile = map != null && (map.FromFile || map.Land.IsFromSource) && map.Land.W == world.W && map.Land.H == world.H ? map.Land
                : File.Exists(path) ? LandChunks.Read(File.ReadAllText(path), world.W, world.H) : null;
            if (landFromFile == null) Debug.LogWarning($"No land for a {world.W}×{world.H} world in {path}: water won't run down the hills");
        }
        return landFromFile;
    }

    public string BakeLand()
    {
        var world = grid.World;
        float unit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        var land = new int[world.W * world.H];
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
                land[world.Idx(x, y)] = Mathf.RoundToInt(ground.BaseHeight(x, y) / unit);
        string path = Path.Combine(ChemGrid.Folders.World, "land.json");
        File.WriteAllText(path, LandFile.Write(land, world.W, world.H, unit));
        landFromFile = null;
        MapLand.Forget();
        SetLand(world);
        return $"wrote {path} ({new FileInfo(path).Length / 1024} KB)";
    }

    public static float FireHeight(string terrainType) => terrainType switch
    {
        TerrainIds.Tree => 2.6f,
        TerrainIds.LogWall => 1.4f,
        TerrainIds.Deadfall => 0.5f,
        _ => 0f,
    };

    public void LandMoved(int x, int y, int reach)
    {
        if (ground == null) return;
        for (int v = y - reach - 1; v <= y + reach + 1; v++)
            for (int u = x - reach - 1; u <= x + reach + 1; u++)
                if (grid.World.InBounds(u, v))
                {
                    ground.Reshape(u, v);
                    ground.Repaint(u, v);
                }
        rocks?.Moved(x, y, reach + 1);
    }

    public void CellChanged(int x, int y, string from, string to, bool reshaped)
    {
        if (ground == null) return;
        if (from == null && (to == TerrainIds.Deadfall || to == TerrainIds.Stump)) BurnTrees(x, y);
        if (from == null) return;
        if (from != to)
        {
            TerrainChanged(x, y, from, to);
            PutBack(x, y, from, to);
        }
        if (reshaped) ground.Reshape(x, y);
        ground.Repaint(x, y);
    }

    void TerrainChanged(int x, int y, string from, string to)
    {
        int key = grid.World.Idx(x, y);
        if (from == TerrainIds.Tree)
        {
            BurnTrees(x, y);
            grown.Remove(key);
        }
        if ((from == TerrainIds.Grass || from == TerrainIds.Brush) && to == TerrainIds.Dirt) scorched.Add(key);
        if (to == TerrainIds.Sprouts || to == TerrainIds.Grass || to == TerrainIds.Sapling || to == TerrainIds.Sand) scorched.Remove(key);
        if (from == TerrainIds.Sapling && to == TerrainIds.Tree) grown.Add(key);
        var now = grid.World.At(x, y);
        if (from == TerrainIds.Pane && panesByCell.TryGetValue(key, out var pane) && pane != null) pane.Shatter(key);
        if (from == TerrainIds.LogWall || from == TerrainIds.Stone) CabinWallLost(BuildingWallAt(from, x, y), burning: now.Fire > 0 || now.Cover == CoverIds.Ash);
    }

    public void BindPanes(WindowPanes panes) => panes.Bind(grid, panesByCell);

    public void UnbindPanes(WindowPanes panes) => panes.Unbind(panesByCell);

    public void PaneCracked(int key, bool cracked)
    {
        if (panesByCell.TryGetValue(key, out var pane) && pane != null) pane.Cracked(key, cracked);
    }

    void IndexPalms()
    {
        palmsByCell.Clear();
        foreach (var prop in FindObjectsByType<GeneratedProp>(FindObjectsSortMode.None))
        {
            if (prop.kind != GeneratedProp.Kind.Palm) continue;
            var cell = grid.CellAt(prop.transform.position);
            if (!layout.InGrid(cell.x, cell.y)) continue;
            int key = grid.World.Idx(cell.x, cell.y);
            if (!palmsByCell.TryGetValue(key, out var list)) palmsByCell[key] = list = new();
            list.Add(prop);
        }
    }

    void BurnTrees(int x, int y)
    {
        if (palmsByCell.TryGetValue(grid.World.Idx(x, y), out var palms))
            foreach (var palm in palms) palm.Burn(charred);
        if (!ground.WhenReady(layout.ChunkOfCell(x, y), () => BurnTreesOnly(x, y))) return;
        BurnTreesOnly(x, y);
    }

    void BurnTreesOnly(int x, int y)
    {
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list)) return;
        foreach (var (at, n) in list)
        {
            var t = trees[at][n];
            t.heightScale = 0f;
            t.widthScale = 0f;
            trees[at][n] = t;
            treesChanged.Add(at);
            ground.Touch(at);
        }
    }

    void PutBack(int x, int y, string from, string to)
    {
        int key = grid.World.Idx(x, y);
        if (to == TerrainIds.Tree && from != TerrainIds.Sapling) StandTrees(x, y);
        if (to == TerrainIds.Pane && panesByCell.TryGetValue(key, out var mended) && mended != null) mended.Mend(key);
    }

    void StandTrees(int x, int y)
    {
        if (palmsByCell.TryGetValue(grid.World.Idx(x, y), out var palms))
            foreach (var palm in palms) palm.Regrow();
        if (!ground.WhenReady(layout.ChunkOfCell(x, y), () => StandTreesOnly(x, y))) return;
        StandTreesOnly(x, y);
    }

    void StandTreesOnly(int x, int y)
    {
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list)) return;
        foreach (var (at, n) in list)
        {
            trees[at][n] = bakedTrees[at][n];
            treesChanged.Add(at);
            ground.Touch(at);
        }
    }

    public bool TreeStands(int x, int y) =>
        treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) && list.Count > 0 && trees[list[0].Item1][list[0].Item2].heightScale > 0f;

    int BuildingWallAt(string terrain, int x, int y)
    {
        if (terrain != TerrainIds.LogWall && terrain != TerrainIds.Stone) return -1;
        if (rowCells.Contains(grid.World.Idx(x, y))) return -1;
        int inside = CabinAround(x, y);
        if (inside >= 0 || terrain == TerrainIds.Stone) return inside;
        return CabinAt(x, y);
    }

    readonly HashSet<int> rowCells = new();
    World hookedRows;

    void HookRows(World world)
    {
        IndexRowCells(world);
        if (hookedRows == world) return;
        if (hookedRows != null) hookedRows.BuildingsChanged -= RowsChanged;
        hookedRows = world;
        world.BuildingsChanged += RowsChanged;
    }

    void IndexRowCells(World world)
    {
        rowCells.Clear();
        foreach (var row in world.RowsDrawn())
            foreach (var c in world.BuildingCellsOf(row)) rowCells.Add(world.Idx(c.X, c.Y));
    }

    void RowsChanged(BuildingsChange change)
    {
        if (hookedRows != null) IndexRowCells(hookedRows);
    }

    int CabinAround(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        for (int i = 0; i < (cabins?.Length ?? 0); i++)
        {
            if (cabins[i] == null || !cabins[i].TryGetComponent<BoxCollider>(out var box)) continue;
            var q = cabins[i].InverseTransformPoint(at) - box.center;
            if (Mathf.Abs(q.x) <= box.size.x / 2f + 0.3f && Mathf.Abs(q.z) <= box.size.z / 2f + 0.3f) return i;
        }
        return -1;
    }

    static string LabelOf(Transform building) =>
        building.TryGetComponent<VillageHouse>(out var house) ? house.Label : "cabin";

    int CabinAt(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < (cabins?.Length ?? 0); i++)
        {
            if (cabins[i] == null) continue;
            var d = cabins[i].position - at;
            d.y = 0f;
            if (d.sqrMagnitude >= bestD) continue;
            bestD = d.sqrMagnitude;
            best = i;
        }
        return best;
    }

    void CabinWallLost(int k, bool burning)
    {
        if (k < 0) return;
        KeepBuilding(k);
        if (burning) cabinBurnt[k]++;
        else cabinWrecked[k]++;
        if (cabinCharred[k] || (cabinBurnt[k] + cabinWrecked[k]) * 2 < cabinCells[k]) return;
        cabinCharred[k] = true;
        var cabin = cabins[k];
        if (cabinWrecked[k] > cabinBurnt[k])
        {
            CaveIn(cabin);
            return;
        }
        foreach (var r in cabin.GetComponentsInChildren<Renderer>())
            if (r.name != "Roof" && r.name != "Panes" && r.GetComponent<ParticleSystem>() == null) r.sharedMaterial = charred;
        foreach (var l in cabin.GetComponentsInChildren<Light>()) l.enabled = false;
        EventLog.Add($"The {LabelOf(cabin)} burns down");
        WallsBurnDown(k);
    }

    static void CaveIn(Transform cabin)
    {
        foreach (var r in cabin.GetComponentsInChildren<Renderer>())
            if (r.name == "Roof" || r.GetComponent<ParticleSystem>() != null) r.enabled = false;
        foreach (var l in cabin.GetComponentsInChildren<Light>()) l.enabled = false;
        cabin.SetPositionAndRotation(cabin.position + Vector3.down * 1.3f, cabin.rotation * Quaternion.Euler(4f, 0f, -3f));
        EventLog.Add($"The {LabelOf(cabin)} caves in");
    }

    bool IsScorched(int x, int y) => scorched.Contains(grid.World.Idx(x, y));

    public bool Grown(int x, int y) => grown.Contains(grid.World.Idx(x, y));

    public bool NeedsTreeDrawn(int x, int y)
    {
        int key = grid.World.Idx(x, y);
        return grown.Contains(key) || (startTrees != null && !startTrees.At(x, y) && !TreeStands(x, y));
    }

    public bool TreeModel(int x, int y, out GameObject prefab, out Vector3 scale)
    {
        if (FelledTree(x, y, out prefab, out scale)) return true;
        scale = Vector3.one;
        var at = layout.ChunkOfCell(x, y);
        if (!ground.HasChunk(at)) return false;
        var protos = ground.TreePrototypes(at);
        if (protos.Length == 0) return false;
        prefab = protos[(int)((uint)(x * 73856093 ^ y * 19349663) % (uint)protos.Length)].prefab;
        return prefab != null;
    }

    public bool TreeModel(int x, int y, string kind, out GameObject prefab, out Vector3 scale)
    {
        scale = Vector3.one;
        prefab = null;
        var at = layout.ChunkOfCell(x, y);
        if (!string.IsNullOrEmpty(kind) && ground.HasChunk(at))
            prefab = ground.TreePrototypes(at).Select(p => p.prefab).FirstOrDefault(p => p != null && p.name == kind);
        return prefab != null || TreeModel(x, y, out prefab, out scale);
    }

    public IEnumerable<GameObject> TreeKinds(int x, int y)
    {
        var at = layout.ChunkOfCell(x, y);
        return ground.HasChunk(at) ? ground.TreePrototypes(at).Select(p => p.prefab).Where(p => p != null) : Enumerable.Empty<GameObject>();
    }

    readonly System.Diagnostics.Stopwatch timer = new();

    public double TreesMs { get; private set; }
    public double GroundMs { get; private set; }

    void LateUpdate()
    {
        if (ground == null) return;
        rocks.Tick(Time.deltaTime);
        timer.Restart();
        foreach (var at in treesChanged)
        {
            ground.WritableData(at).treeInstances = trees[at];
            ground.TreesMoved(at);
        }
        treesChanged.Clear();
        TreesMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        ground.Flush(IsScorched);
        WatchRoofs(Time.deltaTime);
        if (wet != null && grid.World != null) wet.Advance(grid.World.Weather.Rain, Time.deltaTime);
        TintSomeGrass();
        GroundMs = timer.Elapsed.TotalMilliseconds;
    }

    void OnDestroy()
    {
        registry.Leave(this);
        if (grid != null && ground != null) grid.DetailChunksChanged -= ground.DrawDetails;
        if (grid != null) grid.ChunkFreshened -= BuildingsBack;
        if (grid != null) grid.ChunkDrawn -= QueueGrass;
        if (hookedRows != null) hookedRows.BuildingsChanged -= RowsChanged;
        if (charred != null) Destroy(charred);
        wet?.Destroy();
        ground?.Destroy();
    }
}
