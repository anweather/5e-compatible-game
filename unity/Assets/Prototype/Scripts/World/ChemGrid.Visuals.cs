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
    [Tooltip("Draws the far ring on a generated map (Prototype/FarLand: known chunks, the sea plane, far landmarks). The builder sets it, so the shader is in the build.")]
    public Shader farShader;
    [Tooltip("Optional: rock models a raised wall is built of, one per square, sized to cover its face (CellDressing). Without them, rough shapes stand in.")]
    public GameObject[] wallRocks;
    [Tooltip("Optional: smaller stones set on a raised wall's top to break its line.")]
    public GameObject[] wallStones;

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

        public bool Plain => rare == null;

        public GameObject dressing;
        public string terrain;
        public int height, elevation;
        public int key = int.MinValue;
        public bool wet;

        public GameObject cover { get => rare?.cover; set { if (rare != null || (object)value != null) Kept.cover = value; } }
        public GameObject gas { get => rare?.gas; set { if (rare != null || (object)value != null) Kept.gas = value; } }
        public GameObject spark { get => rare?.spark; set { if (rare != null || (object)value != null) Kept.spark = value; } }
        public GameObject thorns { get => rare?.thorns; set { if (rare != null || (object)value != null) Kept.thorns = value; } }
        public GameObject vines { get => rare?.vines; set { if (rare != null || (object)value != null) Kept.vines = value; } }
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

    const int MaxSpellLights = 8;

    ViewBlocks views;
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
        views = new ViewBlocks(World.W, World.H);
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

    public GameObject DressingAt(int x, int y) =>
        views != null && World != null && x >= 0 && y >= 0 && x < World.W && y < World.H ? views[y * World.W + x]?.dressing : null;

    public float? DrawnWaterTop(int x, int y)
    {
        float top = waterSurface.Top(x, y, float.NaN);
        return float.IsNaN(top) ? null : top;
    }

    void RenderAll()
    {
        int across = ChunksAcross, down = (World.H + Chunks.Size - 1) >> Chunks.Shift;
        drawn = new bool[across * down];
        undrawn = drawn.Length;
        drawnChunks.Clear();
        ForgetRedraws();
        spareToHide = true;
        DrawNear();
        partRow = 0;
        partChunk = -1;
        QueueToDraw();
        HideSpareOnceDrawn();
        FinishRender();
    }

    void QueueToDraw()
    {
        using var marked = QueueMarker.Auto();
        var focus = Focus();
        int across = ChunksAcross, down = drawn.Length / Mathf.Max(1, across), radius = DrawRadius();
        queuedRadius = radius;
        queuedFocus.Clear();
        queuedFocus.AddRange(focus);
        toDraw.Clear();
        int kept = partRow > 0 && partChunk >= 0 && !drawn[partChunk] && !StandsUndrawn(focus, partChunk) ? partChunk : -1;
        if (kept >= 0) toDraw.Add(kept);
        else partRow = 0;
        int reach = Mathf.Min(radius, across + down);
        for (int i = 0; i < focus.Count; i++) QueueRound(focus, i, reach, kept);
        int first = kept >= 0 ? 1 : 0;
        toDraw.Sort(first, toDraw.Count - first, NearestFirst(focus));
        nextToDraw = 0;
    }

    void QueueRound(List<Vector2Int> focus, int i, int reach, int kept)
    {
        int across = ChunksAcross, down = drawn.Length / Mathf.Max(1, across);
        var f = focus[i];
        var (cy0, cy1, cx0, cx1) = Round(f, reach, across, down);
        for (int y = cy0; y <= cy1; y++)
            for (int x = cx0; x <= cx1; x++)
            {
                int cx = World.FoldChunkX(x), cy = World.FoldChunkY(y), n = cy * across + cx;
                if (!drawn[n] && n != kept && !ReachedByEarlier(focus, i, cx, cy, reach)) toDraw.Add(n);
            }
    }

    (int cy0, int cy1, int cx0, int cx1) Round(Vector2Int f, int reach, int across, int down)
    {
        int cy0 = f.y - reach, cy1 = f.y + reach, cx0 = f.x - reach, cx1 = f.x + reach;
        if (World.WrapY) cy1 = Mathf.Min(cy1, cy0 + down - 1);
        else (cy0, cy1) = (Mathf.Max(0, cy0), Mathf.Min(down - 1, cy1));
        if (World.WrapX) cx1 = Mathf.Min(cx1, cx0 + across - 1);
        else (cx0, cx1) = (Mathf.Max(0, cx0), Mathf.Min(across - 1, cx1));
        return (cy0, cy1, cx0, cx1);
    }

    bool StandsUndrawn(List<Vector2Int> focus, int but)
    {
        int across = ChunksAcross;
        foreach (var f in focus)
            if (IsUndrawn(f.x, f.y) && f.y * across + f.x != but) return true;
        return false;
    }

    bool IsUndrawn(int cx, int cy)
    {
        int across = ChunksAcross, n = cy * across + cx;
        return cx >= 0 && cy >= 0 && cx < across && n < drawn.Length && !drawn[n];
    }

    bool ReachedByEarlier(List<Vector2Int> focus, int i, int cx, int cy, int reach)
    {
        for (int j = 0; j < i; j++)
            if (Mathf.Abs(World.DeltaChunkX(focus[j].x, cx)) <= reach && Mathf.Abs(World.DeltaChunkY(focus[j].y, cy)) <= reach) return true;
        return false;
    }

    Comparer<int> NearestFirst(List<Vector2Int> focus)
    {
        int across = ChunksAcross;
        return Comparer<int>.Create((a, b) =>
        {
            int da = FocusDistance(focus, a % across, a / across), db = FocusDistance(focus, b % across, b / across);
            return da != db ? da.CompareTo(db) : a.CompareTo(b);
        });
    }

    void DrawnNearestFirst(List<int> into, bool withPart)
    {
        into.Clear();
        into.AddRange(drawnChunks);
        if (withPart && partRow > 0 && partChunk >= 0 && !drawn[partChunk]) into.Add(partChunk);
        into.Sort(NearestFirst(Focus()));
    }

    static int DrawRadius()
    {
        var far = LandFaces.FarLand;
        int known = far != null ? far.KnownChunks : 0;
        return known > 0 ? known : int.MaxValue;
    }

    bool FocusMoved()
    {
        if (DrawRadius() != queuedRadius) return true;
        var focus = Focus();
        if (focus.Count != queuedFocus.Count) return true;
        for (int i = 0; i < focus.Count; i++)
            if (focus[i] != queuedFocus[i]) return true;
        return false;
    }

    bool[] drawn = System.Array.Empty<bool>();
    int undrawn;
    bool spareToHide;
    readonly List<int> toDraw = new();
    int nextToDraw;
    readonly List<Vector2Int> queuedFocus = new();
    int queuedRadius;
    readonly List<Vector2Int> focusChunks = new();
    readonly List<int> drawnChunks = new();
    readonly System.Diagnostics.Stopwatch drawTimer = new();
    int drawUnits;

    int ChunksAcross => (World.W + Chunks.Size - 1) >> Chunks.Shift;

    public int ChunksDrawn => drawn.Length - undrawn;
    public int BlockersStanding => blockers != null ? blockers.Count : 0;

    public int ChunksLeftToDraw
    {
        get
        {
            if (undrawn == 0) return 0;
            int left = 0;
            for (int i = nextToDraw; i < toDraw.Count; i++)
                if (!drawn[toDraw[i]]) left++;
            return left;
        }
    }

    const double DrawBudgetMs = 3.0, DrawFrameMs = 6.0, LeastDrawMs = 0.5;
    double drawBudgetMs = DrawBudgetMs;
    const int ExactDrawUnits = 150, RowUnits = 4, SweptUnits = ExactDrawUnits / SweptChunksAFrame;

    void StartDrawBudget()
    {
        drawTimer.Reset();
        drawUnits = 0;
    }

    bool DrawBudgetSpent() => ExactFrames ? drawUnits >= ExactDrawUnits : drawTimer.Elapsed.TotalMilliseconds >= drawBudgetMs;

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

    int FocusDistance(List<Vector2Int> focus, int cx, int cy)
    {
        int best = int.MaxValue;
        foreach (var f in focus) best = Mathf.Min(best, Mathf.Max(Mathf.Abs(World.DeltaChunkX(f.x, cx)), Mathf.Abs(World.DeltaChunkY(f.y, cy))));
        return best;
    }

    void DrawNear()
    {
        if (undrawn == 0) return;
        using var marked = DrawNearMarker.Auto();
        int reach = World.Rules.Sim.Streaming.Reach, across = ChunksAcross, down = drawn.Length / Mathf.Max(1, across);
        foreach (var f in Focus())
        {
            var (cy0, cy1, cx0, cx1) = Round(f, reach, across, down);
            for (int y = cy0; y <= cy1; y++)
                for (int x = cx0; x <= cx1; x++)
                {
                    int cx = World.FoldChunkX(x), cy = World.FoldChunkY(y);
                    if (drawn[cy * across + cx]) continue;
                    PerfCounts.Add("draw.near_chunks");
                    RenderChunk(cx, cy);
                }
        }
    }

    void DrawSome()
    {
        if (undrawn == 0) return;
        if (FocusMoved()) QueueToDraw();
        if (nextToDraw >= toDraw.Count)
        {
            HideSpareOnceDrawn();
            return;
        }
        using var marked = DrawSomeMarker.Auto();
        drawTimer.Start();
        int across = ChunksAcross, done = 0;
        while (nextToDraw < toDraw.Count && undrawn > 0)
        {
            if (done > 0 && DrawBudgetSpent()) break;
            int n = toDraw[nextToDraw];
            if (drawn[n])
            {
                nextToDraw++;
                partRow = 0;
                continue;
            }
            int cx = n % across, cy = n / across, y0 = cy << Chunks.Shift, y1 = Mathf.Min(World.H, y0 + Chunks.Size);
            if (partRow == 0 && NotMadeYet(n, across)) break;
            int from = y0 + partRow, to = Mathf.Min(y1, from + RowsAStep);
            if (partRow == 0) MakeViews(cx, cy);
            RenderRows(cx, from, to);
            PerfCounts.Add("draw.rows", to - from);
            done += to - from;
            drawUnits += (to - from) * RowUnits;
            partRow = to - y0;
            partChunk = n;
            if (to < y1) continue;
            MarkDrawn(n);
            nextToDraw++;
            partRow = 0;
        }
        drawTimer.Stop();
        HideSpareOnceDrawn();
    }

    readonly List<(int cx, int cy)> drawAhead = new();
    const int DrawAhead = 8;

    bool NotMadeYet(int n, int across)
    {
        if (World.Source is not GeneratedSource g) return false;
        int cx = n % across, cy = n / across;
        if (!World.IsPacked(cx, cy) || g.IsMade(cx, cy)) return false;
        drawAhead.Clear();
        for (int k = nextToDraw; k < toDraw.Count && drawAhead.Count < DrawAhead; k++)
            if (!drawn[toDraw[k]]) drawAhead.Add((toDraw[k] % across, toDraw[k] / across));
        World.MakeAhead(drawAhead);
        return true;
    }

    int partRow, partChunk = -1;
    const int RowsAStep = 1;

    void HideSpareOnceDrawn()
    {
        if (!spareToHide || ChunksLeftToDraw > 0) return;
        spareToHide = false;
        blockers.HideSpare();
    }

    public bool IsChunkDrawn(int cx, int cy)
    {
        int across = ChunksAcross, n = cy * across + cx;
        return cx >= 0 && cy >= 0 && cx < across && n < drawn.Length && drawn[n];
    }

    const int RenderTurns = 3;
    int renderTurn;
    bool turnWaited;

    void Render(bool ticked = false)
    {
        bool wait = ticked && !turnWaited;
        turnWaited = wait;
        StartDrawBudget();
        StartRedrawBudget();
        RedrawSomeLooks();
        RenderSwept();
        DrawSome();
        UndrawFar();
        RenderCarried();
        if (!wait) RenderTurn();
        RenderFresh(out int fresh);
        FinishRender();
        AnnounceFresh(fresh);
    }

    void RenderTurn()
    {
        using var marked = CellsMarker.Auto();
        renderTurn = (renderTurn + 1) % RenderTurns;
        int turn = 0;
        budgeted = true;
        foreach (var (cx, cy) in World.ActiveChunks)
        {
            if (turn++ % RenderTurns != renderTurn) continue;
            if (!IsChunkDrawn(cx, cy)) continue;
            PerfCounts.Add("render.chunks");
            RenderChunk(cx, cy);
        }
        budgeted = false;
    }

    const double RedrawBudgetMs = 4.0;
    const int MinRedrawn = 4, ExactRedrawn = 160;
    static readonly bool RedrawUnbudgeted = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-no-redraw-budget") >= 0;
    readonly System.Diagnostics.Stopwatch redrawTimer = new();
    readonly List<Vector2Int> carried = new(), carrying = new();
    int redrawn;
    bool budgeted, redrawSpent, deferred;

    public int CarriedChunks => carried.Count;

    void StartRedrawBudget()
    {
        redrawTimer.Reset();
        redrawn = 0;
        redrawSpent = false;
    }

    bool RedrawSpent() =>
        redrawn >= MinRedrawn && (ExactFrames ? redrawn >= ExactRedrawn : redrawTimer.Elapsed.TotalMilliseconds >= RedrawBudgetMs);

    void RenderCarried()
    {
        if (carried.Count == 0) return;
        carrying.Clear();
        carrying.AddRange(carried);
        carried.Clear();
        var focus = Focus();
        carrying.Sort((a, b) => FocusDistance(focus, a.x, a.y).CompareTo(FocusDistance(focus, b.x, b.y)));
        budgeted = true;
        foreach (var c in carrying)
            if (IsChunkDrawn(c.x, c.y)) RenderChunk(c.x, c.y);
        budgeted = false;
    }

    readonly Queue<Vector2Int> sweptChunks = new();
    readonly List<int> sweptOrder = new();
    const int SweptChunksAFrame = 6;

    public int SweptChunksDrawn { get; private set; }
    public int SweptChunksLeft => sweptChunks.Count;

    public int EveryChunkRedraws { get; private set; }

    void RedrawEveryChunk()
    {
        if (views == null) return;
        EveryChunkRedraws++;
        sweptChunks.Clear();
        partRow = 0;
        DrawnNearestFirst(sweptOrder, false);
        int across = ChunksAcross;
        foreach (int n in sweptOrder) sweptChunks.Enqueue(new Vector2Int(n % across, n / across));
    }

    void RenderSwept()
    {
        if (sweptChunks.Count == 0) return;
        using var marked = SweptMarker.Auto();
        drawTimer.Start();
        for (int n = 0; n < SweptChunksAFrame && sweptChunks.Count > 0; n++)
        {
            if (n > 0 && DrawBudgetSpent()) break;
            var ch = sweptChunks.Dequeue();
            if (World.IsActive(ch.x, ch.y) || !IsChunkDrawn(ch.x, ch.y)) continue;
            SweptChunksDrawn++;
            drawUnits += SweptUnits;
            RenderChunk(ch.x, ch.y);
        }
        drawTimer.Stop();
    }

    readonly HashSet<string> looksOwed = new();
    readonly List<int> looksQueue = new();
    int looksNext, looksRow;

    public int LookChunksLeft => looksQueue.Count - looksNext;

    void QueueLooks(IEnumerable<string> terrains)
    {
        looksOwed.UnionWith(terrains);
        DrawnNearestFirst(looksQueue, true);
        looksNext = looksRow = 0;
    }

    void RedrawSomeLooks()
    {
        if (looksNext >= looksQueue.Count) return;
        using var marked = LooksMarker.Auto();
        drawTimer.Start();
        int across = ChunksAcross, done = 0;
        while (looksNext < looksQueue.Count && (done == 0 || !DrawBudgetSpent()))
        {
            int n = looksQueue[looksNext], cy = n / across;
            int y0 = cy << Chunks.Shift, y1 = Mathf.Min(World.H, y0 + Chunks.Size);
            RedressRow(n % across, y0 + looksRow);
            drawUnits++;
            done++;
            if (++looksRow < y1 - y0) continue;
            looksRow = 0;
            looksNext++;
        }
        drawTimer.Stop();
        if (looksNext < looksQueue.Count) return;
        looksQueue.Clear();
        looksOwed.Clear();
        looksNext = 0;
    }

    void RedressRow(int cx, int y)
    {
        int x0 = cx << Chunks.Shift, x1 = Mathf.Min(World.W, x0 + Chunks.Size);
        for (int x = x0; x < x1; x++)
        {
            int index = y * World.W + x;
            var v = views[index];
            if (v?.terrain == null || !looksOwed.Contains(v.terrain)) continue;
            var c = World.CellAt(index);
            if (c != null) Redress(v, c, x, y, false);
        }
    }

    void ForgetRedraws()
    {
        sweptChunks.Clear();
        looksQueue.Clear();
        looksOwed.Clear();
        looksNext = looksRow = 0;
        chunkLooks = new int[drawn.Length];
    }

    void RenderChunk(int cx, int cy)
    {
        int n = cy * ChunksAcross + cx;
        if (n < drawn.Length && !drawn[n])
        {
            MakeViews(cx, cy);
            MarkDrawn(n);
        }
        else if (n < drawn.Length) CatchUpLooks(n, cx, cy);
        int y0 = cy << Chunks.Shift;
        deferred = false;
        RenderRows(cx, y0, Mathf.Min(World.H, y0 + Chunks.Size));
        if (deferred && !carried.Contains(new Vector2Int(cx, cy))) carried.Add(new Vector2Int(cx, cy));
        deferred = false;
    }

    void RenderRows(int cx, int y0, int y1)
    {
        int x0 = cx << Chunks.Shift, x1 = Mathf.Min(World.W, x0 + Chunks.Size);
        var cells = World.ChunkCells(cx, Chunks.ChunkOf(y0));
        if (cells.IsEmpty) return;
        for (int y = y0; y < y1; y++)
        {
            int row = Chunks.LocalOf(y) << Chunks.Shift;
            for (int x = x0; x < x1; x++) RenderCell(x, y, cells[row + x - x0]);
        }
    }

    void MakeViews(int cx, int cy)
    {
        int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
        int x1 = Mathf.Min(World.W, x0 + Chunks.Size), y1 = Mathf.Min(World.H, y0 + Chunks.Size);
        if (views[y0 * World.W + x0] == null) chunkLooks[cy * ChunksAcross + cx] = lookStamp;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) views[y * World.W + x] ??= new View();
    }

    void MarkDrawn(int n)
    {
        if (drawn[n]) return;
        drawn[n] = true;
        undrawn--;
        drawnChunks.Add(n);
        PerfCounts.Add("draw.chunks");
        ChunkDrawn?.Invoke(n % ChunksAcross, n / ChunksAcross);
    }

    public event System.Action<int, int> ChunkDrawn;

    public void DrawnChunksNearestFirst(List<Vector2Int> into)
    {
        into.Clear();
        if (drawnChunks.Count == 0) return;
        DrawnNearestFirst(lookOrder, false);
        int across = ChunksAcross;
        foreach (int n in lookOrder) into.Add(new Vector2Int(n % across, n / across));
    }

    readonly List<int> lookOrder = new();

    public int FreshInactiveDrawn { get; private set; }

    void RenderFresh(out int fresh)
    {
        fresh = freshChunks.Count;
        foreach (var f in freshChunks)
        {
            if (World.IsActive(f.CX, f.CY) || !IsChunkDrawn(f.CX, f.CY)) continue;
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

    public double SurfacesMs { get; private set; }

    static readonly Unity.Profiling.ProfilerMarker GroundMarker = new("ChemGrid.ChangeGround"),
        WaterMarker = new("ChemGrid.RenderWater"), FireMarker = new("ChemGrid.RenderFire"), GasMarker = new("ChemGrid.RenderGas"),
        FinishMarker = new("ChemGrid.FinishRender"), WaterMeshMarker = new("ChemGrid.WaterMesh"),
        SlickMeshMarker = new("ChemGrid.SlickMesh"), ShapingMarker = new("ChemGrid.Shaping"), RecycleMarker = new("ChemGrid.RecycleFx"),
        DrawNearMarker = new("ChemGrid.DrawNear"), DrawSomeMarker = new("ChemGrid.DrawSome"), QueueMarker = new("ChemGrid.QueueToDraw"),
        SweptMarker = new("ChemGrid.RenderSwept"), LooksMarker = new("ChemGrid.RedrawLooks"), MarkDrawnMarker = new("ChemGrid.MarkDrawn"),
        RecolourMarker = new("ChemGrid.RecolourWorld"), CellsMarker = new("ChemGrid.RenderCells"),
        LavaMarker = new("ChemGrid.CracksLava"), FxMarker = new("ChemGrid.LightsFx"), OthersMarker = new("ChemGrid.RenderOthers");

    void FinishRender()
    {
        using var marked = FinishMarker.Auto();
        finishTimer.Restart();
        bool busy = !ExactFrames && StepMs >= BusyStepMs;
        using (WaterMeshMarker.Auto()) waterSurface.Rebuild(busy);
        using (SlickMeshMarker.Auto()) slicks.Rebuild(busy);
        using (LavaMarker.Auto())
        {
            cracks.Rebuild();
            lava.Rebuild();
        }
        using (FxMarker.Auto())
        {
            spellLights.Place();
            cellFx.Advance(Time.deltaTime);
        }
        using (OthersMarker.Auto())
        {
            WatchAcidCells();
            ForgetLookFire();
            RenderWhirls();
            RenderTriggered();
            RenderStars();
            RenderFlashes();
            RenderMovers();
            RenderChargeRun();
            RenderShedStrikes();
        }
        materials.ScrollWater(Time.time);
        waterSurface.Scroll(Time.time);
        using (ShapingMarker.Auto()) AnimateShaping();
        using (RecycleMarker.Auto()) RecycleFx();
        SurfacesMs = finishTimer.Elapsed.TotalMilliseconds;
    }

    void RenderCell(int x, int y, Cell c)
    {
        int index = y * World.W + x;
        var v = views[index];
        int key = Key(c);
        if (key == v.key && v.terrain == c.T && v.height == c.Height) return;
        if (budgeted && !redrawSpent && !RedrawUnbudgeted) redrawSpent = RedrawSpent();
        if (budgeted && redrawSpent)
        {
            deferred = true;
            return;
        }
        redrawn++;
        redrawTimer.Start();
        RedrawCell(v, c, x, y, key);
        redrawTimer.Stop();
    }

    void RedrawCell(View v, Cell c, int x, int y, int key)
    {
        v.key = key;

        bool redressed = v.terrain != c.T || v.height != c.Height;
        if (redressed || v.coverId != c.Cover)
            using (GroundMarker.Auto()) ChangeGround(v, c, x, y);
        RenderHeat(v, c, redressed);
        RenderRust(v, c, redressed);
        float groundTop = GroundTop(x, y, c);
        var look = World.TerrainOf(c).Look;
        float stands = look != null ? look.Height : 0f;
        float ground = CellDressing.IsDressed(look) ? groundTop : groundTop + stands;
        var center = CellCenter(x, y);
        bool lake = valley != null && valley.IsLake(c, x, y);

        using (WaterMarker.Auto()) ground = RenderWater(v, c, x, y, ground, groundTop, lake);
        RenderCover(v, c, x, y, center, ground);
        using (FireMarker.Auto()) RenderFire(v, c, x, y, center, ground + ValleyFace.FireHeight(c.T), c.T == TerrainIds.Tree ? 1.8f : 1f);
        using (GasMarker.Auto()) RenderGas(v, c, x, y, center, ground, GasDrawnAt(x, y));
        if (!lake || (x % 5 == 0 && y % 5 == 0)) RenderCharge(v, c, x, y, center, ground);
    }

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
            lava.GroundChanged(x, y);
        }
        v.terrain = c.T;
        v.height = c.Height;
        v.elevation = elevation;
        v.coverId = c.Cover;
    }

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

    void Redress(View v, Cell c, int x, int y, bool reshaped)
    {
        float shown = GroundTop(x, y, c);
        if (reshaped) shown = StandsAlone(c) && v.dressing != null ? v.dressing.transform.position.y : ShownGround(x, y, c);
        if (v.dressing != null)
        {
            PerfCounts.Add("draw.dressings_destroyed");
            drawUnits++;
            Destroy(v.dressing);
        }
        v.dressing = dressings.Build(c, x, y, shown);
        if (v.dressing != null)
        {
            PerfCounts.Add("draw.dressings_built");
            drawUnits++;
        }
        if (blockers.Place(x, y, c, shown)) drawUnits++;
        if (reshaped) StartShaping(v, x, y, shown);
    }

    public void BuiltWall(int x, int y, float tall)
    {
        int index = World.Idx(x, y);
        blockers.BuiltWall(index, tall);
        var c = World.At(x, y);
        if (views != null && views[index] != null) Redress(views[index], c, x, y, false);
        else blockers.Place(x, y, c, GroundTop(x, y, c));
    }

    public bool IsBuiltWall(int x, int y, Cell c) => blockers.IsBuiltWall(World.Idx(x, y), c);

    const int HeatSteps = 6;

    int HeatLevel(Cell c)
    {
        if (c.Heat <= 0) return 0;
        int hotFor = Mathf.Max(1, World.TerrainOf(c).HotFor);
        return Mathf.Clamp(Mathf.CeilToInt(c.Heat * HeatSteps / (float)hotFor), 1, HeatSteps);
    }

    void RenderHeat(View v, Cell c, bool redressed)
    {
        int level = HeatLevel(c);
        if (level == v.heatLevel && !(redressed && level > 0)) return;
        v.heatLevel = level;
        dressings.Glow(v.dressing, level / (float)HeatSteps);
    }

    const int RustSteps = 4;

    int RustLevel(Cell c)
    {
        if (c.Rust <= 0) return 0;
        int every = World.TerrainOf(c).RustEvery;
        return every > 0 ? Mathf.Min(RustSteps, c.Rust * (RustSteps + 1) / every) : 0;
    }

    void RenderRust(View v, Cell c, bool redressed)
    {
        int level = RustLevel(c), from = redressed ? 0 : v.rustLevel;
        if (level > from) dressings.Tarnish(v.dressing, (level - from) * 0.28f);
        v.rustLevel = level;
    }

    int Key(Cell c)
    {
        unchecked
        {
            int k = c.Water;
            k = k * 31 + c.Kind;
            k = k * 31 + (c.Frozen ? 1 : 0);
            k = k * 31 + (c.Cover?.GetHashCode() ?? 0);
            k = k * 31 + c.Fire;
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

    bool Standing(Cell c) => c.Water >= 2 || c.Height < 0 || World.TerrainOf(c).Elevation < 0;

    float RenderWater(View v, Cell c, int x, int y, float ground, float groundTop, bool lake)
    {
        bool wet = c.Water > 0 && (!lake || c.Frozen) && Standing(c);
        if (wet || v.wet) waterSurface.Changed(x, y, wet);
        v.wet = wet;
        if (lake) return c.Water > 0 ? Mathf.Max(ground, valley.LakeLevelOf(c, x, y)) : ground;
        return wet ? Mathf.Max(ground, waterSurface.Top(x, y, groundTop + WaterDepth(c))) : ground;
    }

    float WaterDepth(Cell c) => c.Water / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;

    public float LakeLevelAt(int x, int y, Cell c) =>
        valley != null ? valley.LakeLevelOf(c, x, y) : float.NegativeInfinity;

    public float DrawnWaterTopAt(float gx, float gy, float fallback) => waterSurface.TopAt(gx, gy, fallback);

    public string WaterSummary => waterSurface.Summary;

    public (int tiles, int different) CheckWaterTiles() => waterSurface.CheckTiles();

    public float WaterSurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return 0f;
        float top = GroundTop(x, y, c) + WaterDepth(c);
        return valley != null && c.Water > 0 && valley.IsLake(c, x, y) ? Mathf.Max(top, valley.LakeLevelOf(c, x, y)) : top;
    }

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

    const float EmberHeat = 0.5f;

    float LavaHeat(Cell c)
    {
        if (c.T == TerrainIds.Lava) return 1f;
        return CellLavaMesh.IsLava(c) ? HeatLevel(c) / (float)HeatSteps : EmberHeat;
    }

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
        cracks?.Destroy();
        lava?.Destroy();
        spellLights?.Destroy();
        cellFx?.Destroy();
        thornClumps?.Destroy();
        vineClumps?.Destroy();
        DestroyMovers();
        if (plainDust != null) Destroy(plainDust);
        if (Application.isPlaying) ForgetCellRoots();
    }
}
