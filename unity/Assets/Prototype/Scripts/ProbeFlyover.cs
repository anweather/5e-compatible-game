using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chem;
using UnityEngine;

// The probe's flyover scenario, on a generated map (unity/Temp/map names one; made for scale-12x10): how loading holds up
// when the player flies fast. The wizard is put in the dev fly mode (PlaytestTools.Fly, F4) on the west coast at the
// spawn's row and moved east at the fly mode's 40 m/s (FlyoverFastProbe: 80, Shift on 40) every frame, as FlyStep moves
// it (the transform, by the speed times the unscaled frame time), Height over the ground under it, with the camera
// behind it looking east, until it passes the east coast. Every frame: its time and the wizard's cell; and along the
// line east of the wizard out to the known ring, which land is drawn (a chunk terrain switched on, or a far-ring known
// mesh) and which is a hole (land with neither), how far the drawn ground reaches ahead, and whether the terrains ahead
// have trees. Memory at the start and the end. Editor numbers, not a player's.
public class FlyoverProbe : ProbeScenario
{
    protected virtual float Speed => 40f;
    // Whether it flies back to the west coast after reaching the east (FlyoverBackProbe).
    protected virtual bool Back => false;
    const float Height = 30f, Step = 48f, SettleMost = 30f;

    protected override IEnumerator Run()
    {
        var land = MapLand.Current;
        var terrains = MapTerrains.Instance;
        var tools = PlaytestTools.Instance;
        Check("generated_map", land != null && terrains != null && terrains.Lazy, $"name a generated map in unity/{ChemGrid.EditorMapFile}");
        Check("fly_mode_there", tools != null);
        if (land == null || terrains == null || !terrains.Lazy || tools == null) yield break;

        // The coasts on the spawn's row: the first and last land cells.
        int row = land.Map.Spawn.y, w = land.Map.W;
        int west = -1, east = -1;
        for (int x = 0; x < w; x++)
            if (land.LandAt(x, row) > land.Map.SeaLevel && !land.Sea.IsSea(x, row))
            {
                if (west < 0) west = x;
                east = x;
            }
        Check("coasts_found", west >= 0 && east > west, $"row {row}: west coast at cell {west}, east coast at cell {east}");
        if (west < 0 || east <= west) yield break;
        Metric("west_coast_x", west);
        Metric("east_coast_x", east);

        var start = grid.CellCenter(west, row);
        var end = grid.CellCenter(east, row);
        start.y = FlyY(start, land);
        Teleport(start);
        tools.Fly(true);
        var wizardT = wizard.transform;
        wizardT.position = start;
        Look(wizardT.position);

        // Loading settles at the start, as after a hop.
        var far = FarRing.Instance;
        // The terrains plan a few times a second: until they've planned for where the wizard is now, none are waiting.
        terrains.ReachNow();
        yield return null;
        yield return null;
        float settled = 0f;
        for (; settled < SettleMost && (terrains.Waiting > 0 || (far != null && !far.Settled)); settled += Time.unscaledDeltaTime)
        {
            Look(wizardT.position);
            yield return null;
        }
        yield return Wait(1f);
        Metric("start_settle_s", settled);
        Memory("start");
        yield return null;

        int sight = MapTerrains.Sight(), know = far != null ? far.Dials.known : sight;
        float chunkM = land.Layout.ChunkSize, terrainReach = sight * chunkM, knownReach = know * chunkM;
        Metric("sight_chunks", sight);
        Metric("known_chunks", know);
        Metric("far_clip_m", Camera.main.farClipPlane);

        var ms = new List<float>();
        var at = new List<int>();
        int frames = 0, holeFrames = 0, nearHoleFrames = 0, treelessSeen = 0, terrainsSeen = 0, mostWaiting = 0, mostActive = 0;
        float leastAhead = float.MaxValue, leastAheadAt = 0f, sumAhead = 0f;
        int aheadFrames = 0;
        var holeChunks = new HashSet<(int, int)>();
        var nearHoleChunks = new HashSet<(int, int)>();
        var treeless = new HashSet<(int, int)>();
        var seenTerrains = new HashSet<(int, int)>();
        var holeAt = new SortedDictionary<int, int>();
        var nearHoleAt = new List<string>();
        int closeHoleFrames = 0;
        float leg = end.x - start.x, total = Back ? 2f * leg : leg, flown = 0f;
        // The first frame is the one after the start's memory reading (Memory("start"): a full collection and an object walk,
        // 0.8 to 1 s on the Scale Island): not the flight's, as after the readings on the way.
        bool measuredKm = false, measuredHalf = false, skipFrame = true;
        // With unity/Temp/flyprofile there, every timed marker is read each frame and the slow frames' are summed
        // (FlyProfile): what a slow frame is made of. It costs a little itself, so the timing runs go without it.
        using var profile = FlyProfile.Wanted() ? new FlyProfile() : null;
        while (flown < total)
        {
            float dt = Time.unscaledDeltaTime;
            profile?.Frame(dt * 1000f, grid.CellAt(wizardT.position).x, !skipFrame);
            // The frame after a memory reading (a full collection and an object walk) isn't the flight's.
            if (skipFrame) skipFrame = false;
            else ms.Add(dt * 1000f);
            var p = wizardT.position;
            at.Add(grid.CellAt(p).x);
            frames++;

            // Along the line ahead (east, or west on the way back): the drawn ground ahead, the holes in it.
            float heading = flown < leg ? 1f : -1f;
            float ahead = -1f;
            bool hole = false, nearHole = false, overLand = false;
            for (float d = 0f; d <= knownReach; d += Step)
            {
                var q = new Vector3(p.x + d * heading, 0f, p.z);
                if (q.x > end.x + chunkM || q.x < start.x - chunkM) break;
                var xz = new Vector2(q.x, q.z);
                if (!land.IsLand(xz) || land.IsSea(xz)) continue;
                overLand = true;
                var t = terrains.TerrainAt(q);
                bool terrain = t != null && t.terrainData != null && t.isActiveAndEnabled && t.drawHeightmap;
                var cell = land.CellOf(xz);
                var mc = (cell.x / ChunkGen.Size, cell.y / ChunkGen.Size);
                bool known = far != null && (!far.LandIsTerrain(mc.Item1, mc.Item2) || far.MeshShows(mc.Item1, mc.Item2));
                if (terrain)
                {
                    if (seenTerrains.Add(mc)) terrainsSeen++;
                    if (d <= terrainReach && t.terrainData.treeInstanceCount == 0 && treeless.Add(mc)) treelessSeen++;
                }
                if (terrain || known)
                {
                    if (ahead < 0f || ahead == d - Step) ahead = d;
                    continue;
                }
                hole = true;
                holeChunks.Add(mc);
                int band = (int)(d / chunkM);
                holeAt[band] = holeAt.TryGetValue(band, out int hn) ? hn + 1 : 1;
                if (d <= chunkM) closeHoleFrames++;
                if (d <= terrainReach)
                {
                    nearHole = true;
                    nearHoleChunks.Add(mc);
                    if (nearHoleAt.Count < 12)
                        nearHoleAt.Add($"frame {frames} wizard x {grid.CellAt(p).x} hole {d:0} m on at ({mc.Item1},{mc.Item2}): terrain {TerrainState(t)} far {(far != null ? far.Explain(mc.Item1, mc.Item2) : "none")}");
                }
                break;
            }
            if (hole) holeFrames++;
            if (nearHole) nearHoleFrames++;
            if (overLand && hole)
            {
                if (ahead < leastAhead) (leastAhead, leastAheadAt) = (Mathf.Max(0f, ahead), grid.CellAt(p).x);
                sumAhead += Mathf.Max(0f, ahead);
                aheadFrames++;
            }
            mostWaiting = Mathf.Max(mostWaiting, terrains.Waiting);
            mostActive = Mathf.Max(mostActive, World.ActiveChunks.Count);

            // Memory after the first kilometre: what the flight holds once the terrains round it stand, the baseline the end
            // is held to (memory_flat).
            if (!measuredKm && flown >= FirstKm)
            {
                measuredKm = true;
                skipFrame = true;
                Memory("km1");
            }
            // And half way, to tell what the flight still fills in after the first km from growth with distance.
            if (!measuredHalf && flown >= total / 2f)
            {
                measuredHalf = true;
                skipFrame = true;
                Memory("half");
            }

            // There and back: past the east coast the wizard turns and flies west to where it started.
            float move = Speed * dt;
            flown += move;
            float dir = flown <= leg ? 1f : -1f;
            var next = new Vector3(start.x + (flown <= leg ? flown : 2f * leg - flown), 0f, p.z);
            next.y = Mathf.Lerp(p.y, FlyY(next, land), Mathf.Clamp01(dt * 2f));
            wizardT.position = next;
            Look(next, dir);
            yield return null;
        }

        tools.Fly(false);
        if (profile != null) Debug.Log($"FLYOVER PROFILE {Speed:0} m/s{profile.Report()}");
        Memory("end");
        var stats = FrameStats.Of(ms);
        Metric("speed_m_s", Speed);
        Metric("frames", stats.Count);
        Metric("flight_s", Sum(ms) / 1000f);
        Metric("frame_mean_ms", stats.MeanMs);
        Metric("frame_p95_ms", stats.P95Ms);
        Metric("frame_p99_ms", stats.P99Ms);
        Metric("frame_worst_ms", stats.WorstMs);
        Metric("frames_over_33ms", ms.FindAll(m => m > 33.3f).Count);
        Metric("frames_over_100ms", ms.FindAll(m => m > 100f).Count);
        Metric("frames_with_a_hole_ahead", holeFrames);
        Metric("frames_with_a_hole_within_sight", nearHoleFrames);
        Metric("hole_chunks", holeChunks.Count);
        Metric("frames_with_a_hole_within_one_chunk", closeHoleFrames);
        var holeBands = new StringBuilder();
        foreach (var kv in holeAt) holeBands.Append($"{kv.Key * chunkM:0}-{(kv.Key + 1) * chunkM:0} m: {kv.Value}; ");
        Check("holes_by_distance", true, holeBands.Length > 0 ? holeBands.ToString() : "none");
        Metric("hole_chunks_within_sight", nearHoleChunks.Count);
        Metric("drawn_ahead_least_m", leastAhead == float.MaxValue ? -1f : leastAhead);
        Metric("drawn_ahead_least_at_x", leastAheadAt);
        Metric("drawn_ahead_mean_m_when_short", aheadFrames > 0 ? sumAhead / aheadFrames : -1f);
        Metric("terrains_seen", terrainsSeen);
        Metric("terrains_treeless_within_sight", treelessSeen);
        Metric("most_terrains_waiting", mostWaiting);
        Metric("most_active_chunks", mostActive);
        var c = terrains.Counts;
        Metric("terrains_standing_end", c.standing);
        Metric("terrains_created_ever", c.created);

        // The worst frames and where they were; the frames over 33 ms by 200-cell band.
        var order = new List<int>();
        for (int i = 0; i < ms.Count; i++) order.Add(i);
        order.Sort((a, b) => ms[b].CompareTo(ms[a]));
        var inv = CultureInfo.InvariantCulture;
        var worst = new StringBuilder();
        for (int i = 0; i < Mathf.Min(10, order.Count); i++) worst.Append(string.Format(inv, "{0:0} ms at x {1}; ", ms[order[i]], at[order[i]]));
        var bands = new SortedDictionary<int, int>();
        for (int i = 0; i < ms.Count; i++)
            if (ms[i] > 33.3f) bands[at[i] / 200 * 200] = bands.TryGetValue(at[i] / 200 * 200, out int n) ? n + 1 : 1;
        var bandText = new StringBuilder();
        foreach (var kv in bands) bandText.Append($"{kv.Key}: {kv.Value}; ");
        var holeText = new StringBuilder();
        foreach (var h in nearHoleChunks) holeText.Append($"({h.Item1},{h.Item2}) ");
        Debug.Log($"FLYOVER {Speed:0} m/s: worst {worst}\nover 33 ms by x band: {bandText}\nholes within sight at map chunks: {holeText}");
        Check("flew_coast_to_coast", flown >= total, $"{flown:0} of {total:0} m in {frames} frames{(Back ? ", there and back" : "")}");
        // Memory stays flat however far the flight goes: what's held at the end within MostGrowth of what was held after
        // the first kilometre (native allocated and managed used, after a full collection), and the terrains bounded. The
        // managed figure is allowed MostManagedGrowth: it holds the core's unpacked chunks, which pack again only
        // streaming.packTicks after they're last needed, so at 80 m/s a few hundred more wait behind the flight (bounded by
        // speed times that hold, not by the distance flown). It also follows the scenery under the flight (half way, over
        // the island's middle, holds less than either coast).
        float grewNative = mem["end_allocated"] / Mathf.Max(1f, mem["km1_allocated"]) - 1f;
        float grewManaged = mem["end_mono"] / Mathf.Max(1f, mem["km1_mono"]) - 1f;
        Metric("mem_growth_allocated_pct", grewNative * 100f);
        Metric("mem_growth_mono_pct", grewManaged * 100f);
        Check("memory_flat", grewNative <= MostGrowth && grewManaged <= MostManagedGrowth,
            $"after the first km -> the end: allocated {mem["km1_allocated"]:0} -> {mem["end_allocated"]:0} MB ({grewNative * 100f:+0;-0}%), " +
            $"managed {mem["km1_mono"]:0} -> {mem["end_mono"]:0} MB ({grewManaged * 100f:+0;-0}%), at most +{MostGrowth * 100f:0}% and +{MostManagedGrowth * 100f:0}%");
        int bound = (2 * (sight + 2) + 1) * (2 * (sight + 2) + 1) * 2;
        Check("terrains_bounded", c.standing <= bound && c.created <= bound, $"{c.standing} standing, {c.created} made ever, at most {bound}");
        Check("worst_frames", true, worst.ToString());
        Check("over_33ms_by_x", true, bandText.Length > 0 ? bandText.ToString() : "none");
        Check("no_hole_within_sight", nearHoleChunks.Count == 0, $"{nearHoleChunks.Count} land chunks within the drawn dial ({terrainReach:0} m) seen with nothing drawn: {holeText}");
        if (nearHoleAt.Count > 0) Debug.Log("FLYOVER HOLES " + string.Join(" | ", nearHoleAt));
    }

    // Memory readings by "<when>_<what>" (allocated, reserved, mono), after a full collection.
    readonly Dictionary<string, float> mem = new();
    const float FirstKm = 1000f, MostGrowth = 0.10f, MostManagedGrowth = 0.15f;

    // A memory reading now: metrics mem_<when>_..., and a log line with what the view's caches hold (to find a leak).
    void Memory(string when)
    {
        // The walk names every loaded object (hundreds of thousands, through a regex): gigabytes of garbage, which a
        // collection in flight then paid for in the frames measured. So only before and after the flight, and before the
        // collection here, which takes it.
        bool walk = when is "start" or "end";
        string objects = walk ? System.Environment.NewLine + "FLYOVER MESHES " + Named<Mesh>() + System.Environment.NewLine + "FLYOVER OBJECTS " + Named<GameObject>() : "";
        System.GC.Collect();
        const float mb = 1024f * 1024f;
        mem[when + "_allocated"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / mb;
        mem[when + "_reserved"] = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / mb;
        mem[when + "_mono"] = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / mb;
        Metric($"mem_{when}_allocated_mb", mem[when + "_allocated"]);
        Metric($"mem_{when}_reserved_mb", mem[when + "_reserved"]);
        Metric($"mem_{when}_mono_mb", mem[when + "_mono"]);
        var terrains = MapTerrains.Instance;
        var c = terrains.Counts;
        var world = grid.World;
        var far = FarRing.Instance;
        var impostors = TreeImpostors.Instance;
        Debug.Log($"FLYOVER HOLDINGS at={when} terrains standing={c.standing} pooled={c.pooled} created={c.created} " +
                  $"ground: {terrains.Ground?.Holdings()} world: unpacked={world?.UnpackedCount} pool={world?.PackPoolCount} " +
                  $"far_meshes={(far != null ? far.DrawnCount : 0)} impostor_blocks={(impostors != null ? impostors.Held : 0)} " +
                  $"cell_chunks_drawn={grid.ChunksHeldDrawn} undrawn_ever={grid.ChunksUndrawn} blockers={grid.BlockersStanding} " +
                  System.Environment.NewLine + PerfMemory.Line("flyover " + when) + objects);
    }

    static string TerrainState(Terrain t)
    {
        if (t == null) return "none";
        return t.isActiveAndEnabled ? "on" : "off";
    }

    // Loaded objects of a type by name (digits and commas taken out, so a chunk's or a cell's are one), the most first.
    static string Named<T>() where T : Object
    {
        var groups = new Dictionary<string, int>();
        foreach (var o in Resources.FindObjectsOfTypeAll<T>())
        {
            string name = System.Text.RegularExpressions.Regex.Replace(o.name, "[0-9,.-]+", "#");
            groups[name] = groups.TryGetValue(name, out int n) ? n + 1 : 1;
        }
        var list = new List<KeyValuePair<string, int>>(groups);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        var text = new StringBuilder();
        for (int i = 0; i < Mathf.Min(12, list.Count); i++) text.Append($"{list[i].Key}:{list[i].Value}; ");
        return text.ToString();
    }

    static float Sum(List<float> xs)
    {
        float s = 0f;
        foreach (var x in xs) s += x;
        return s;
    }

    // Height over the ground under a point, or over the water.
    float FlyY(Vector3 p, MapLand land) => Mathf.Max(grid.GroundAt(p), land.WaterLevel) + Height;

    // The camera behind the wizard and over it, looking east and a little down.
    void Look(Vector3 p, float dir = 1f) => Cut(p + new Vector3(-8f * dir, 4f, 0f), p + new Vector3(40f * dir, -8f, 0f));
}

// What the flyover's slow frames are made of (unity/Temp/flyprofile there): every timed marker of every thread read each
// frame (PerfMarkers, wide), each one's mean over the frames over SlowMs beside its mean over all, and the worst frames'
// own breakdowns. The markers overlap (a parent and its children all show).
sealed class FlyProfile : System.IDisposable
{
    const float SlowMs = 25f;
    readonly PerfMarkers markers = new(wide: true, keep: false);
    readonly double[] slow, all;
    int slowFrames, frames;
    long garbage, slowGarbage;
    readonly List<(float ms, int x, string line)> worst = new(), typical = new();

    // The frame's markers of a millisecond or more, and its garbage.
    string Breakdown()
    {
        var line = new StringBuilder();
        for (int i = 0; i < all.Length; i++)
            if (markers.Last(i) >= 1.0) line.Append(' ').Append(markers.Names[i]).Append('=').Append(markers.Last(i).ToString("0.0", CultureInfo.InvariantCulture));
        return line.Append(" gc_kb=").Append(markers.AllocatedLastFrame / 1024).ToString();
    }

    public static bool Wanted() => System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "flyprofile"));

    public FlyProfile()
    {
        slow = new double[markers.Names.Count];
        all = new double[markers.Names.Count];
    }

    // The frame just ended: its time (ms) and the wizard's cell x.
    public void Frame(float ms, int x, bool counted)
    {
        markers.Sample();
        if (!counted) return;
        frames++;
        bool isSlow = ms >= SlowMs;
        if (isSlow) slowFrames++;
        garbage += markers.AllocatedLastFrame;
        if (isSlow) slowGarbage += markers.AllocatedLastFrame;
        for (int i = 0; i < all.Length; i++)
        {
            double v = markers.Last(i);
            all[i] += v;
            if (isSlow) slow[i] += v;
        }
        if (isSlow && ms < 45f && slowFrames % 40 == 0 && typical.Count < 15) typical.Add((ms, x, Breakdown()));
        if (worst.Count < 12 || ms > worst[^1].ms)
        {
            worst.Add((ms, x, Breakdown()));
            worst.Sort((a, b) => b.ms.CompareTo(a.ms));
            if (worst.Count > 12) worst.RemoveAt(12);
        }
    }

    public string Report()
    {
        var inv = CultureInfo.InvariantCulture;
        var line = new StringBuilder($" slow_at={SlowMs} slow_frames={slowFrames} frames={frames} gc_kb_slow={slowGarbage / 1024 / Mathf.Max(1, slowFrames)} gc_kb_all={garbage / 1024 / Mathf.Max(1, frames)}\nslow/all means:");
        var order = new List<int>();
        for (int i = 0; i < all.Length; i++)
            if (slow[i] / Mathf.Max(1, slowFrames) >= 0.1 || all[i] / Mathf.Max(1, frames) >= 0.1) order.Add(i);
        order.Sort((a, b) => slow[b].CompareTo(slow[a]));
        for (int k = 0; k < order.Count && k < 120; k++)
        {
            int i = order[k];
            line.Append(' ').Append(markers.Names[i]).Append('=').Append((slow[i] / Mathf.Max(1, slowFrames)).ToString("0.00", inv))
                .Append('/').Append((all[i] / Mathf.Max(1, frames)).ToString("0.00", inv));
        }
        foreach (var (ms, x, l) in typical) line.Append("\nTYPICAL ").Append(ms.ToString("0", inv)).Append(" ms at x ").Append(x).Append(':').Append(l);
        foreach (var (ms, x, l) in worst) line.Append("\nWORST ").Append(ms.ToString("0", inv)).Append(" ms at x ").Append(x).Append(':').Append(l);
        return line.ToString();
    }

    public void Dispose() => markers.Dispose();
}

// The flyover at 80 m/s (the fly mode's 40 with Shift held).
public class FlyoverFastProbe : FlyoverProbe
{
    protected override float Speed => 80f;
}

// The flyover there and back at 40 m/s: east coast to coast, then west again to the start, so the terrains and their
// data are let go behind and made again on the way back (turning back doesn't thrash or leak).
public class FlyoverBackProbe : FlyoverProbe
{
    protected override bool Back => true;
}
