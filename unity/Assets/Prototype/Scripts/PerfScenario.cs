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
using UnityEngine.Experimental.Rendering;
using Debug = UnityEngine.Debug;

// One timed scenario in a player build, for a performance pass
// (docs/perf/, the MacOSProfiling build in PrototypeBuild). Start the game with
//   -fps-probe -perf-scenario <meadow|wildfire|forestload|showerstorm|storm|rain|snow|fog|village|dragon|dragonclear|fireball|bot> [-perf-seconds N]
// and FpsProbe hands over to this instead of its showcase: PerfStage sets the
// scene, it settles, every frame is measured for N seconds (30 by default, 20
// for the wildfire, 6 for the fireball: its cast and the 5 s after), one "PERF RESULT" line of key=value pairs goes to the
// player log, and the game quits. With v-sync and the frame cap off, frame times
// are the real work, read from Update here at the head of the frame so they
// line up with the markers of the frame just ended (PerfMarkers). The cell
// counts every 2 s are taken out of the frame they fall in.
//
// In a development build each frame's Profiler markers are read too: a
// "PERF MARKERS" line with each one's mean and peak, a "PERF RENDER" line of what
// was drawn (draw calls, batches, SetPass calls, triangles, shadow casters), and a "PERF SPIKE" line for
// every frame over 50 ms (written after the run: logging is itself slow), and "PERF SLOW" lines: the markers of the
// frames at the run's p95 and over beside every frame's, and the chunks the wizard was in for them.
// -perf-list-markers also writes every marker there is to perf-markers.txt.
// Every run logs a "PERF LOAD" line as it starts (the map, the seconds since start-up, its chunks); -perf-memory adds
// "PERF MEMORY" lines (PerfMemory) at the start, as measuring begins and at the end. walkout walks the wizard east from
// the nine's spawn out of the nine (onto the grass on the scale island); stand puts the wizard in one chunk
// (-perf-at CX,CY, -perf-camera-off: PerfStage.Stand.cs); -perf-switches makes a sim-only rule switch
// and a look-changing one during a fixed-camera run (PerfStage.Scale.cs, docs/perf/2026-11-10-scale.md).
// The wildfire ends with shots with and without FlameBank's flames (FlameShots),
// so a script can count the pixels Prototype/Flame really drew.
//
// Step 2 of a pass (attribution) adds: PerfCounts, read each frame into the spike
// lines and a "PERF COUNTS" line of name=sum/peak/frames; "PERF EARLY" lines for
// frames over 100 ms before measuring starts, from the first frame here;
// -perf-spike-ms N for a lower bar than 50; -perf-wide (PerfMarkers); -perf-hour H;
// -perf-step-ms N, a tick's allowance a frame (slices in proportion with ExactFrames); -perf-exact and -perf-time-budget, which force ChemGrid.ExactFrames on or off
// (the bot turns it on; compare a budget in work units with one in milliseconds);
// -perf-flames-off, which draws a flamewall without its flames; and -perf-pso-diff
// <file>, which lists what a -pso-trace file has that the shipped collection hasn't;
// and -perf-water-check, which redraws the water tiles at the end and counts any that differ;
// -perf-same-check draws every water tile both ways and paints every ground strip with its noise
// kept and afresh, counting any that differ bit for bit (#70; doubles that work, so not for timing).
// -perf-offscreen draws the camera into a 1080p texture of real play's colour and depth
// formats: with no desktop session a camera bound for the window draws nothing, so frame
// times mean nothing and no pipeline state is made (with -perf-offscreen-4k, 3840x2160).
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
    PerfStage stage;
    const string Bot = "bot", Wildfire = "wildfire", Fog = "fog", Fireball = "fireball";
    const float WarmUpSeconds = 3f, SettleSeconds = 5f, DefaultSeconds = 30f, BurnSeconds = 20f, FireballSeconds = 6f;
    const float CountEverySeconds = 2f, SpikeMs = 50f, Over30Ms = 30f;
    const int MostSpikes = 200, ShotQuietFrames = 3, WorstFrames = 5;
    const double BytesPerMb = 1024.0 * 1024.0;
    const float EarlyMs = 100f, HostWaitSeconds = 40f, ShotSavedSeconds = 1f;
    const int MostEarly = 40;
    const int OffscreenWide = 1920, OffscreenHigh = 1080;
    static readonly string[] Known = { "meadow", Wildfire, "storm", "rain", "snow", Fog, "village", Bot, "forest", "aim", "flamewall", "pso", "dragon", "dragonclear", "dragonlow", "forestload", "showerstorm", Fireball, "walkout", "stand" };

    static readonly string[] WindowCounts = { "tick", "walk", "net", "cast", "draw", "blockers", "chunk" };

    readonly List<float> frameMs = new();
    readonly List<float> mainMs = new(), renderMs = new(), gpuMs = new();
    readonly List<(float ms, string line)> spikes = new();
    readonly List<float> shotMs = new();
    // Every measured frame in the order PerfMarkers sampled it (NaN for the bot's shots), and the chunk the wizard was in.
    readonly List<float> sampledMs = new();
    readonly List<Vector2Int> sampledChunk = new();
    string scenario;
    float seconds;
    ChemGrid grid;
    GameObject wizard;
    PerfMarkers markers;
    PerfWindows windows;
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
        QuickExit.BeforeExit += Finish;
        PerfCounts.On = true;
        early = new PerfMarkers();
        earlyFrom = Time.realtimeSinceStartupAsDouble;
        scenario = Arg("-perf-scenario");
        // The scene is up and the map made: the load, and what it holds (-perf-memory, slow).
        Debug.Log($"PERF LOAD map={ChemGrid.MapId} since_start_s={Time.realtimeSinceStartup.ToString("0.00", CultureInfo.InvariantCulture)} keep_start_ms={ChemGrid.KeepStartMs.ToString("0", CultureInfo.InvariantCulture)} chunks={ChemGrid.Current?.World.ChunkCoords.Count ?? 0} active_chunks={ChemGrid.Current?.World.ActiveChunks.Count ?? 0}");
        if (Has("-perf-memory")) Debug.Log(PerfMemory.Line("start"));
        ReadTimes();
        DrawOffscreenIfAsked();
        if (Arg("-perf-pso-diff") != null)
        {
            PerfPsoCheck.Diff(Arg("-perf-pso-diff"));
            Application.Quit();
            yield break;
        }
        if (Refused())
        {
            Application.Quit();
            yield break;
        }
        yield return new WaitForSecondsRealtime(WarmUpSeconds);
        if (Has("-perf-exact")) ChemGrid.ExactFrames = true;
        if (Has("-perf-time-budget")) ChemGrid.ExactFrames = false;
        if (float.TryParse(Arg("-perf-step-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out float stepMs) && stepMs > 0f)
        {
            // The slices a frame in a playtest run, in proportion to the game's 6 ms and 256.
            int slices = Mathf.Max(1, Mathf.RoundToInt(stepMs / 6f * 256f));
            ChemGrid.Current?.Grid.SetStepBudget(stepMs, slices);
            Debug.Log($"PERF STEP BUDGET ms={stepMs.ToString("0.0", CultureInfo.InvariantCulture)} slices={slices}");
        }
        if (Has("-perf-same-check")) CellWaterMesh.CheckKeptPoints = ValleyGround.CheckKeptNoise = true;
        yield return WaitToHost();
        grid = ChemGrid.Current?.Grid;
        wizard = WizardRig.FindLocal();
        if (scenario != Bot) yield return SetTheStage();
        if (Has("-perf-flames-off")) grid.Flames.enabled = false;
        // A wildfire burns from the bolts, and fog's cost is in how it comes. The others settle, even a walk.
        if (scenario != Wildfire && scenario != Fog) yield return new WaitForSecondsRealtime(SettleSeconds);
        if (stage != null) stage.Go = true;

        if (Has("-perf-memory")) Debug.Log(PerfMemory.Line("measure"));
        early.Dispose();
        early = null;
        markers = new PerfMarkers(Has("-perf-wide"));
        tickAtStart = grid.World.Tick;
        startedAt = countAt = lastFrame = Time.realtimeSinceStartupAsDouble;
        windows = new PerfWindows(grid, startedAt);
        measuring = true;
        if (scenario == Bot) yield break;

        // Update ends the run, so the frame that crosses the end is counted, however long it took.
        while (measuring) yield return null;
        yield return Shot(scenario);
        if (scenario == Wildfire) yield return FlameShots();
        yield return new WaitForSecondsRealtime(ShotSavedSeconds); // quitting drops a shot still being written
        Application.Quit();
    }

    // With -host-online, until the session is up and its wizard stands in the world.
    IEnumerator WaitToHost()
    {
        if (!Has("-host-online")) yield break;
        double gaveUpAt = Time.realtimeSinceStartupAsDouble + HostWaitSeconds;
        var net = FindAnyObjectByType<NetChem>();
        while ((net == null || !net.Online) && Time.realtimeSinceStartupAsDouble < gaveUpAt)
        {
            net = FindAnyObjectByType<NetChem>();
            yield return null;
        }
        yield return new WaitForSecondsRealtime(WarmUpSeconds);
        Debug.Log($"PERF HOSTED online={net != null && net.Online}");
    }

    // URP takes the camera's formats from its target, so this matches URP's HDR colour and depth.
    static void DrawOffscreenIfAsked()
    {
        if (!Has("-perf-offscreen")) return;
        // -perf-offscreen-4k draws into 3840x2160, as Andrew's own screen is.
        int scale = Has("-perf-offscreen-4k") ? 2 : 1;
        Camera.main.targetTexture = new RenderTexture(OffscreenWide * scale, OffscreenHigh * scale, GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.D32_SFloat_S8_UInt);
        Debug.Log($"PERF OFFSCREEN {OffscreenWide * scale}x{OffscreenHigh * scale}");
    }

    void ReadTimes()
    {
        if (float.TryParse(Arg("-perf-spike-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out float bar) && bar > 0f) spikeMs = bar;
        seconds = scenario == Wildfire ? BurnSeconds : scenario == Fireball ? FireballSeconds : DefaultSeconds;
        if (float.TryParse(Arg("-perf-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out float asked) && asked > 0f) seconds = asked;
    }

    bool Refused()
    {
        if (Known.Contains(scenario) && (scenario != Bot || FindAnyObjectByType<PlaytestBot>() != null)) return false;
        Debug.LogError($"PERF RESULT refused: \"{scenario}\" isn't one of {string.Join("|", Known)}, or bot without -playtest");
        return true;
    }

    IEnumerator SetTheStage()
    {
        stage = new GameObject("Perf stage").AddComponent<PerfStage>();
        stage.Scenario = scenario;
        if (float.TryParse(Arg("-perf-hour"), NumberStyles.Float, CultureInfo.InvariantCulture, out float hour)) stage.Hour = hour;
        while (!stage.Ready) yield return null;
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
        var burning = Enumerable.Range(0, world.W * world.H).Where(i => world.CellAt(i).Fire > 0).ToList();
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
        var file = Path.Combine(ChemGrid.Folders.PlayerData, $"perf-{name}.png");
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
        sampledMs.Add(quietFrames > 0 ? float.NaN : ms);
        sampledChunk.Add(wizard != null ? grid.ChunkAt(wizard.transform.position) : default);
        int hadMain = mainMs.Count, hadGpu = gpuMs.Count;
        FpsProbe.ReadTiming(mainMs, renderMs, gpuMs);
        windows.Frame(ms, mainMs.Count > hadMain ? mainMs[^1] : -1f, gpuMs.Count > hadGpu ? gpuMs[^1] : -1f);
        if (ms > spikeMs) NoteSpike(ms, now);
        ReadCounts();
        if (now >= countAt)
        {
            CountCells();
            countAt = now + CountEverySeconds;
        }
        if (windows.Due(now))
        {
            long from = Stopwatch.GetTimestamp();
            windows.Close(now, burningNow, markers.WindowSummary());
            overheadMs += (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
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
            if (WindowCounts.Any(k => pair.Key.StartsWith(k, StringComparison.Ordinal))) windows.Count(pair.Key, pair.Value);
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
        foreach (var c in grid.World.AllCells())
        {
            if (c.Fire > 0) burningNow++;
            if (c.Gas == GasIds.Fog) fogNow++;
        }
        burningPeak = Math.Max(burningPeak, burningNow);
        fogPeak = Math.Max(fogPeak, fogNow);
        overheadMs += (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (message.Contains("Ring Buffer")) PerfCounts.Add("log.ring_buffer_full");
        if (type == LogType.Warning) warnings++;
        else if (type != LogType.Log) errors++;
    }

    // The bot quits when its run is done (OnApplicationQuit never runs in a Windows player).
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
        if (Has("-perf-memory")) Debug.Log(PerfMemory.Line("end"));
        string missing = string.Join(",", markers.Missing());
        Debug.Log($"PERF MARKERS scenario={scenario}{markers.Summary()}" + (missing.Length > 0 ? $" missing={missing}" : ""));
        Debug.Log($"PERF RENDER scenario={scenario}{markers.RenderSummary()}");
        LogSlow();
        var inv = CultureInfo.InvariantCulture;
        Debug.Log($"PERF COUNTS scenario={scenario} frames={frameNumber}" + string.Concat(counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $" {c.Key}={c.Value.sum.ToString(inv)}/{c.Value.peak.ToString(inv)}/{c.Value.frames.ToString(inv)}")));
        foreach (var (_, line) in spikes.OrderByDescending(s => s.ms).Take(MostSpikes)) Debug.Log(line);
        foreach (string line in windows.Lines) Debug.Log(line);
        if (stage != null && scenario == "forestload") LogLatency(inv);
        if (grid != null && Environment.GetCommandLineArgs().Contains("-perf-water-check"))
        {
            var (tiles, different) = grid.CheckWaterTiles();
            Debug.Log($"PERF WATER CHECK tiles={tiles} different={different}");
        }
        if (Environment.GetCommandLineArgs().Contains("-perf-same-check"))
        {
            var (tiles, tilesDifferent, vertices, keptMs, perPointMs) = CellWaterMesh.KeptPointsChecked;
            var (strips, stripsDifferent) = ValleyGround.KeptNoiseChecked;
            Debug.Log($"PERF SAME CHECK water_tiles={tiles} different={tilesDifferent} vertices={vertices} points_kept_ms={keptMs.ToString("0.0", inv)} " +
                $"points_delegates_ms={perPointMs.ToString("0.0", inv)} paint_strips={strips} different={stripsDifferent}");
        }
        if (Environment.GetCommandLineArgs().Contains("-perf-list-markers")) ListMarkers();
    }

    // The frames at the run's p95 and over: what their markers were made of, and where the wizard was.
    void LogSlow()
    {
        if (frameMs.Count == 0) return;
        float at = (float)FrameStats.Of(frameMs).P95Ms;
        Debug.Log($"PERF SLOW scenario={scenario}{markers.SlowSummary(sampledMs, at)}");
        var where = new Dictionary<Vector2Int, (int slow, int all)>();
        for (int f = 0; f < sampledMs.Count; f++)
        {
            if (float.IsNaN(sampledMs[f])) continue;
            where.TryGetValue(sampledChunk[f], out var had);
            where[sampledChunk[f]] = (had.slow + (sampledMs[f] >= at ? 1 : 0), had.all + 1);
        }
        Debug.Log($"PERF SLOW CHUNKS scenario={scenario} chunk=slow/all" + string.Concat(where.OrderByDescending(w => w.Value.slow).Take(12)
            .Select(w => $" {w.Key.x},{w.Key.y}={w.Value.slow}/{w.Value.all}")));
    }

    // The times a player feels: each as seconds into the run, then ms.
    void LogLatency(CultureInfo inv)
    {
        static string Listed(IEnumerable<(float at, float ms)> times) => string.Join(",", times.Select(t => $"{t.at.ToString("0", CultureInfo.InvariantCulture)}:{t.ms.ToString("0.0", CultureInfo.InvariantCulture)}"));
        void Log(string name, List<(float at, float ms)> times)
        {
            if (times.Count == 0)
            {
                Debug.Log($"PERF LATENCY {name} none");
                return;
            }
            var stats = FrameStats.Of(times.Select(t => t.ms).ToList());
            Debug.Log($"PERF LATENCY {name} n={stats.Count} p50={stats.P50Ms.ToString("0.0", inv)} p95={stats.P95Ms.ToString("0.0", inv)} worst={times.Max(t => t.ms).ToString("0.0", inv)} each={Listed(times)}");
        }
        Log("cast", stage.CastLatency);
        Log("move", stage.MoveLatency);
    }

    string ResultLine(double elapsed)
    {
        var inv = CultureInfo.InvariantCulture;
        var stats = FrameStats.Of(frameMs);
        var line = new StringBuilder($"PERF RESULT scenario={scenario} build={BuildInfo.Label.Replace(' ', '_')} res={Screen.width}x{Screen.height} frames={stats.Count} seconds={elapsed.ToString("0.0", inv)}");
        var chem = ChemGrid.Current;
        if (chem != null) line.Append($" changed={chem.World.ChangedCount} flights={chem.World.DragonFlights} palette={chem.World.StartShapes} keep_start_ms={ChemGrid.KeepStartMs.ToString("0.0", inv)}");
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
        // The sparse World (D17): chunks unpacked now, the drawn hint, and the packing since the world was made.
        var w = grid.World;
        line.Append($" unpacked={w.UnpackedCount} drawn_hint={w.DrawnCount} packs={w.Packs} unpacks={w.Unpacks} read_unpacks={w.ReadUnpacks} pack_ms={w.PackMs.ToString("0.0", inv)} unpack_ms={w.UnpackMs.ToString("0.0", inv)}");
        line.Append($" burning_peak={burningPeak} burning_end={burningNow} fog_cells_peak={fogPeak} fog_cells_end={fogNow} fog_drawn={grid.Fog.SquaresShown}");
        line.Append($" hour={(grid.World.Minutes / 60.0 % 24.0).ToString("0.0", inv)} weather={grid.World.Weather.Kind}");
        line.Append($" worst={(frameMs.Count > 0 ? frameMs.Max() : 0f).ToString("0.0", inv)} over_30ms={frameMs.Count(ms => ms > Over30Ms)}");
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
        var file = Path.Combine(ChemGrid.Folders.PlayerData, "perf-markers.txt");
        File.WriteAllLines(file, lines.OrderBy(l => l, StringComparer.Ordinal));
        Debug.Log($"PERF MARKER LIST {file}");
    }
}
