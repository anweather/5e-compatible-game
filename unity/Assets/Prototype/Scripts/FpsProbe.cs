using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A frame-rate check for player builds. Start the game with -fps-probe and it
// plays the showcase (the valley fly-over, then fires, ice and lightning)
// with v-sync off, measures every frame, saves a few screenshots, writes an
// "FPS PROBE" line to the player log, then starts a wildfire in the meadow and
// writes an "FPS PROBE fire" line for its 20 s, and quits. Without the flag it
// does nothing.
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
        yield return Wildfire();
        Application.Quit();
    }

    const float BurnSeconds = 20f;
    static readonly WaitForSeconds BoltGap = new(0.4f);

    // Then a wildfire, as the probe's wildfire scenario sets it (the Wildfire mod on, Fire Bolts
    // into the meadow north of the yard, 20 s burning with the camera on it), so a build's numbers
    // cover a big fire too: a second line, "FPS PROBE fire".
    IEnumerator Wildfire()
    {
        var grid = FindAnyObjectByType<ChemGrid>();
        var wizard = WizardRig.FindLocal();
        var caster = wizard != null ? wizard.GetComponent<SpellCaster>() : null;
        if (grid == null || caster == null) yield break;
        if (grid.TryGetComponent<RuleSwitcher>(out var switcher)) switcher.Toggle("wildfire");
        EventLog.HudVisible = false;
        if (Camera.main.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var brain)) brain.enabled = false;
        var eye = grid.YardCellCenter(13, 4) + new Vector3(0f, 30f, -26f);
        Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(grid.YardCellCenter(13, 0) - eye));
        foreach (int x in new[] { 2, 8, 14, 20, 25 })
        {
            var cell = grid.YardCell(x, 0);
            caster.Cast(SpellIds.FireBolt, cell.x, cell.y, 0);
            yield return BoltGap;
        }
        frameMs.Clear();
        mainMs.Clear();
        renderMs.Clear();
        gpuMs.Clear();
        started = Time.realtimeSinceStartup;
        bool shotTaken = false;
        while (Elapsed < BurnSeconds)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000f);
            ReadTiming();
            if (!shotTaken && Elapsed > 10f)
            {
                shotTaken = true;
                var file = System.IO.Path.Combine(Application.persistentDataPath, "probe-fire.png");
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log($"FPS PROBE screenshot: {file}");
            }
        }
        int burning = 0;
        foreach (var c in grid.World.Cells) if (c.Fire > 0) burning++;
        Report("FPS PROBE fire", $"{burning} cells burning at the end");
        EventLog.HudVisible = true;
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

    // "<label>: N frames ..." and "<label> timing: ...", for the frames measured since the last start.
    void Report(string label = "FPS PROBE", string note = null)
    {
        var stats = FrameStats.Of(frameMs);
        float mean = stats.MeanMs, p95 = stats.P95Ms, worst = stats.WorstMs;
        Debug.Log($"{label}: {stats.Count} frames over {Elapsed:0.0} s at {Screen.width}x{Screen.height}; " +
                  $"mean {mean:0.0} ms ({1000f / mean:0} fps), 95th percentile {p95:0.0} ms ({1000f / p95:0} fps), worst {worst:0.0} ms; " +
                  $"GPU {SystemInfo.graphicsDeviceName}" + (note != null ? $"; {note}" : ""));
        Debug.Log(FrameTimingManager.IsFeatureEnabled()
            ? $"{label} timing: {Split("CPU main", mainMs)}; {Split("CPU render", renderMs)}; {Split("GPU", gpuMs)}"
            : $"{label} timing: n/a (Frame Timing Stats is off in Player Settings)");
    }
}
