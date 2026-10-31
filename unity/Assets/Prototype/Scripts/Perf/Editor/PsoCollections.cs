using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.Experimental.Rendering;

// Merges pipeline-state traces (players run with -pso-trace <file>) into the
// collection PsoWarmUp loads: Resources/Pso<graphics API>.graphicsstate. Traces
// from several playtest seeds cover more of the game than one; all of them must
// be from the same graphics API (Metal traces on the Mac, Direct3D12 on the PC).
// docs/RELEASING.md says when to record them again.
public static class PsoCollections
{
    const string Folder = "Assets/Prototype/Scripts/Perf/Resources";

    /// <summary>
    /// Merges the trace files into Resources/Pso&lt;API&gt;.graphicsstate (made or replaced) and
    /// returns a one-line summary, or why it couldn't.
    /// </summary>
    public static string Merge(params string[] traces)
    {
        if (traces.Length == 0) return "PSO merge: no traces given.";
        var merged = new GraphicsStateCollection();
        var variants = new List<GraphicsStateCollection.ShaderVariant>();
        var states = new List<GraphicsStateCollection.GraphicsState>();
        for (int i = 0; i < traces.Length; i++)
        {
            var trace = new GraphicsStateCollection();
            if (!trace.LoadFromFile(traces[i])) return $"PSO merge: couldn't load {traces[i]}.";
            if (i == 0)
            {
                merged.graphicsDeviceType = trace.graphicsDeviceType;
                merged.runtimePlatform = trace.runtimePlatform;
                merged.qualityLevelName = trace.qualityLevelName;
            }
            else if (trace.graphicsDeviceType != merged.graphicsDeviceType)
                return $"PSO merge: {traces[i]} is {trace.graphicsDeviceType}, not {merged.graphicsDeviceType}.";
            trace.GetVariants(variants);
            foreach (var v in variants)
            {
                merged.AddVariant(v.shader, v.passId, v.keywords);
                trace.GetGraphicsStatesForVariant(v, states);
                foreach (var state in states) merged.AddGraphicsStateForVariant(v.shader, v.passId, v.keywords, state);
            }
        }
        Directory.CreateDirectory(Folder);
        string path = $"{Folder}/Pso{merged.graphicsDeviceType}.graphicsstate";
        if (!merged.SaveToFile(path)) return $"PSO merge: couldn't write {path}.";
        AssetDatabase.ImportAsset(path);
        return $"PSO merge: {traces.Length} traces, {merged.totalGraphicsStateCount} states in {merged.variantCount} variants -> {path}";
    }

    /// <summary>
    /// Merge in a batch Editor: -executeMethod PsoCollections.MergeFromCommandLine -pso-merge a.pso b.pso.
    /// Name a copy of the shipped collection first to add to it rather than replace it.
    /// </summary>
    public static void MergeFromCommandLine()
    {
        var args = System.Environment.GetCommandLineArgs();
        var traces = new List<string>();
        for (int i = System.Array.IndexOf(args, "-pso-merge") + 1; i > 0 && i < args.Length && !args[i].StartsWith("-"); i++) traces.Add(args[i]);
        UnityEngine.Debug.Log(Merge(traces.ToArray()));
    }
}
