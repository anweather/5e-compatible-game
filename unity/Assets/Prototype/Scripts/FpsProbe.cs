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
// does nothing. With -cloth-wizards N as well, between the two it stands N
// wizards in a row in the yard (the local one and copies, ClothCrowd), films
// them walking on the spot for 15 s and writes a "CLOTH PROBE" line: what the
// robes' cloth (RobeCloth) costs a frame, with the frame times.
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
        int wizards = ClothWizards();
        if (wizards > 0) yield return Cloth(wizards);
        yield return Wildfire();
        Application.Quit();
    }

    const float ClothSeconds = 15f;
    static readonly WaitForSeconds SettleWait = new(2f);

    // The N after -cloth-wizards, or 0.
    static int ClothWizards()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-cloth-wizards");
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n) ? Mathf.Clamp(n, 1, 32) : 0;
    }

    // N wizards in a row across the yard, the camera before them, 15 s: "CLOTH PROBE: N wizards ...",
    // the robes' stepping time a frame (all of them together) and the frame times.
    IEnumerator Cloth(int count)
    {
        var grid = FindAnyObjectByType<ChemGrid>();
        var wizard = WizardRig.FindLocal();
        if (grid == null || wizard == null) yield break;
        if (Camera.main.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var brain)) brain.enabled = false;
        var spots = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            var cell = grid.YardCell(12 + i - count / 2, 6);
            spots.Add(grid.CellCenter(cell.x, cell.y) + Vector3.up * (grid.SurfaceY(cell.x, cell.y) + 0.02f));
        }
        var facing = Quaternion.LookRotation(Vector3.back);
        // The local wizard first, the copies beside it.
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.SetPositionAndRotation(spots[0], facing);
        body.enabled = true;
        var crowd = ClothCrowd.Make(wizard, spots.GetRange(1, count - 1), facing);
        var middle = (spots[0] + spots[^1]) / 2f;
        var eye = middle + new Vector3(0f, 2.2f, -4f - count * 0.5f);
        Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(middle + Vector3.up - eye));
        // A moment for the copies to dress and their robes to settle.
        yield return SettleWait;

        frameMs.Clear();
        mainMs.Clear();
        renderMs.Clear();
        gpuMs.Clear();
        var clothMs = new List<float>();
        started = Time.realtimeSinceStartup;
        RobeCloth.StepSeconds = 0;
        while (Elapsed < ClothSeconds)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000f);
            clothMs.Add((float)(RobeCloth.StepSeconds * 1000.0));
            RobeCloth.StepSeconds = 0;
            ReadTiming();
        }
        var cloth = FrameStats.Of(clothMs);
        Debug.Log($"CLOTH PROBE: {count} wizards, {FindObjectsByType<RobeCloth>(FindObjectsSortMode.None).Length} robes; RobeCloth.Step mean {cloth.MeanMs:0.000} ms, " +
                  $"95th percentile {cloth.P95Ms:0.000} ms a frame (all robes)");
        Report($"CLOTH PROBE {count} wizards");
        ClothCrowd.Clear(crowd);
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
