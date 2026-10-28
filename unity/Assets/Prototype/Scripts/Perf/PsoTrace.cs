using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Records every graphics pipeline state the player creates (a GraphicsStateCollection
// trace; development builds only), to find hitches from first-time pipeline creation (playtest finding F-0002)
// and to make the collection a warm-up can load. Only with -pso-trace <file>: each frame
// that created new states writes a "PSO TRACE" line to the log (how many, the total so
// far, and that frame's time), and the collection is saved to <file> at quit, through
// QuickExit's BeforeExit on Windows (the warm-up that uses it is PsoWarmUp). Without the
// flag it does nothing.
public class PsoTrace : MonoBehaviour
{
    const string Flag = "-pso-trace";

    GraphicsStateCollection trace;
    string path;
    int lastCount;
    bool saved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartIfAsked()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, Flag);
        if (at < 0 || at + 1 >= args.Length) return;
        var go = new GameObject("PSO trace");
        DontDestroyOnLoad(go);
        var self = go.AddComponent<PsoTrace>();
        self.path = Path.GetFullPath(args[at + 1]);
        self.trace = new GraphicsStateCollection();
        bool tracing = self.trace.BeginTrace();
        Debug.Log($"PSO TRACE: {(tracing ? "tracing" : "couldn't start tracing")} on {SystemInfo.graphicsDeviceType}; saving to {self.path} at quit.");
        // Unity records states only in a development build, and only where pipelines can be made in parallel.
        if (!Debug.isDebugBuild) Debug.LogWarning("PSO TRACE: this isn't a development build, so nothing will be recorded.");
        if (!SystemInfo.supportsParallelPSOCreation) Debug.LogWarning($"PSO TRACE: {SystemInfo.graphicsDeviceType} here can't make pipelines in parallel, so nothing will be recorded.");
        QuickExit.BeforeExit += self.Save;
    }

    // States made while drawing a frame show up in the next Update, whose delta is that frame's time.
    void Update()
    {
        int count = trace.totalGraphicsStateCount;
        if (count == lastCount) return;
        Debug.Log($"PSO TRACE: frame {Time.frameCount} +{count - lastCount} states (total {count}), frame {Time.unscaledDeltaTime * 1000f:0.0} ms, at {Time.realtimeSinceStartup:0.0} s");
        lastCount = count;
    }

    void OnApplicationQuit() => Save();

    void Save()
    {
        if (saved) return;
        saved = true;
        trace.EndTrace();
        bool ok = trace.SaveToFile(path);
        Debug.Log($"PSO TRACE: {(ok ? "saved" : "couldn't save")} {trace.totalGraphicsStateCount} states in {trace.variantCount} variants to {path}");
    }
}
