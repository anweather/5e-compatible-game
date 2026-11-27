using System.Diagnostics;
using UnityEngine;

// Frame budgets for work spread over frames: chemistry ticks run in slices
// (StepWithinBudget), water tiles are drawn a few at a time (CellWaterMesh).
// Normally a budget is real milliseconds. In a playtest run (ExactFrames, set
// by PlaytestBot) it is a fixed count of work units instead, so the same work
// lands on the same frames every run and a replay matches exactly: with real
// milliseconds a faster frame got further through a tick, or drew an ice
// collider a frame sooner, and two runs of one seed drifted apart
// (playtest/DESIGN.md, "Replays aren't exact").
public partial class ChemGrid
{
    public static bool ExactFrames { get; set; }

    public static float ChoreClock => ExactFrames ? Time.time : Time.unscaledTime;

    public sealed class FrameBudget
    {
        double ms;
        int units;
        readonly Stopwatch watch = new();
        int used;

        public FrameBudget(double ms, int exactUnits)
        {
            this.ms = ms;
            this.units = exactUnits;
        }

        public void Set(double ms, int exactUnits)
        {
            this.ms = ms;
            units = exactUnits;
        }

        public void Start()
        {
            watch.Restart();
            used = 0;
        }

        public bool Over() => ExactFrames ? used++ >= units : watch.Elapsed.TotalMilliseconds >= ms;
    }
}
