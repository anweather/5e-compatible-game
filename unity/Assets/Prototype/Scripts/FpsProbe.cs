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
        var frames = new List<float>();
        float started = Time.realtimeSinceStartup;
        int shot = 0;
        while (!show.Done && Time.realtimeSinceStartup - started < MaxSeconds)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (shot < ShotsAt.Length && Time.realtimeSinceStartup - started >= ShotsAt[shot])
            {
                var file = System.IO.Path.Combine(Application.persistentDataPath, $"probe-{ShotsAt[shot]:00}s.png");
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log($"FPS PROBE screenshot: {file}");
                shot++;
            }
        }

        frames.Sort();
        float mean = frames.Average();
        float p95 = frames[(int)(frames.Count * 0.95f)];
        float worst = frames[frames.Count - 1];
        Debug.Log($"FPS PROBE: {frames.Count} frames over {Time.realtimeSinceStartup - started:0.0} s at {Screen.width}x{Screen.height}; " +
                  $"mean {mean:0.0} ms ({1000f / mean:0} fps), 95th percentile {p95:0.0} ms ({1000f / p95:0} fps), worst {worst:0.0} ms; " +
                  $"GPU {SystemInfo.graphicsDeviceName}");
        Application.Quit();
    }
}
