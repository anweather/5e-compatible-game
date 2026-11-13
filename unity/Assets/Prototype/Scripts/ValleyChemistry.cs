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

    TerrainGrid layout = TerrainGrid.Valley();
    ValleyGround ground;
    WetGround wet;
    LandRocks rocks;

    // A layer's colour in the rules, from what it's made of.
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

    /// <summary>The rules switched: the land's layers take the colours of what they're made of, tinted from their art.</summary>
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

    // The terrain chunks under a drawn chunk of cells (cx, cy) wait for the grass's tint, if they don't have it. Only drawn
    // chunks, nearest a focus first; a chunk drawn later joins the queue as it draws (ChemGrid.ChunkDrawn), so the chunk a
    // player stands in never waits behind the whole map (#200, J-086).
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

    // A chunk's grass a frame: every chunk's at once was a frame of 300 ms, and tufts a moment late are only tufts.
    void TintSomeGrass()
    {
        while (grassToTint.Count > 0)
        {
            var at = grassToTint.Dequeue();
            grassQueued.Remove(at);
            // A generated map's chunk whose terrain went meanwhile takes the tint when it's made again (ChunkMade).
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
    // The tint each chunk's grass wears now (none: white, as built).
    readonly Dictionary<TerrainData, Color> tintedWith = new();

    /// <summary>Whether a rule switch's new colours are still going onto the drawn land.</summary>
    public bool Recolouring => grassToTint.Count > 0;

    /// <summary>The rules switched: the lake's colour, tinted from its art.</summary>
    public void RecolourWater(Color tint)
    {
        // Looked for once a world, not at every switch: GameObject.Find walks the whole scene, 20-50 ms on the nine,
        // which has no lake (#181).
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
    // The trees as the valley was baked, untouched by burning or felling: what a fallen tree was.
    readonly Dictionary<(int i, int j), TreeInstance[]> bakedTrees = new();
    readonly Dictionary<int, List<((int i, int j) chunk, int index)>> treesByCell = new();
    readonly HashSet<(int i, int j)> treesChanged = new();
    readonly HashSet<int> scorched = new();
    // The coast's palms (GeneratedProp) by the square they stand on: they burn with it.
    readonly Dictionary<int, List<GeneratedProp>> palmsByCell = new();
    // Cells whose tree grew from a sapling in this world (Grown).
    readonly HashSet<int> grown = new();
    // The tree cells of the world as it started, a block a chunk (StartTrees): the land's, drawn by the terrain (or, for a
    // few, not at all).
    StartTrees startTrees;
    // Every window pane by its cell (a world index).
    readonly Dictionary<int, WindowPanes> panesByCell = new();
    int[] cabinCells, cabinBurnt, cabinWrecked;
    bool[] cabinCharred;
    Material charred;

    /// <summary>
    /// Whether the terrain stays flat under raised walls, because the grid builds them of rock
    /// models (ChemGrid.wallRocks, CellDressing) that the terrain's own wall would bury.
    /// </summary>
    public bool FlatUnderWalls => grid != null && grid.wallRocks != null && grid.wallRocks.Length > 0 && grid.wallRocks[0] != null;

    /// <summary>The ground under the cells; null before the first Bind.</summary>
    public ValleyGround Ground => ground;

    GroundBuilder builder;

    // The map's lowest land, a chunk at a time (the land is held a chunk at a time, never as an int a cell). Each chunk is
    // decoded once: a row at a time through At decoded each chunk 64 times, 9 s on the Scale Island.
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

    // The builder the terrains were made with (the map's ground/), or in land mode, or for a map with none, one from the
    // map's land and this world's cells, with any pinned map's own ground laid in (PinnedGround).
    GroundBuilder BuiltGround(World world)
    {
        if (terrains.Builder != null) return terrains.Builder;
        var template = terrains.template;
        // The land the world reads too (LandOf, read once a map): a second copy was an int a cell more (#207).
        var land = LandOf(world) ?? LandChunks.Read(File.ReadAllText(Path.Combine(ChemGrid.Folders.World, MapFile.LandName)), world.W, world.H);
        var source = new GroundFromLand.Source
        {
            World = world,
            Land = land,
            Layout = layout,
            StepHeight = grid.stepHeight,
            Unit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep,
            Lowest = Lowest(land),
            Layers = template.terrainLayers.Select(l => l.name).ToArray(),
            Grasses = template.detailPrototypes.Select(d => (d.prototype != null ? d.prototype : d.prototypeTexture as Object)?.name ?? "").ToArray(),
            TreeKinds = template.treePrototypes.Select(t => t.prefab.name).ToArray(),
            DetailResolution = template.detailResolution,
            Biomes = BiomeGround.For(ChemGrid.MapSource),
        };
        if (terrains.Lazy) return LazyGround(source, template);
        // Build reads every chunk's cells side by side, and a read of a packed chunk unpacks it (D17), which is never safe
        // off the main thread: so every chunk is unpacked here first. Only a map built whole comes here (the valley's size);
        // those not needed pack again in the world's own time.
        foreach (var (cx, cy) in world.ChunkCoords) world.At(cx << Chem.Chunks.Shift, cy << Chem.Chunks.Shift);
        var pack = GroundFromLand.Build(source);
        // A generated map's pinned squares (the nine's valley) draw their own map's shaped ground, as that map does.
        if (GroundSwitch.Mode == GroundSwitch.On)
        {
            int laid = PinnedGround.Lay(pack, layout, ChemGrid.MapSource);
            if (laid > 0) Debug.Log($"PINNED GROUND: {laid} chunks from the pinned map's ground/, the seam eased over {PinnedGround.SeamCells} cells");
        }
        return new GroundBuilder(pack, template, layout.ChunkSize);
    }

    // A generated map's ground made a chunk at a time, as its terrains are (#205): the same arrays GroundFromLand.Build and
    // PinnedGround.Lay make, chunk by chunk, from the world the source holds when asked (each Bind sets it).
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

    /// <summary>The rocks on stamped land; null until the ground is bound.</summary>
    public LandRocks Rocks => rocks;

    /// <summary>How wet the ground looks, 0 dry to 1 soaked (WetGround).</summary>
    public float Wetness => wet != null ? wet.Wetness : 0f;

    /// <summary>Starts over on a world (ChemGrid calls this for every new world): fresh terrain copies, trees and cabin counted.</summary>
    public void Bind(World world)
    {
        var steps = new StepTimes("valley bind");
        lakeSought = false; // a new world: the lake (or none) is looked for again at the next recolour
        if (ground == null && MapLand.Current is { } map)
        {
            layout = map.Layout;
            lakeLevel = map.WaterLevel;
        }
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
            // The chunks waited blank for the land (no ground/, or land mode): the sea measures its depth again on them
            // (once the drawn tier's are made, on a generated map).
            if (!builder.Lazy && builder != terrains.Builder && Ocean.Instance != null) Ocean.Instance.Build();
            steps.Mark("sea_remeasured");
            rocks = new LandRocks(grid);
            rocks.Lay(ChemGrid.Folders.World);
            // Details only on the terrains near the camera (ChemGrid.Detail).
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
        // The river's water stands at its banks' level, not on its deep channel's bed: its cells'
        // ground counts from there (the terrain is still drawn cut, so the wizard can swim in it).
        var (channelCells, channelDepths) = builder.Channel;
        ground.RaiseBase(channelCells, channelDepths);
        steps.Mark("raise_base");
        SetLand(world);
        steps.Mark("set_land");
        CheckGround(world);
        steps.Mark("land");
        // Rain wets the ground (WetGround): its own copies of the new terrain copies' layers.
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
        // A generated map's first world: the drawn tier's terrains, made now (the rest as the characters move), and the sea
        // measured on them.
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

    // The world's starting trees, a chunk at a time (#206, J-086): every cell of the map at the start was a loop over 3
    // million cells on the nine. A chunk is read when it first activates, or when a tree's drawing first asks about it;
    // until then nothing has changed it (an inactive chunk is held as it is), so it's read as it started. The valley,
    // drawn whole from the start, reads all of its chunks now.
    void BindStartTrees(World world)
    {
        startTrees = new StartTrees(world);
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

    // The walls of the scene's cabins and houses: on the valley a log wall anywhere goes to the nearest. On a generated map
    // (the nine's valley in its middle) only the chunks round the houses are looked at, never every cell of the map (#206):
    // a wall further than that is a generated village's, none of the scene's.
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

    // The chunks within HouseReach of a scene house, each once, by row.
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

    // How far round a scene house its walls are looked for on a generated map, in cells.
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

    // A generated map's chunk made for this world (#205): its trees indexed, its layers wet, its grass tinted as the rest.
    void ChunkMade((int i, int j) at, TerrainData data)
    {
        IndexTrees(at, data);
        // A chunk made as a new world binds is wet with the rest, by the new world's WetGround.
        if (!binding) wet?.Add(data);
        if (grassTint != Color.white) QueueGrassAt(at);
    }

    // A generated map's chunk nothing changed gave its data back: what was kept by it goes.
    void ChunkGone((int i, int j) at, TerrainData data)
    {
        trees.Remove(at);
        bakedTrees.Remove(at);
        treesChanged.Remove(at);
        foreach (var (x, y) in layout.CellsIn(at.i, at.j)) treesByCell.Remove(grid.World.Idx(x, y));
        tintedWith.Remove(data);
        grassColors.Remove(data);
    }

    // A row laid over the cells at the start (WorldBuildings.LayInto) stands where the bake drew trees and grass: they go,
    // and its ground is painted as its cells say.
    void ClearLaidRows(World world)
    {
        foreach (var row in world.RowsDrawn().Where(r => world.IsBaseBuilding(r.Id)))
        {
            foreach (var c in world.BuildingCellsOf(row))
            {
                BurnTrees(c.X, c.Y);
                // A clearing keeps the ground the bake drew, grass and all: only what's laid on it is painted.
                if (c.Terrain != TerrainIds.Grass) ground.PaintAnew(c.X, c.Y);
            }
            // The open ground its pad reaches over (WorldBuildings.PadAreas), as far as its bank's foot.
            foreach (var a in WorldBuildings.PadAreas(row).Where(a => a.Clear))
                for (int y = a.Y0 - a.Bank; y <= a.Y1 + a.Bank; y++)
                    for (int x = a.X0 - a.Bank; x <= a.X1 + a.Bank; x++)
                        if (world.InBounds(x, y)) BurnTrees(x, y);
        }
    }

    // Land stamped before the first tick (the map's own stamps, World.LayMapStamps, or a save's): painted as it stands, a
    // crag's face or a deep floor whose cell isn't a tree has no tree standing on it, and the rocks dress it.
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
        // A pad's crag (World.LayCrag): its face, its lip and its foot.
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

    /// <summary>Where the trunk of a tree drawn in a cell stands (the first, if there are several); false when none is.</summary>
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

    /// <summary>
    /// The tree that stood in a cell, for drawing it fallen (CellDressing): its model and its
    /// scale; false when the valley drew none there.
    /// </summary>
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

    /// <summary>The valley's own ground under a cell, before the cell's offset.</summary>
    public float BaseHeight(int x, int y) => ground != null ? ground.BaseHeight(x, y) : 0f;

    /// <summary>The ground drawn at a cell's centre now (it lags its target while easing).</summary>
    public float ShownGround(int x, int y) => ground != null ? ground.ShownGround(x, y) : 0f;

    /// <summary>The terrain's ground drawn now at a world point over the cells.</summary>
    public float GroundAt(Vector3 p) => ground != null ? ground.GroundAt(p.x, p.z) : 0f;

    /// <summary>Whether the drawn ground is still easing somewhere.</summary>
    public bool GroundMoving => ground != null && ground.Shaping;

    /// <summary>Eases reshaped ground by dt seconds (ChemGrid calls this once a frame).</summary>
    public void AdvanceShaping(float dt) => ground?.Advance(dt);

    /// <summary>True for a cell of the lake: a pit whose ground is under the lake's surface.</summary>
    public bool IsLake(Cell c, int x, int y) => c.T == TerrainIds.Pit && BaseHeight(x, y) < lakeLevel;

    /// <summary>How high above the ground a cell's fire burns: in the crown of a tree, along a cabin wall.</summary>
    // The water runs down the valley's hills (playtest #31), which only the baked ground holds. The
    // land comes from world/land.json, integers baked once from this terrain and committed, so every
    // peer on every platform has the same (BakeLand writes it); without it the land is flat.
    static LandChunks landFromFile;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLand()
    {
        landFromFile = null;
    }

    /// <summary>The map changed: the next world reads its land afresh.</summary>
    public static void ForgetLand() => landFromFile = null;

    // The ground drawn and the land the core reads are one ground: a few cells' worth, as a probe can't see land.
    void CheckGround(World world)
    {
        if (landFromFile == null) return;
        float unit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        // The map's own land: a loaded world's stamps are on the ground, not in the file.
        var apart = LandCheck.Disagreements((x, y) => ground.BaseHeight(x, y) - world.LandMovedAt(x, y) * unit, landFromFile, unit, unit);
        if (apart.Count > 0) Debug.LogError($"The drawn ground isn't this map's land on {apart.Count} of {LandCheck.Cells} cells: {string.Join("; ", apart.Take(4))}");
    }

    static void SetLand(World world)
    {
        var land = LandOf(world);
        if (land == null) world.SetLand((int[])null);
        else world.SetLandByChunk(land);
    }

    /// <summary>
    /// The map's land.json, read once a map, or null when it has none: the one copy the view holds (MapLand's, which the
    /// sea and the shore read too), shared by the ground, the land check and the folk's walks. Three copies were an int a
    /// cell each, 477 MB on the 12 x 10 (J-086). Never written to: the World takes its own (SetLand).
    /// </summary>
    public static LandChunks LandOf(World world)
    {
        if (landFromFile == null || landFromFile.W != world.W || landFromFile.H != world.H)
        {
            string path = Path.Combine(ChemGrid.Folders.World, "land.json");
            var map = MapLand.Current;
            landFromFile = map != null && map.FromFile && map.Land.W == world.W && map.Land.H == world.H ? map.Land
                : File.Exists(path) ? LandChunks.Read(File.ReadAllText(path), world.W, world.H) : null;
            if (landFromFile == null) Debug.LogWarning($"No land for a {world.W}×{world.H} world in {path}: water won't run down the hills");
        }
        return landFromFile;
    }

    /// <summary>
    /// Writes world/land.json from the terrain as drawn now (in Play mode, once the valley is bound):
    /// each cell's baked ground in whole water units. Run after the valley's land changes
    /// (Prototype > Bake Valley Land) and commit the file.
    /// </summary>
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

    /// <summary>
    /// A cell's terrain, height or cover changed (from is its terrain before, null for its first
    /// look, which the baked valley already shows): burn its trees, char the cabin, repaint it
    /// and, if it was <paramref name="reshaped"/>, ease its ground to its new height.
    /// </summary>
    /// <summary>A land stamp moved these cells: they ease to it as a dug cell does.</summary>
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
        // First look at a felled or burnt tree (a world replayed for a late joiner): the baked tree goes.
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
        // Plant Growth: green ground again, and a sapling grown into a tree is drawn by its dressing (Grown).
        if (to == TerrainIds.Sprouts || to == TerrainIds.Grass || to == TerrainIds.Sapling || to == TerrainIds.Sand) scorched.Remove(key);
        if (from == TerrainIds.Sapling && to == TerrainIds.Tree) grown.Add(key);
        // A wall that burned out is no longer alight (the core puts the fire out as it turns to rubble),
        // but it leaves ash; one that caved in under earth shaping doesn't.
        var now = grid.World.At(x, y);
        if (from == TerrainIds.Pane && panesByCell.TryGetValue(key, out var pane)) pane.Shatter(key);
        if (from == TerrainIds.LogWall || from == TerrainIds.Stone) CabinWallLost(BuildingWallAt(from, x, y), burning: now.Fire > 0 || now.Cover == CoverIds.Ash);
    }

    /// <summary>A row house's panes came or went (RowHouses): their cells shatter them, as a village's do.</summary>
    public void BindPanes(WindowPanes panes) => panes.Bind(grid, panesByCell);

    /// <summary>A row house's panes are gone.</summary>
    public void UnbindPanes(WindowPanes panes) => panes.Unbind(panesByCell);

    /// <summary>A cell was redrawn: a pane on it shows its crack, or not.</summary>
    public void PaneCracked(int key, bool cracked)
    {
        if (panesByCell.TryGetValue(key, out var pane)) pane.Cracked(key, cracked);
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
        // A generated map's chunk not made yet: its trees burn as it's made.
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

    // Straight back to a tree (not grown from a sapling) or a pane: the dragon's breath put the cell as it was.
    void PutBack(int x, int y, string from, string to)
    {
        int key = grid.World.Idx(x, y);
        if (to == TerrainIds.Tree && from != TerrainIds.Sapling) StandTrees(x, y);
        if (to == TerrainIds.Pane && panesByCell.TryGetValue(key, out var mended)) mended.Mend(key);
    }

    // The baked trees of a cell, and its palms, as they were.
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

    /// <summary>Whether a tree the valley drew in a cell stands now, for the probe; false for a cell with none.</summary>
    public bool TreeStands(int x, int y) =>
        treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) && list.Count > 0 && trees[list[0].Item1][list[0].Item2].heightScale > 0f;

    // The building a wall cell belongs to, or -1: a log wall is the cabin or house whose box it's
    // in (village houses stand close), else the nearest; a stone wall only one it's in, since the
    // yard has stone walls of its own.
    int BuildingWallAt(string terrain, int x, int y)
    {
        if (terrain != TerrainIds.LogWall && terrain != TerrainIds.Stone) return -1;
        // A row's wall (a landmark's house, the castle, a mod's) is none of the scene's: RowHouses draws it.
        if (rowCells.Contains(grid.World.Idx(x, y))) return -1;
        int inside = CabinAround(x, y);
        if (inside >= 0 || terrain == TerrainIds.Stone) return inside;
        return CabinAt(x, y);
    }

    // The cells of the rows drawn from the world (World.RowsDrawn), kept as the rows change: without them a laid row's log
    // walls went to the nearest scene house (CabinAt), which then needed them burnt too before its roof fell in.
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

    // The cabin or house whose box (its BoxCollider, a little grown) holds a cell, or -1.
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

    // The cabin nearest a cell, or -1 without any.
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

    // A cabin's log wall burnt through, or caved in under earth shaping. Once half its walls are
    // gone the cabin falls: charred if fire took most of them, else caved in (sunk, its roof down).
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

    /// <summary>
    /// Whether a cell's tree grew there from a sapling while this world ran (Plant Growth): the
    /// valley's terrain has no tree standing for it, so CellDressing draws one. A late joiner
    /// sees one where the world started with a tree as a blocker only (the cells don't say how a
    /// tree came to be); elsewhere NeedsTreeDrawn draws it.
    /// </summary>
    public bool Grown(int x, int y) => grown.Contains(grid.World.Idx(x, y));

    /// <summary>
    /// Whether a tree cell needs a tree drawn on it (CellDressing): one grew there from a sapling (Grown), or the cell
    /// wasn't a tree when the world started and the land draws none there now, as for a tree a spell or a mod laid
    /// (docs/design/events-and-placing.md 2b). A late joiner, starting from the same world, draws the same.
    /// </summary>
    public bool NeedsTreeDrawn(int x, int y)
    {
        int key = grid.World.Idx(x, y);
        return grown.Contains(key) || (startTrees != null && !startTrees.At(x, y) && !TreeStands(x, y));
    }

    /// <summary>
    /// A tree model to draw growing in a cell (CellDressing): the valley's own tree that stood
    /// there if one did (a stump's tree grows back), else one of its chunk's kinds picked by the
    /// cell, at its usual size; false without a valley tree to copy.
    /// </summary>
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

    /// <summary>
    /// The valley's tree of this kind (a terrain tree prototype's name: Conifer, Cypress) as the cell's chunk draws it, for a
    /// great tree; else the cell's own (TreeModel). False without a valley tree to copy.
    /// </summary>
    public bool TreeModel(int x, int y, string kind, out GameObject prefab, out Vector3 scale)
    {
        scale = Vector3.one;
        prefab = null;
        var at = layout.ChunkOfCell(x, y);
        if (!string.IsNullOrEmpty(kind) && ground.HasChunk(at))
            prefab = ground.TreePrototypes(at).Select(p => p.prefab).FirstOrDefault(p => p != null && p.name == kind);
        return prefab != null || TreeModel(x, y, out prefab, out scale);
    }

    /// <summary>The valley's tree kinds drawn in a cell's chunk: what a great tree is measured against.</summary>
    public IEnumerable<GameObject> TreeKinds(int x, int y)
    {
        var at = layout.ChunkOfCell(x, y);
        return ground.HasChunk(at) ? ground.TreePrototypes(at).Select(p => p.prefab).Where(p => p != null) : Enumerable.Empty<GameObject>();
    }

    // Terrain edits are batched once per frame.
    readonly System.Diagnostics.Stopwatch timer = new();

    /// <summary>Milliseconds the last frame spent handing burnt trees and changed ground to the terrains (the wildfire probe reads them).</summary>
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
        if (grid != null && ground != null) grid.DetailChunksChanged -= ground.DrawDetails;
        if (grid != null) grid.ChunkFreshened -= BuildingsBack;
        if (grid != null) grid.ChunkDrawn -= QueueGrass;
        if (hookedRows != null) hookedRows.BuildingsChanged -= RowsChanged;
        if (charred != null) Destroy(charred);
        wet?.Destroy();
        ground?.Destroy();
    }
}
