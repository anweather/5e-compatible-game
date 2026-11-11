using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

// One start-up timeline, from the app's start to the first frame of play: each step logs
// "STARTUP <step> at <ms since start> (+<ms since the last step>)", so a big map's slow start says where it goes.
// Logging only; after the first frame (Done) it says nothing more, so a world rebuilt later (a reset, hosting) is quiet.
public static class StartupTimeline
{
    static readonly Stopwatch watch = new();
    static double last;
    static bool done = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Begin()
    {
        watch.Restart();
        last = 0;
        done = false;
        UnityEngine.Debug.Log($"STARTUP engine up at 0 ms (the engine took {Time.realtimeSinceStartupAsDouble * 1000:0} ms before scripts ran)");
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeScene() => Mark("before the first scene loads");

    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        Mark($"scene {scene.name} loaded (its Awakes done)");
    }

    /// <summary>A step done: logged with the time since start and since the last step, until the first frame.</summary>
    public static void Mark(string step) => Mark(step, 0);

    /// <summary>A step done, logged only if it took at least minMs (the time still counts as passed).</summary>
    public static void Mark(string step, double minMs)
    {
        if (done) return;
        double now = watch.Elapsed.TotalMilliseconds, took = now - last;
        last = now;
        if (took >= minMs) UnityEngine.Debug.Log($"STARTUP {step} at {now:0} ms (+{took:0})");
    }

    /// <summary>The first frame: the last line, and the timeline stops.</summary>
    public static void Done(string step)
    {
        Mark(step);
        done = true;
    }
}
