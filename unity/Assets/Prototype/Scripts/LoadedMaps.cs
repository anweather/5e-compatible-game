using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Chem;
using UnityEngine;

// A generated map loaded once for the view. The far ring, the biome ground and the pinned valley each read the map in play,
// and GeneratedMap.Load solves its plan (most of a second on the scale island, which loads the nine inside it too): they
// share one load, started by the first to ask (the far ring, on a worker, as the scene wakes). The view only reads it; the
// world's own cells come from the core's load, so nothing the lockstep shares changes. Kept per folder and map.json's time,
// and forgotten as each Play starts.
public static class LoadedMaps
{
    static readonly Dictionary<string, Task<GeneratedMap>> loads = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Forget()
    {
        lock (loads) loads.Clear();
    }

    /// <summary>The generated map in this folder, loaded now or by whoever asked first (throws as GeneratedMap.Load does).</summary>
    public static GeneratedMap Get(string mapDir) => Load(mapDir).GetAwaiter().GetResult();

    /// <summary>The load of the generated map in this folder, started now if no one has asked for it yet.</summary>
    public static Task<GeneratedMap> Load(string mapDir)
    {
        string full = Path.GetFullPath(mapDir);
        string key = full + "|" + File.GetLastWriteTimeUtc(Path.Combine(full, MapFile.FileName)).Ticks;
        lock (loads)
        {
            if (!loads.TryGetValue(key, out var load)) loads[key] = load = Task.Run(() => GeneratedMap.Load(mapDir));
            return load;
        }
    }
}
