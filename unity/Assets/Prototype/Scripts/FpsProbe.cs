using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A frame-rate check for player builds. Start the game with -fps-probe and it
// plays the showcase (the valley fly-over, then fires, ice and lightning)
// with v-sync off, measures every frame, saves a few screenshots, writes one
// "FPS PROBE" line to the player log and quits. Without the flag it does nothing.
// (A game can always capture its own frames; a terminal needs macOS's Screen
// Recording permission to capture the screen.) It also reports the CPU main
// thread, CPU render thread and GPU time per frame, from FrameTimingManager,
// when the build has Frame Timing Stats on (Player Settings).
public class FpsProbe : MonoBehaviour
{
    const float WarmUpSeconds = 3f;
    const float MaxSeconds = 90f;
    static readonly float[] ShotsAt = { 10f, 22f, 32f, 40f };

    readonly List<float> frameMs = new();
    // CPU main thread, CPU render thread and GPU time per frame (FrameTimingManager), when the build has it.
    readonly List<float> mainMs = new(), renderMs = new(), gpuMs = new();
    readonly FrameTiming[] timing = new FrameTiming[1];
    float started;
    int shot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        if (Environment.GetCommandLineArgs().Contains("-fps-probe"))
            new GameObject("FPS probe").AddComponent<FpsProbe>();
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        yield return new WaitForSeconds(WarmUpSeconds);

        var show = new GameObject("Showcase").AddComponent<Showcase>();
        started = Time.realtimeSinceStartup;
        while (!show.Done && Elapsed < MaxSeconds)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000f);
            ReadTiming();
            ScreenshotWhenDue();
        }
        Report();
        Application.Quit();
    }

    float Elapsed => Time.realtimeSinceStartup - started;

    void ScreenshotWhenDue()
    {
        if (shot >= ShotsAt.Length || Elapsed < ShotsAt[shot]) return;
        var file = System.IO.Path.Combine(Application.persistentDataPath, $"probe-{ShotsAt[shot]:00}s.png");
        ScreenCapture.CaptureScreenshot(file);
        Debug.Log($"FPS PROBE screenshot: {file}");
        shot++;
    }

    // The timings land a few frames late; a frame the GPU didn't report (0) is left out.
    void ReadTiming()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) < 1) return;
        var t = timing[0];
        if (t.cpuMainThreadFrameTime > 0) mainMs.Add((float)t.cpuMainThreadFrameTime);
        if (t.cpuRenderThreadFrameTime > 0) renderMs.Add((float)t.cpuRenderThreadFrameTime);
        if (t.gpuFrameTime > 0) gpuMs.Add((float)t.gpuFrameTime);
    }

    static string Split(string name, List<float> ms)
    {
        if (ms.Count == 0) return $"{name} n/a";
        var stats = FrameStats.Of(ms);
        return $"{name} mean {stats.MeanMs:0.0} ms, 95th percentile {stats.P95Ms:0.0} ms";
    }

    void Report()
    {
        var stats = FrameStats.Of(frameMs);
        float mean = stats.MeanMs, p95 = stats.P95Ms, worst = stats.WorstMs;
        Debug.Log($"FPS PROBE: {stats.Count} frames over {Elapsed:0.0} s at {Screen.width}x{Screen.height}; " +
                  $"mean {mean:0.0} ms ({1000f / mean:0} fps), 95th percentile {p95:0.0} ms ({1000f / p95:0} fps), worst {worst:0.0} ms; " +
                  $"GPU {SystemInfo.graphicsDeviceName}");
        Debug.Log(FrameTimingManager.IsFeatureEnabled()
            ? $"FPS PROBE timing: {Split("CPU main", mainMs)}; {Split("CPU render", renderMs)}; {Split("GPU", gpuMs)}"
            : "FPS PROBE timing: n/a (Frame Timing Stats is off in Player Settings)");
    }
}
