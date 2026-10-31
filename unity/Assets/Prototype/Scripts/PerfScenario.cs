using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using Debug = UnityEngine.Debug;

// One timed scenario in a player build, for a performance pass
// (docs/perf/, the MacOSProfiling build in PrototypeBuild). Start the game with
//   -fps-probe -perf-scenario <meadow|wildfire|storm|snow|fog|village|bot> [-perf-seconds N]
// and FpsProbe hands over to this instead of its showcase: PerfStage sets the
// scene, it settles, every frame is measured for N seconds (30 by default, 20
// for the wildfire), one "PERF RESULT" line of key=value pairs goes to the
// player log, and the game quits. With v-sync and the frame cap off, frame times
// are the real work, read from Update here at the head of the frame so they
// line up with the markers of the frame just ended (PerfMarkers). The cell
// counts every 2 s are taken out of the frame they fall in.
//
// In a development build each frame's Profiler markers are read too: a
// "PERF MARKERS" line with each one's mean and peak, and a "PERF SPIKE" line for
// every frame over 50 ms (written after the run: logging is itself slow).
// -perf-list-markers also writes every marker there is to perf-markers.txt.
// The wildfire ends with shots with and without FlameBank's flames (FlameShots),
// so a script can count the pixels Prototype/Flame really drew.
//
// Step 2 of a pass (attribution) adds: PerfCounts, read each frame into the spike
// lines and a "PERF COUNTS" line of name=sum/peak/frames; "PERF EARLY" lines for
// frames over 100 ms before measuring starts, from the first frame here;
// -perf-spike-ms N for a lower bar than 50; -perf-wide (PerfMarkers); -perf-hour H;
// -perf-exact and -perf-time-budget, which force ChemGrid.ExactFrames on or off
// (the bot turns it on; compare a budget in work units with one in milliseconds);
// -perf-flames-off, which draws a flamewall without its flames; and -perf-pso-diff
// <file>, which lists what a -pso-trace file has that the shipped collection hasn't;
// and -perf-water-check, which redraws the water tiles at the end and counts any that differ.
//
// The bot scenario films the playtest bot (add -playtest -playtest-seed N
// -playtest-seconds N): it has no stage, -perf-seconds does nothing, and the
// result is written as the bot quits. Game time stays locked to 1/30 s a frame
// but the bot's frame cap comes off here, so a frame takes only the time it
// needs. The frames of the bot's own screenshots are left out of the times and
// their worst reported alone.
[DefaultExecutionOrder(-32000)]
public class PerfScenario : MonoBehaviour
{
    const string Bot = "bot", Wildfire = "wildfire", Fog = "fog";
    const float WarmUpSeconds = 3f, SettleSeconds = 5f, DefaultSeconds = 30f, BurnSeconds = 20f;
    const float CountEverySeconds = 2f, SpikeMs = 50f;
    const int MostSpikes = 200, ShotQuietFrames = 3, WorstFrames = 5;
    const double BytesPerMb = 1024.0 * 1024.0;
    const float EarlyMs = 100f;
    const int MostEarly = 40;
    static readonly string[] Known = { "meadow", Wildfire, "storm", "snow", Fog, "village", Bot, "forest", "aim", "flamewall", "pso" };

    readonly List<float> frameMs = new();
    readonly List<float> mainMs = new(), renderMs = new(), gpuMs = new();
    readonly List<(float ms, string line)> spikes = new();
    readonly List<float> shotMs = new();
    string scenario;
    float seconds;
    ChemGrid grid;
    GameObject wizard;
    PerfMarkers markers;
    bool measuring;
    double lastFrame, overheadMs, startedAt, countAt;
    int tickAtStart, frameNumber, quietFrames, errors, warnings;
    int burningPeak, burningNow, fogPeak, fogNow;
    readonly Dictionary<string, (long sum, long peak, int frames)> counts = new();
    PerfMarkers early;
    double earlyFrom;
    int earlyFrame, earlyLogged;
    float spikeMs = SpikeMs;

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.logMessageReceived += OnLog;
        PerfCounts.On = true;
        early = new PerfMarkers();
        earlyFrom = Time.realtimeSinceStartupAsDouble;
        scenario = Arg("-perf-scenario");
        if (float.TryParse(Arg("-perf-spike-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out float bar) && bar > 0f) spikeMs = bar;
        seconds = scenario == Wildfire ? BurnSeconds : DefaultSeconds;
        if (float.TryParse(Arg("-perf-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out float asked) && asked > 0f) seconds = asked;
        if (Arg("-perf-pso-diff") != null)
        {
            PerfPsoCheck.Diff(Arg("-perf-pso-diff"));
            Application.Quit();
            yield break;
        }
        if (!Known.Contains(scenario) || (scenario == Bot && FindAnyObjectByType<PlaytestBot>() == null))
        {
            Debug.LogError($"PERF RESULT refused: \"{scenario}\" isn't one of {string.Join("|", Known)}, or bot without -playtest");
            Application.Quit();
            yield break;
        }
        yield return new WaitForSecondsRealtime(WarmUpSeconds);
        if (Has("-perf-exact")) ChemGrid.ExactFrames = true;
        if (Has("-perf-time-budget")) ChemGrid.ExactFrames = false;
        grid = FindAnyObjectByType<ChemGrid>();
        wizard = WizardRig.FindLocal();
        PerfStage stage = null;
        if (scenario != Bot)
        {
            stage = new GameObject("Perf stage").AddComponent<PerfStage>();
            stage.Scenario = scenario;
            if (float.TryParse(Arg("-perf-hour"), NumberStyles.Float, CultureInfo.InvariantCulture, out float hour)) stage.Hour = hour;
            while (!stage.Ready) yield return null;
        }
        if (Has("-perf-flames-off")) grid.Flames.enabled = false;
        // A wildfire burns from the bolts, and fog's cost is in how it comes. The others settle, even a walk.
        if (scenario != Wildfire && scenario != Fog) yield return new WaitForSecondsRealtime(SettleSeconds);
        if (stage != null) stage.Go = true;

        early.Dispose();
        early = null;
        markers = new PerfMarkers(Has("-perf-wide"));
        tickAtStart = grid.World.Tick;
        startedAt = countAt = lastFrame = Time.realtimeSinceStartupAsDouble;
        measuring = true;
        if (scenario == Bot) yield break;

        // Update ends the run, so the frame that crosses the end is counted, however long it took.
        while (measuring) yield return null;
        yield return Shot(scenario);
        if (scenario == Wildfire) yield return FlameShots();
        Application.Quit();
    }

    // Two shots with Prototype/Flame and one without, of the fire from the air and from close by, so
    // a script can count what it really drew: the first two differ by a flicker, the last by the flames.
    IEnumerator FlameShots()
    {
        var flames = grid.GetComponentInChildren<FlameBank>();
        if (flames == null)
        {
            Debug.Log("PERF FLAMES none");
            yield break;
        }
        Debug.Log($"PERF FLAMES cells={flames.CellsBurning} shader_supported={flames.ShaderSupported}");
        yield return Shot("wildfire-wide-a");
        yield return Shot("wildfire-wide-b");
        flames.enabled = false;
        yield return Shot("wildfire-wide-off");
        flames.enabled = true;
        LookAtFire();
        yield return Shot("wildfire-close-a");
        yield return Shot("wildfire-close-b");
        flames.enabled = false;
        yield return Shot("wildfire-close-off");
    }

    // The camera 5 m from the middle burning cell (PerfStage has taken it from Cinemachine).
    void LookAtFire()
    {
        var world = grid.World;
        var burning = Enumerable.Range(0, world.Cells.Length).Where(i => world.Cells[i].Fire > 0).ToList();
        if (burning.Count == 0) return;
        int cell = burning[burning.Count / 2];
        var at = grid.CellCenter(cell % world.W, cell / world.W);
        at.y = grid.GroundAt(at) + 0.8f;
        var eye = at + new Vector3(0f, 1.5f, -5f);
        Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
    }

    // A screenshot is written at the end of the frame it was asked for.
    static IEnumerator Shot(string name)
    {
        var file = Path.Combine(Application.persistentDataPath, $"perf-{name}.png");
        ScreenCapture.CaptureScreenshot(file);
        Debug.Log($"PERF SHOT {file}");
        yield return null;
        yield return null;
    }

    static bool Has(string name) => Environment.GetCommandLineArgs().Contains(name);

    // Before measuring starts: the slow frames of start-up, with what the markers say of them.
    void NoteEarly()
    {
        if (early == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        float ms = (float)((now - earlyFrom) * 1000.0);
        earlyFrom = now;
        earlyFrame++;
        early.Sample();
        PerfCounts.NextFrame();
        if (ms > EarlyMs && earlyLogged++ < MostEarly)
            Debug.Log($"PERF EARLY frame={earlyFrame} since_start={now.ToString("0.00", CultureInfo.InvariantCulture)} ms={ms.ToString("0.0", CultureInfo.InvariantCulture)}{early.Breakdown()}");
    }

    void Update()
    {
        if (!measuring)
        {
            NoteEarly();
            return;
        }
        double now = Time.realtimeSinceStartupAsDouble;
        float ms = (float)((now - lastFrame) * 1000.0 - overheadMs);
        lastFrame = now;
        overheadMs = 0;
        frameNumber++;
        markers.Sample();
        // A bot's shot is a frame or two of its own, then a slow render or so behind it.
        quietFrames = markers.BotShotLastFrame ? ShotQuietFrames : Math.Max(0, quietFrames - 1);
        if (quietFrames > 0) shotMs.Add(ms);
        else frameMs.Add(ms);
        FpsProbe.ReadTiming(mainMs, renderMs, gpuMs);
        if (ms > spikeMs) NoteSpike(ms, now);
        ReadCounts();
        if (now >= countAt)
        {
            CountCells();
            countAt = now + CountEverySeconds;
        }
        if (scenario != Bot && now - startedAt >= seconds) Finish();
    }

    // The frame's PerfCounts into the run's, after the spike line has had them.
    void ReadCounts()
    {
        foreach (var pair in PerfCounts.Frame)
        {
            counts.TryGetValue(pair.Key, out var had);
            counts[pair.Key] = (had.sum + pair.Value, Math.Max(had.peak, pair.Value), had.frames + 1);
        }
        PerfCounts.NextFrame();
    }

    void NoteSpike(float ms, double now)
    {
        var inv = CultureInfo.InvariantCulture;
        var at = wizard != null ? wizard.transform.position : Vector3.zero;
        var chunk = grid.ChunkAt(at);
        string shot = quietFrames > 0 ? " bot_shot=1" : "";
        var counted = new StringBuilder();
        foreach (var pair in PerfCounts.Frame.OrderBy(p => p.Key, StringComparer.Ordinal)) counted.Append(" c.").Append(pair.Key).Append('=').Append(pair.Value.ToString(inv));
        spikes.Add((ms, $"PERF SPIKE scenario={scenario} frame={frameNumber} t={(now - startedAt).ToString("0.00", inv)} ms={ms.ToString("0.0", inv)}" +
            $" hour={(grid.World.Minutes / 60.0 % 24.0).ToString("0.0", inv)} chunk={chunk.x},{chunk.y} weather={grid.World.Weather.Kind} active_chunks={grid.World.ActiveChunks.Count}{shot}{markers.Breakdown()}{counted}"));
    }

    // How many cells burn and hold fog, over the whole world; its time doesn't count against the frame.
    void CountCells()
    {
        long from = Stopwatch.GetTimestamp();
        burningNow = fogNow = 0;
        foreach (var c in grid.World.Cells)
        {
            if (c.Fire > 0) burningNow++;
            if (c.Gas == GasIds.Fog) fogNow++;
        }
        burningPeak = Math.Max(burningPeak, burningNow);
        fogPeak = Math.Max(fogPeak, fogNow);
        overheadMs = (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (message.Contains("Ring Buffer")) PerfCounts.Add("log.ring_buffer_full");
        if (type == LogType.Warning) warnings++;
        else if (type != LogType.Log) errors++;
    }

    // The bot quits when its run is done.
    void OnApplicationQuit() => Finish();

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        markers?.Dispose();
        markers = null;
        early?.Dispose();
        early = null;
        PerfCounts.On = false;
    }

    void Finish()
    {
        if (!measuring) return;
        measuring = false;
        double elapsed = Time.realtimeSinceStartupAsDouble - startedAt;
        CountCells();
        Debug.Log(ResultLine(elapsed));
        string missing = string.Join(",", markers.Missing());
        Debug.Log($"PERF MARKERS scenario={scenario}{markers.Summary()}" + (missing.Length > 0 ? $" missing={missing}" : ""));
        var inv = CultureInfo.InvariantCulture;
        Debug.Log($"PERF COUNTS scenario={scenario} frames={frameNumber}" + string.Concat(counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $" {c.Key}={c.Value.sum.ToString(inv)}/{c.Value.peak.ToString(inv)}/{c.Value.frames.ToString(inv)}")));
        foreach (var (_, line) in spikes.OrderByDescending(s => s.ms).Take(MostSpikes)) Debug.Log(line);
        if (grid != null && Environment.GetCommandLineArgs().Contains("-perf-water-check"))
        {
            var (tiles, different) = grid.CheckWaterTiles();
            Debug.Log($"PERF WATER CHECK tiles={tiles} different={different}");
        }
        if (Environment.GetCommandLineArgs().Contains("-perf-list-markers")) ListMarkers();
    }

    string ResultLine(double elapsed)
    {
        var inv = CultureInfo.InvariantCulture;
        var stats = FrameStats.Of(frameMs);
        var line = new StringBuilder($"PERF RESULT scenario={scenario} build={BuildInfo.Label.Replace(' ', '_')} res={Screen.width}x{Screen.height} frames={stats.Count} seconds={elapsed.ToString("0.0", inv)}");
        void Add(string key, double value, string format = "0.0") => line.Append($" {key}={value.ToString(format, inv)}");
        void AddSplit(string key, List<float> ms)
        {
            if (ms.Count == 0)
            {
                line.Append($" {key}_mean=na {key}_p95=na");
                return;
            }
            var split = FrameStats.Of(ms);
            Add(key + "_mean", split.MeanMs);
            Add(key + "_p95", split.P95Ms);
        }
        Add("mean", stats.MeanMs, "0.00");
        Add("p50", stats.P50Ms, "0.00");
        Add("p95", stats.P95Ms, "0.00");
        Add("p99", stats.P99Ms, "0.00");
        line.Append(" worst5=").Append(string.Join(",", FrameStats.Worst(frameMs, WorstFrames).Select(ms => ms.ToString("0.0", inv))));
        AddSplit("cpu_main", mainMs);
        AddSplit("cpu_render", renderMs);
        AddSplit("gpu", gpuMs);
        Add("mem_allocated_mb", UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / BytesPerMb, "0");
        Add("mem_reserved_mb", UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / BytesPerMb, "0");
        Add("gc_heap_mb", UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / BytesPerMb, "0");
        Add("gc_used_mb", UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / BytesPerMb, "0");
        Add("gfx_driver_mb", UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / BytesPerMb, "0");
        Add("gfx_used_mb", markers.GfxUsedBytes / BytesPerMb, "0");
        Add("system_used_mb", markers.SystemUsedBytes / BytesPerMb, "0");
        line.Append($" active_chunks={grid.World.ActiveChunks.Count} ticks={grid.World.Tick - tickAtStart}");
        line.Append($" burning_peak={burningPeak} burning_end={burningNow} fog_cells_peak={fogPeak} fog_cells_end={fogNow} fog_drawn={grid.Fog.SquaresShown}");
        line.Append($" hour={(grid.World.Minutes / 60.0 % 24.0).ToString("0.0", inv)} weather={grid.World.Weather.Kind}");
        line.Append($" spikes_over_50ms={spikes.Count} errors={errors} warnings={warnings}");
        if (shotMs.Count > 0) line.Append($" bot_shot_frames={shotMs.Count} bot_shot_worst={shotMs.Max().ToString("0.0", inv)}");
        return line.ToString();
    }

    // Every marker and counter this player has, for choosing which to read.
    static void ListMarkers()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var lines = handles.Select(ProfilerRecorderHandle.GetDescription).Select(d => $"{d.Category.Name}\t{d.Name}\t{d.UnitType}");
        var file = Path.Combine(Application.persistentDataPath, "perf-markers.txt");
        File.WriteAllLines(file, lines.OrderBy(l => l, StringComparer.Ordinal));
        Debug.Log($"PERF MARKER LIST {file}");
    }
}
