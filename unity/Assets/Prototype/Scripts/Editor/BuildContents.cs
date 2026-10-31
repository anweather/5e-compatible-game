using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// What a finished player holds, read from its build report: the graphics-state
// collection its PSO warm-up loads (PsoWarmUp) and the bytes of shaders. A headless
// build has nobody to notice a player that came out without them.
public static class BuildContents
{
    public readonly struct Summary
    {
        public readonly bool Complete;
        public readonly string Line;

        public Summary(bool complete, string line)
        {
            Complete = complete;
            Line = line;
        }
    }

    /// <summary>The collection a target's player warms up from, named for its graphics API.</summary>
    internal static string PsoCollection(BuildTarget target) =>
        target == BuildTarget.StandaloneWindows64 ? "PsoDirect3D12" : "PsoMetal";

    public static Summary Of(BuildTarget target, BuildReport report)
    {
        var assets = report.packedAssets.SelectMany(file => file.contents)
            .Select(asset => (asset.sourceAssetPath, asset.type?.Name, asset.packedSize));
        return Of(PsoCollection(target), assets);
    }

    internal static Summary Of(string collection, IEnumerable<(string path, string type, ulong bytes)> assets)
    {
        var all = assets.ToList();
        var pso = all.Where(a => a.path != null && a.path.EndsWith($"/{collection}.graphicsstate", System.StringComparison.Ordinal)).ToList();
        var shaders = all.Where(a => a.type == nameof(Shader)).ToList();
        ulong psoBytes = pso.Aggregate(0UL, (sum, a) => sum + a.bytes);
        ulong shaderBytes = shaders.Aggregate(0UL, (sum, a) => sum + a.bytes);
        bool complete = psoBytes > 0 && shaderBytes > 0;
        string line = $"{collection} {(psoBytes > 0 ? $"{psoBytes} bytes" : "MISSING")}, " +
                      $"{shaders.Count} shaders {shaderBytes / (1024f * 1024f):0.0} MB";
        return new Summary(complete, line);
    }
}
