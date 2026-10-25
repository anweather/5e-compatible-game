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
// Recording permission to capture the screen.)
public class FpsProbe : MonoBehaviour
{
    const float WarmUpSeconds = 3f;
    const float MaxSeconds = 90f;
    static readonly float[] ShotsAt = { 10f, 22f, 32f, 40f };

    readonly List<float> frameMs = new();
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

    void Report()
    {
        frameMs.Sort();
        float mean = frameMs.Average();
        float p95 = frameMs[(int)(frameMs.Count * 0.95f)];
        float worst = frameMs[frameMs.Count - 1];
        Debug.Log($"FPS PROBE: {frameMs.Count} frames over {Elapsed:0.0} s at {Screen.width}x{Screen.height}; " +
                  $"mean {mean:0.0} ms ({1000f / mean:0} fps), 95th percentile {p95:0.0} ms ({1000f / p95:0} fps), worst {worst:0.0} ms; " +
                  $"GPU {SystemInfo.graphicsDeviceName}");
    }
}
