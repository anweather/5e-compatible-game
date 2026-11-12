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
            if (land.Land[row * w + x] > land.Map.SeaLevel && !land.Sea.IsSea(x, row))
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
        float settled = 0f;
        for (; settled < SettleMost && (terrains.Waiting > 0 || (far != null && !far.Settled)); settled += Time.unscaledDeltaTime)
        {
            Look(wizardT.position);
            yield return null;
        }
        yield return Wait(1f);
        Metric("start_settle_s", settled);
        System.GC.Collect();
        Metric("mem_start_allocated_mb", UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f));
        Metric("mem_start_reserved_mb", UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f));
        Metric("mem_start_mono_mb", UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024f * 1024f));

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
        int closeHoleFrames = 0;
        float flown = 0f, total = end.x - start.x;
        while (flown < total)
        {
            float dt = Time.unscaledDeltaTime;
            ms.Add(dt * 1000f);
            var p = wizardT.position;
            at.Add(grid.CellAt(p).x);
            frames++;

            // Along the line east: the drawn ground ahead, the holes in it.
            float ahead = -1f;
            bool hole = false, nearHole = false, overLand = false;
            for (float d = 0f; d <= knownReach; d += Step)
            {
                var q = new Vector3(p.x + d, 0f, p.z);
                if (q.x > end.x + chunkM) break;
                var xz = new Vector2(q.x, q.z);
                if (!land.IsLand(xz) || land.IsSea(xz)) continue;
                overLand = true;
                var t = terrains.TerrainAt(q);
                bool terrain = t != null && t.terrainData != null && t.isActiveAndEnabled && t.drawHeightmap;
                var cell = land.CellOf(xz);
                var mc = (cell.x / ChunkGen.Size, cell.y / ChunkGen.Size);
                bool known = far != null && !far.LandIsTerrain(mc.Item1, mc.Item2);
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

            float move = Speed * dt;
            flown += move;
            var next = new Vector3(p.x + move, 0f, p.z);
            next.y = Mathf.Lerp(p.y, FlyY(next, land), Mathf.Clamp01(dt * 2f));
            wizardT.position = next;
            Look(next);
            yield return null;
        }

        tools.Fly(false);
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
        Metric("mem_end_allocated_mb", UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f));
        Metric("mem_end_reserved_mb", UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f));
        Metric("mem_end_mono_mb", UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024f * 1024f));
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
        Check("flew_coast_to_coast", flown >= total, $"{flown:0} of {total:0} m in {frames} frames");
        Check("worst_frames", true, worst.ToString());
        Check("over_33ms_by_x", true, bandText.Length > 0 ? bandText.ToString() : "none");
        Check("no_hole_within_sight", nearHoleChunks.Count == 0, $"{nearHoleChunks.Count} land chunks within the drawn dial ({terrainReach:0} m) seen with nothing drawn: {holeText}");
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
    void Look(Vector3 p) => Cut(p + new Vector3(-8f, 4f, 0f), p + new Vector3(40f, -8f, 0f));
}

// The flyover at 80 m/s (the fly mode's 40 with Shift held).
public class FlyoverFastProbe : FlyoverProbe
{
    protected override float Speed => 80f;
}
