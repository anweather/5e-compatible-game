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
// The wildfire ends with two shots of one view, with and without FlameBank's
// flames, so a script can count the pixels Prototype/Flame really drew.
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
    static readonly string[] Known = { "meadow", Wildfire, "storm", "snow", Fog, "village", Bot };

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
        scenario = Arg("-perf-scenario");
        seconds = scenario == Wildfire ? BurnSeconds : DefaultSeconds;
        if (float.TryParse(Arg("-perf-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out float asked) && asked > 0f) seconds = asked;
        if (!Known.Contains(scenario) || (scenario == Bot && FindAnyObjectByType<PlaytestBot>() == null))
        {
            Debug.LogError($"PERF RESULT refused: \"{scenario}\" isn't one of {string.Join("|", Known)}, or bot without -playtest");
            Application.Quit();
            yield break;
        }
        yield return new WaitForSecondsRealtime(WarmUpSeconds);
        grid = FindAnyObjectByType<ChemGrid>();
        wizard = WizardRig.FindLocal();
        if (scenario != Bot)
        {
            var stage = new GameObject("Perf stage").AddComponent<PerfStage>();
            stage.Scenario = scenario;
            while (!stage.Ready) yield return null;
        }
        // A wildfire burns from the bolts, and fog's cost is in how it comes.
        if (scenario != Wildfire && scenario != Fog) yield return new WaitForSecondsRealtime(SettleSeconds);

        markers = new PerfMarkers();
        tickAtStart = grid.World.Tick;
        startedAt = countAt = lastFrame = Time.realtimeSinceStartupAsDouble;
        measuring = true;
        if (scenario == Bot) yield break;

        // Update ends the run, so the frame that crosses the end is counted, however long it took.
        while (measuring) yield return null;
        yield return Shot(scenario);
        var flames = scenario == Wildfire ? grid.GetComponentInChildren<FlameBank>() : null;
        if (flames != null)
        {
            // The same view without Prototype/Flame: what differs between the two is what it drew.
            Debug.Log($"PERF FLAMES cells={flames.CellsBurning}");
            flames.enabled = false;
            yield return null;
            yield return null;
            yield return Shot(Wildfire + "-noflames");
        }
        Application.Quit();
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

    void Update()
    {
        if (!measuring) return;
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
        if (ms > SpikeMs) NoteSpike(ms, now);
        if (now >= countAt)
        {
            CountCells();
            countAt = now + CountEverySeconds;
        }
        if (scenario != Bot && now - startedAt >= seconds) Finish();
    }

    void NoteSpike(float ms, double now)
    {
        var inv = CultureInfo.InvariantCulture;
        var at = wizard != null ? wizard.transform.position : Vector3.zero;
        var chunk = grid.ChunkAt(at);
        string shot = quietFrames > 0 ? " bot_shot=1" : "";
        spikes.Add((ms, $"PERF SPIKE scenario={scenario} frame={frameNumber} t={(now - startedAt).ToString("0.00", inv)} ms={ms.ToString("0.0", inv)}" +
            $" hour={(grid.World.Minutes / 60.0 % 24.0).ToString("0.0", inv)} chunk={chunk.x},{chunk.y} active_chunks={grid.World.ActiveChunks.Count}{shot}{markers.Breakdown()}"));
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
        foreach (var (_, line) in spikes.OrderByDescending(s => s.ms).Take(MostSpikes)) Debug.Log(line);
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
