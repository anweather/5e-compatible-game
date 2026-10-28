using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Records every graphics pipeline state the player creates (a GraphicsStateCollection
// trace), to find hitches from first-time pipeline creation (playtest finding F-0002)
// and to make the collection a warm-up can load. Only with -pso-trace <file>: each frame
// that created new states writes a "PSO TRACE" line to the log (how many, the total so
// far, and that frame's time), and the collection is saved to <file> at quit, through
// QuickExit's BeforeExit on Windows. -pso-warmup <file> loads such a collection at start
// and creates its states before the first frame, logging how long that took, to see
// whether a warm-up removes the hitches. Without either flag it does nothing.
public class PsoTrace : MonoBehaviour
{
    const string Flag = "-pso-trace";
    const string WarmUpFlag = "-pso-warmup";

    GraphicsStateCollection trace;
    string path;
    int lastCount;
    bool saved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartIfAsked()
    {
        var args = Environment.GetCommandLineArgs();
        WarmUpIfAsked(args);
        int at = Array.IndexOf(args, Flag);
        if (at < 0 || at + 1 >= args.Length) return;
        var go = new GameObject("PSO trace");
        DontDestroyOnLoad(go);
        var self = go.AddComponent<PsoTrace>();
        self.path = Path.GetFullPath(args[at + 1]);
        self.trace = new GraphicsStateCollection();
        bool tracing = self.trace.BeginTrace();
        Debug.Log($"PSO TRACE: {(tracing ? "tracing" : "couldn't start tracing")} on {SystemInfo.graphicsDeviceType}; saving to {self.path} at quit.");
        QuickExit.BeforeExit += self.Save;
    }

    static void WarmUpIfAsked(string[] args)
    {
        int at = Array.IndexOf(args, WarmUpFlag);
        if (at < 0 || at + 1 >= args.Length) return;
        var collection = new GraphicsStateCollection();
        string file = Path.GetFullPath(args[at + 1]);
        if (!collection.LoadFromFile(file))
        {
            Debug.Log($"PSO WARMUP: couldn't load {file}");
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        collection.WarmUp(default).Complete();
        Debug.Log($"PSO WARMUP: {collection.completedWarmupCount} of {collection.totalGraphicsStateCount} states in {clock.Elapsed.TotalMilliseconds:0} ms from {file}");
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
