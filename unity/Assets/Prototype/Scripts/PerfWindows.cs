using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

// A long performance run cut into 5 second windows, for PerfScenario: what a run's one line of
// totals hides is when it went wrong. Each window keeps its frames, then writes one "PERF WINDOW"
// line as the run ends (logging is itself slow): frame times, main thread, GPU, how long the
// grid spent on ticks and on drawing, the world's speed and debt, and what there was to do. The
// counts of lights and particles search the scene, so they're taken out of the frame (Close).
public sealed class PerfWindows
{
    const double Seconds = 5.0;
    static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

    readonly List<float> frames = new(), main = new(), gpu = new(), step = new(), render = new();
    readonly Dictionary<string, long> counted = new();
    readonly List<string> lines = new();
    readonly ChemGrid grid;
    readonly double startedAt;
    double openedAt;
    int ticksAtOpen, collectionsAtOpen;

    public PerfWindows(ChemGrid grid, double now)
    {
        this.grid = grid;
        startedAt = now;
        Open(now);
    }

    /// <summary>The windows so far, oldest first.</summary>
    public IReadOnlyList<string> Lines => lines;

    void Open(double now)
    {
        openedAt = now;
        ticksAtOpen = grid.World.Tick;
        collectionsAtOpen = GC.CollectionCount(0);
        frames.Clear();
        main.Clear();
        gpu.Clear();
        step.Clear();
        render.Clear();
        counted.Clear();
    }

    /// <summary>One frame's times; the CPU and GPU ones are negative when the frame has none yet.</summary>
    public void Frame(float ms, float mainMs, float gpuMs)
    {
        frames.Add(ms);
        if (mainMs >= 0f) main.Add(mainMs);
        if (gpuMs >= 0f) gpu.Add(gpuMs);
        step.Add((float)grid.StepMs);
        render.Add((float)grid.RenderMs);
    }

    /// <summary>A named count of the frame, into the window's.</summary>
    public void Count(string name, long value)
    {
        counted.TryGetValue(name, out long had);
        counted[name] = had + value;
    }

    /// <summary>Whether this window's time is up.</summary>
    public bool Due(double now) => now - openedAt >= Seconds;

    /// <summary>Writes the window and opens the next.</summary>
    public void Close(double now, int burning, string markers)
    {
        var world = grid.World;
        double wall = now - openedAt;
        double due = wall * world.Rules.Sim.TicksPerSecond;
        int done = world.Tick - ticksAtOpen;
        var line = new StringBuilder($"PERF WINDOW t={Num(now - startedAt)} frames={frames.Count}");
        Split(line, "frame", frames);
        Split(line, "main", main);
        Split(line, "gpu", gpu);
        Split(line, "tick", step);
        Split(line, "view", render);
        line.Append($" sim_pct={Num(due > 0 ? done * 100.0 / due : 100.0)} ticks={done} owed_s={Num(grid.OwedSeconds)}");
        line.Append($" weather={world.Weather.Kind} chunks={world.ActiveChunks.Count} burning={burning} flames={grid.Flames.CellsBurning}");
        line.Append($" lights={LightsOn()} particles={ParticlesAlive()} gen0={GC.CollectionCount(0) - collectionsAtOpen}");
        foreach (var pair in counted.OrderBy(c => c.Key, StringComparer.Ordinal)) line.Append($" {pair.Key}={pair.Value.ToString(Plain)}");
        line.Append(markers);
        lines.Add(line.ToString());
        Open(now);
    }

    static void Split(StringBuilder line, string name, List<float> ms)
    {
        if (ms.Count == 0) return;
        var stats = FrameStats.Of(ms);
        line.Append($" {name}_mean={Num(stats.MeanMs)} {name}_p50={Num(stats.P50Ms)} {name}_p95={Num(stats.P95Ms)} {name}_p99={Num(stats.P99Ms)} {name}_worst={Num(ms.Max())}");
    }

    static int LightsOn() => UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.type != LightType.Directional);

    static int ParticlesAlive() => UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Sum(p => p.particleCount);

    static string Num(double value) => value.ToString("0.0", Plain);
}
