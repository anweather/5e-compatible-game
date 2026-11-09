using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// How the grid looks. The valley's chunk terrains are the ground: the cells
// shape and paint them (ValleyChemistry). Each frame Render() walks the cells
// and, for any cell whose state changed, updates what stands on it: its
// dressing (CellDressing: brambles, a stump, planks, crags), its invisible
// blocker (CellBlockers), its water or ice (one surface over all the wet
// cells: CellWaterMesh), oil, grease, mud or glass (one film: CellSlickMesh), thorns (CellThorns), and its fire, gas and
// charge effects (ChemGrid.Effects.cs).
// Reshaped ground eases to its new height (ChemGrid.Shaping.cs). A small pool
// of flickering lights follows the biggest fires (FireLightPool). Cubes stand
// in for any effect prefab that isn't assigned (for example before the Asset
// Store packs are restored).
public partial class ChemGrid
{
    [Header("Effects (optional; cubes stand in when missing)")]
    [Tooltip("What flames are drawn with (Prototype/Flame); the builder sets it, which puts it in the build.")]
    public Shader flameShader;
    [Tooltip("The flame flipbooks, tiny, medium and large (the Particle Pack's); the builder sets them.")]
    public Texture2D[] flameSheets;
    public GameObject smokeFx;
    public GameObject steamFx;
    public GameObject sparksFx;
    public int maxFireLights = 16;
    [Tooltip("Optional: the material of the cells' water (vertex alpha fades its edges). A plain see-through blue stands in.")]
    public Material waterMaterial;
    [Tooltip("Optional: the material of oil and grease films (tinted by vertex colours). One is made at runtime if missing.")]
    public Material slickMaterial;
    [Tooltip("What lava is drawn with (Prototype/CellLava); the builder sets it, which puts it in the build.")]
    public Shader lavaShader;
    [Tooltip("Draws the aim marker (AimMarker). The builder sets it, so the shader is in the build.")]
    public Shader aimShader;
    [Tooltip("Optional: rock models a raised wall is built of, one per square, sized to cover its face (CellDressing). Without them, rough shapes stand in.")]
    public GameObject[] wallRocks;
    [Tooltip("Optional: smaller stones set on a raised wall's top to break its line.")]
    public GameObject[] wallStones;

    // What is currently shown for one cell, so a cell is only redrawn when it changes. One a cell, so what few cells show at
    // once (a cover, gas, sparks, thorns, a fire's level) is kept apart in a Rare, made when one is first set: 56 bytes a
    // cell of bare ground rather than 136 (cell-memory: 3 million of them on a 1728 x 1728 map). Read and set alike.
    sealed class View
    {
        sealed class Rare
        {
            public GameObject cover, gas, spark, thorns, vines, gasPrefab;
            public string coverId, gasKind;
            public int fireLevel, heatLevel, rustLevel;
            public bool charged, flashed, fogged;
        }

        Rare rare;
        Rare Kept => rare ??= new Rare();

        public GameObject dressing;
        public string terrain;
        public int height, elevation;
        public int key = int.MinValue;
        public bool wet;

        // A reference is kept as it was given (a destroyed object too, as a field would): only a real null makes no Rare.
        public GameObject cover { get => rare?.cover; set { if (rare != null || (object)value != null) Kept.cover = value; } }
        public GameObject gas { get => rare?.gas; set { if (rare != null || (object)value != null) Kept.gas = value; } }
        public GameObject spark { get => rare?.spark; set { if (rare != null || (object)value != null) Kept.spark = value; } }
        public GameObject thorns { get => rare?.thorns; set { if (rare != null || (object)value != null) Kept.thorns = value; } }
        public GameObject vines { get => rare?.vines; set { if (rare != null || (object)value != null) Kept.vines = value; } }
        // The effect the gas is drawn with: the prefab, or its copy in a look.
        public GameObject gasPrefab { get => rare?.gasPrefab; set { if (rare != null || (object)value != null) Kept.gasPrefab = value; } }
        public string coverId { get => rare?.coverId; set { if (rare != null || value != null) Kept.coverId = value; } }
        public string gasKind { get => rare?.gasKind; set { if (rare != null || value != null) Kept.gasKind = value; } }
        public int fireLevel { get => rare?.fireLevel ?? 0; set { if (rare != null || value != 0) Kept.fireLevel = value; } }
        public int heatLevel { get => rare?.heatLevel ?? 0; set { if (rare != null || value != 0) Kept.heatLevel = value; } }
        public int rustLevel { get => rare?.rustLevel ?? 0; set { if (rare != null || value != 0) Kept.rustLevel = value; } }
        public bool charged { get => rare != null && rare.charged; set { if (rare != null || value) Kept.charged = value; } }
        public bool flashed { get => rare != null && rare.flashed; set { if (rare != null || value) Kept.flashed = value; } }
        public bool fogged { get => rare != null && rare.fogged; set { if (rare != null || value) Kept.fogged = value; } }
    }

    // Lights from the Light spell at once, however many cells are lit.
    const int MaxSpellLights = 8;

    View[] views;
    readonly MaterialCache materials = new();
    CellDressing dressings;
    CellBlockers blockers;
    CellWaterMesh waterSurface;
    CellSlickMesh slicks;
    CellCrackMesh cracks;
    CellLavaMesh lava;
    CellLightPool spellLights;
    CellFxTexture cellFx;
    CellThorns thornClumps;
    CellVines vineClumps;
    FireLightPool fireLights;

    void InitVisuals()
    {
        // A chunk's views are made as it first draws (RenderChunk): 3 million made at once were much of a big map's build.
        views = new View[World.W * World.H];
        dressings ??= new CellDressing(this, materials);
        if (blockers == null) blockers = new CellBlockers(this);
        else blockers.ReleaseAll();
        waterSurface?.Destroy();
        waterSurface = new CellWaterMesh(this, waterMaterial != null ? waterMaterial : materials.Water(),
            materials.Lit("Ice", new Color(0.80f, 0.93f, 1f), 0.97f));
        slicks?.Destroy();
        cracks?.Destroy();
        lava?.Destroy();
        spellLights?.Destroy();
        slicks = new CellSlickMesh(this, slickMaterial != null ? slickMaterial : materials.Slick());
        cracks = new CellCrackMesh(this);
        lava = new CellLavaMesh(this, lavaShader != null ? lavaShader : Shader.Find("Prototype/CellLava"));
        spellLights = new CellLightPool(this, MaxSpellLights);
        cellFx?.Destroy();
        acidWatch.Clear();
        var yard = Yard;
        cellFx = new CellFxTexture(World.W, World.H, new Vector4(1f / cellSize, yard.x, yard.y + yard.height - 1, 0f));
        thornClumps ??= new CellThorns(this, materials);
        vineClumps ??= new CellVines(this, materials);
        boulderRests.Clear();
        shaping.Clear();
        landAreas.Clear();
        landGround.Clear();
        ForgetFx();
        fireLights = new FireLightPool(this, maxFireLights);
    }

    /// <summary>What stands on a cell (its CellDressing: a wall's crags, a stone block), or null; OccluderFade dithers one that hides the wizard.</summary>
    public GameObject DressingAt(int x, int y) =>
        views != null && World != null && x >= 0 && y >= 0 && x < World.W && y < World.H ? views[y * World.W + x]?.dressing : null;

    /// <summary>The top of the water drawn on a cell (CellWaterMesh), or null where none is drawn.</summary>
    public float? DrawnWaterTop(int x, int y)
    {
        float top = waterSurface.Top(x, y, float.NaN);
        return float.IsNaN(top) ? null : top;
    }

    // Every cell once, for a new world: the chunks near the players and the camera now, the rest a few a frame after,
    // nearest first (view-rebuild: every cell of a 1728-cell map at once was 2.3 s of each start and reset). The sleeping
    // chunks' trees and walls get their blockers too, as they draw. Until every chunk has drawn, the old world's spare
    // blockers stay standing (most stand where the new world's will).
    void RenderAll()
    {
        int across = ChunksAcross, down = (World.H + Chunks.Size - 1) >> Chunks.Shift;
        drawn = new bool[across * down];
        undrawn = drawn.Length;
        spareToHide = true;
        DrawNear();
        var focus = Focus();
        toDraw.Clear();
        for (int cy = 0; cy < down; cy++)
            for (int cx = 0; cx < across; cx++)
                if (!drawn[cy * across + cx]) toDraw.Add(cy * across + cx);
        // Nearest first, then by row and column (a stable order: the same on every peer, though only the view reads it).
        toDraw.Sort((a, b) =>
        {
            int da = FocusDistance(focus, a % across, a / across), db = FocusDistance(focus, b % across, b / across);
            return da != db ? da.CompareTo(db) : a.CompareTo(b);
        });
        nextToDraw = 0;
        partRow = 0;
        HideSpareOnceDrawn();
        FinishRender();
    }

    // Chunks not yet drawn since the world was built (RenderAll), by world chunk index (cy * ChunksAcross + cx).
    bool[] drawn = System.Array.Empty<bool>();
    int undrawn;
    bool spareToHide;
    readonly List<int> toDraw = new();
    int nextToDraw;
    readonly List<Vector2Int> focusChunks = new();
    readonly System.Diagnostics.Stopwatch drawTimer = new();

    int ChunksAcross => (World.W + Chunks.Size - 1) >> Chunks.Shift;

    /// <summary>Chunks of this world not drawn yet: they draw a few a frame after a build, nearest the players first.</summary>
    public int ChunksLeftToDraw => undrawn;

    // A frame's drawing of chunks still undrawn, past those near the players: about this many ms (at least one chunk), or
    // in a playtest run this many chunks (ExactFrames).
    const double DrawBudgetMs = 3.0;
    const int ExactChunksAFrame = 2;

    // Where the chunks that must be drawn are: every wizard's (the solo one, or every player's online), the start's and the
    // camera's.
    List<Vector2Int> Focus()
    {
        focusChunks.Clear();
        foreach (var (x, y) in PlayerCells()) focusChunks.Add(new Vector2Int(Chunks.ChunkOf(x), Chunks.ChunkOf(y)));
        focusChunks.Add(new Vector2Int(Chunks.ChunkOf(PlayerStart.x), Chunks.ChunkOf(PlayerStart.y)));
        var cam = Camera.main;
        if (cam != null)
        {
            var cell = CellAt(cam.transform.position);
            if (World.InBounds(cell.x, cell.y)) focusChunks.Add(new Vector2Int(Chunks.ChunkOf(cell.x), Chunks.ChunkOf(cell.y)));
        }
        return focusChunks;
    }

    static int FocusDistance(List<Vector2Int> focus, int cx, int cy)
    {
        int best = int.MaxValue;
        foreach (var f in focus) best = Mathf.Min(best, Mathf.Max(Mathf.Abs(f.x - cx), Mathf.Abs(f.y - cy)));
        return best;
    }

    // Every undrawn chunk within the streaming reach of a focus, drawn now: what a player stands in or walks into.
    void DrawNear()
    {
        if (undrawn == 0) return;
        int reach = World.Rules.Sim.Streaming.Reach, across = ChunksAcross, down = drawn.Length / Mathf.Max(1, across);
        foreach (var f in Focus())
            for (int cy = Mathf.Max(0, f.y - reach); cy <= Mathf.Min(down - 1, f.y + reach); cy++)
                for (int cx = Mathf.Max(0, f.x - reach); cx <= Mathf.Min(across - 1, f.x + reach); cx++)
                    if (!drawn[cy * across + cx]) RenderChunk(cx, cy);
    }

    // The rest a few a frame, nearest first.
    void DrawSome()
    {
        if (undrawn == 0) return;
        DrawNear();
        drawTimer.Restart();
        int across = ChunksAcross, done = 0;
        // A few rows at a time, so a crowded chunk (a town's) is spread over frames too; it counts as drawn once whole.
        while (nextToDraw < toDraw.Count && undrawn > 0)
        {
            if (ExactFrames ? done >= ExactChunksAFrame * Chunks.Size : done > 0 && drawTimer.Elapsed.TotalMilliseconds >= DrawBudgetMs) break;
            int n = toDraw[nextToDraw];
            if (drawn[n])
            {
                nextToDraw++;
                partRow = 0;
                continue;
            }
            int cx = n % across, cy = n / across, y0 = cy << Chunks.Shift, y1 = Mathf.Min(World.H, y0 + Chunks.Size);
            int from = y0 + partRow, to = Mathf.Min(y1, from + RowsAStep);
            if (partRow == 0) MakeViews(cx, cy);
            RenderRows(cx, from, to);
            done += to - from;
            partRow = to - y0;
            if (to < y1) continue;
            MarkDrawn(n);
            nextToDraw++;
            partRow = 0;
        }
        HideSpareOnceDrawn();
    }

    // How many of its rows DrawSome has drawn of the chunk it's on, and how many it draws a step.
    int partRow;
    const int RowsAStep = 8;

    void HideSpareOnceDrawn()
    {
        if (undrawn > 0 || !spareToHide) return;
        spareToHide = false;
        blockers.HideSpare();
    }

    /// <summary>Whether a chunk of this world has been drawn since it was built (it draws on first sight, or soon after).</summary>
    public bool IsChunkDrawn(int cx, int cy)
    {
        int across = ChunksAcross, n = cy * across + cx;
        return cx >= 0 && cy >= 0 && cx < across && n < drawn.Length && drawn[n];
    }

    // Each frame: the simulated chunks' cells (a sleeping chunk doesn't change; ChunkStreaming).
    // The active chunks are looked over a third a frame: cells change a few times a second at most,
    // and walking 150 000 of them every frame was 4 ms with nothing to show for it.
    const int RenderTurns = 3;
    int renderTurn;

    void Render()
    {
        renderTurn = (renderTurn + 1) % RenderTurns;
        int turn = 0;
        DrawSome();
        foreach (var (cx, cy) in World.ActiveChunks)
        {
            if (turn++ % RenderTurns != renderTurn) continue;
            // A chunk not drawn yet draws whole, in its turn (DrawSome); a new world's chunks are all active at first.
            if (IsChunkDrawn(cx, cy)) RenderChunk(cx, cy);
        }
        RenderFresh(out int fresh);
        RenderSwept();
        FinishRender();
        AnnounceFresh(fresh);
    }

    // The inactive chunks still to look over after a sweep (RedrawEveryChunk), and how many a frame: a cell
    // that hasn't changed costs a hash, so a chunk is about what a third of the active ones costs a frame.
    readonly Queue<Vector2Int> sweptChunks = new();
    const int SweptChunksAFrame = 6;

    /// <summary>Inactive chunks redrawn after a sweep, for the probe; and how many are still to come.</summary>
    public int SweptChunksDrawn { get; private set; }
    public int SweptChunksLeft => sweptChunks.Count;

    /// <summary>How many sweeps have queued every chunk (RedrawEveryChunk), for the probe: one a switch, however many mods it took.</summary>
    public int EveryChunkRedraws { get; private set; }

    /// <summary>
    /// A revert or a lapse swept its mod's materials from every cell (World.Lapse.cs), inactive chunks too, which
    /// Render doesn't walk: each inactive chunk is looked over once, a few a frame, so the lapse clears everywhere.
    /// </summary>
    void RedrawEveryChunk()
    {
        if (views == null) return;
        EveryChunkRedraws++;
        sweptChunks.Clear();
        int cw = (World.W + Chunks.Size - 1) >> Chunks.Shift, chh = (World.H + Chunks.Size - 1) >> Chunks.Shift;
        for (int cy = 0; cy < chh; cy++)
            for (int cx = 0; cx < cw; cx++) sweptChunks.Enqueue(new Vector2Int(cx, cy));
    }

    void RenderSwept()
    {
        for (int n = 0; n < SweptChunksAFrame && sweptChunks.Count > 0; n++)
        {
            var ch = sweptChunks.Dequeue();
            if (World.IsActive(ch.x, ch.y)) continue; // Render walks it
            SweptChunksDrawn++;
            RenderChunk(ch.x, ch.y);
        }
    }

    // Every cell of a chunk, looked over.
    void RenderChunk(int cx, int cy)
    {
        int n = cy * ChunksAcross + cx;
        if (n < drawn.Length && !drawn[n])
        {
            MakeViews(cx, cy);
            MarkDrawn(n);
        }
        int y0 = cy << Chunks.Shift;
        RenderRows(cx, y0, Mathf.Min(World.H, y0 + Chunks.Size));
    }

    // Rows y0 to y1 (not included) of chunk column cx.
    void RenderRows(int cx, int y0, int y1)
    {
        int x0 = cx << Chunks.Shift, x1 = Mathf.Min(World.W, x0 + Chunks.Size);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) RenderCell(x, y);
    }

    // A chunk's views, made as it first draws (a drawn chunk's are all there).
    void MakeViews(int cx, int cy)
    {
        int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
        int x1 = Mathf.Min(World.W, x0 + Chunks.Size), y1 = Mathf.Min(World.H, y0 + Chunks.Size);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) views[y * World.W + x] ??= new View();
    }

    void MarkDrawn(int n)
    {
        if (drawn[n]) return;
        drawn[n] = true;
        undrawn--;
    }

    /// <summary>Inactive chunks redrawn because the dragon's breath reset them, for the probe.</summary>
    public int FreshInactiveDrawn { get; private set; }

    // A chunk the dragon's breath reset may be inactive, and those aren't walked above.
    void RenderFresh(out int fresh)
    {
        fresh = freshChunks.Count;
        foreach (var f in freshChunks)
        {
            if (World.IsActive(f.CX, f.CY)) continue;
            FreshInactiveDrawn++;
            RenderChunk(f.CX, f.CY);
        }
    }

    void AnnounceFresh(int fresh)
    {
        for (int i = 0; i < fresh; i++) ChunkFreshened?.Invoke(freshChunks[i]);
        freshChunks.RemoveRange(0, fresh);
    }

    const double BusyStepMs = 3.0;
    readonly System.Diagnostics.Stopwatch finishTimer = new();

    /// <summary>Of RenderMs, the milliseconds the last frame spent on the water, slicks and easing ground.</summary>
    public double SurfacesMs { get; private set; }

    // Profiler markers (Window > Analysis > Profiler) round the parts of drawing the cells.
    static readonly Unity.Profiling.ProfilerMarker GroundMarker = new("ChemGrid.ChangeGround"),
        WaterMarker = new("ChemGrid.RenderWater"), FireMarker = new("ChemGrid.RenderFire"), GasMarker = new("ChemGrid.RenderGas"),
        FinishMarker = new("ChemGrid.FinishRender"), WaterMeshMarker = new("ChemGrid.WaterMesh"),
        SlickMeshMarker = new("ChemGrid.SlickMesh"), ShapingMarker = new("ChemGrid.Shaping"), RecycleMarker = new("ChemGrid.RecycleFx");

    void FinishRender()
    {
        using var marked = FinishMarker.Auto();
        finishTimer.Restart();
        // A frame a tick has already spent draws no tiles: the two together made the slowest frames.
        bool busy = !ExactFrames && StepMs >= BusyStepMs;
        using (WaterMeshMarker.Auto()) waterSurface.Rebuild(busy);
        using (SlickMeshMarker.Auto()) slicks.Rebuild(busy);
        cracks.Rebuild();
        lava.Rebuild();
        spellLights.Place();
        cellFx.Advance(Time.deltaTime);
        WatchAcidCells();
        ForgetLookFire();
        RenderWhirls();
        RenderTriggered();
        RenderStars();
        RenderFlashes();
        RenderMovers();
        RenderChargeRun();
        RenderShedStrikes();
        materials.ScrollWater(Time.time);
        waterSurface.Scroll(Time.time);
        using (ShapingMarker.Auto()) AnimateShaping();
        using (RecycleMarker.Auto()) RecycleFx();
        SurfacesMs = finishTimer.Elapsed.TotalMilliseconds;
    }

    void RenderCell(int x, int y)
    {
        int index = y * World.W + x;
        var c = World.Cells[index];
        var v = views[index];
        int key = Key(c);
        if (key == v.key && v.terrain == c.T && v.height == c.Height) return;
        v.key = key;

        bool redressed = v.terrain != c.T || v.height != c.Height;
        if (redressed || v.coverId != c.Cover)
            using (GroundMarker.Auto()) ChangeGround(v, c, x, y);
        RenderHeat(v, c, redressed);
        RenderRust(v, c, redressed);
        float groundTop = GroundTop(x, y, c);
        // The look from the cell's own terrain definition (World.TerrainOf's cache), not looked up by id twice a cell.
        var look = World.TerrainOf(c).Look;
        float stands = look != null ? look.Height : 0f;
        float ground = CellDressing.IsDressed(look) ? groundTop : groundTop + stands;
        var center = CellCenter(x, y);
        bool lake = valley != null && valley.IsLake(c, x, y);

        using (WaterMarker.Auto()) ground = RenderWater(v, c, x, y, ground, groundTop, lake);
        RenderCover(v, c, x, y, center, ground);
        using (FireMarker.Auto()) RenderFire(v, c, x, y, center, ground + ValleyChemistry.FireHeight(c.T), c.T == TerrainIds.Tree ? 1.8f : 1f);
        using (GasMarker.Auto()) RenderGas(v, c, x, y, center, ground, GasDrawnAt(x, y));
        // A lightning strike across the lake charges thousands of cells; on the lake, sparks on
        // one cell in 25 read the same without thousands of effects.
        if (!lake || (x % 5 == 0 && y % 5 == 0)) RenderCharge(v, c, x, y, center, ground);
    }

    // A new terrain, height or cover: the terrain is reshaped (eased, see ChemGrid.Shaping.cs)
    // and repainted, and the cell's dressing and blocker rebuilt where its ground stands now. A snow
    // bank coming or going reshapes it too (CellHeightfield stands the ground higher under one).
    void ChangeGround(View v, Cell c, int x, int y)
    {
        int elevation = World.ElevationUnits(c);
        bool reshaped = v.terrain != null && v.elevation != elevation;
        bool rebuild = v.terrain != c.T || v.height != c.Height;
        bool banked = CellPaint.IsBank(v.coverId, World.Rules) != CellPaint.IsBank(c.Cover, World.Rules);
        if (v.terrain != c.T) TerrainTurned(v.terrain, c, x, y);
        if (valley != null) valley.CellChanged(x, y, v.terrain, c.T, reshaped || banked);
        if (rebuild) Redress(v, c, x, y, reshaped);
        if (reshaped || banked)
        {
            waterSurface.GroundChanged(x, y);
            slicks.GroundChanged(x, y);
            lava.GroundChanged();
        }
        v.terrain = c.T;
        v.height = c.Height;
        v.elevation = elevation;
        v.coverId = c.Cover;
    }

    // A wall coming down (roots, a blast) throws up dust and stone; rust scoured off flies in flakes.
    void TerrainTurned(string from, Cell c, int x, int y)
    {
        if (from == null) return;
        var at = CellCenter(x, y);
        at.y = GroundTop(x, y, c);
        if (from == TerrainIds.RustedMetal && c.T == TerrainIds.Metal) BurstFx.Play(BurstFx.Kind.Chips, at, 1f, null, CellDressing.Look(World.Rules, TerrainIds.RustedMetal).color);
        if (c.T != TerrainIds.Rubble || !World.Rules.Terrain.TryGetValue(from, out var was) || !was.Has("blocking")) return;
        Dust(at + Vector3.up * 0.4f);
        BurstFx.Play(BurstFx.Kind.Chips, at + Vector3.up * 0.3f, 1.4f, null, CellDressing.Look(World.Rules, TerrainIds.Rubble).color);
    }

    // The cell's dressing and blocker, built again where its ground stands now. A rock wall growing
    // taller rises from its old top, not from the flat ground under it.
    void Redress(View v, Cell c, int x, int y, bool reshaped)
    {
        float shown = GroundTop(x, y, c);
        if (reshaped) shown = StandsAlone(c) && v.dressing != null ? v.dressing.transform.position.y : ShownGround(x, y, c);
        if (v.dressing != null) Destroy(v.dressing);
        v.dressing = dressings.Build(c, x, y, shown);
        blockers.Place(x, y, c, shown);
        if (reshaped) StartShaping(v, x, y, shown);
    }

    /// <summary>A cell of a row house's wall stands this tall (0: no longer one): its blocker says so at once, and what the cell drew on its own goes or comes back.</summary>
    public void BuiltWall(int x, int y, float tall)
    {
        int index = World.Idx(x, y);
        blockers.BuiltWall(index, tall);
        var c = World.At(x, y);
        if (views != null && views[index] != null) Redress(views[index], c, x, y, false);
        else blockers.Place(x, y, c, GroundTop(x, y, c));
    }

    /// <summary>Whether a row house draws this cell: its own dressing would stand inside the wall.</summary>
    public bool IsBuiltWall(int x, int y, Cell c) => blockers.IsBuiltWall(World.Idx(x, y), c);

    // Hot metal (Cell.Heat, counting down from its terrain's hot.for) glows in this many steps, so a
    // cooling plate is redrawn a few times, not every tick.
    const int HeatSteps = 6;

    // How hot a cell looks: 0 cold, up to HeatSteps just heated.
    int HeatLevel(Cell c)
    {
        if (c.Heat <= 0) return 0;
        int hotFor = Mathf.Max(1, World.TerrainOf(c).HotFor);
        return Mathf.Clamp(Mathf.CeilToInt(c.Heat * HeatSteps / (float)hotFor), 1, HeatSteps);
    }

    // Hot metal's glow on its plate (CellDressing.Glow), set again when the plate is rebuilt.
    void RenderHeat(View v, Cell c, bool redressed)
    {
        int level = HeatLevel(c);
        if (level == v.heatLevel && !(redressed && level > 0)) return;
        v.heatLevel = level;
        dressings.Glow(v.dressing, level / (float)HeatSteps);
    }

    // Metal on its way to rust (Cell.Rust, counting to its terrain's rust.every) shows it in this many steps.
    const int RustSteps = 4;

    int RustLevel(Cell c)
    {
        if (c.Rust <= 0) return 0;
        int every = World.TerrainOf(c).RustEvery;
        return every > 0 ? Mathf.Min(RustSteps, c.Rust * (RustSteps + 1) / every) : 0;
    }

    // Each step further along turns the plate a little more; a plate built new starts clean.
    void RenderRust(View v, Cell c, bool redressed)
    {
        int level = RustLevel(c), from = redressed ? 0 : v.rustLevel;
        if (level > from) dressings.Tarnish(v.dressing, (level - from) * 0.28f);
        v.rustLevel = level;
    }

    // A hash of everything drawn for a cell apart from its terrain.
    int Key(Cell c)
    {
        unchecked
        {
            int k = c.Water;
            k = k * 31 + c.Kind;
            k = k * 31 + (c.Frozen ? 1 : 0);
            k = k * 31 + (c.Cover?.GetHashCode() ?? 0);
            k = k * 31 + c.Fire;
            // Weather's veil of fog draws nothing, so its coming and going over every cell redraws nothing.
            bool gas = c.Gas != null && !FogBank.IsVeil(c.Gas, c.GasAmt);
            k = k * 31 + (gas ? c.Gas.GetHashCode() : 0);
            k = k * 31 + (gas ? c.GasAmt : 0);
            k = k * 31 + (c.Charge > 0 ? 1 : 0);
            k = k * 31 + HeatLevel(c);
            k = k * 31 + RustLevel(c);
            k = k * 31 + (c.Star > 0 ? 1 : 0);
            k = k * 31 + (c.Cracked != 0 ? 1 : 0);
            k = k * 31 + (c.Lit > 0 ? 1 : 0);
            return k;
        }
    }

    // --- water and ice ----------------------------------------------------------

    // Water the surface draws: at least 2 units, or any water on low or dug ground (a pit, the
    // river, a trench). A lone unit on open ground is the film rain leaves on a quarter of the
    // meadow, a cell here and there; drawn, it turned every tile into thousands of specks and
    // stalled the frame for seconds while rain fell. (A wet look for the ground in rain would
    // be the terrain's to show.) Ice follows the same rule.
    bool Standing(Cell c) => c.Water >= 2 || c.Height < 0 || World.TerrainOf(c).Elevation < 0;

    // Water joins the wet cells' surface (CellWaterMesh); the lake's own surface stands for its
    // cells, except where they freeze: lake ice joins the ice, at the lake's surface. Returns the
    // height that covers and effects sit on.
    float RenderWater(View v, Cell c, int x, int y, float ground, float groundTop, bool lake)
    {
        bool wet = c.Water > 0 && (!lake || c.Frozen) && Standing(c);
        if (wet || v.wet) waterSurface.Changed(x, y, wet);
        v.wet = wet;
        if (lake) return c.Water > 0 ? Mathf.Max(ground, valley.lakeLevel) : ground;
        return wet ? Mathf.Max(ground, waterSurface.Top(x, y, groundTop + WaterDepth(c))) : ground;
    }

    float WaterDepth(Cell c) => c.Water / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;

    /// <summary>The lake's surface if this cell is lake (its water is drawn by the lake), else negative infinity.</summary>
    public float LakeLevelAt(int x, int y, Cell c) =>
        valley != null && valley.IsLake(c, x, y) ? valley.lakeLevel : float.NegativeInfinity;

    /// <summary>The top of the water or ice drawn at a point in grid units (see CellWaterMesh.TopAt).</summary>
    public float DrawnWaterTopAt(float gx, float gy, float fallback) => waterSurface.TopAt(gx, gy, fallback);

    /// <summary>For a performance pass: the water tiles redrawn, and how many came out different (CellWaterMesh.CheckTiles).</summary>
    public (int tiles, int different) CheckWaterTiles() => waterSurface.CheckTiles();

    /// <summary>Height of the water surface in a cell, as drawn (its ground when the cell is dry).</summary>
    public float WaterSurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return 0f;
        float top = GroundTop(x, y, c) + WaterDepth(c);
        return valley != null && c.Water > 0 && valley.IsLake(c, x, y) ? Mathf.Max(top, valley.lakeLevel) : top;
    }

    // --- covers ------------------------------------------------------------------

    // Slippery covers, a mod's own covers (and mud and glass ground) are a glossy film over the cells (CellSlickMesh); ash
    // (whatever the rules say fire leaves: CellPaint.IsAsh) is the terrain's own charred paint (CellPaint), or a sheet
    // in its colour without a valley. A mod's own ash wears its film over the charred paint as well.
    void RenderCover(View v, Cell c, int x, int y, Vector3 center, float ground)
    {
        bool cooled = lava.Changed(x, y, c, LavaHeat(c), MoltenTint(c, x, y));
        if (groundScoped) slicks.Scoped(x, y, GroundFilmAt(c, x, y));
        slicks.Crust(x, y, c, cooled);
        slicks.Changed(x, y, c);
        cracks.Changed(x, y, c, ground);
        spellLights.Changed(x, y, c, ground);
        if (valley != null) valley.PaneCracked(World.Idx(x, y), c.Cracked != 0);
        cellFx.Oil(x, y, slicks.IsSheen(c.Cover) && c.Water > 0 && !c.Frozen);
        RenderThorns(v, c, x, y, center, ground);
        RenderVines(v, c, x, y, center);
        if (valley != null || slicks.IsFilm(c.Cover) || !CellPaint.IsAsh(c.Cover, World.Rules))
        {
            Hide(v.cover);
            return;
        }
        var film = v.cover = Ensure(v.cover, "Cover", center);
        film.transform.position = new Vector3(center.x, ground + 0.02f, center.z);
        film.transform.localScale = new Vector3(0.96f * cellSize, 0.03f, 0.96f * cellSize);
        film.GetComponent<Renderer>().sharedMaterial = materials.Lit("Cover-" + c.Cover, CoverColor(c.Cover, new Color(0.35f, 0.34f, 0.33f)), 0.05f);
    }

    // Spike Growth's thorns: a clump of spikes on the cell (CellThorns) while the cover is there.
    void RenderThorns(View v, Cell c, int x, int y, Vector3 center, float ground)
    {
        if (c.Cover != CoverIds.Thorns)
        {
            if (v.thorns != null) Destroy(v.thorns);
            v.thorns = null;
            return;
        }
        if (v.thorns == null) v.thorns = thornClumps.Build(x, y, center, ground);
        else CellThorns.Place(v.thorns, center, ground);
        TintClump(v.thorns, CoverIds.Thorns, x, y);
    }

    // Burning debris glows at this heat in the lava's mesh, duller than a pool.
    const float EmberHeat = 0.5f;

    float LavaHeat(Cell c)
    {
        if (c.T == TerrainIds.Lava) return 1f;
        return CellLavaMesh.IsLava(c) ? HeatLevel(c) / (float)HeatSteps : EmberHeat;
    }

    // Plant Growth's vines on what they climb (CellVines) while the cover is there.
    void RenderVines(View v, Cell c, int x, int y, Vector3 center)
    {
        if (c.Cover != CoverIds.Vines)
        {
            if (v.vines != null) Destroy(v.vines);
            v.vines = null;
            return;
        }
        if (v.vines == null) v.vines = vineClumps.Build(x, y, c, center);
        TintClump(v.vines, CoverIds.Vines, x, y);
    }

    // --- stand-in cubes ------------------------------------------------------------

    // A cell's stand-in cube, made (or remade, if the slot holds a particle effect) and shown.
    GameObject Ensure(GameObject go, string name, Vector3 at)
    {
        if (go == null || go.GetComponent<ParticleSystem>() != null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            Primitives.RemoveCollider(go);
        }
        go.SetActive(true);
        return go;
    }

    // Hides a stand-in cube; particle effects are retired instead (see Retire).
    static void Hide(GameObject go)
    {
        if (go != null && go.activeSelf && go.GetComponent<ParticleSystem>() == null) go.SetActive(false);
    }

    void OnDestroy()
    {
        LetGoCurrent();
        materials.Destroy();
        waterSurface?.Destroy();
        slicks?.Destroy();
        lava?.Destroy();
        spellLights?.Destroy();
        cellFx?.Destroy();
        thornClumps?.Destroy();
        vineClumps?.Destroy();
        DestroyMovers();
        if (plainDust != null) Destroy(plainDust);
    }
}
