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
        // How far it drew details (grass, small plants) as built.
        public float detailDistance;
        // original: the chunk's data as the scene had it (null when the terrain was built from the builder's arrays);
        // data: its runtime copy. fresh: data is as built and nothing has changed it yet (the first Bind takes it as is).
        public TerrainData original, data;
        public bool fresh;
        // Something changed data since it was built (heights, paint, grass, trees): a new world builds it again. Untouched,
        // a world on the same map takes it as it is (view-rebuild: 729 chunks built again were most of a big map's bind).
        public bool touched;
        // Where the terrain stands (its transform's height), read once a Bind rather than once a cell.
        public float bottom;
        // Heights in metres above the terrain's position, before the cells' offsets: [z, x].
        public float[,] baseHeights;
        // CellPaint.Layers as indices into the terrain's layers (-1: the terrain lacks it).
        public int[] layers;
        // The saved paint, [z, x, terrain layer], read the first time the chunk is painted.
        public float[,,] baseAlpha;
        // This frame's painted texels, [z, x, terrain layer] over the whole alphamap, and the box
        // round them (Flush writes it to the terrain once).
        public float[,,] frameAlpha;
        public int dx0 = int.MaxValue, dz0 = int.MaxValue, dx1 = -1, dz1 = -1;
        public bool heightsChanged;
        // In syncQueue / paintQueue already, so a chunk waits in each once.
        public bool syncQueued, paintQueued;
        // Made one at a time (a generated map's, #205): whether its data and the arrays read from it are made for this
        // world, and what waited for that (cells to repaint, cells to paint afresh, anything else).
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

    /// <summary>The world the ground was last bound to (null before the first Bind).</summary>
    public World World => world;
    // Each cell's base height, a block a chunk terrain (#207): a cell of a generated map's unmade chunk stands on the map's land.
    CellsByChunk<float> cellBase;
    // Each cell's paint as baked (CellPaint.Mix of the starting world); a cell still painting
    // this keeps the saved paint. None for a cell of an unmade chunk.
    CellsByChunk<IReadOnlyList<float>> bakedPaint;

    readonly GroundBuilder builder;
    // A generated map's ground, a chunk at a time (#205): no chunk has data until its terrain is made (Attach) or something
    // writes to it, and an untouched chunk whose terrain goes gives its data back (Release), to be used again.
    readonly bool lazy;
    readonly HashSet<(int i, int j)> attached = new();
    readonly Dictionary<(int res, int texels), Stack<TerrainData>> spare = new();
    // The offsets the arrays were made with (the baked ground's: no laid land), and the river's extra under its cells.
    // Kept only for the chunks the map's laid land moves (the rest are the heightfield's own until a cell there changes).
    readonly Dictionary<(int i, int j), float[,]> bakedOffsets = new();
    readonly Dictionary<int, float> raised = new();
    HashSet<(int i, int j)> detailNear = new();
    const int MostSpare = 16;

    /// <summary>
    /// A generated map's ground, made a chunk at a time from <paramref name="builder"/>'s arrays (#205, J-086): a chunk is
    /// made when its terrain is (Attach) or when something changes it, and nothing is made for the rest of the map.
    /// </summary>
    public ValleyGround(TerrainGrid layout, GroundBuilder builder)
    {
        this.layout = layout;
        this.builder = builder;
        lazy = true;
        foreach (var at in layout.GridChunks()) chunks[at] = new Chunk { at = at, detailDistance = MapTerrains.DetailMetres };
    }

    /// <summary>Whether its chunks are made one at a time (a generated map's).</summary>
    public bool Lazy => lazy;

    /// <summary>The arrays its chunks are built from.</summary>
    public GroundBuilder Builder => builder;

    /// <summary>The map's land, in land units by cell (land.json): a chunk not made yet stands on it. Set before Bind.</summary>
    public int[] Land { get; set; }

    /// <summary>A chunk was made for this world (its data, trees and paint read): what keeps a chunk's state takes it up.</summary>
    public event System.Action<(int i, int j), TerrainData> ChunkReady;

    /// <summary>A chunk nothing had changed gave its data back (its terrain went): what kept its state lets it go.</summary>
    public event System.Action<(int i, int j), TerrainData> ChunkGone;

    /// <summary>Whether a chunk's data is made for this world (always, for a ground made whole).</summary>
    public bool IsReady((int i, int j) at) => !lazy || (chunks.TryGetValue(at, out var ch) && ch.ready);

    /// <summary>How many chunks have data now.</summary>
    public int WithData => chunks.Values.Count(c => c.data != null);

    /// <summary>
    /// What the ground holds now, for the flyover probe's memory line: chunks with data (and of those, without a terrain
    /// and why they're kept), the cells' blocks, the spare data and the heightfield's chunks worked out.
    /// </summary>
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

    /// <summary>
    /// Runs <paramref name="then"/> once the chunk is made, and says false; true when it is already (the caller goes on).
    /// </summary>
    public bool WhenReady((int i, int j) at, System.Action then)
    {
        if (IsReady(at) || !chunks.TryGetValue(at, out var ch)) return true;
        (ch.whenReady ??= new List<System.Action>()).Add(then);
        return false;
    }

    /// <summary>Something the view keeps for this chunk changed (its trees): it keeps its data when its terrain goes.</summary>
    public void Touch((int i, int j) at)
    {
        if (chunks.TryGetValue(at, out var ch)) ch.touched = true;
    }

    /// <summary>The tree kinds a chunk draws (every chunk has the template's), without making it.</summary>
    public TreePrototype[] TreePrototypes((int i, int j) at) =>
        lazy && builder != null && !IsReady(at) ? builder.TreePrototypes : Data(at).treePrototypes;

    /// <summary>
    /// Before a batch of chunks is made at once: their arrays and their height offsets worked out side by side, so none
    /// waits for the others one at a time.
    /// </summary>
    public void Prepare(IReadOnlyList<(int i, int j)> wanted)
    {
        builder.Prepare(wanted);
        heightfield?.Prepare(wanted);
    }

    /// <summary>The height offsets of these chunks worked out side by side, where they aren't yet.</summary>
    public void PrepareHeights(IReadOnlyList<(int i, int j)> wanted) => heightfield?.Prepare(wanted);

    /// <summary>A terrain now stands for this chunk: its data made for this world if it isn't, and given to it.</summary>
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

    /// <summary>
    /// The chunk's terrain goes (back to the pool). Untouched, its data goes too, to be used again, and the chunk is made
    /// afresh if it's wanted again; changed, it keeps it (and all it was given) for when its terrain comes back.
    /// </summary>
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
        // Its cells stand on the land again, with no paint of their own.
        cellBase.Drop(at.i, at.j);
        bakedPaint.Drop(at.i, at.j);
        paintedMix.Drop(at.i, at.j);
    }

    /// <summary>
    /// A generated map's chunks with no data (no terrain stands for them) that <paramref name="far"/> says are far from
    /// everyone: their height offsets and arrays let go, as a released chunk's are. The cells drawn round a character read
    /// the offsets of chunks with no terrain (ShownGround, for what stands on them) and their terrains' arrays are made
    /// ahead, and neither went again: a crossing of the island kept hundreds. Worked out again if they're wanted again.
    /// </summary>
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

    // A cell's ground while its chunk isn't made: the map's land (which the drawn ground matches to a unit, CheckGround).
    float LandMetres(int k) => Land != null && k < Land.Length ? Land[k] * landUnit : 0f;

    // A chunk made for this world (#205): its data from the arrays (data it kept, untouched, as it is), its base heights
    // under the offsets the arrays were made with, its cells' base and paint, the ground moved since drawn over it, and
    // what waited for it.
    void MakeReady(Chunk ch)
    {
        if (ch.data == null) ch.data = builder.Build(ch.at, Spared(ch.at));
        else builder.FreshLayers(ch.data);
        if (ch.terrain != null) Use(ch, ch.data);
        ch.ready = true;
        ch.touched = false;
        ch.bottom = builder.Bottom;
        var data = ch.data;
        int res = data.heightmapResolution;
        var heights = data.GetHeights(0, 0, res, res);
        var baked = bakedOffsets.TryGetValue(ch.at, out var kept) ? kept : heightfield.Current(ch.at.i, ch.at.j);
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
        // The ground drawn now where it isn't the baked (laid land, a stamp, a dug cell): drawn over the arrays'.
        if (heightfield.Shapes(ch.at.i, ch.at.j) && Moved(heightfield.Current(ch.at.i, ch.at.j), baked)) WriteHeights(layout.SampleRect(ch.at.i, ch.at.j));
        TakeWaiting(ch, data);
    }

    // A made chunk's cells: their base from its heights (and the river's extra), their paint as it was made.
    void ReadCells(Chunk ch)
    {
        foreach (var (x, y) in layout.CellsIn(ch.at.i, ch.at.j))
        {
            int k = world.Idx(x, y);
            cellBase[k] = SampleBase(x, y) + (raised.TryGetValue(k, out float more) ? more : 0f);
            bakedPaint[k] = paintedMix[k] = CellPaint.Mix(world, world.At(x, y));
        }
    }

    static bool Moved(float[,] now, float[,] baked)
    {
        int h = now.GetLength(0), w = now.GetLength(1);
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (Mathf.Abs(now[z, x] - baked[z, x]) > 1e-5f) return true;
        return false;
    }

    // What waited for a chunk to be made: its cells to repaint and to paint afresh, then those keeping its state, then the rest.
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

    // The chunk a cell's paint and trees are in, made: false while it isn't (a generated map's chunk out of sight).
    bool CellReady(int x, int y) => !lazy || (chunks.TryGetValue(layout.ChunkOfCell(x, y), out var ch) && ch.ready);

    /// <summary>
    /// The ground of <paramref name="terrains"/>; <paramref name="built"/>: they already carry <paramref name="builder"/>'s
    /// chunks (MapTerrains built them as the scene started), so the first Bind builds them no second time.
    /// </summary>
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

    /// <summary>How far the terrains draw details as built (ChemGrid.DetailDistance takes it).</summary>
    public float DetailDistance
    {
        get
        {
            if (lazy) return MapTerrains.DetailMetres;
            return chunks.Count > 0 ? chunks.Values.Max(c => c.detailDistance) : 0f;
        }
    }

    /// <summary>
    /// Only the terrains <paramref name="near"/> the camera draw their details (ChemGrid.DetailChunks):
    /// the others would draw none at that range anyway but still cost a detail pass a frame.
    /// </summary>
    public void DrawDetails(IReadOnlyCollection<(int i, int j)> near)
    {
        using var marked = DrawDetailsMarker.Auto();
        PerfCounts.Add("detail.changes");
        if (lazy)
        {
            // Only the terrains made (#205); one made later takes the last near set as it's attached.
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

    /// <summary>The chunks the cells reach as drawn now: a runtime copy once one changed, else the saved one. Made ones only, on a generated map.</summary>
    public IEnumerable<((int i, int j) at, TerrainData data)> Chunks()
    {
        foreach (var kv in chunks)
            if (!lazy || kv.Value.ready) yield return (kv.Key, Shown(kv.Value));
    }

    /// <summary>One of the chunks the cells reach, as drawn now (made now, on a generated map, if it isn't).</summary>
    public TerrainData Data((int i, int j) at) => Shown(Made(chunks[at]));

    /// <summary>Whether the cells reach this chunk (it has a terrain).</summary>
    public bool HasChunk((int i, int j) at) => chunks.ContainsKey(at);

    /// <summary>One of the chunks the cells reach, to change: its runtime copy, made now if it has none yet.</summary>
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

    // The chunk's runtime copy (Bind makes them all; this only covers a chunk Bind didn't reach).
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

    // Back to the saved chunk, dropping the copy (the terrain may be gone already, as the scene unloads).
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

    /// <summary>How many chunks have their height offsets worked out now (a generated map's only as they're wanted).</summary>
    public int HeightfieldChunks => heightfield?.Worked ?? 0;

    /// <summary>What working out the cells' height offsets took at the last Bind (ms).</summary>
    public double HeightfieldMs { get; private set; }

    /// <summary>What making the chunks took at the last Bind (ms): the probe compares built with baked.</summary>
    public double BindMs { get; private set; }

    // How many heightmap samples a frame works out for reshaped ground (#151): a radius 32 stamp's take a dozen frames or so.
    const int MostSamplesAFrame = 4096;

    /// <summary>True while reshaped ground is still easing to its new height.</summary>
    public bool Shaping => heightfield != null && !heightfield.Settled;

    /// <summary>For the probe: cells still waiting for their paint (a valley-wide snowfall takes a minute or two).</summary>
    public int Repainting => repaint.Count;

    /// <summary>Starts over on a world: the saved chunks again (copied when first changed), their base heights recovered.</summary>
    public void Bind(World w, float stepHeight, float shapeSpeed, bool flatUnderWalls = false)
    {
        using var bound = BindMarker.Auto();
        world = w;
        landUnit = stepHeight / w.Rules.Sim.WaterUnitsPerStep;
        heightfield = new CellHeightfield(layout, stepHeight, shapeSpeed) { FlatUnderWalls = flatUnderWalls, LaidLandDrawn = false };
        var reset = System.Diagnostics.Stopwatch.StartNew();
        // A generated map's chunks are worked out as they're wanted (#206): every chunk's at once was 0.5 s on the nine.
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
        // The valley's every cell, drawn whole from the start.
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

    // A generated map's Bind (#205): no chunk is made for the new world until its terrain is or something changes it. One
    // made for the last world keeps its data if nothing changed it (made afresh from it), else gives it up; those with a
    // terrain now are made at once. Every other cell stands on the map's land until its chunk is made.
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
        // The offsets the arrays were made with are the heightfield's as it's reset (no laid land): kept for the chunks the
        // laid land moves, before it's drawn.
        bakedOffsets.Clear();
        var laid = w.LandLaidCells().ToList();
        foreach (var r in heightfield.DirtyRects(laid))
            if (!bakedOffsets.ContainsKey((r.I, r.J))) bakedOffsets[(r.I, r.J)] = (float[,])heightfield.Current(r.I, r.J).Clone();
        heightfield.LaidLandDrawn = true;
        heightfield.Update(w, laid, snap: true);
        // No chunk's cells yet: each block is made with its chunk (MakeReady), the rest stand on the land.
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

    // Each cell's mix as its texels were last composed (the baked one at first): a cell asked to
    // repaint that would paint the same mix again changes nothing.
    CellsByChunk<IReadOnlyList<float>> paintedMix;

    // The cells' arrays, a block a chunk terrain, made as their chunks are (none yet).
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
        // Paint still waiting was the old world's, and what it would write from has just gone.
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
    }

    /// <summary>CellPaint's layers as indices into a terrain's layers, by name ("&lt;name&gt;_TerrainLayer...", as ValleyBuilder names them); -1 where missing.</summary>
    public static int[] LayerIndices(TerrainLayer[] layers) => LayerIndices(layers.Select(l => l != null ? l.name : "").ToArray());

    /// <summary>The same, by the layers' names.</summary>
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
        // Cells along a row share a chunk: the last one found is kept rather than looked up again.
        if (lastBase == null || lastBase.at != (i, j)) lastBase = chunks.TryGetValue((i, j), out var found) ? found : null;
        return lastBase != null && lastBase.at == (i, j) ? lastBase.bottom + lastBase.baseHeights[sz, sx] : 0f;
    }

    /// <summary>The valley's own ground at a cell, before the cell's offset.</summary>
    public float BaseHeight(int x, int y) => cellBase != null && world.InBounds(x, y) ? cellBase[world.Idx(x, y)] + world.LandMovedAt(x, y) * landUnit : 0f;

    /// <summary>
    /// Counts some cells' ground from higher than the terrain is drawn there (after Bind): the river's
    /// cells over its channel, whose water stands at the banks' level. <paramref name="cells"/> are
    /// world cell indices, <paramref name="metres"/> how much higher each.
    /// </summary>
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
            // A chunk not made yet takes it as it's made (its cells stand on the land until then).
            raised[cells[k]] = (raised.TryGetValue(cells[k], out float was) ? was : 0f) + metres[k];
            int x = cells[k] % world.W, y = cells[k] / world.W;
            if (CellReady(x, y)) cellBase[cells[k]] += metres[k];
        }
    }

    /// <summary>The ground drawn at a cell's centre now (it lags its target while easing).</summary>
    public float ShownGround(int x, int y)
    {
        var (i, j, sx, sz) = layout.CellSample(x, y);
        float drawnBase = cellBase != null && world.InBounds(x, y) ? cellBase[world.Idx(x, y)] : 0f;
        return drawnBase + (heightfield != null && heightfield.Shapes(i, j) ? heightfield.Current(i, j)[sz, sx] : 0f);
    }

    /// <summary>The terrain's ground drawn now at a world point over the cells (0 off them).</summary>
    public float GroundAt(float wx, float wz)
    {
        var (x, y) = layout.CellAt(wx, wz);
        if (!chunks.TryGetValue(layout.ChunkOfCell(x, y), out var ch)) return 0f;
        // A generated map's chunk not made: the cell's ground as it stands (the land and its offset), nothing made for it.
        if (lazy && !ch.ready) return world != null && world.InBounds(x, y) ? ShownGround(x, y) : 0f;
        var data = Shown(ch);
        if (ch.terrain != null)
        {
            var p = ch.terrain.transform.position;
            return p.y + data.GetInterpolatedHeight((wx - p.x) / data.size.x, (wz - p.z) / data.size.z);
        }
        var (cx, cz) = layout.ChunkCorner(ch.at.i, ch.at.j);
        return ch.bottom + data.GetInterpolatedHeight((wx - cx) / data.size.x, (wz - cz) / data.size.z);
    }

    /// <summary>A cell's elevation or height changed: its ground eases there (see Advance).</summary>
    public void Reshape(int x, int y) => reshaped.Add((x, y));

    /// <summary>A cell's terrain or cover changed: its texels are repainted at the next Flush.</summary>
    public void Repaint(int x, int y) => repaint.Add((x, y));

    /// <summary>A cell the bake drew as something else (a laid row's): its paint is laid afresh, and its grass goes.</summary>
    public void PaintAnew(int x, int y)
    {
        if (!CellReady(x, y))
        {
            // Laid afresh when its chunk is made (its baked paint is read then).
            var ch = chunks[layout.ChunkOfCell(x, y)];
            (ch.anew ??= new List<(int x, int y)>()).Add((x, y));
            return;
        }
        int key = world.Idx(x, y);
        bakedPaint[key] = null;
        paintedMix[key] = null;
        repaint.Add((x, y));
    }

    /// <summary>Eases reshaped ground by <paramref name="dt"/> seconds and hands the moved samples to the terrains.</summary>
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

    // For a performance pass: how many rectangles, and how many heightmap samples in them.
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
        // A chunk not made takes the ground as it is then, when it's made (MakeReady).
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

    // The easing writes the same few rectangle sizes every frame: one buffer per size, reused.
    readonly Dictionary<(int w, int h), float[,]> heightBuffers = new();

    float[,] HeightBuffer(int w, int h)
    {
        if (!heightBuffers.TryGetValue((w, h), out var buffer)) heightBuffers[(w, h)] = buffer = new float[h, w];
        return buffer;
    }

    /// <summary>Once a frame: the batched paint, and the terrains' LOD and colliders for moved ground.</summary>
    public void Flush(System.Func<int, int, bool> scorched)
    {
        using var marked = FlushMarker.Auto();
        using (SyncMarker.Auto()) SyncOneHeightmap();
        using (TreeColliderMarker.Auto()) RebuildOneTreeCollider();
        PerfCounts.Peak("ground.repaint_waiting", repaint.Count);
        using (DetailMarker.Auto()) WriteDetail();
        if (repaint.Count == 0)
        {
            // The paint composed in earlier frames still goes out, a chunk a frame.
            WriteOnePaint();
            return;
        }
        // A big fire scorches hundreds of cells a tick: repaint only as many as fit the frame's
        // budget, the rest over the next frames, so the terrain's paint never stalls a frame.
        TakeBatch(scorched);
        if (batch.Count == 0)
        {
            WriteOnePaint();
            return;
        }
        // A cell's paint reaches its neighbours' texels: repaint them too, as strips of cells.
        System.Func<int, int, IReadOnlyList<float>> paintOf = (x, y) => PaintOf(x, y, scorched(x, y));
        paintTimer.Restart();
        HashSet<(int x, int y)> around;
        using (PaintMarker.Auto())
        {
            around = CellPaint.Around(layout, batch);
            foreach (var r in CellPaint.Runs(layout, around)) Paint(r, paintOf);
        }
        msPerCell = Mathf.Lerp(msPerCell, (float)paintTimer.Elapsed.TotalMilliseconds / batch.Count, 0.3f);
        // A neighbour composed here with a mix it hasn't been painted with (it's waiting its turn)
        // is no longer what paintedMix says: it paints in full on its turn, whatever its mix then.
        foreach (var (x, y) in around)
        {
            if (!world.InBounds(x, y)) continue;
            int key = world.Idx(x, y);
            if (!ReferenceEquals(paintedMix[key], CellPaint.Mix(world, x, y, scorched(x, y)))) paintedMix[key] = null;
        }
        WriteOnePaint();
        using (DetailMarker.Auto()) ClearBareDetail(scorched);
    }

    // Profiler markers (Window > Analysis > Profiler) round the terrain's per-frame work.
    static readonly Unity.Profiling.ProfilerMarker FlushMarker = new("ValleyGround.Flush"),
        PaintMarker = new("ValleyGround.Paint"), DetailMarker = new("ValleyGround.ClearGrass"), UploadMarker = new("ValleyGround.UploadPaint"),
        DrawDetailsMarker = new("ValleyGround.DrawDetails"), HeightUpdateMarker = new("ValleyGround.HeightUpdate"), HeightQueueMarker = new("ValleyGround.HeightQueue"),
        HeightEaseMarker = new("ValleyGround.HeightEase"), HeightWriteMarker = new("ValleyGround.HeightWrite"),
        SyncMarker = new("ValleyGround.SyncHeightmap"), TreeColliderMarker = new("ValleyGround.TreeCollider"), BlockMarker = new("ValleyGround.Paint.Block"),
        ComposeMarker = new("ValleyGround.Paint.Compose"), ApplyMarker = new("ValleyGround.UploadPaint.Apply");

    // Each texel's noise, worked out the first time it paints and kept (CellPaint.Block): a shower's
    // ash repaints the same texels for minutes, and the noise was much of a texel's cost (#70).
    readonly TexelNoise texelNoise = new();

    /// <summary>For a check (-perf-same-check): paints each strip afresh too, counting any that differ bit for bit from the kept noise's.</summary>
    public static bool CheckKeptNoise { get; set; }

    /// <summary>The strips painted both ways under CheckKeptNoise, and those that came out different.</summary>
    public static (int painted, int different) KeptNoiseChecked => (keptChecked, keptDifferent);

    static int keptChecked, keptDifferent;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options).
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

    // Composing paint gets about this much of a frame (ms); the cells it takes follow from how long
    // the last ones took, between the bounds below. The budget decides, not the most: at 48 a cheaper
    // paint (kept noise) stopped well short of its millisecond while a shower's ash waited (#70), so
    // the most is only a guard against a wild estimate.
    const float PaintBudgetMs = 1f;
    const int FewestRepaints = 8, MostRepaints = 1000;
    readonly List<(int x, int y)> batch = new();
    readonly System.Diagnostics.Stopwatch paintTimer = new();
    float msPerCell = 0.02f;

    // Cells asked to repaint looked at a frame, painted or not: a storm's rain washing the ash off
    // scorched ground asks for thousands that paint just as they did (charred), and looking costs little.
    const int MostLooks = 2000;
    readonly List<(int x, int y)> looked = new();

    // This frame's cells to paint: those whose mix differs from the one their texels were composed
    // with. The rest are dropped without painting, and don't use the frame's budget.
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

    // A cell whose chunk isn't made (out of sight on a generated map) repaints when it is.
    bool WaitsForChunk((int x, int y) cell)
    {
        if (CellReady(cell.x, cell.y)) return false;
        if (chunks.TryGetValue(layout.ChunkOfCell(cell.x, cell.y), out var ch)) (ch.repaint ??= new List<(int x, int y)>()).Add(cell);
        return true;
    }

    int PaintCellsThisFrame() => Mathf.Clamp(Mathf.FloorToInt(PaintBudgetMs / Mathf.Max(msPerCell, 1e-4f)), FewestRepaints, MostRepaints);

    // Moved ground reaches a terrain's LOD and collider (SyncHeightmap, a few ms on a full chunk)
    // one chunk a frame, in the order they moved; the rest wait in the queue for the next frames. Only
    // the chunks waiting are looked at, never every chunk of the map (#199, J-086).
    void SyncOneHeightmap()
    {
        while (syncQueue.Count > 0)
        {
            var ch = syncQueue.Dequeue();
            ch.syncQueued = false;
            // A new world (Restore) took its waiting heights away.
            if (!ch.heightsChanged || ch.data == null) continue;
            ch.data.SyncHeightmap();
            ch.heightsChanged = false;
            return;
        }
    }

    /// <summary>A chunk's trees changed: its collider catches up, a chunk a frame.</summary>
    public void TreesMoved((int i, int j) at) => treeColliders.Add(at);

    // A terrain's collider keeps the trees it was given: setting treeInstances draws the change and leaves the old trunks
    // standing unseen (#161). Only giving it its data again takes them, and a fire changes many chunks at once. Players
    // and props are all it stops, so a frame or two late moves nothing shared.
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
    // Metres in a land unit: what a land stamp's integers are drawn at.
    float landUnit;

    // What a cell paints over the saved paint: nothing while it paints as it was baked.
    IReadOnlyList<float> PaintOf(int x, int y, bool scorched)
    {
        var mix = CellPaint.Mix(world, x, y, scorched);
        return ReferenceEquals(mix, bakedPaint[world.Idx(x, y)]) ? null : mix;
    }

    // Composes a strip of cells' paint into its chunk's frame copy; WritePaint sends it to the terrain.
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

    // Uploads one chunk's waiting paint a frame, in the order they were painted, so a fire across several
    // chunks spreads its uploads over frames rather than stacking them in one. Only the chunks waiting
    // are looked at (#199).
    void WriteOnePaint()
    {
        while (paintQueue.Count > 0)
        {
            var ch = paintQueue.Dequeue();
            ch.paintQueued = false;
            // A new world (Recover) dropped what it had waiting.
            if (ch.dx1 < 0 || ch.data == null) continue;
            WritePaint(ch);
            return;
        }
    }

    // One paint texture's texels in the chunk's box, written in place through GetPixelData (no copy,
    // nothing for the garbage collector); <paramref name="first"/> is the layer in its red channel.
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


    // Writes the box round a chunk's painted strips into the terrain's paint textures (its
    // alphamapTextures, four layers to a texture, one per channel: the paint's own storage, so
    // GetAlphamaps reads it back) and uploads them with their mip levels. TerrainData.SetAlphamaps
    // does the same and also regenerates the base map, the low-detail texture drawn past the
    // terrain's basemapDistance, about 20 ms a call; that stalled a big fire's frames, and nothing
    // in the valley is far enough off to draw the base map.
    static void WritePaint(Chunk ch)
    {
        using var marked = UploadMarker.Auto();
        if (ch.dx1 < 0) return;
        ch.touched = true;
        var textures = ch.data.alphamapTextures;
        for (int t = 0; t < textures.Length; t++)
        {
            WriteTexture(textures[t], t * 4, ch);
            // The whole texture, with its mips: a chunk's paint is only 64 × 64 texels, so
            // uploading just the painted box saves nothing worth its staging copies.
            using (ApplyMarker.Auto()) textures[t].Apply(true);
        }
        ch.dx0 = ch.dz0 = int.MaxValue;
        ch.dx1 = ch.dz1 = -1;
    }

    // Notes which of this frame's cells lose their detail grass (bare) or get it back (grown again,
    // Plant Growth: the valley's own grass from the saved chunk). WriteDetail writes them a chunk a
    // frame: one read and one write per grass kind (each SetDetailLayer rebuilds the terrain's grass
    // patches there, so a call per cell, or every chunk of a big fire in one frame, stalled it).
    void ClearBareDetail(System.Func<int, int, bool> scorched)
    {
        foreach (var (x, y) in batch)
        {
            // Snow only lies on the grass (clearing it under snow would leave the grass bare after the thaw).
            var c = world.At(x, y);
            if (CellPaint.IsSnowy(c, world.Rules)) continue;
            bool grassy = CellPaint.IsGrassy(CellPaint.Mix(world, x, y, scorched(x, y)));
            int key = world.Idx(x, y);
            if (grassy && !cleared.Remove(key)) continue;
            if (!grassy) cleared.Add(key);
            var r = layout.TexelRect(x, y);
            if (!chunks.ContainsKey((r.I, r.J)) || !IsReady((r.I, r.J))) continue;
            if (!pendingDetail.TryGetValue((r.I, r.J), out var cells)) pendingDetail[(r.I, r.J)] = cells = new Dictionary<int, (ChunkRect, bool)>();
            cells[key] = (r, grassy); // a cell bared and grown again before its chunk's turn: the last wins
        }
    }

    // One chunk's waiting grass changes a frame.
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

    // Grass changes waiting for their chunk's turn: by chunk, then by world cell index.
    readonly Dictionary<(int i, int j), Dictionary<int, (ChunkRect rect, bool grassy)>> pendingDetail = new();
    readonly List<ChunkRect> bare = new(), regrown = new();
    // Cells whose detail grass was cleared (world cell indices), so grass growing back there gets it back.
    readonly HashSet<int> cleared = new();

    // The saved chunk's detail grass copied back over texel rectangles of its runtime copy: like
    // ClearDetail, the box round them read once per grass kind (from both) and written back once.
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

    // Texel rectangles as detail-map spans, and the box round them (detail and alphamap resolutions may differ).
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

    /// <summary>
    /// A CellPaint block laid over a terrain's paint (<paramref name="baseAlpha"/>, the whole
    /// alphamap, [z, x, layer]) for the block's rectangle, spread over all of the terrain's layers
    /// (<paramref name="layers"/> from LayerIndices).
    /// </summary>
    public static float[,,] Compose(PaintBlock block, int[] layers, float[,,] baseAlpha)
    {
        var r = block.Rect;
        var full = new float[r.Height, r.Width, baseAlpha.GetLength(2)];
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++) ComposeTexel(block, (layers, baseAlpha), full, x, z, (0, 0));
        return full;
    }

    // One texel of the block over the saved paint (its layer indices and alphamap), into
    // <paramref name="into"/> at (x, z) moved by <paramref name="offset"/>.
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

    /// <summary>Clears the detail grass under a texel rectangle (detail and alphamap resolutions may differ).</summary>
    public static void ClearDetail(TerrainData data, ChunkRect r) => ClearDetail(data, new List<ChunkRect> { r });

    /// <summary>
    /// Clears the detail grass under several texel rectangles of one terrain: the box round them read
    /// once per grass kind, the rectangles zeroed in it, and written back once.
    /// </summary>
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

    /// <summary>Frees the runtime copies, the saved chunks back on the terrains.</summary>
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
