using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// The probe's tiersweep scenario, on the nine (unity/Temp/map = nine): the three tier dials (docs/design/world-12x8-scope.md)
// swept over a grid, for frame time and memory per setting. Active (streaming reach / keep, lockstep) goes as a rule
// switch; drawn and known are this peer's view (FarRing.SetView). For each setting the eye flies the same square, a chunk
// a side round the spawn, 45 m over the wizard, who keeps under it so the rings follow, for --seconds (default 10) after
// the known ring has settled. Reports per setting: frame mean and p95, mem_allocated_mb and mem_reserved_mb, chunks per tier
// (active, terrains drawing, known meshes), the known ring's settle time and its mesh worker's ms; a table goes to the
// log and to Temp/Probe/tiersweep.md. Ends on today's dials. Checks: the far ring up, every setting measured, the dials
// took (known meshes follow known), the rules' dials back at the end.
public class TierSweepProbe : ProbeScenario
{
    static readonly int[] Actives = { 1, 2, 3 }, Drawns = { 2, 4, 7 }, Knowns = { 4, 7, 12 };
    const float Up = 45f, MostSettle = 15f;
    const string Clear = ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }";

    struct Row
    {
        public int Active, Drawn, Known, ActiveChunks, Terrains, KnownMeshes, Frames;
        public float Mean, P95, Allocated, Reserved, SettleMs, WorkerMs;
    }

    readonly List<Row> rows = new();

    protected override IEnumerator Run()
    {
        yield return Switch(At(11f) + Clear);
        for (float t = 0f; t < 20f && !RingUp(FarRing.Instance); t += Time.deltaTime) yield return null;
        var far = FarRing.Instance;
        Check("far_ring_up", RingUp(far), far == null ? $"no far ring: name the nine in unity/{ChemGrid.EditorMapFile}" : $"{far.KnownDrawn} known chunks drawn");
        if (!RingUp(far)) yield break;
        var rules = World.Rules.Sim.Streaming;
        var today = (rules.Reach, rules.Keep, rules.Sight, rules.Know);
        var home = wizard.transform.position;
        float window = Mathf.Clamp(Seconds, 3f, 60f), side = MapLand.Current.Layout.ChunkSize;

        // One lap at today's dials first, unrecorded, so the first setting doesn't pay for the warm-up.
        yield return Lap(home, side, window * 0.5f, null);
        foreach (int a in Actives)
        {
            // The rules want sight past keep; the view's drawn dial stands in for sight here anyway.
            yield return Switch(At(11f) + Clear + $", 'streaming': {{ 'reach': {a}, 'keep': {a + 1}, 'sight': {Mathf.Max(today.Sight, a + 2)} }}");
            foreach (int k in Knowns)
                foreach (int d in Drawns)
                {
                    if (d > k) continue;
                    var row = new Row { Active = a, Drawn = d, Known = k };
                    yield return Measure(far, home, side, window, row);
                }
        }

        FarRing.SetView(null, null);
        yield return Switch(At(11f) + Clear);
        Teleport(home);
        var back = World.Rules.Sim.Streaming;
        Check("rules_dials_back", far.Dials == (today.Sight, today.Know) && (back.Reach, back.Keep, back.Sight, back.Know) == today,
            $"drawn {far.Dials.drawn}, known {far.Dials.known}, reach {back.Reach}, keep {back.Keep}");
        Report();
    }

    static bool RingUp(FarRing far) => far != null && far.IsReady && far.KnownDrawn > 0;

    IEnumerator Measure(FarRing far, Vector3 home, float side, float window, Row row)
    {
        Teleport(home);
        Cut(home + Vector3.up * Up, home + Vector3.forward * side);
        float started = Time.realtimeSinceStartup;
        FarRing.SetView(row.Drawn, row.Known);
        yield return null;
        for (float t = 0f; t < MostSettle && !far.Settled; t += Time.unscaledDeltaTime) yield return null;
        row.SettleMs = (Time.realtimeSinceStartup - started) * 1000f;
        row.WorkerMs = (float)FarRing.LastBuild.workerMs;
        var ms = new List<float>();
        yield return Lap(home, side, window, ms);
        var stats = FrameStats.Of(ms);
        row.Frames = stats.Count;
        row.Mean = stats.MeanMs;
        row.P95 = stats.P95Ms;
        row.Allocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        row.Reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);
        row.ActiveChunks = World.ActiveChunks.Count;
        var terrains = FindAnyObjectByType<MapTerrains>();
        row.Terrains = terrains != null ? terrains.Drawing : -1;
        row.KnownMeshes = far.KnownDrawn;
        rows.Add(row);
        string key = $"a{row.Active}_d{row.Drawn}_k{row.Known}";
        Metric(key + "_mean_ms", row.Mean);
        Metric(key + "_p95_ms", row.P95);
        Metric(key + "_mem_allocated_mb", row.Allocated);
        Metric(key + "_mem_reserved_mb", row.Reserved);
        Metric(key + "_active_chunks", row.ActiveChunks);
        Metric(key + "_terrains_drawn", row.Terrains);
        Metric(key + "_known_meshes", row.KnownMeshes);
        Metric(key + "_known_settle_ms", row.SettleMs);
        Metric(key + "_known_worker_ms", row.WorkerMs);
    }

    // The eye round a square a chunk a side from home, the wizard under it, frame times into ms (when given).
    IEnumerator Lap(Vector3 home, float side, float seconds, List<float> ms)
    {
        var corners = new[] { Vector3.zero, Vector3.right * side, new Vector3(side, 0f, side), Vector3.forward * side, Vector3.zero };
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            ms?.Add(Time.unscaledDeltaTime * 1000f);
            float along = t / seconds * 4f;
            int leg = Mathf.Min(3, (int)along);
            var at = home + Vector3.Lerp(corners[leg], corners[leg + 1], along - leg);
            var ground = new Vector3(at.x, grid.GroundAt(at) + 0.05f, at.z);
            var ahead = home + corners[leg + 1];
            Teleport(ground);
            Cut(ground + Vector3.up * Up - (ahead - at).normalized * 20f, new Vector3(ahead.x, ground.y, ahead.z));
            yield return null;
        }
    }

    void Report()
    {
        int expected = 0;
        foreach (int k in Knowns)
            foreach (int d in Drawns)
                if (d <= k) expected++;
        expected *= Actives.Length;
        Check("every_setting_measured", rows.Count == expected && rows.TrueForAll(r => r.Frames > 0), $"{rows.Count}/{expected} settings");
        // The dials took: more known chunks drawn as meshes at known 12 than at known 4 (same active and drawn).
        var small = rows.Find(r => r.Active == 2 && r.Drawn == 2 && r.Known == 4);
        var large = rows.Find(r => r.Active == 2 && r.Drawn == 2 && r.Known == 12);
        Check("dials_took", large.KnownMeshes > small.KnownMeshes, $"known meshes {small.KnownMeshes} at known 4, {large.KnownMeshes} at 12");

        var inv = CultureInfo.InvariantCulture;
        var table = new StringBuilder();
        table.AppendLine("| Active | Drawn | Known | Mean ms | p95 ms | Allocated MB | Reserved MB | Active chunks | Terrains drawn | Known meshes | Known settle ms | Mesh worker ms |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            table.AppendLine(string.Format(inv, "| {0} | {1} | {2} | {3:0.0} | {4:0.0} | {5:0} | {6:0} | {7} | {8} | {9} | {10:0} | {11:0.0} |",
                r.Active, r.Drawn, r.Known, r.Mean, r.P95, r.Allocated, r.Reserved, r.ActiveChunks, r.Terrains, r.KnownMeshes, r.SettleMs, r.WorkerMs));
        Debug.Log("TIER SWEEP\n" + table);
        try
        {
            Directory.CreateDirectory("Temp/Probe");
            File.WriteAllText("Temp/Probe/tiersweep.md", table.ToString());
        }
        catch (IOException e)
        {
            Debug.LogWarning($"TIER SWEEP: the table wasn't written: {e.Message}");
        }
    }
}
