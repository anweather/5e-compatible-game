using System.Collections.Generic;
using Chem;
using UnityEngine;

// The water on the cells (the pond, the river, trenches, puddles; the lake
// has its own surface): a mesh over the wet cells with a rounded, wobbly
// outline (ContourSurface), and a second one for ice, which is solid to walk
// on. Both are drawn in tiles of TileCells × TileCells cells, each its own
// mesh (and, for ice, its own collider), and only the tiles near a change
// are rebuilt, nearest the camera first and a few milliseconds a frame: the
// river runs the length of the valley, and one mesh over all
// of it took over 100 ms to rebuild for a single puddle.
//
// Each wet cell's surface is its ground plus its water depth, but never above
// the land around it:
//  - a body of water (wet cells connected to each other) stays a little under
//    the lowest bank around it, a bank being dry ground higher than the wet
//    cell beside it, so a full pond reads as a pond below the meadow and not as
//    a slab on it (the level passes only to water standing no higher, so a
//    river running downhill stays under its own banks all along);
//  - where a wet cell meets dry ground no higher than its own (a puddle on flat
//    ground, water reaching the dry end of a fresh trench), it is a thin film.
// Points between cells take the blended top of the wet cells around them, so
// neighbouring cells meet without steps. The outline reaches a quarter cell
// into a bank, under the terrain; on open ground it rounds off inside the wet
// cells and fades out, so a puddle is a blob with a soft edge, not a square.
//
// A liquid that isn't water (a mod's honey or paint: Rules.Liquids.cs) is the same surface in its
// look's colour: a point's vertex colour is the blend of the liquids round it, and uv2.y how much
// of it is theirs, so plain water, at 0, is drawn from the material alone, as ever.
public sealed class CellWaterMesh
{
    // How far the water stays under the land around it, and a puddle's film over its ground.
    const float Inset = 0.05f, Film = 0.03f;
    // A bank's value for the outline (CellContour, iso 0.5): the edge reaches a quarter cell into
    // it (one heightmap sample on a full-detail chunk, where the bank has risen above the water).
    const float BankValue = 0.34f, ShoreValue = 0.45f, IceLift = 0.01f;
    // The outline: 4 lattice points per cell, wobbly enough that a run of cells isn't a straight shore; how far in from the edge the water
    // turns opaque (in blended value), and the texture's repeat (calmer than the lake's 10 m).
    static readonly ContourSpec Outline = new(4, 0.5f, 0.7f, 31);
    const float FadeWidth = 0.3f, RippleMetres = 16f;

    /// <summary>Cells a tile side.</summary>
    public const int TileCells = 16;

    readonly ChemGrid grid;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    readonly Material waterMaterial, iceMaterial;
    readonly Color ownColor;
    readonly HashSet<int> wet = new();
    // By cell, held a block a chunk (WaterCells): whether it's wet, its drawn top (NaN for none) and the next pass's, its
    // kind, cap, ground and flow as last read, and whether it's a bank or calm. Only chunks that drew water, or stand
    // within two cells of some, have a block: eleven arrays a cell of the map were 36 bytes a cell, 1.43 GB on the 12 x 10
    // (J-086).
    readonly WaterCells cells;
    // The cells with a top now, and those with one in nextTop.
    List<int> topped = new(), nextTopped = new();
    // Tiles to redraw at the next Rebuild (a tile key: ty * tilesX + tx), and each tile's surfaces.
    readonly HashSet<int> dirtyTiles = new();
    readonly Dictionary<int, (ContourSurface water, ContourSurface ice)> tiles = new();
    // Each liquid's colour by Cell.Liquid (alpha 0: drawn as water, as water itself and a liquid with
    // no look are), and each cell's liquid as last seen.
    Color[] liquidColors;
    readonly int tilesX;
    readonly GameObject root;
    // The cells FindBanks marked (WaterCell.bank), to clear them.
    List<int> banks = new(), nextBanks = new();
    // Banks and the wet cells beside them (WaterCell.calm): the outline doesn't wobble there, or it would pull
    // back off the bank's slope (a gap) or climb over it (water showing through the ground).
    List<int> calm = new(), nextCalm = new();

    // A cell's state, as the arrays a cell held it.
    struct WaterCell
    {
        public float top, nextTop, cap, ground;
        public Vector2 flow;
        public int groundPass, flowRead;
        public byte kind;
        public bool wet, bank, calm;
    }

    /// <summary>
    /// The cells' state a block a world chunk (64 x 64 cells, 144 KB), each made the first time one of its cells is
    /// written; a cell of a chunk with none reads as a dry cell with no top. Blocks go back to a pool with the mesh, so a
    /// world built again (a reset, a map switch) takes them from there.
    /// </summary>
    sealed class WaterCells
    {
        const int Mask = Chunks.Size - 1;
        static readonly WaterCell Blank = new() { top = float.NaN };
        static readonly Stack<WaterCell[]> Pool = new();
        const int MostPooled = 256;
        readonly WaterCell[][] blocks;
        readonly int w, across;

        public WaterCells(int width, int height)
        {
            w = width;
            across = (width + Mask) >> Chunks.Shift;
            blocks = new WaterCell[across * ((height + Mask) >> Chunks.Shift)][];
        }

        /// <summary>How many chunks have a block, for a performance pass.</summary>
        public int Made { get; private set; }

        public ref readonly WaterCell Read(int key) => ref Read(key % w, key / w);

        public ref readonly WaterCell Read(int x, int y)
        {
            var block = blocks[(y >> Chunks.Shift) * across + (x >> Chunks.Shift)];
            if (block == null) return ref Blank;
            return ref block[((y & Mask) << Chunks.Shift) | (x & Mask)];
        }

        public bool Has(int key) => blocks[(key / w >> Chunks.Shift) * across + (key % w >> Chunks.Shift)] != null;

        public ref WaterCell Write(int key)
        {
            int x = key % w, y = key / w, n = (y >> Chunks.Shift) * across + (x >> Chunks.Shift);
            var block = blocks[n] ??= Take();
            return ref block[((y & Mask) << Chunks.Shift) | (x & Mask)];
        }

        WaterCell[] Take()
        {
            Made++;
            PerfCounts.Add("water.cell_blocks_made");
            if (Pool.Count == 0) return Filled(new WaterCell[Chunks.Area]);
            return Filled(Pool.Pop());
        }

        static WaterCell[] Filled(WaterCell[] block)
        {
            System.Array.Fill(block, Blank);
            return block;
        }

        /// <summary>Every block back to the pool (up to MostPooled kept).</summary>
        public void Release()
        {
            for (int n = 0; n < blocks.Length; n++)
            {
                if (blocks[n] != null && Pool.Count < MostPooled) Pool.Push(blocks[n]);
                blocks[n] = null;
            }
            Made = 0;
        }

        // Each Play mode starts with none, with the domain reload off too, so the Editor holds no blocks out of play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPool()
        {
            Pool.Clear();
        }
    }

    public CellWaterMesh(ChemGrid grid, Material waterMaterial, Material iceMaterial)
    {
        this.grid = grid;
        // One instance of the water material for every tile, so the ripples drift together as the lake's do.
        this.waterMaterial = new Material(waterMaterial) { name = waterMaterial.name };
        ownColor = this.waterMaterial.HasProperty(ColorId) ? this.waterMaterial.GetColor(ColorId) : Color.white;
        this.iceMaterial = iceMaterial;
        tilesX = (grid.World.W + TileCells - 1) / TileCells;
        liquidColors = LiquidColors(grid.World.Rules);
        cells = new WaterCells(grid.World.W, grid.World.H);
        // Read for every cell and point of a tile, so made once: a lambda here would be garbage each call.
        levelOver = levelBudget.Over;
        waterValue = (x, y) => Value(x, y, false);
        iceValue = (x, y) => Value(x, y, true);
        waterTop = (x, y) => TopOf(x, y, false);
        iceTop = (x, y) => TopOf(x, y, true);
        waterHeight = (gx, gy) => ContourSurface.BlendAround(gx, gy, waterTop, 0f);
        iceHeight = (gx, gy) => ContourSurface.BlendAround(gx, gy, iceTop, 0f);
        waterColor = (gx, gy, v) => Worn(gx, gy, Mathf.Max(Opacity(v, false), RunoffOpacity(gx, gy, v)));
        iceColor = (gx, gy, v) => new Color(1f, 1f, 1f, Mathf.Max(Opacity(v, true), RunoffOpacity(gx, gy, v)));
        calmness = (x, y) => grid.World.InBounds(x, y) && cells.Read(x, y).calm ? 0f : 1f;
        flowX = (x, y) => FlowOf(x, y).x;
        flowY = (x, y) => FlowOf(x, y).y;
        ownAt = (x, y) => Holds(x, y, false) ? (float?)LiquidColor(x, y).a : null;
        hueR = (x, y) => Hue(x, y)?.r;
        hueG = (x, y) => Hue(x, y)?.g;
        hueB = (x, y) => Hue(x, y)?.b;
        kept = new KeptCells(this);
        root = new GameObject("Water and ice");
        root.transform.SetParent(grid.transform, false);
    }

    /// <summary>The water's colour as drawn.</summary>
    public Color Color => waterMaterial.GetColor(ColorId);

    /// <summary>The rules switched: the water's colour, tinted from its art (ChemGrid.WorldLook.cs), and each liquid's from its look.</summary>
    public void Recolour(Color tint, Rules rules)
    {
        waterMaterial.SetColor(ColorId, ownColor * new Color(tint.r, tint.g, tint.b, 1f));
        var colors = LiquidColors(rules);
        bool same = System.Linq.Enumerable.SequenceEqual(colors, liquidColors);
        liquidColors = colors;
        if (same) return;
        var world = grid.World;
        foreach (int key in wet)
            if (world.CellAt(key).Kind != 0) MarkAround(key % world.W, key / world.W, "water.mark.kind");
    }

    static Color[] LiquidColors(Rules rules)
    {
        var colors = new Color[rules.Liquids.Count];
        for (int n = 1; n < colors.Length; n++)
        {
            var look = rules.Liquids[n].Look;
            if (look == null) continue;
            var drawn = look.Mix != null ? look.Mix.Blended : LookColour.Of(look);
            // Vertex colours reach the shader as they are, unlike a material's: the look is in sRGB.
            colors[n] = new Color(drawn.R, drawn.G, drawn.B, 1f).linear;
        }
        return colors;
    }

    /// <summary>A cell's water changed (or its ground did): the tiles round it are redrawn at the next Rebuild.</summary>
    public void Changed(int x, int y, bool isWet)
    {
        int key = grid.World.Idx(x, y);
        var c = grid.World.CellAt(key);
        int level = isWet ? c.Water : 0;
        levels.TryGetValue(key, out int was);
        // A dry cell keeps no level (every cell ever wet kept one: a crossing's rivers and lakes, held for good).
        if (level != 0) levels[key] = level;
        else levels.Remove(key);
        // An outline moves only when a cell turns wet, dry, or to or from ice. A level alone (the
        // river's changes on most ticks) moves the surface where its top moves: Recompute marks those.
        bool outline = isWet ? wet.Add(key) : wet.Remove(key);
        if (isWet || cells.Has(key)) cells.Write(key).wet = isWet;
        outline |= isWet && c.Frozen ? ice.Add(key) : ice.Remove(key);
        int kind = isWet ? c.Kind : 0;
        if (kind != cells.Read(key).kind)
        {
            cells.Write(key).kind = (byte)kind;
            MarkAround(x, y, "water.mark.kind");
        }
        if (PerfCounts.On) CountChange(c, outline, was != level);
        if (outline) MarkAround(x, y, "water.mark.outline");
        stale |= outline || was != level;
    }

    // Each wet cell's water and which are frozen, as last seen (0 and absent when dry).
    readonly Dictionary<int, int> levels = new();
    readonly HashSet<int> ice = new();

    // For a performance pass: what kind of change this was.
    static void CountChange(Cell c, bool outline, bool level)
    {
        PerfCounts.Add("water.changed");
        if (outline) PerfCounts.Add("water.changed_wet_or_dry");
        else PerfCounts.Add(level ? "water.changed_level" : "water.changed_other_only");
        PerfCounts.Add(c.T == TerrainIds.River ? "water.changed_on_river" : "water.changed_off_river");
    }

    /// <summary>Ground moved at a cell: the water beside it may sit differently against its banks.</summary>
    public void GroundChanged(int x, int y)
    {
        flowPass++;
        lastFlowX = float.NaN;
        PerfCounts.Add("water.ground_changed");
        if (PerfCounts.On && !AnyWetNear(new RectInt(x - 1, y - 1, 3, 3))) PerfCounts.Add("water.ground_changed_no_water_near");
        if (wet.Count == 0) return;
        MarkAround(x, y, "water.mark.ground");
        stale = true;
    }

    // The surface's levels and banks need working out again (something changed since).
    bool stale;
    // Milliseconds a frame may spend drawing tiles; the rest wait for the next frames, nearest first.
    // In a playtest run a count of tiles instead (ChemGrid.ExactFrames): ice tiles carry colliders.
    // A tile of 32 cells took 14 to 25 ms on the Mac and 10 to 15 on the PC, too much to split: at 16
    // the budget means what it says. At 24 ms the bot saw spikes of a batch no player ever gets.
    const double DrawBudgetMs = 3.0;
    const int ExactDrawTiles = 2;
    readonly ChemGrid.FrameBudget drawBudget = new(DrawBudgetMs, ExactDrawTiles);
    readonly List<int> drawOrder = new();

    // A cell's change reaches two cells round it (its outline reads one ring out, a bank's another):
    // the tiles under that square are redrawn.
    void MarkAround(int x, int y, string why)
    {
        int tx0 = Mathf.Max(0, (x - 2) / TileCells), tx1 = Mathf.Min(tilesX - 1, (x + 2) / TileCells);
        int ty0 = Mathf.Max(0, (y - 2) / TileCells), ty1 = (y + 2) / TileCells;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                if (dirtyTiles.Add(ty * tilesX + tx) && PerfCounts.On) PerfCounts.Add(why);
    }

    /// <summary>The water surface drawn at a wet cell after the last Rebuild, or <paramref name="fallback"/>.</summary>
    public float Top(int x, int y, float fallback)
    {
        float top = cells.Read(x, y).top;
        return float.IsNaN(top) ? fallback : top;
    }

    /// <summary>The top of the water or ice as drawn at a point in grid units, blended between the cells as its mesh is, or <paramref name="fallback"/>.</summary>
    public float TopAt(float gx, float gy, float fallback) => ContourSurface.BlendAround(gx, gy,
        (x, y) => grid.World.InBounds(x, y) && !float.IsNaN(cells.Read(x, y).top) ? cells.Read(x, y).top : null, fallback);

    /// <summary>Drifts the water's ripples, as the lake's do (once a frame).</summary>
    public void Scroll(float time)
    {
        // Prototype/CellWater moves its own ripples with the flow; a plain Lit material drifts its map.
        if (waterMaterial.HasProperty("_BaseMap")) waterMaterial.SetTextureOffset("_BaseMap", new Vector2(0.004f, 0.003f) * time);
    }

    /// <summary>
    /// Redraws the tiles near any change since the last time: the levels and banks are worked out
    /// again only when something changed, and the tiles are drawn nearest the camera first, within
    /// a few milliseconds a frame (the rest next frame), so a burst of changes never stalls one.
    /// </summary>
    public void Rebuild(bool busy = false)
    {
        if (dirtyTiles.Count == 0 && !stale && step == LevelStep.Resting) return;
        PerfCounts.Add("water.rebuilds");
        if (step != LevelStep.Resting || (stale && Time.frameCount - restingSince >= RestFrames))
            using (RecomputeMarker.Auto()) Recompute(levelOver);
        PerfCounts.Peak("water.dirty_tiles", dirtyTiles.Count);
        if (dirtyTiles.Count == 0 || busy) return;
        drawBudget.Start();
        drawOrder.Clear();
        drawOrder.AddRange(dirtyTiles);
        var cam = Camera.main;
        if (cam != null && drawOrder.Count > 1)
        {
            var eye = cam.transform.position;
            drawOrder.Sort((a, b) => TileDistance(a, eye).CompareTo(TileDistance(b, eye)));
        }
        foreach (int tile in drawOrder)
        {
            if (drawBudget.Over()) break;
            using (DrawTileMarker.Auto()) DrawTile(tile);
            PerfCounts.Add("water.tiles_drawn");
            dirtyTiles.Remove(tile);
        }
    }

    // Profiler markers round what a Rebuild is made of (ContourSurface marks inside DrawTile).
    static readonly Unity.Profiling.ProfilerMarker RecomputeMarker = new("Water.Recompute"), TopsMarker = new("Water.ComputeTops"),
        BanksMarker = new("Water.FindBanks"), DrawTileMarker = new("Water.DrawTile");

    // Working out the levels and the banks, a pass over every wet cell: rain leaves tens of thousands
    // and changes some every frame, so a pass in one go took a frame of 20 to 30 ms, every frame. It
    // runs a few milliseconds a frame instead, and what it works from changing on the way is fine:
    // something changing means another pass after this one, and a level is a moment late.
    enum LevelStep { Resting, Caps, Flood, Tops, Banks }
    LevelStep step;
    int stepAt;
    const double LevelBudgetMs = 2.0;
    // Cells between looks at the budget; in a playtest run, that many cells make a unit of it.
    const int CellsAStride = 256, ExactLevelStrides = 16;
    readonly ChemGrid.FrameBudget levelBudget = new(LevelBudgetMs, ExactLevelStrides);
    readonly System.Func<bool> levelOver;
    // Frames between the end of one pass and the start of the next. In rain or by a fire some level
    // moves every frame, so passes ran back to back, 2 ms every frame (#70); a level drawn this
    // much later than it moved doesn't show.
    const int RestFrames = 8;
    int restingSince = int.MinValue / 2;

    // The levels of every body of water and the banks round them, as far as the frame's budget goes;
    // where a level moved, far from the cell that changed, those tiles are redrawn too.
    void Recompute(System.Func<bool> over)
    {
        levelBudget.Start();
        if (step == LevelStep.Resting) BeginLevels();
        using (TopsMarker.Auto())
        {
            if (step == LevelStep.Caps && !Caps(over)) return;
            if (step == LevelStep.Flood && !Flood(over)) return;
            if (step == LevelStep.Tops)
            {
                if (!Tops(over)) return;
                MoveTops();
            }
        }
        using (BanksMarker.Auto())
        {
            if (!FindBanks(over)) return;
            SetBanks();
        }
    }

    void BeginLevels()
    {
        stale = false;
        pass++;
        queueWork.Clear();
        nextTopped.Clear();
        nextTopped.AddRange(wet);
        PerfCounts.Add("water.recomputes");
        PerfCounts.Peak("water.wet_cells", wet.Count);
        NextStep(LevelStep.Caps);
    }

    void NextStep(LevelStep next)
    {
        step = next;
        stepAt = 0;
    }

    // Finished, or out of time at the top of a stride.
    bool TimeToStop(System.Func<bool> over) => stepAt % CellsAStride == 0 && stepAt > 0 && over();

    void MoveTops()
    {
        var world = grid.World;
        foreach (int key in nextTopped)
        {
            ref readonly var c = ref cells.Read(key);
            if (!Mathf.Approximately(c.top, c.nextTop))
            {
                MarkAround(key % world.W, key / world.W, "water.mark.top");
                PerfCounts.Add("water.tops_moved");
                if (PerfCounts.On && Mathf.Abs(c.top - c.nextTop) < 0.02f) PerfCounts.Add("water.tops_moved_under_2cm");
            }
        }
        foreach (int key in topped) cells.Write(key).top = float.NaN;
        foreach (int key in nextTopped)
        {
            ref var c = ref cells.Write(key);
            c.top = c.nextTop;
        }
        (topped, nextTopped) = (nextTopped, topped);
        NextStep(LevelStep.Banks);
    }

    /// <summary>
    /// For a performance pass (-perf-water-check): redraws every tile that isn't waiting for a redraw
    /// and counts those that came out different, water or ice, so a tile left stale by a skipped
    /// redraw shows. Drawn afresh, the tile is then right: run it at the end.
    /// </summary>
    public (int tiles, int different) CheckTiles()
    {
        while (stale || step != LevelStep.Resting) Recompute(() => false);
        int count = 0, different = 0;
        foreach (var (tile, t) in tiles)
        {
            if (dirtyTiles.Contains(tile)) continue;
            int tx = tile % tilesX, ty = tile / tilesX, x0 = tx * TileCells, y0 = ty * TileCells;
            var box = new RectInt(x0, y0, Mathf.Min(TileCells, grid.World.W - x0), Mathf.Min(TileCells, grid.World.H - y0));
            var (water, solid) = (Picture(t.water.mesh), Picture(t.ice.mesh));
            Draw(t.water, box, false);
            Draw(t.ice, box, true);
            count++;
            if (!Same(water, Picture(t.water.mesh)) || !Same(solid, Picture(t.ice.mesh))) different++;
        }
        return (count, different);
    }

    static (Vector3[] at, Color[] colors, int[] triangles) Picture(Mesh mesh) => (mesh.vertices, mesh.colors, mesh.triangles);

    static bool Same((Vector3[] at, Color[] colors, int[] triangles) a, (Vector3[] at, Color[] colors, int[] triangles) b)
    {
        if (a.at.Length != b.at.Length || !System.Linq.Enumerable.SequenceEqual(a.triangles, b.triangles)) return false;
        for (int n = 0; n < a.at.Length; n++)
            if ((a.at[n] - b.at[n]).sqrMagnitude > 1e-6f || !a.colors[n].Equals(b.colors[n])) return false;
        return true;
    }

    // Squared distance on the ground from a point to a tile's middle.
    float TileDistance(int tile, Vector3 eye)
    {
        int cx = (tile % tilesX) * TileCells + TileCells / 2, cy = (tile / tilesX) * TileCells + TileCells / 2;
        var mid = grid.CellCenter(Mathf.Min(cx, grid.World.W - 1), Mathf.Min(cy, grid.World.H - 1));
        float dx = mid.x - eye.x, dz = mid.z - eye.z;
        return dx * dx + dz * dz;
    }

    // A tile's water and ice: made the first time it has any, emptied when it has none.
    void DrawTile(int tile)
    {
        int tx = tile % tilesX, ty = tile / tilesX;
        var world = grid.World;
        int x0 = tx * TileCells, y0 = ty * TileCells;
        var box = new RectInt(x0, y0, Mathf.Min(TileCells, world.W - x0), Mathf.Min(TileCells, world.H - y0));
        if (box.width <= 0 || box.height <= 0) return;
        if (!tiles.TryGetValue(tile, out var t))
        {
            if (!AnyWetNear(box))
            {
                PerfCounts.Add("water.tiles_none");
                return;
            }
            t = (new ContourSurface(grid, "Water", waterMaterial, Outline, RippleMetres, tiled: true),
                 new ContourSurface(grid, "Ice", iceMaterial, Outline, RippleMetres, tiled: true));
            t.water.go.transform.SetParent(root.transform, true);
            t.ice.go.transform.SetParent(root.transform, true);
            t.ice.go.AddComponent<MeshCollider>();
            tiles[tile] = t;
        }
        Draw(t.water, box, false);
        // Most tiles have no ice and never did: outlining nothing is half the cost of a tile.
        if (!t.ice.Empty || AnyIceNear(box))
        {
            Draw(t.ice, box, true);
            t.ice.go.GetComponent<MeshCollider>().sharedMesh = t.ice.Empty ? null : t.ice.mesh;
        }
        // A tile with nothing left to draw goes (its cells dried, or their chunk was let go far behind, ChemGrid.Undraw):
        // kept, every tile that ever had water stayed, two meshes and two objects each. Made again if water comes back.
        if (!t.water.Empty || !t.ice.Empty) return;
        t.water.Destroy();
        t.ice.Destroy();
        tiles.Remove(tile);
    }

    // Whether ice could show in the box: the outline reads a ring round it, and a cell only scores
    // for the ice beside some (Value), so two rings out. Without any, only banks score, and they
    // don't reach the outline's iso value (BankValue; calm cells don't wobble).
    bool AnyIceNear(RectInt box)
    {
        for (int y = box.yMin - 2; y <= box.yMax + 1; y++)
            for (int x = box.xMin - 2; x <= box.xMax + 1; x++)
                if (Holds(x, y, true)) return true;
        return false;
    }

    // Whether any cell in the box, or the ring round it, holds water (a tile also draws the edge of
    // water lying just past it).
    bool AnyWetNear(RectInt box)
    {
        var world = grid.World;
        for (int y = box.yMin - 1; y <= box.yMax; y++)
            for (int x = box.xMin - 1; x <= box.xMax; x++)
                if (world.InBounds(x, y) && cells.Read(x, y).wet) return true;
        return false;
    }

    // --- heights -------------------------------------------------------------------

    // Kept in the cells, so working out the levels allocates nothing (past a chunk's first block): the cap on each wet
    // cell, and each cell's ground, read once a pass (groundPass says which pass read it).
    int pass;
    readonly Queue<int> queueWork = new();

    bool Caps(System.Func<bool> over)
    {
        for (; stepAt < nextTopped.Count; stepAt++)
        {
            if (TimeToStop(over)) return false;
            int key = nextTopped[stepAt];
            float lowest = LowestBank(key);
            cells.Write(key).cap = lowest;
            queueWork.Enqueue(key);
        }
        NextStep(LevelStep.Flood);
        return true;
    }

    // Each body of water takes the lowest dry ground around any of its cells, passed on only to
    // water standing no higher: a pond is held to its lowest bank all over, but a river running
    // downhill isn't held to the banks far down its course.
    bool Flood(System.Func<bool> over)
    {
        var world = grid.World;
        var queue = queueWork;
        float perUnit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        while (queue.Count > 0)
        {
            stepAt++;
            if (TimeToStop(over)) return false;
            int key = queue.Dequeue();
            int x = key % world.W, y = key / world.W;
            float cap = cells.Read(x, y).cap;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + World.DirX(d), ny = y + World.DirY(d);
                if (!world.InBounds(nx, ny)) continue;
                int n = world.Idx(nx, ny);
                ref readonly var c = ref cells.Read(nx, ny);
                if (!c.wet || c.cap <= cap || Standing(n, perUnit) > Standing(key, perUnit) + perUnit) continue;
                cells.Write(n).cap = cap;
                queue.Enqueue(n);
            }
        }
        NextStep(LevelStep.Tops);
        return true;
    }

    bool Tops(System.Func<bool> over)
    {
        var world = grid.World;
        float perUnit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        for (; stepAt < nextTopped.Count; stepAt++)
        {
            if (TimeToStop(over)) return false;
            int key = nextTopped[stepAt];
            int x = key % world.W, y = key / world.W;
            var c = world.CellAt(key);
            float ground = Ground(key);
            float top = Mathf.Min(ground + c.Water * perUnit, cells.Read(x, y).cap - Inset);
            if (MeetsLowGround(key, ground)) top = Mathf.Min(top, ground + Film);
            // Ice on the lake floats at the lake's surface, however deep the water under it.
            cells.Write(key).nextTop = Mathf.Max(Mathf.Max(ground + Film, top), grid.LakeLevelAt(x, y, c) + Film);
        }
        return true;
    }

    // A cell's ground, read once a pass: every wet cell asks after all 8 cells round it.
    float Ground(int key)
    {
        ref readonly var was = ref cells.Read(key);
        if (was.groundPass == pass) return was.ground;
        float ground = OwnGround(key);
        ref var c = ref cells.Write(key);
        c.groundPass = pass;
        return c.ground = ground;
    }

    // The lowest bank: a dry neighbour (of 8) higher than the cell's own ground.
    float LowestBank(int key)
    {
        var world = grid.World;
        float own = Ground(key), lowest = float.PositiveInfinity;
        int x = key % world.W, y = key / world.W;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Ring[d].dx, ny = y + Ring[d].dy;
            if (!world.InBounds(nx, ny)) continue;
            int n = world.Idx(nx, ny);
            if (cells.Read(nx, ny).wet) continue;
            float ground = Ground(n);
            if (ground > own + Level && ground < lowest) lowest = ground;
        }
        return lowest;
    }

    // Whether a dry neighbour (of 8) is no higher than the cell's own ground.
    bool MeetsLowGround(int key, float own)
    {
        var world = grid.World;
        int x = key % world.W, y = key / world.W;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Ring[d].dx, ny = y + Ring[d].dy;
            if (!world.InBounds(nx, ny)) continue;
            int n = world.Idx(nx, ny);
            if (!cells.Read(nx, ny).wet && Ground(n) <= own + Level) return true;
        }
        return false;
    }

    // Grounds closer than this count as level.
    const float Level = 0.01f;

    float OwnGround(int key) => grid.GroundTop(key % grid.World.W, key / grid.World.W, grid.World.CellAt(key));

    // Where a wet cell's water stands: its ground and its water's depth.
    float Standing(int key, float perUnit) => Ground(key) + grid.World.CellAt(key).Water * perUnit;

    // --- the mesh --------------------------------------------------------------------

    // Dry cells beside the water that stand above its surface: the outline reaches into them.
    bool FindBanks(System.Func<bool> over)
    {
        var world = grid.World;
        for (; stepAt < topped.Count; stepAt++)
        {
            if (TimeToStop(over)) return false;
            int key = topped[stepAt];
            int x = key % world.W, y = key / world.W;
            float top = cells.Read(x, y).top;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Ring[d].dx, ny = y + Ring[d].dy;
                if (!world.InBounds(nx, ny)) continue;
                int n = world.Idx(nx, ny);
                if (cells.Read(nx, ny).wet || Ground(n) <= top) continue;
                nextBanks.Add(n);
                nextCalm.Add(n);
                nextCalm.Add(key);
            }
        }
        return true;
    }

    // The pass's banks take over, whole, so no tile draws from a half-found set.
    void SetBanks()
    {
        Replace(banks, nextBanks, true);
        Replace(calm, nextCalm, false);
        (banks, nextBanks) = (nextBanks, banks);
        (calm, nextCalm) = (nextCalm, calm);
        nextBanks.Clear();
        nextCalm.Clear();
        NextStep(LevelStep.Resting);
        restingSince = Time.frameCount;
    }

    // Sets the marks to the new list. A cell that gained or lost one moves the outline round it,
    // with no level moving, so those tiles are redrawn too.
    void Replace(List<int> was, List<int> now, bool bank)
    {
        var world = grid.World;
        foreach (int key in now)
        {
            if (Marked(key, bank)) continue;
            Mark(key, bank, true);
            changedMarks.Add(key);
        }
        foreach (int key in was) goneMarks.Add(key);
        foreach (int key in now) goneMarks.Remove(key);
        foreach (int key in goneMarks)
        {
            Mark(key, bank, false);
            changedMarks.Add(key);
        }
        foreach (int key in changedMarks) MarkAround(key % world.W, key / world.W, "water.mark.bank");
        goneMarks.Clear();
        changedMarks.Clear();
    }

    readonly HashSet<int> goneMarks = new(), changedMarks = new();

    // A cell's bank mark, or its calm one.
    bool Marked(int key, bool bank) => bank ? cells.Read(key).bank : cells.Read(key).calm;

    void Mark(int key, bool bank, bool on)
    {
        ref var c = ref cells.Write(key);
        if (bank) c.bank = on;
        else c.calm = on;
    }

    static readonly (int dx, int dy)[] Ring = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>
    /// Whether a tile's points are drawn from its cells read once (KeptCells) rather than through
    /// delegates per point and cell: the same vertices, cheaper (#70). Off for a before run.
    /// </summary>
    public static bool KeptPoints { get; set; } = true;

    /// <summary>For the probe: draws each tile both ways, counting whether they came out the same bit for bit.</summary>
    public static bool CheckKeptPoints { get; set; }

    /// <summary>
    /// The tiles drawn both ways under CheckKeptPoints, those that came out different, their vertices,
    /// and the time each way spent over their points (ms).
    /// </summary>
    public static (int drawn, int different, long vertices, double keptMs, double perPointMs) KeptPointsChecked =>
        (keptChecked, keptDifferent, keptVertices, keptTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
         perPointTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    static int keptChecked, keptDifferent;
    static long keptVertices, keptTicks, perPointTicks;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetKeptPoints()
    {
        CheckKeptPoints = false;
        keptChecked = keptDifferent = 0;
        keptVertices = keptTicks = perPointTicks = 0;
    }
    readonly KeptCells kept;
    ContourSurface checkSurface;

    // The water (or the ice) of one tile's wet cells: their outline, each point at the blended top
    // of the wet cells around it, fading out toward an edge on open ground.
    void Draw(ContourSurface surface, RectInt box, bool frozen)
    {
        FindRocksNear(box);
        if (!KeptPoints)
        {
            DrawPerPoint(surface, box, frozen);
            return;
        }
        kept.Read(box, frozen);
        surface.Build(box, frozen ? iceValue : waterValue, kept, calmness);
        if (!CheckKeptPoints) return;
        checkSurface ??= new ContourSurface(grid, "Water check", waterMaterial, Outline, RippleMetres, tiled: true);
        checkSurface.go.SetActive(false);
        DrawPerPoint(checkSurface, box, frozen);
        keptChecked++;
        keptVertices += surface.Vertices;
        keptTicks += surface.PointsTicks;
        perPointTicks += checkSurface.PointsTicks;
        if (!surface.SamePointsAs(checkSurface)) keptDifferent++;
    }

    // The same, through the delegates: each point asks after each cell round it.
    void DrawPerPoint(ContourSurface surface, RectInt box, bool frozen) =>
        surface.Build(box, frozen ? iceValue : waterValue, frozen ? iceHeight : waterHeight, frozen ? iceColor : waterColor, calmness,
            frozen ? null : (gx, gy, v) => FlowAndFoam(surface, gx, gy, v));

    /// <summary>
    /// A tile's cells as its points read them, read once a draw: each cell's top (NaN for none),
    /// whether it's the river, and its flow (read when first asked, as FlowOf would be). A point
    /// blends them as the delegates do, the same sums in the same order (ContourSurface.BlendKept).
    /// </summary>
    sealed class KeptCells : ContourSurface.IPoints
    {
        // The box and the ring round it, and two past its far side: a tile's points run from the
        // centre of its first cell to the centre of the cell past its last, blending the cells round each.
        const int Side = TileCells + 4;
        readonly CellWaterMesh owner;
        readonly float[] tops = new float[Side * Side], flowX = new float[Side * Side], flowY = new float[Side * Side];
        // Read only for a tile with a liquid beside water in reach: how much each cell is its liquid's
        // colour and that colour, NaN off the liquids, so water beside honey doesn't pale it.
        readonly float[] own = new float[Side * Side], hueR = new float[Side * Side], hueG = new float[Side * Side], hueB = new float[Side * Side];
        bool liquids;
        readonly bool[] river = new bool[Side * Side];
        readonly int[] flowRead = new int[Side * Side];
        int x0, y0, cols, rows, read;
        bool frozen;
        Vector3 origin;
        float cellSize;

        public KeptCells(CellWaterMesh owner) => this.owner = owner;

        public bool Flows => !frozen;

        public void Read(RectInt box, bool frozen)
        {
            this.frozen = frozen;
            var grid = owner.grid;
            var world = grid.World;
            origin = grid.CellCenter(0, 0);
            cellSize = grid.cellSize;
            x0 = box.xMin - 1;
            y0 = box.yMin - 1;
            cols = Mathf.Min(Side, box.width + 4);
            rows = Mathf.Min(Side, box.height + 4);
            read++;
            liquids = false;
            for (int b = 0; b < rows; b++)
                for (int a = 0; a < cols; a++)
                {
                    int x = x0 + a, y = y0 + b, n = b * cols + a;
                    tops[n] = owner.TopOf(x, y, frozen) ?? float.NaN;
                    var c = world.InBounds(x, y) ? world.At(x, y) : null;
                    river[n] = c != null && c.T == TerrainIds.River;
                    if (!frozen) ReadLiquid(x, y, n);
                }
        }

        void ReadLiquid(int x, int y, int n)
        {
            if (owner.liquidColors.Length < 2) return;
            own[n] = owner.ownAt(x, y) ?? float.NaN;
            var hue = owner.Hue(x, y);
            hueR[n] = hue?.r ?? float.NaN;
            hueG[n] = hue?.g ?? float.NaN;
            hueB[n] = hue?.b ?? float.NaN;
            liquids |= hue.HasValue;
        }

        float Blended(float[] cells, float gx, float gy, float fallback) => ContourSurface.BlendKept(cells, x0, y0, cols, gx, gy, fallback);

        public void Point(float gx, float gy, float value, out float height, out Color color, out Vector4 flow)
        {
            bool inside = ContourSurface.InKept(x0, y0, cols, rows, gx, gy);
            // The colour asks after the flow only inside the outline's iso value (RunoffOpacity).
            bool runoff = value >= Outline.Iso, flows = !frozen || runoff;
            Vector2 blended;
            if (inside)
            {
                if (flows) ReadFlows(gx, gy);
                height = ContourSurface.BlendKept(tops, (x0, y0, cols), (gx, gy), 0f, flows ? (flowX, flowY) : (null, null), out blended);
            }
            else
            {
                // Off the kept cells (never, for a tile's own points), the delegates.
                height = frozen ? owner.iceHeight(gx, gy) : owner.waterHeight(gx, gy);
                blended = flows ? owner.BlendedFlow(gx, gy) : Vector2.zero;
            }
            color = new Color(1f, 1f, 1f, Mathf.Max(Opacity(value, frozen), runoff && blended.sqrMagnitude > 0.04f ? 0.7f : 0f));
            if (frozen)
            {
                flow = Vector4.zero;
                return;
            }
            flow = owner.Foamed(blended, ContourSurface.Flat(origin, cellSize, gx, gy), OnRiver(gx, gy), value);
            if (liquids) Wear(gx, gy, inside, ref color, ref flow);
        }

        // A point's colour and share of the liquids round it.
        void Wear(float gx, float gy, bool inside, ref Color color, ref Vector4 flow)
        {
            color = inside ? new Color(Blended(hueR, gx, gy, 1f), Blended(hueG, gx, gy, 1f), Blended(hueB, gx, gy, 1f), color.a) : owner.Worn(gx, gy, color.a);
            flow.w = inside ? Blended(own, gx, gy, 0f) : owner.Own(gx, gy);
        }

        bool OnRiver(float gx, float gy)
        {
            int cx = (int)gx - x0, cy = (int)gy - y0;
            return cx >= 0 && cy >= 0 && cx < cols && cy < rows ? river[cy * cols + cx] : owner.OnRiver(gx, gy);
        }

        // The flows of the four kept cells round a point, read if they aren't yet.
        void ReadFlows(float gx, float gy)
        {
            int x = Mathf.FloorToInt(gx - 0.5f) - x0, y = Mathf.FloorToInt(gy - 0.5f) - y0;
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int n = (y + dy) * cols + x + dx;
                    if (flowRead[n] == read) continue;
                    var f = owner.FlowOf(x0 + x + dx, y0 + y + dy);
                    flowX[n] = f.x;
                    flowY[n] = f.y;
                    flowRead[n] = read;
                }
        }
    }

    readonly System.Func<int, int, float> waterValue, iceValue, calmness;
    readonly System.Func<int, int, float?> waterTop, iceTop, flowX, flowY, ownAt, hueR, hueG, hueB;
    readonly System.Func<float, float, float> waterHeight, iceHeight;
    readonly System.Func<float, float, float, Color> waterColor, iceColor;

    // ---------- flow (Prototype/CellWater: its ripples move with the water, foam where it's broken) ----------

    // Each wet cell's flow (world x, z in m/s; WaterCell.flow): the river's current along its course, or water on a
    // hillside running down it; kept until the ground changes (the river's line doesn't). WaterCell.flowRead says which
    // reading of the ground it's from: GroundChanged starts a new one.
    int flowPass = 1;
    RockWake[] rocks;
    // The rocks near the tile being drawn, read once a draw: where each stands, the way its wake runs
    // (normalised, on the ground) and its radius. Asking each rock's transform at every point cost more
    // than the rest of the point.
    readonly List<(Vector3 at, Vector2 down, float radius)> rocksNear = new();
    // A rock's wake reaches this many of its radii (FlowAndFoam).
    const float WakeRadii = 7f;
    // Runoff: metres a second per unit of slope, and its fastest.
    const float RunoffSpeed = 4f, RunoffMax = 1.6f;

    Vector2 FlowOf(int x, int y)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return Vector2.zero;
        int key = world.Idx(x, y);
        ref readonly var kept = ref cells.Read(x, y);
        if (kept.flowRead == flowPass) return kept.flow;
        PerfCounts.Add("water.flow_misses");
        var c = world.CellAt(key);
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundTop(x, y, c);
        var current = WaterCurrents.Instance != null ? WaterCurrents.Instance.At(at) : Vector3.zero;
        var flow = new Vector2(current.x, current.z);
        if (flow.sqrMagnitude < 0.0001f && c.T != TerrainIds.River) flow = Downhill(x, y);
        ref var read = ref cells.Write(key);
        read.flowRead = flowPass;
        read.flow = flow;
        return flow;
    }

    // The way the ground falls at a cell, as a runoff's speed (cells run +x east and +y south, -z).
    Vector2 Downhill(int x, int y)
    {
        var world = grid.World;
        if (x <= 0 || y <= 0 || x >= world.W - 1 || y >= world.H - 1) return Vector2.zero;
        float east = OwnGround(world.Idx(x + 1, y)) - OwnGround(world.Idx(x - 1, y));
        float south = OwnGround(world.Idx(x, y + 1)) - OwnGround(world.Idx(x, y - 1));
        var slope = new Vector2(east, -south) / (2f * grid.cellSize);
        return Vector2.ClampMagnitude(-slope * RunoffSpeed, RunoffMax);
    }

    // Asked twice a point, for its colour and for its flow.
    float lastFlowX = float.NaN, lastFlowY;
    Vector2 lastFlow;

    Vector2 BlendedFlow(float gx, float gy)
    {
        if (gx == lastFlowX && gy == lastFlowY) return lastFlow;
        lastFlowX = gx;
        lastFlowY = gy;
        return lastFlow = new Vector2(ContourSurface.BlendAround(gx, gy, flowX, 0f), ContourSurface.BlendAround(gx, gy, flowY, 0f));
    }

    // The river's rocks whose wakes could reach this box: most tiles have none.
    void FindRocksNear(RectInt box)
    {
        rocks ??= Object.FindObjectsByType<RockWake>(FindObjectsSortMode.None);
        rocksNear.Clear();
        var a = ContourSurface.FlatOn(grid, box.xMin, box.yMin);
        var b = ContourSurface.FlatOn(grid, box.xMax, box.yMax);
        var middle = (a + b) * 0.5f;
        float half = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z)) * 0.5f;
        foreach (var rock in rocks)
        {
            if (rock == null) continue;
            var at = rock.transform.position;
            float reach = half + rock.radius * WakeRadii;
            if (Mathf.Abs(at.x - middle.x) <= reach && Mathf.Abs(at.z - middle.z) <= reach)
                rocksNear.Add((at, new Vector2(rock.downstream.x, rock.downstream.z).normalized, rock.radius));
        }
        PerfCounts.Add("water.rocks_near", rocksNear.Count);
    }

    // A point's flow, and its foam: where moving water meets its edge (a bank, the end of a runoff)
    // and round the river's rocks, trailing off downstream.
    Vector4 FlowAndFoam(ContourSurface surface, float gx, float gy, float value)
    {
        Vector4 flow = Foamed(BlendedFlow(gx, gy), surface.Flat(gx, gy), OnRiver(gx, gy), value);
        flow.w = Own(gx, gy);
        return flow;
    }

    // Whether a point (grid units) is over a river cell.
    bool OnRiver(float gx, float gy)
    {
        var c = grid.World.InBounds((int)gx, (int)gy) ? grid.World.At((int)gx, (int)gy) : null;
        return c != null && c.T == TerrainIds.River;
    }

    // A point's blended flow with its foam, the point at p on the ground plane.
    Vector3 Foamed(Vector2 flow, Vector3 p, bool onRiver, float value)
    {
        float speed = flow.magnitude;
        float edge = 1f - Mathf.InverseLerp(Outline.Iso, Outline.Iso + 0.35f, value);
        // Banks foam where the river runs; a runoff is all edge, and only flecks.
        float foam = edge * Mathf.Clamp01(speed / 1.2f) * (onRiver ? 1f : 0.15f);
        foreach (var (at, down, radius) in rocksNear)
        {
            var d = new Vector2(p.x - at.x, p.z - at.z);
            float along = Vector2.Dot(d, down), across = Mathf.Abs(d.x * down.y - d.y * down.x);
            // Round its upstream face, then a wake three times as long as it is wide.
            float reach = along > 0f ? new Vector2(along / 3f, across).magnitude : d.magnitude;
            foam = Mathf.Max(foam, 1f - Mathf.InverseLerp(radius * 0.8f, radius * 2.2f, reach));
        }
        return new Vector3(flow.x, flow.y, foam);
    }

    // ---------- liquids ----------

    // The colour of the kind a cell holds (past the table, for the moment of a switch: as water).
    Color LiquidColor(int x, int y)
    {
        int kind = grid.World.At(x, y).Kind;
        return kind < liquidColors.Length ? liquidColors[kind] : default;
    }

    // The colour of the liquid in a cell, for those drawn in one.
    Color? Hue(int x, int y)
    {
        if (liquidColors.Length < 2 || !Holds(x, y, false)) return null;
        var color = LiquidColor(x, y);
        return color.a > 0f ? color : null;
    }

    // How much of a point is its liquids' colour (none for water), from the unfrozen cells round it.
    float Own(float gx, float gy) => liquidColors.Length < 2 ? 0f : ContourSurface.BlendAround(gx, gy, ownAt, 0f);

    // A point's vertex colour: white with this alpha for water, else the blend of the liquids round it.
    Color Worn(float gx, float gy, float alpha) => liquidColors.Length < 2
        ? new Color(1f, 1f, 1f, alpha)
        : new Color(ContourSurface.BlendAround(gx, gy, hueR, 1f), ContourSurface.BlendAround(gx, gy, hueG, 1f), ContourSurface.BlendAround(gx, gy, hueB, 1f), alpha);

    /// <summary>For a probe: every water vertex drawn and those in a liquid's colour, read back from the tiles' meshes.</summary>
    public (int vertices, int worn) WornVertices()
    {
        int all = 0, worn = 0;
        var own = new List<Vector2>();
        var colors = new List<Color>();
        foreach (var t in tiles.Values)
        {
            t.water.mesh.GetColors(colors);
            t.water.mesh.GetUVs(2, own);
            all += colors.Count;
            for (int n = 0; n < own.Count; n++)
                if (!IsPlain(colors[n], own[n].y)) worn++;
        }
        return (all, worn);
    }

    /// <summary>Whether a point's colour and share of a liquid are plain water's: white, none.</summary>
    public static bool IsPlain(Color color, float own) => own <= 0f && Mathf.Min(color.r, Mathf.Min(color.g, color.b)) >= 1f;

    /// <summary>For a probe: the colour and the liquid's share at the vertex nearest a cell's centre, read back from its tile's mesh.</summary>
    public (Color color, float own)? DrawnAt(int x, int y)
    {
        if (!tiles.TryGetValue(y / TileCells * tilesX + x / TileCells, out var t) || t.water.Empty) return null;
        var mesh = t.water.mesh;
        var at = mesh.vertices;
        var colors = mesh.colors;
        var own = new List<Vector2>();
        mesh.GetUVs(2, own);
        var cell = grid.CellCenter(x, y);
        int nearest = -1;
        float best = float.PositiveInfinity;
        for (int n = 0; n < at.Length; n++)
        {
            float dx = at[n].x - cell.x, dz = at[n].z - cell.z, d = dx * dx + dz * dz;
            if (d >= best) continue;
            best = d;
            nearest = n;
        }
        if (nearest < 0) return null;
        return (colors[nearest], own[nearest].y);
    }

    // A thin runoff down a hillside is a line one cell wide, whose soft outline would fade it to
    // nothing: running water keeps most of its colour.
    float RunoffOpacity(float gx, float gy, float value) =>
        value >= Outline.Iso && BlendedFlow(gx, gy).sqrMagnitude > 0.04f ? 0.7f : 0f;

    bool Holds(int x, int y, bool frozen)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return false;
        return cells.Read(x, y).wet && world.CellAt(world.Idx(x, y)).Frozen == frozen;
    }

    // In, a bank or the other kind where it meets this kind (water beside ice: they meet under each
    // other's edge), or out. Only beside it: open water far from any ice scores nothing for the ice,
    // or the outline's wobble would lift patches of ice out of the middle of a wide river.
    float Value(int x, int y, bool frozen)
    {
        if (Holds(x, y, frozen)) return 1f;
        if (Holds(x, y, !frozen)) return Beside(x, y, frozen) ? ShoreValue : 0f;
        return grid.World.InBounds(x, y) && cells.Read(x, y).bank ? BankValue : 0f;
    }

    // Whether one of the 8 cells round (x, y) holds this kind.
    bool Beside(int x, int y, bool frozen)
    {
        foreach (var (dx, dy) in Ring)
            if (Holds(x + dx, y + dy, frozen)) return true;
        return false;
    }

    // Ice stands a hair above the water it meets, so the two never flicker where they overlap.
    float? TopOf(int x, int y, bool frozen)
    {
        if (!Holds(x, y, frozen)) return null;
        // Wet since the pass under way began: it has no level yet, and the cells round it speak for it.
        float top = cells.Read(x, y).top;
        return float.IsNaN(top) ? null : top + (frozen ? IceLift : 0f);
    }

    // Opaque inside; toward an edge on open ground, fading out (an edge under a bank is hidden
    // by the bank anyway). Ice stays solid.
    static float Opacity(float value, bool frozen) =>
        frozen ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value));

    public void Destroy()
    {
        foreach (var t in tiles.Values)
        {
            t.water.Destroy();
            t.ice.Destroy();
        }
        tiles.Clear();
        checkSurface?.Destroy();
        cells.Release();
        Object.Destroy(waterMaterial);
        Object.Destroy(root);
    }
}
