using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The ground of the chunk terrains under the chemistry's cells, as Unity
// TerrainData: the cells' heights (CellHeightfield: baked ground plus each
// cell's offset, eased) and their paint (CellPaint: soft-edged paint laid over
// the saved valley's own, with the detail grass cleared off bare ground).
//
// Only cells whose paint differs from how they were baked paint anything: the
// rest keep the saved paint (the meadow, the old yard's soft-edged pad and
// gully), so a burnt patch fades into whatever was there before.
//
// Works on runtime copies of the chunks, made when a world starts (Bind), so
// playing never changes the saved valley: chunks built from the map's arrays (GroundBuilder; the
// scene's start built the first, MapTerrains), or in tests copies of chunks given to it. They are made up front, not when a
// chunk first changes: swapping a terrain's collider under a prop resting on it
// drops the contact, and the prop falls through the ground. A new world on the same
// map keeps a chunk built from the arrays that nothing has changed since (touched). The baked heights already hold the starting cells' offsets
// (the ground pack was baked with the same CellHeightfield), so the base under
// each sample is what was baked minus that offset.
public sealed class ValleyGround
{
    sealed class Chunk
    {
        public (int i, int j) at;
        public Terrain terrain;
        public float detailDistance;
        public TerrainData original, data;
        public bool fresh;
        public bool touched;
        public float bottom;
        public float[,] baseHeights;
        public int[] layers;
        public float[,,] baseAlpha;
        public float[,,] frameAlpha;
        public int dx0 = int.MaxValue, dz0 = int.MaxValue, dx1 = -1, dz1 = -1;
        public bool heightsChanged;
        public bool syncQueued, paintQueued;
        public bool ready;
        public List<(int x, int y)> repaint, anew;
        public List<System.Action> whenReady;
    }

    readonly TerrainGrid layout;
    readonly Dictionary<(int i, int j), Chunk> chunks = new();
    readonly HashSet<(int x, int y)> repaint = new();
    readonly List<(int x, int y)> reshaped = new();
    CellHeightfield heightfield;
    World world;

    public World World => world;
    CellsByChunk<float> cellBase;
    CellsByChunk<IReadOnlyList<float>> bakedPaint;

    readonly GroundBuilder builder;
    readonly bool lazy;
    readonly HashSet<(int i, int j)> attached = new();
    readonly Dictionary<(int res, int texels), Stack<TerrainData>> spare = new();
    readonly Dictionary<(int i, int j), float[,]> bakedOffsets = new();
    readonly HashSet<(int i, int j)> laidChunks = new();
    readonly Dictionary<int, float> raised = new();
    HashSet<(int i, int j)> detailNear = new();
    const int MostSpare = 16;

    public ValleyGround(TerrainGrid layout, GroundBuilder builder)
    {
        this.layout = layout;
        this.builder = builder;
        lazy = true;
        foreach (var at in layout.GridChunks()) chunks[at] = new Chunk { at = at, detailDistance = MapTerrains.DetailMetres };
    }

    public bool Lazy => lazy;

    public GroundBuilder Builder => builder;

    public LandChunks Land { get; set; }

    public event System.Action<(int i, int j), TerrainData> ChunkReady;

    public event System.Action<(int i, int j), TerrainData> ChunkGone;

    public bool IsReady((int i, int j) at) => !lazy || (chunks.TryGetValue(at, out var ch) && ch.ready);

    public int WithData => chunks.Values.Count(c => c.data != null);

    public string Holdings()
    {
        int data = 0, loose = 0, touched = 0, queued = 0, detail = 0, colliders = 0;
        foreach (var ch in chunks.Values)
        {
            if (ch.data == null) continue;
            data++;
            if (attached.Contains(ch.at)) continue;
            loose++;
            if (ch.touched) touched++;
            if (ch.syncQueued || ch.paintQueued) queued++;
            if (pendingDetail.ContainsKey(ch.at)) detail++;
            if (treeColliders.Contains(ch.at)) colliders++;
        }
        int spared = spare.Values.Sum(s => s.Count);
        return $"data={data} attached={attached.Count} loose={loose} (touched={touched} queued={queued} detail={detail} colliders={colliders}) " +
               $"spare={spared} arrays={builder?.Held ?? 0} heightfield={HeightfieldChunks} cellBase={cellBase?.Made() ?? 0} " +
               $"bakedPaint={bakedPaint?.Made() ?? 0} paintedMix={paintedMix?.Made() ?? 0} repaint={repaint.Count} bakedOffsets={bakedOffsets.Count} raised={raised.Count}";
    }

    public bool WhenReady((int i, int j) at, System.Action then)
    {
        if (IsReady(at) || !chunks.TryGetValue(at, out var ch)) return true;
        (ch.whenReady ??= new List<System.Action>()).Add(then);
        return false;
    }

    public void Touch((int i, int j) at)
    {
        if (chunks.TryGetValue(at, out var ch)) ch.touched = true;
    }

    public TreePrototype[] TreePrototypes((int i, int j) at) =>
        lazy && builder != null && !IsReady(at) ? builder.TreePrototypes : Data(at).treePrototypes;

    public void Prepare(IReadOnlyList<(int i, int j)> wanted)
    {
        builder.Prepare(wanted);
        heightfield?.Prepare(wanted);
    }

    public void PrepareHeights(IReadOnlyList<(int i, int j)> wanted) => heightfield?.Prepare(wanted);

    public void Attach((int i, int j) at, Terrain terrain)
    {
        var ch = chunks[at];
        ch.terrain = terrain;
        attached.Add(at);
        if (!ch.ready) MakeReady(ch);
        Use(ch, ch.data);
        var p = terrain.transform.position;
        terrain.transform.position = new Vector3(p.x, builder.Bottom, p.z);
        terrain.detailObjectDistance = detailNear.Contains(at) ? ch.detailDistance : 0f;
    }

    public void Release((int i, int j) at)
    {
        if (!chunks.TryGetValue(at, out var ch)) return;
        ch.terrain = null;
        attached.Remove(at);
        if (!ch.ready || ch.touched || ch.syncQueued || ch.paintQueued || pendingDetail.ContainsKey(at) || treeColliders.Contains(at)) return;
        ChunkGone?.Invoke(at, ch.data);
        Spare(ch.data);
        ch.data = null;
        ch.ready = false;
        ch.baseHeights = null;
        ch.baseAlpha = ch.frameAlpha = null;
        builder.Forget(at);
        heightfield.Forget(at.i, at.j);
        cellBase.Drop(at.i, at.j);
        bakedPaint.Drop(at.i, at.j);
        paintedMix.Drop(at.i, at.j);
    }

    public int ForgetFar(System.Func<(int i, int j), bool> far)
    {
        if (!lazy || heightfield == null) return 0;
        int forgot = 0;
        foreach (var ch in chunks.Values)
        {
            if (ch.data != null || ch.ready || attached.Contains(ch.at) || !far(ch.at)) continue;
            if (heightfield.Forget(ch.at.i, ch.at.j)) forgot++;
            builder.Forget(ch.at);
        }
        return forgot;
    }

    void Spare(TerrainData data)
    {
        var key = (data.heightmapResolution, data.alphamapResolution);
        if (!spare.TryGetValue(key, out var stack)) spare[key] = stack = new Stack<TerrainData>();
        if (stack.Count < MostSpare) stack.Push(data);
        else Free(data);
    }

    static void Free(Object o)
    {
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    TerrainData Spared((int i, int j) at)
    {
        var key = (layout.Resolution(at.i, at.j), layout.TexelResolution(at.i, at.j));
        return spare.TryGetValue(key, out var stack) && stack.Count > 0 ? stack.Pop() : null;
    }

    float LandMetres(int k) => Land != null && k < Land.W * Land.H ? GroundFromLand.LandUnits(Land, k % Land.W, k / Land.W) * landUnit : 0f;

    void MakeReady(Chunk ch)
    {
        using var marked = MakeReadyMarker.Auto();
        if (ch.data == null) ch.data = builder.Build(ch.at, Spared(ch.at));
        else builder.FreshLayers(ch.data);
        if (ch.terrain != null) Use(ch, ch.data);
        ch.ready = true;
        ch.touched = false;
        ch.bottom = builder.Bottom;
        var data = ch.data;
        int res = data.heightmapResolution;
        var heights = data.GetHeights(0, 0, res, res);
        var baked = BakedOffsets(ch.at);
        float size = data.size.y;
        ch.baseHeights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) ch.baseHeights[z, x] = heights[z, x] * size - baked[z, x];
        ch.layers = LayerIndices(data.terrainLayers);
        ch.baseAlpha = null;
        ch.frameAlpha = null;
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
        ReadCells(ch);
        if (heightfield.Shapes(ch.at.i, ch.at.j) && Moved(heightfield.Current(ch.at.i, ch.at.j), baked)) WriteHeights(layout.SampleRect(ch.at.i, ch.at.j));
        TakeWaiting(ch, data);
    }

    void ReadCells(Chunk ch)
    {
        foreach (var (x, y) in layout.CellsIn(ch.at.i, ch.at.j))
        {
            int k = world.Idx(x, y);
            cellBase[k] = SampleBase(x, y) + (raised.TryGetValue(k, out float more) ? more : 0f);
            bakedPaint[k] = paintedMix[k] = CellPaint.Mix(world, world.At(x, y));
        }
    }

    float[,] BakedOffsets((int i, int j) at)
    {
        if (bakedOffsets.TryGetValue(at, out var kept)) return kept;
        if (!laidChunks.Contains(at)) return heightfield.Current(at.i, at.j);
        return bakedOffsets[at] = heightfield.ComputeUnlaid(world, at.i, at.j);
    }

    static bool Moved(float[,] now, float[,] baked)
    {
        int h = now.GetLength(0), w = now.GetLength(1);
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (Mathf.Abs(now[z, x] - baked[z, x]) > 1e-5f) return true;
        return false;
    }

    void TakeWaiting(Chunk ch, TerrainData data)
    {
        if (ch.repaint != null)
        {
            foreach (var c in ch.repaint) repaint.Add(c);
            ch.repaint = null;
        }
        if (ch.anew != null)
        {
            foreach (var (x, y) in ch.anew) PaintAnew(x, y);
            ch.anew = null;
        }
        ChunkReady?.Invoke(ch.at, data);
        if (ch.whenReady == null) return;
        var then = ch.whenReady;
        ch.whenReady = null;
        foreach (var a in then) a();
    }

    bool CellReady(int x, int y) => !lazy || (chunks.TryGetValue(layout.ChunkOfCell(x, y), out var ch) && ch.ready);

    public ValleyGround(TerrainGrid layout, Terrain[] terrains, GroundBuilder builder = null, bool built = false)
    {
        this.layout = layout;
        this.builder = builder;
        foreach (var (i, j) in layout.GridChunks())
        {
            var terrain = terrains[j * layout.ChunksX + i];
            if (terrain == null) continue;
            bool own = built && builder != null;
            chunks[(i, j)] = new Chunk
            {
                at = (i, j),
                terrain = terrain,
                original = own ? null : terrain.terrainData,
                data = own ? terrain.terrainData : null,
                fresh = own,
                detailDistance = terrain.detailObjectDistance,
            };
            if (builder == null) continue;
            var p = terrain.transform.position;
            terrain.transform.position = new Vector3(p.x, builder.Bottom, p.z);
        }
    }

    public float DetailDistance
    {
        get
        {
            if (lazy) return MapTerrains.DetailMetres;
            return chunks.Count > 0 ? chunks.Values.Max(c => c.detailDistance) : 0f;
        }
    }

    public void DrawDetails(IReadOnlyCollection<(int i, int j)> near)
    {
        using var marked = DrawDetailsMarker.Auto();
        PerfCounts.Add("detail.changes");
        if (lazy)
        {
            detailNear = new HashSet<(int i, int j)>(near);
            foreach (var at in attached)
            {
                var ch = chunks[at];
                ch.terrain.detailObjectDistance = detailNear.Contains(at) ? ch.detailDistance : 0f;
            }
            return;
        }
        foreach (var kv in chunks)
            if (kv.Value.terrain != null) kv.Value.terrain.detailObjectDistance = near.Contains(kv.Key) ? kv.Value.detailDistance : 0f;
    }

    public IEnumerable<((int i, int j) at, TerrainData data)> Chunks()
    {
        foreach (var kv in chunks)
            if (!lazy || kv.Value.ready) yield return (kv.Key, Shown(kv.Value));
    }

    public TerrainData Data((int i, int j) at) => Shown(Made(chunks[at]));

    public bool HasChunk((int i, int j) at) => chunks.ContainsKey(at);

    public TerrainData WritableData((int i, int j) at)
    {
        var ch = Made(chunks[at]);
        ch.touched = true;
        return Writable(ch);
    }

    Chunk Made(Chunk ch)
    {
        if (lazy && !ch.ready) MakeReady(ch);
        return ch;
    }

    static TerrainData Shown(Chunk ch) => ch.data != null ? ch.data : ch.original;

    TerrainData Writable(Chunk ch)
    {
        if (ch.data != null) return ch.data;
        ch.data = builder != null ? builder.Build(ch.at) : Object.Instantiate(ch.original);
        Use(ch, ch.data);
        return ch.data;
    }

    static void Use(Chunk ch, TerrainData data)
    {
        if (ch.terrain == null) return;
        ch.terrain.terrainData = data;
        if (ch.terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = data;
    }

    static void Restore(Chunk ch)
    {
        if (ch.data == null) return;
        if (ch.terrain != null && ch.original != null) Use(ch, ch.original);
        if (Application.isPlaying) Object.Destroy(ch.data);
        else Object.DestroyImmediate(ch.data);
        ch.data = null;
        ch.heightsChanged = false;
        ch.touched = false;
    }

    public int HeightfieldChunks => heightfield?.Worked ?? 0;

    public double HeightfieldMs { get; private set; }

    public double BindMs { get; private set; }

    const int MostSamplesAFrame = 4096;

    public bool Shaping => heightfield != null && !heightfield.Settled;

    public int Repainting => repaint.Count;

    public void Bind(World w, float stepHeight, float shapeSpeed, bool flatUnderWalls = false)
    {
        using var bound = BindMarker.Auto();
        world = w;
        landUnit = stepHeight / w.Rules.Sim.WaterUnitsPerStep;
        heightfield = new CellHeightfield(layout, stepHeight, shapeSpeed) { FlatUnderWalls = flatUnderWalls, LaidLandDrawn = false };
        var reset = System.Diagnostics.Stopwatch.StartNew();
        if (lazy) heightfield.ResetLazily(w);
        else heightfield.Reset(w);
        HeightfieldMs = reset.Elapsed.TotalMilliseconds;
        Debug.Log($"GROUND HEIGHTFIELD {(lazy ? "reset a chunk at a time" : "reset")}: {HeightfieldMs:0} ms");
        repaint.Clear();
        cleared.Clear();
        pendingDetail.Clear();
        treeColliders.Clear();
        reshaped.Clear();
        if (lazy)
        {
            BindLazily(w);
            return;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var kv in chunks) Recover(kv.Key, kv.Value);
        heightfield.LaidLandDrawn = true;
        foreach (var r in heightfield.Update(w, w.LandLaidCells(), snap: true)) WriteHeights(r);
        BindMs = timer.Elapsed.TotalMilliseconds;
        Debug.Log($"GROUND BIND {(builder != null ? "built" : "baked")}: {chunks.Count} chunks in {BindMs:0} ms");
        NewCellArrays();
        for (int y = 0; y < w.H; y++)
        {
            for (int x = 0; x < w.W; x++)
            {
                int k = w.Idx(x, y);
                cellBase[k] = SampleBase(x, y);
                bakedPaint[k] = paintedMix[k] = CellPaint.Mix(w, w.At(x, y));
            }
        }
    }

    void BindLazily(World w)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        raised.Clear();
        syncQueue.Clear();
        paintQueue.Clear();
        foreach (var ch in chunks.Values)
        {
            ch.ready = false;
            ch.repaint = ch.anew = null;
            ch.whenReady = null;
            ch.syncQueued = ch.paintQueued = false;
            ch.heightsChanged = false;
            if (ch.data != null && ch.touched)
            {
                Free(ch.data);
                ch.data = null;
                ch.touched = false;
            }
        }
        bakedOffsets.Clear();
        laidChunks.Clear();
        foreach (var r in heightfield.DirtyRects(w.LandLaidCells())) laidChunks.Add((r.I, r.J));
        heightfield.LaidLandDrawn = true;
        if (cellBase == null) NewCellArrays();
        cellBase.Clear();
        bakedPaint.Clear();
        paintedMix.Clear();
        var now = attached.ToList();
        Prepare(now);
        foreach (var at in now) MakeReady(chunks[at]);
        BindMs = timer.Elapsed.TotalMilliseconds;
        Debug.Log($"GROUND BIND made a chunk at a time: {now.Count} of {chunks.Count} chunks made in {BindMs:0} ms");
    }

    static readonly Unity.Profiling.ProfilerMarker BindMarker = new("ValleyGround.Bind");

    CellsByChunk<IReadOnlyList<float>> paintedMix;

    void NewCellArrays()
    {
        cellBase = new CellsByChunk<float>(layout, LandMetres);
        bakedPaint = new CellsByChunk<IReadOnlyList<float>>(layout);
        paintedMix = new CellsByChunk<IReadOnlyList<float>>(layout);
    }

    void Recover((int i, int j) at, Chunk ch)
    {
        if (ch.fresh) ch.fresh = false;
        else if (builder != null && ch.data != null && !ch.touched) builder.FreshLayers(ch.data);
        else Restore(ch);
        var data = Writable(ch);
        ch.touched = false;
        ch.bottom = ch.terrain.transform.position.y;
        int res = data.heightmapResolution;
        var heights = data.GetHeights(0, 0, res, res);
        var offsets = heightfield.Current(at.i, at.j);
        float size = data.size.y;
        ch.baseHeights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) ch.baseHeights[z, x] = heights[z, x] * size - offsets[z, x];
        ch.layers = LayerIndices(data.terrainLayers);
        ch.baseAlpha = null;
        ch.frameAlpha = null;
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
    }

    public static int[] LayerIndices(TerrainLayer[] layers) => LayerIndices(layers.Select(l => l != null ? l.name : "").ToArray());

    public static int[] LayerIndices(string[] layers)
    {
        var map = new int[CellPaint.Layers.Count];
        for (int k = 0; k < map.Length; k++)
        {
            map[k] = -1;
            for (int n = 0; n < layers.Length; n++)
                if (layers[n].StartsWith(CellPaint.Layers[k] + "_TerrainLayer", System.StringComparison.Ordinal)) map[k] = n;
        }
        return map;
    }

    Chunk lastBase;

    float SampleBase(int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        if (lastBase == null || lastBase.at != (i, j)) lastBase = chunks.TryGetValue((i, j), out var found) ? found : null;
        return lastBase != null && lastBase.at == (i, j) ? lastBase.bottom + lastBase.baseHeights[sz, sx] : 0f;
    }

    public float BaseHeight(int x, int y) => cellBase != null && world.InBounds(x, y) ? cellBase[world.Idx(x, y)] + world.LandMovedAt(x, y) * landUnit : 0f;

    public void RaiseBase(int[] cells, float[] metres)
    {
        if (cellBase == null || cells == null || metres == null) return;
        int n = Mathf.Min(cells.Length, metres.Length);
        for (int k = 0; k < n; k++)
        {
            if (cells[k] < 0 || cells[k] >= world.W * world.H) continue;
            if (!lazy)
            {
                cellBase[cells[k]] += metres[k];
                continue;
            }
            raised[cells[k]] = (raised.TryGetValue(cells[k], out float was) ? was : 0f) + metres[k];
            int x = cells[k] % world.W, y = cells[k] / world.W;
            if (CellReady(x, y)) cellBase[cells[k]] += metres[k];
        }
    }

    public float ShownGround(int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        float drawnBase = cellBase != null && world.InBounds(x, y) ? cellBase[world.Idx(x, y)] : 0f;
        return drawnBase + (heightfield != null && heightfield.Shapes(i, j) ? heightfield.OffsetNow(i, j, sx, sz) : 0f);
    }

    public float GroundAt(float wx, float wz)
    {
        var (x, y) = layout.CellAt(wx, wz);
        x = ViewWrap.FoldX(x);
        y = ViewWrap.FoldY(y);
        if (!chunks.TryGetValue(layout.ChunkOfCell(x, y), out var ch)) return 0f;
        if (lazy && !ch.ready) return world != null && world.InBounds(x, y) ? ShownGround(x, y) : 0f;
        var data = Shown(ch);
        if (ch.terrain != null)
        {
            var p = ch.terrain.transform.position;
            return p.y + data.GetInterpolatedHeight((wx - p.x) / data.size.x, (wz - p.z) / data.size.z);
        }
        var (cx, cz) = layout.ChunkCorner(ch.at.i, ch.at.j);
        float half = layout.ChunkSize * 0.5f;
        cx = ViewWrap.NearX(cx + half) - half;
        cz = ViewWrap.NearZ(cz + half) - half;
        return ch.bottom + data.GetInterpolatedHeight((wx - cx) / data.size.x, (wz - cz) / data.size.z);
    }

    public void Reshape(int x, int y) => reshaped.Add((x, y));

    public void Repaint(int x, int y) => repaint.Add((x, y));

    public void PaintAnew(int x, int y)
    {
        if (!CellReady(x, y))
        {
            var ch = chunks[layout.ChunkOfCell(x, y)];
            (ch.anew ??= new List<(int x, int y)>()).Add((x, y));
            return;
        }
        int key = world.Idx(x, y);
        bakedPaint[key] = null;
        paintedMix[key] = null;
        repaint.Add((x, y));
    }

    public void Advance(float dt)
    {
        if (heightfield == null) return;
        if (reshaped.Count > 0)
        {
            PerfCounts.Add("shape.reshaped_cells", reshaped.Count);
            using (HeightQueueMarker.Auto()) heightfield.Queue(reshaped);
            reshaped.Clear();
        }
        if (heightfield.Waiting > 0)
        {
            using (HeightUpdateMarker.Auto()) heightfield.FillSome(world, MostSamplesAFrame);
            PerfCounts.Add("shape.update_filled", heightfield.SamplesFilled);
        }
        List<ChunkRect> moved;
        using (HeightEaseMarker.Auto()) moved = heightfield.Advance(dt);
        CountRects("shape.ease", moved);
        using (HeightWriteMarker.Auto())
            foreach (var r in moved) WriteHeights(r);
    }

    static void CountRects(string what, List<ChunkRect> rects)
    {
        if (!PerfCounts.On) return;
        long samples = 0;
        foreach (var r in rects) samples += r.Width * r.Height;
        PerfCounts.Add(what + "_rects", rects.Count);
        PerfCounts.Add(what + "_samples", samples);
    }

    void WriteHeights(ChunkRect r)
    {
        if (!chunks.TryGetValue((r.I, r.J), out var ch) || (lazy && !ch.ready)) return;
        var offsets = heightfield.Current(r.I, r.J);
        var data = Writable(ch);
        float size = data.size.y;
        var heights = HeightBuffer(r.Width, r.Height);
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) heights[z, x] = (ch.baseHeights[r.Z + z, r.X + x] + offsets[r.Z + z, r.X + x]) / size;
        data.SetHeightsDelayLOD(r.X, r.Z, heights);
        ch.heightsChanged = true;
        ch.touched = true;
        if (ch.syncQueued) return;
        ch.syncQueued = true;
        syncQueue.Enqueue(ch);
    }

    readonly Dictionary<(int w, int h), float[,]> heightBuffers = new();

    float[,] HeightBuffer(int w, int h)
    {
        if (!heightBuffers.TryGetValue((w, h), out var buffer)) heightBuffers[(w, h)] = buffer = new float[h, w];
        return buffer;
    }

    public void Flush(System.Func<int, int, bool> scorched)
    {
        using var marked = FlushMarker.Auto();
        using (SyncMarker.Auto()) SyncOneHeightmap();
        using (TreeColliderMarker.Auto()) RebuildOneTreeCollider();
        PerfCounts.Peak("ground.repaint_waiting", repaint.Count);
        using (DetailMarker.Auto()) WriteDetail();
        if (repaint.Count == 0)
        {
            WriteOnePaint();
            return;
        }
        TakeBatch(scorched);
        if (batch.Count == 0)
        {
            WriteOnePaint();
            return;
        }
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => PaintOf(x, y, scorched(x, y));
        paintTimer.Restart();
        HashSet<(int x, int y)> around;
        using (PaintMarker.Auto())
        {
            around = CellPaint.Around(layout, batch);
            foreach (var r in CellPaint.Runs(layout, around)) Paint(r, paintOf);
        }
        msPerCell = Mathf.Lerp(msPerCell, (float)paintTimer.Elapsed.TotalMilliseconds / batch.Count, 0.3f);
        foreach (var (x, y) in around)
        {
            if (!world.InBounds(x, y)) continue;
            int key = world.Idx(x, y);
            if (!ReferenceEquals(paintedMix[key], CellPaint.Mix(world, x, y, scorched(x, y)))) paintedMix[key] = null;
        }
        WriteOnePaint();
        using (DetailMarker.Auto()) ClearBareDetail(scorched);
    }

    static readonly Unity.Profiling.ProfilerMarker FlushMarker = new("ValleyGround.Flush"),
        PaintMarker = new("ValleyGround.Paint"), DetailMarker = new("ValleyGround.ClearGrass"), UploadMarker = new("ValleyGround.UploadPaint"),
        DrawDetailsMarker = new("ValleyGround.DrawDetails"), HeightUpdateMarker = new("ValleyGround.HeightUpdate"), HeightQueueMarker = new("ValleyGround.HeightQueue"),
        HeightEaseMarker = new("ValleyGround.HeightEase"), HeightWriteMarker = new("ValleyGround.HeightWrite"),
        SyncMarker = new("ValleyGround.SyncHeightmap"), TreeColliderMarker = new("ValleyGround.TreeCollider"), BlockMarker = new("ValleyGround.Paint.Block"),
        ComposeMarker = new("ValleyGround.Paint.Compose"), ApplyMarker = new("ValleyGround.UploadPaint.Apply"),
        MakeReadyMarker = new("ValleyGround.MakeReady");

    readonly TexelNoise texelNoise = new();

    public static bool CheckKeptNoise { get; set; }

    public static (int painted, int different) KeptNoiseChecked => (keptChecked, keptDifferent);

    static int keptChecked, keptDifferent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetKeptNoise()
    {
        CheckKeptNoise = false;
        keptChecked = keptDifferent = 0;
    }

    void CheckAfresh(PaintBlock kept, ChunkRect r, System.Func<int, int, IReadOnlyList<float>> paintOf)
    {
        var fresh = CellPaint.Block(layout, r, paintOf);
        keptChecked++;
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++)
            {
                bool same = Bits(kept.Cover[z, x]) == Bits(fresh.Cover[z, x]);
                for (int k = 0; k < CellPaint.Layers.Count; k++) same &= Bits(kept.Mix[z, x, k]) == Bits(fresh.Mix[z, x, k]);
                if (same) continue;
                keptDifferent++;
                return;
            }
    }

    static int Bits(float f) => System.BitConverter.SingleToInt32Bits(f);

    const float PaintBudgetMs = 1f;
    const int FewestRepaints = 8, MostRepaints = 1000;
    readonly List<(int x, int y)> batch = new();
    readonly System.Diagnostics.Stopwatch paintTimer = new();
    float msPerCell = 0.02f;

    const int MostLooks = 2000;
    readonly List<(int x, int y)> looked = new();

    void TakeBatch(System.Func<int, int, bool> scorched)
    {
        batch.Clear();
        looked.Clear();
        int most = PaintCellsThisFrame();
        foreach (var cell in repaint)
        {
            if (batch.Count == most || looked.Count == MostLooks) break;
            looked.Add(cell);
            if (WaitsForChunk(cell)) continue;
            int key = world.Idx(cell.x, cell.y);
            var mix = CellPaint.Mix(world, cell.x, cell.y, scorched(cell.x, cell.y));
            bool same = ReferenceEquals(mix, paintedMix[key]);
            PerfCounts.Add(same ? "ground.repaint_same" : "ground.repaint_new");
            if (same) continue;
            paintedMix[key] = mix;
            batch.Add(cell);
        }
        foreach (var cell in looked) repaint.Remove(cell);
    }

    bool WaitsForChunk((int x, int y) cell)
    {
        if (CellReady(cell.x, cell.y)) return false;
        if (chunks.TryGetValue(layout.ChunkOfCell(cell.x, cell.y), out var ch)) (ch.repaint ??= new List<(int x, int y)>()).Add(cell);
        return true;
    }

    int PaintCellsThisFrame() => Mathf.Clamp(Mathf.FloorToInt(PaintBudgetMs / Mathf.Max(msPerCell, 1e-4f)), FewestRepaints, MostRepaints);

    void SyncOneHeightmap()
    {
        while (syncQueue.Count > 0)
        {
            var ch = syncQueue.Dequeue();
            ch.syncQueued = false;
            if (!ch.heightsChanged || ch.data == null) continue;
            ch.data.SyncHeightmap();
            ch.heightsChanged = false;
            return;
        }
    }

    public void TreesMoved((int i, int j) at)
    {
        treeColliders.Add(at);
        TreesChanged?.Invoke(at);
    }

    public static event System.Action<(int i, int j)> TreesChanged;

    void RebuildOneTreeCollider()
    {
        if (treeColliders.Count == 0) return;
        var at = treeColliders.First();
        treeColliders.Remove(at);
        var terrain = chunks[at].terrain;
        if (terrain == null || !terrain.TryGetComponent<TerrainCollider>(out var collider)) return;
        var data = collider.terrainData;
        collider.terrainData = null;
        collider.terrainData = data;
    }

    readonly HashSet<(int i, int j)> treeColliders = new();
    readonly Queue<Chunk> syncQueue = new(), paintQueue = new();
    float landUnit;

    IReadOnlyList<float> PaintOf(int x, int y, bool scorched)
    {
        var mix = CellPaint.Mix(world, x, y, scorched);
        return ReferenceEquals(mix, bakedPaint[world.Idx(x, y)]) ? null : mix;
    }

    void Paint(ChunkRect r, System.Func<int, int, IReadOnlyList<float>> paintOf)
    {
        if (!chunks.TryGetValue((r.I, r.J), out var ch) || (lazy && !ch.ready)) return;
        var data = Writable(ch);
        int res = data.alphamapResolution;
        ch.baseAlpha ??= data.GetAlphamaps(0, 0, res, res);
        ch.frameAlpha ??= (float[,,])ch.baseAlpha.Clone();
        PaintBlock block;
        using (BlockMarker.Auto()) block = CellPaint.Block(layout, r, paintOf, texelNoise);
        if (CheckKeptNoise) CheckAfresh(block, r, paintOf);
        using (ComposeMarker.Auto())
            for (int z = 0; z < r.Height; z++)
                for (int x = 0; x < r.Width; x++) ComposeTexel(block, (ch.layers, ch.baseAlpha), ch.frameAlpha, x, z, (r.X, r.Z));
        PerfCounts.Add("ground.paint_texels", r.Width * r.Height);
        ch.dx0 = Mathf.Min(ch.dx0, r.X);
        ch.dz0 = Mathf.Min(ch.dz0, r.Z);
        ch.dx1 = Mathf.Max(ch.dx1, r.XMax);
        ch.dz1 = Mathf.Max(ch.dz1, r.ZMax);
        if (ch.paintQueued) return;
        ch.paintQueued = true;
        paintQueue.Enqueue(ch);
    }

    void WriteOnePaint()
    {
        while (paintQueue.Count > 0)
        {
            var ch = paintQueue.Dequeue();
            ch.paintQueued = false;
            if (ch.dx1 < 0 || ch.data == null) continue;
            WritePaint(ch);
            return;
        }
    }

    static void WriteTexture(Texture2D tex, int first, Chunk ch)
    {
        var pixels = tex.GetPixelData<Color32>(0);
        var alpha = ch.frameAlpha;
        int width = tex.width, channels = alpha.GetLength(2) - first;
        for (int z = ch.dz0; z <= ch.dz1; z++)
            for (int x = ch.dx0; x <= ch.dx1; x++)
            {
                var c = new Color32();
                if (channels > 0) c.r = Texel(alpha[z, x, first]);
                if (channels > 1) c.g = Texel(alpha[z, x, first + 1]);
                if (channels > 2) c.b = Texel(alpha[z, x, first + 2]);
                if (channels > 3) c.a = Texel(alpha[z, x, first + 3]);
                pixels[z * width + x] = c;
            }
    }

    static byte Texel(float alpha) => (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);

    static void WritePaint(Chunk ch)
    {
        using var marked = UploadMarker.Auto();
        if (ch.dx1 < 0) return;
        ch.touched = true;
        var textures = ch.data.alphamapTextures;
        for (int t = 0; t < textures.Length; t++)
        {
            WriteTexture(textures[t], t * 4, ch);
            using (ApplyMarker.Auto()) textures[t].Apply(true);
        }
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
    }

    void ClearBareDetail(System.Func<int, int, bool> scorched)
    {
        foreach (var (x, y) in batch)
        {
            var c = world.At(x, y);
            if (CellPaint.IsSnowy(c, world.Rules)) continue;
            bool grassy = CellPaint.IsGrassy(CellPaint.Mix(world, x, y, scorched(x, y)));
            int key = world.Idx(x, y);
            if (grassy && !cleared.Remove(key)) continue;
            if (!grassy) cleared.Add(key);
            var r = layout.TexelRect(x, y);
            if (!chunks.ContainsKey((r.I, r.J)) || !IsReady((r.I, r.J))) continue;
            if (!pendingDetail.TryGetValue((r.I, r.J), out var cells)) pendingDetail[(r.I, r.J)] = cells = new Dictionary<int, (ChunkRect, bool)>();
            cells[key] = (r, grassy);
        }
    }

    void WriteDetail()
    {
        if (pendingDetail.Count == 0) return;
        var at = pendingDetail.Keys.First();
        var cells = pendingDetail[at];
        pendingDetail.Remove(at);
        bare.Clear();
        regrown.Clear();
        foreach (var (r, grassy) in cells.Values) (grassy ? regrown : bare).Add(r);
        chunks[at].touched = true;
        if (bare.Count > 0) ClearDetail(Writable(chunks[at]), bare);
        if (regrown.Count > 0) RestoreDetail(chunks[at], regrown);
    }

    readonly Dictionary<(int i, int j), Dictionary<int, (ChunkRect rect, bool grassy)>> pendingDetail = new();
    readonly List<ChunkRect> bare = new(), regrown = new();
    readonly HashSet<int> cleared = new();

    void RestoreDetail(Chunk ch, List<ChunkRect> rects)
    {
        var data = Writable(ch);
        var spans = DetailSpans(data, rects, out int bx0, out int bz0, out int w, out int h);
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            var map = data.GetDetailLayer(bx0, bz0, w, h, layer);
            var saved = builder != null ? builder.DetailLayer(ch.at, layer, bx0, bz0, w, h) : ch.original.GetDetailLayer(bx0, bz0, w, h, layer);
            foreach (var (x0, z0, x1, z1) in spans)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) map[z - bz0, x - bx0] = saved[z - bz0, x - bx0];
            data.SetDetailLayer(bx0, bz0, layer, map);
        }
    }

    static List<(int x0, int z0, int x1, int z1)> DetailSpans(TerrainData data, List<ChunkRect> rects, out int bx0, out int bz0, out int w, out int h)
    {
        float k = data.detailResolution / (float)data.alphamapResolution;
        bx0 = int.MaxValue;
        bz0 = int.MaxValue;
        int bx1 = int.MinValue, bz1 = int.MinValue;
        var spans = new List<(int x0, int z0, int x1, int z1)>(rects.Count);
        foreach (var r in rects)
        {
            int x0 = Mathf.FloorToInt(r.X * k), z0 = Mathf.FloorToInt(r.Z * k);
            int x1 = Mathf.Max(x0, Mathf.CeilToInt((r.XMax + 1) * k) - 1), z1 = Mathf.Max(z0, Mathf.CeilToInt((r.ZMax + 1) * k) - 1);
            spans.Add((x0, z0, x1, z1));
            bx0 = Mathf.Min(bx0, x0); bz0 = Mathf.Min(bz0, z0); bx1 = Mathf.Max(bx1, x1); bz1 = Mathf.Max(bz1, z1);
        }
        w = bx1 - bx0 + 1;
        h = bz1 - bz0 + 1;
        return spans;
    }

    public static float[,,] Compose(PaintBlock block, int[] layers, float[,,] baseAlpha)
    {
        var r = block.Rect;
        var full = new float[r.Height, r.Width, baseAlpha.GetLength(2)];
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) ComposeTexel(block, (layers, baseAlpha), full, x, z, (0, 0));
        return full;
    }

    static void ComposeTexel(PaintBlock block, (int[] layers, float[,,] alpha) saved, float[,,] into, int x, int z, (int x, int z) offset)
    {
        var (layers, baseAlpha) = saved;
        int ox = offset.x, oz = offset.z;
        var r = block.Rect;
        float a = block.Cover[z, x];
        int all = into.GetLength(2);
        for (int n = 0; n < all; n++) into[oz + z, ox + x, n] = baseAlpha[r.Z + z, r.X + x, n] * (1f - a);
        if (a <= 0f) return;
        for (int k = 0; k < layers.Length; k++)
            if (layers[k] >= 0) into[oz + z, ox + x, layers[k]] += a * block.Mix[z, x, k];
    }

    public static void ClearDetail(TerrainData data, ChunkRect r) => ClearDetail(data, new List<ChunkRect> { r });

    public static void ClearDetail(TerrainData data, List<ChunkRect> rects)
    {
        if (rects.Count == 0) return;
        var spans = DetailSpans(data, rects, out int bx0, out int bz0, out int w, out int h);
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            var map = data.GetDetailLayer(bx0, bz0, w, h, layer);
            bool any = false;
            foreach (var (x0, z0, x1, z1) in spans)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        any |= map[z - bz0, x - bx0] != 0;
                        map[z - bz0, x - bx0] = 0;
                    }
            if (any) data.SetDetailLayer(bx0, bz0, layer, map);
        }
    }

    public void Destroy()
    {
        foreach (var ch in chunks.Values)
        {
            Restore(ch);
            if (ch.terrain != null) ch.terrain.detailObjectDistance = ch.detailDistance;
        }
        foreach (var stack in spare.Values)
            foreach (var data in stack)
                if (data != null) Free(data);
        spare.Clear();
    }
}
