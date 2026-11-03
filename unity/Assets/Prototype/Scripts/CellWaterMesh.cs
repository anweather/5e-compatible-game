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
    // By cell, for the levels' work over every wet cell: whether it's wet, and its drawn top (NaN for none).
    readonly bool[] wetCell;
    float[] tops, nextTops;
    // The cells with a top now, and those with one in nextTops.
    List<int> topped = new(), nextTopped = new();
    // Tiles to redraw at the next Rebuild (a tile key: ty * tilesX + tx), and each tile's surfaces.
    readonly HashSet<int> dirtyTiles = new();
    readonly Dictionary<int, (ContourSurface water, ContourSurface ice)> tiles = new();
    readonly int tilesX;
    readonly GameObject root;
    // By cell, for the outline's reads: what FindBanks found, and the cells it marked, to clear them.
    readonly bool[] bankCell;
    List<int> banks = new(), nextBanks = new();
    // Banks and the wet cells beside them: the outline doesn't wobble there, or it would pull
    // back off the bank's slope (a gap) or climb over it (water showing through the ground).
    readonly bool[] calmCell;
    List<int> calm = new(), nextCalm = new();

    public CellWaterMesh(ChemGrid grid, Material waterMaterial, Material iceMaterial)
    {
        this.grid = grid;
        // One instance of the water material for every tile, so the ripples drift together as the lake's do.
        this.waterMaterial = new Material(waterMaterial) { name = waterMaterial.name };
        ownColor = this.waterMaterial.HasProperty(ColorId) ? this.waterMaterial.GetColor(ColorId) : Color.white;
        this.iceMaterial = iceMaterial;
        tilesX = (grid.World.W + TileCells - 1) / TileCells;
        int cells = grid.World.W * grid.World.H;
        wetCell = new bool[cells];
        tops = new float[cells];
        nextTops = new float[cells];
        System.Array.Fill(tops, float.NaN);
        caps = new float[cells];
        groundOf = new float[cells];
        groundPass = new int[cells];
        bankCell = new bool[cells];
        calmCell = new bool[cells];
        flowOf = new Vector2[cells];
        flowRead = new int[cells];
        // Read for every cell and point of a tile, so made once: a lambda here would be garbage each call.
        levelOver = levelBudget.Over;
        waterValue = (x, y) => Value(x, y, false);
        iceValue = (x, y) => Value(x, y, true);
        waterTop = (x, y) => TopOf(x, y, false);
        iceTop = (x, y) => TopOf(x, y, true);
        waterHeight = (gx, gy) => ContourSurface.BlendAround(gx, gy, waterTop, 0f);
        iceHeight = (gx, gy) => ContourSurface.BlendAround(gx, gy, iceTop, 0f);
        waterColor = (gx, gy, v) => new Color(1f, 1f, 1f, Mathf.Max(Opacity(v, false), RunoffOpacity(gx, gy, v)));
        iceColor = (gx, gy, v) => new Color(1f, 1f, 1f, Mathf.Max(Opacity(v, true), RunoffOpacity(gx, gy, v)));
        calmness = (x, y) => grid.World.InBounds(x, y) && calmCell[grid.World.Idx(x, y)] ? 0f : 1f;
        flowX = (x, y) => FlowOf(x, y).x;
        flowY = (x, y) => FlowOf(x, y).y;
        root = new GameObject("Water and ice");
        root.transform.SetParent(grid.transform, false);
    }

    /// <summary>The water's colour as drawn.</summary>
    public Color Color => waterMaterial.GetColor(ColorId);

    /// <summary>The rules switched: the water's colour, tinted from its art (ChemGrid.WorldLook.cs).</summary>
    public void Recolour(Color tint) => this.waterMaterial.SetColor(ColorId, ownColor * new Color(tint.r, tint.g, tint.b, 1f));

    /// <summary>A cell's water changed (or its ground did): the tiles round it are redrawn at the next Rebuild.</summary>
    public void Changed(int x, int y, bool isWet)
    {
        int key = grid.World.Idx(x, y);
        var c = grid.World.Cells[key];
        int level = isWet ? c.Water : 0;
        levels.TryGetValue(key, out int was);
        levels[key] = level;
        // An outline moves only when a cell turns wet, dry, or to or from ice. A level alone (the
        // river's changes on most ticks) moves the surface where its top moves: Recompute marks those.
        bool outline = isWet ? wet.Add(key) : wet.Remove(key);
        wetCell[key] = isWet;
        outline |= isWet && c.Frozen ? ice.Add(key) : ice.Remove(key);
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
        float top = tops[grid.World.Idx(x, y)];
        return float.IsNaN(top) ? fallback : top;
    }

    /// <summary>The top of the water or ice as drawn at a point in grid units, blended between the cells as its mesh is, or <paramref name="fallback"/>.</summary>
    public float TopAt(float gx, float gy, float fallback) => ContourSurface.BlendAround(gx, gy,
        (x, y) => grid.World.InBounds(x, y) && !float.IsNaN(tops[grid.World.Idx(x, y)]) ? tops[grid.World.Idx(x, y)] : null, fallback);

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
            if (!Mathf.Approximately(tops[key], nextTops[key]))
            {
                MarkAround(key % world.W, key / world.W, "water.mark.top");
                PerfCounts.Add("water.tops_moved");
                if (PerfCounts.On && Mathf.Abs(tops[key] - nextTops[key]) < 0.02f) PerfCounts.Add("water.tops_moved_under_2cm");
            }
        foreach (int key in topped) tops[key] = float.NaN;
        foreach (int key in nextTopped) tops[key] = nextTops[key];
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
                if (world.InBounds(x, y) && wetCell[world.Idx(x, y)]) return true;
        return false;
    }

    // --- heights -------------------------------------------------------------------

    // Reused by ComputeTops, so working out the levels allocates nothing: the cap on each wet
    // cell, and each cell's ground, read once a pass (groundPass says which pass read it).
    readonly float[] caps, groundOf;
    readonly int[] groundPass;
    int pass;
    readonly Queue<int> queueWork = new();

    bool Caps(System.Func<bool> over)
    {
        for (; stepAt < nextTopped.Count; stepAt++)
        {
            if (TimeToStop(over)) return false;
            int key = nextTopped[stepAt];
            caps[key] = LowestBank(key);
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
        var cap = caps;
        var queue = queueWork;
        float perUnit = grid.stepHeight / world.Rules.Sim.WaterUnitsPerStep;
        while (queue.Count > 0)
        {
            stepAt++;
            if (TimeToStop(over)) return false;
            int key = queue.Dequeue();
            int x = key % world.W, y = key / world.W;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + World.DirX(d), ny = y + World.DirY(d);
                if (!world.InBounds(nx, ny)) continue;
                int n = world.Idx(nx, ny);
                if (!wetCell[n] || cap[n] <= cap[key] || Standing(n, perUnit) > Standing(key, perUnit) + perUnit) continue;
                cap[n] = cap[key];
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
            var c = world.Cells[key];
            float ground = Ground(key);
            float top = Mathf.Min(ground + c.Water * perUnit, caps[key] - Inset);
            if (MeetsLowGround(key, ground)) top = Mathf.Min(top, ground + Film);
            // Ice on the lake floats at the lake's surface, however deep the water under it.
            nextTops[key] = Mathf.Max(Mathf.Max(ground + Film, top), grid.LakeLevelAt(x, y, c) + Film);
        }
        return true;
    }

    // A cell's ground, read once a pass: every wet cell asks after all 8 cells round it.
    float Ground(int key)
    {
        if (groundPass[key] == pass) return groundOf[key];
        groundPass[key] = pass;
        return groundOf[key] = OwnGround(key);
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
            if (wetCell[n]) continue;
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
            if (!wetCell[n] && Ground(n) <= own + Level) return true;
        }
        return false;
    }

    // Grounds closer than this count as level.
    const float Level = 0.01f;

    float OwnGround(int key) => grid.GroundTop(key % grid.World.W, key / grid.World.W, grid.World.Cells[key]);

    // Where a wet cell's water stands: its ground and its water's depth.
    float Standing(int key, float perUnit) => Ground(key) + grid.World.Cells[key].Water * perUnit;

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
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Ring[d].dx, ny = y + Ring[d].dy;
                if (!world.InBounds(nx, ny)) continue;
                int n = world.Idx(nx, ny);
                if (wetCell[n] || Ground(n) <= tops[key]) continue;
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
        Replace(banks, nextBanks, bankCell);
        Replace(calm, nextCalm, calmCell);
        (banks, nextBanks) = (nextBanks, banks);
        (calm, nextCalm) = (nextCalm, calm);
        nextBanks.Clear();
        nextCalm.Clear();
        NextStep(LevelStep.Resting);
        restingSince = Time.frameCount;
    }

    // Sets the marks to the new list. A cell that gained or lost one moves the outline round it,
    // with no level moving, so those tiles are redrawn too.
    void Replace(List<int> was, List<int> now, bool[] marks)
    {
        var world = grid.World;
        foreach (int key in now)
        {
            if (marks[key]) continue;
            marks[key] = true;
            changedMarks.Add(key);
        }
        foreach (int key in was) goneMarks.Add(key);
        foreach (int key in now) goneMarks.Remove(key);
        foreach (int key in goneMarks)
        {
            marks[key] = false;
            changedMarks.Add(key);
        }
        foreach (int key in changedMarks) MarkAround(key % world.W, key / world.W, "water.mark.bank");
        goneMarks.Clear();
        changedMarks.Clear();
    }

    readonly HashSet<int> goneMarks = new(), changedMarks = new();

    static readonly (int dx, int dy)[] Ring = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    // The water (or the ice) of one tile's wet cells: their outline, each point at the blended top
    // of the wet cells around it, fading out toward an edge on open ground.
    void Draw(ContourSurface surface, RectInt box, bool frozen)
    {
        FindRocksNear(box);
        surface.Build(box, frozen ? iceValue : waterValue, frozen ? iceHeight : waterHeight, frozen ? iceColor : waterColor, calmness,
            frozen ? null : (gx, gy, v) => FlowAndFoam(surface, gx, gy, v));
    }

    readonly System.Func<int, int, float> waterValue, iceValue, calmness;
    readonly System.Func<int, int, float?> waterTop, iceTop, flowX, flowY;
    readonly System.Func<float, float, float> waterHeight, iceHeight;
    readonly System.Func<float, float, float, Color> waterColor, iceColor;

    // ---------- flow (Prototype/CellWater: its ripples move with the water, foam where it's broken) ----------

    // Each wet cell's flow (world x, z in m/s): the river's current along its course, or water on a
    // hillside running down it; kept until the ground changes (the river's line doesn't).
    readonly Vector2[] flowOf;
    // Which reading of the ground each cell's flow is from: GroundChanged starts a new one.
    readonly int[] flowRead;
    int flowPass = 1;
    RockWake[] rocks;
    readonly List<RockWake> rocksNear = new();
    // A rock's wake reaches this many of its radii (FlowAndFoam).
    const float WakeRadii = 7f;
    // Runoff: metres a second per unit of slope, and its fastest.
    const float RunoffSpeed = 4f, RunoffMax = 1.6f;

    Vector2 FlowOf(int x, int y)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return Vector2.zero;
        int key = world.Idx(x, y);
        if (flowRead[key] == flowPass) return flowOf[key];
        PerfCounts.Add("water.flow_misses");
        var c = world.Cells[key];
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundTop(x, y, c);
        var current = WaterCurrents.Instance != null ? WaterCurrents.Instance.At(at) : Vector3.zero;
        var flow = new Vector2(current.x, current.z);
        if (flow.sqrMagnitude < 0.0001f && c.T != TerrainIds.River) flow = Downhill(x, y);
        flowRead[key] = flowPass;
        flowOf[key] = flow;
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
            if (Mathf.Abs(at.x - middle.x) <= reach && Mathf.Abs(at.z - middle.z) <= reach) rocksNear.Add(rock);
        }
    }

    // A point's flow, and its foam: where moving water meets its edge (a bank, the end of a runoff)
    // and round the river's rocks, trailing off downstream.
    Vector3 FlowAndFoam(ContourSurface surface, float gx, float gy, float value)
    {
        var flow = BlendedFlow(gx, gy);
        float speed = flow.magnitude;
        float edge = 1f - Mathf.InverseLerp(Outline.Iso, Outline.Iso + 0.35f, value);
        // Banks foam where the river runs; a runoff is all edge, and only flecks.
        var c = grid.World.InBounds((int)gx, (int)gy) ? grid.World.At((int)gx, (int)gy) : null;
        float foam = edge * Mathf.Clamp01(speed / 1.2f) * (c != null && c.T == TerrainIds.River ? 1f : 0.15f);
        var p = surface.Flat(gx, gy);
        foreach (var rock in rocksNear)
        {
            var d = new Vector2(p.x - rock.transform.position.x, p.z - rock.transform.position.z);
            var down = new Vector2(rock.downstream.x, rock.downstream.z).normalized;
            float along = Vector2.Dot(d, down), across = Mathf.Abs(d.x * down.y - d.y * down.x);
            // Round its upstream face, then a wake three times as long as it is wide.
            float reach = along > 0f ? new Vector2(along / 3f, across).magnitude : d.magnitude;
            foam = Mathf.Max(foam, 1f - Mathf.InverseLerp(rock.radius * 0.8f, rock.radius * 2.2f, reach));
        }
        return new Vector3(flow.x, flow.y, foam);
    }

    // A thin runoff down a hillside is a line one cell wide, whose soft outline would fade it to
    // nothing: running water keeps most of its colour.
    float RunoffOpacity(float gx, float gy, float value) =>
        value >= Outline.Iso && BlendedFlow(gx, gy).sqrMagnitude > 0.04f ? 0.7f : 0f;

    bool Holds(int x, int y, bool frozen)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return false;
        int key = world.Idx(x, y);
        return wetCell[key] && world.Cells[key].Frozen == frozen;
    }

    // In, a bank or the other kind where it meets this kind (water beside ice: they meet under each
    // other's edge), or out. Only beside it: open water far from any ice scores nothing for the ice,
    // or the outline's wobble would lift patches of ice out of the middle of a wide river.
    float Value(int x, int y, bool frozen)
    {
        if (Holds(x, y, frozen)) return 1f;
        if (Holds(x, y, !frozen)) return Beside(x, y, frozen) ? ShoreValue : 0f;
        return grid.World.InBounds(x, y) && bankCell[grid.World.Idx(x, y)] ? BankValue : 0f;
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
        float top = tops[grid.World.Idx(x, y)];
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
        Object.Destroy(waterMaterial);
        Object.Destroy(root);
    }
}
