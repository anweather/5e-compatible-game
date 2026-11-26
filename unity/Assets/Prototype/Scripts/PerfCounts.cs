using System.Collections.Generic;
using System.Diagnostics;

// Counts for a performance pass, to go with the Profiler markers: how many cells
// changed, tiles were drawn, puffs made, so a marker's milliseconds can be read per
// unit of work. Nothing is counted unless On, which only PerfScenario sets (so only
// with -perf-scenario), and nothing here changes what the game does. Named counts
// add up over a frame; PerfScenario reads them at the head of the next one, into the
// frame's "PERF SPIKE" line and the run's "PERF COUNTS" line. A count can also be a
// high-water mark (Peak). TickSlice splits a chemistry tick's time among its stages:
// the core has no Profiler markers, and each slice ends where the grid's budget is
// asked, so that's where the time is read.
public static class PerfCounts
{
    static readonly string[] Stages = { "none", "burn", "watercheck", "springs", "flow", "gas", "rest", "timers", "dragon", "end" };

    static readonly Dictionary<string, long> frame = new();
    static readonly Stopwatch sliceWatch = new();
    static readonly string[] stageKeys = MakeKeys();
    static int sliceStage;

    public static int StageCount => Stages.Length;

    public static bool On { get; set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        On = false;
        sliceStage = 0;
        frame.Clear();
        sliceWatch.Reset();
    }

    public static IReadOnlyDictionary<string, long> Frame => frame;

    static string[] MakeKeys()
    {
        var keys = new string[Stages.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = "tick_us." + Stages[i];
        return keys;
    }

    public static void Add(string name, long n = 1)
    {
        if (!On) return;
        frame.TryGetValue(name, out long now);
        frame[name] = now + n;
    }

    public static void Peak(string name, long value)
    {
        if (!On) return;
        if (!frame.TryGetValue(name, out long now) || value > now) frame[name] = value;
    }

    public static void SliceStart(int stage)
    {
        if (!On) return;
        sliceStage = stage;
        sliceWatch.Restart();
    }

    public static void TickSlice(int stage)
    {
        if (!On || !sliceWatch.IsRunning) return;
        NoteSlice();
        sliceStage = stage;
        sliceWatch.Restart();
    }

    public static void SliceStop()
    {
        if (!On) return;
        NoteSlice();
        sliceWatch.Stop();
    }

    static void NoteSlice()
    {
        Add(stageKeys[sliceStage], sliceWatch.ElapsedTicks * 1000000 / Stopwatch.Frequency);
        Add("tick.slices");
    }

    public static void NextFrame() => frame.Clear();
}
