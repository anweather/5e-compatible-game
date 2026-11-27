using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Creates the graphics pipeline states the game is known to use before the first
// frame, so they aren't made on first sight in play: a first Hasten Time took 2.6 s,
// walking to the tower 300 ms (playtest finding F-0002). The states come from
// Resources/Pso<graphics API>.graphicsstate (PsoMetal, PsoDirect3D12), recorded by
// playtest runs with -pso-trace and merged with PsoCollections (docs/RELEASING.md).
// About 0.1 s for 200 states on an M2 Pro. A missing or stale collection only brings
// some hitches back. -pso-warmup <file> warms from that file instead; -pso-trace and
// -no-pso-warmup skip it, so a trace records every state the game makes.
public static class PsoWarmUp
{
    const string FromFile = "-pso-warmup", Skip = "-no-pso-warmup", Tracing = "-pso-trace";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void WarmUp()
    {
        if (Application.isEditor) return;
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, Skip) >= 0 || Array.IndexOf(args, Tracing) >= 0) return;
        var collection = Load(args, out string source);
        if (collection == null) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        collection.WarmUp(default).Complete();
        Debug.Log($"PSO WARMUP: {collection.completedWarmupCount} of {collection.totalGraphicsStateCount} states in {clock.Elapsed.TotalMilliseconds:0} ms from {source}");
    }

    static GraphicsStateCollection Load(string[] args, out string source)
    {
        int at = Array.IndexOf(args, FromFile);
        if (at >= 0 && at + 1 < args.Length)
        {
            source = Path.GetFullPath(args[at + 1]);
            var fromFile = new GraphicsStateCollection();
            if (fromFile.LoadFromFile(source)) return fromFile;
            Debug.Log($"PSO WARMUP: couldn't load {source}");
            return null;
        }
        source = "Pso" + SystemInfo.graphicsDeviceType;
        var shipped = Resources.Load<GraphicsStateCollection>(source);
        if (shipped == null) Debug.Log($"PSO WARMUP: no {source} collection for this graphics API; states are made on first use.");
        return shipped;
    }
}
