using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

// A "PERF MEMORY" line for a PerfScenario (docs/perf/2026-11-10-scale.md): the process's private bytes and working
// set, Unity's native and managed totals, and every loaded Unity object counted and sized by type, biggest first, so
// two maps' loads can be set side by side. Object sizes are Profiler.GetRuntimeMemorySizeLong, which a development
// build reports (a release build says 0). Slow (it walks every object): call it outside the measured frames.
public static class PerfMemory
{
    const double Mb = 1024.0 * 1024.0;
    const int TypesListed = 14;

    /// <summary>One line of what the process holds now, labelled.</summary>
    public static string Line(string label)
    {
        var inv = CultureInfo.InvariantCulture;
        var line = new StringBuilder($"PERF MEMORY at={label} since_start_s={Time.realtimeSinceStartup.ToString("0.0", inv)}");
        void Add(string key, double bytes) => line.Append($" {key}={(bytes / Mb).ToString("0", inv)}");
        var (priv, ws, peak) = ProcessMemory();
        Add("private_mb", priv);
        Add("working_set_mb", ws);
        Add("peak_working_set_mb", peak);
        Add("native_allocated_mb", Profiler.GetTotalAllocatedMemoryLong());
        Add("native_reserved_mb", Profiler.GetTotalReservedMemoryLong());
        Add("managed_used_mb", Profiler.GetMonoUsedSizeLong());
        Add("managed_heap_mb", Profiler.GetMonoHeapSizeLong());
        Add("gfx_driver_mb", Profiler.GetAllocatedMemoryForGraphicsDriver());
        var all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
        var byType = all.GroupBy(o => o.GetType().Name)
            .Select(g => (type: g.Key, count: g.Count(), bytes: g.Sum(o => Profiler.GetRuntimeMemorySizeLong(o))))
            .OrderByDescending(t => t.bytes).ThenByDescending(t => t.count).ToList();
        line.Append($" objects={all.Length} types=");
        line.Append(string.Join(",", byType.Take(TypesListed).Select(t => $"{t.type}:{t.count}:{(t.bytes / Mb).ToString("0", inv)}mb")));
        foreach (string kind in new[] { "GameObject", "Mesh", "Terrain", "TerrainData", "MeshRenderer", "MeshCollider", "Material", "Texture2D" })
        {
            var t = byType.FirstOrDefault(b => b.type == kind);
            line.Append($" n_{kind}={t.count}");
        }
        return line.ToString();
    }

    // Mono's Process reports 0 for these in a player, so on Windows they come from psapi; elsewhere, 0.
    static (double priv, double ws, double peak) ProcessMemory()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return (0, 0, 0);
        var c = new Counters { cb = (uint)Marshal.SizeOf<Counters>() };
        return GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb) ? ((double)(ulong)c.PrivateUsage, (double)(ulong)c.WorkingSetSize, (double)(ulong)c.PeakWorkingSetSize) : (0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Counters
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);
}
