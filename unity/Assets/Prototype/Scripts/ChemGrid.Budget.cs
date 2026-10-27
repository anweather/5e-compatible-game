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
    /// <summary>Budgets count work units, not milliseconds, and chores run on game time: set for a playtest run.</summary>
    public static bool ExactFrames { get; set; }

    /// <summary>
    /// The clock for chores done every so often (finding the wizards again): game time in a
    /// playtest run, so they fall on the same frames, else real time (which runs on while paused).
    /// </summary>
    public static float ChoreClock => ExactFrames ? Time.time : Time.unscaledTime;

    /// <summary>One frame's allowance for work done in units: milliseconds normally, a count of units with ExactFrames.</summary>
    public sealed class FrameBudget
    {
        readonly double ms;
        readonly int units;
        readonly Stopwatch watch = new();
        int used;

        /// <param name="ms">Real milliseconds a frame may spend.</param>
        /// <param name="exactUnits">Units a frame may do with ExactFrames: about what <paramref name="ms"/> buys on the PC.</param>
        public FrameBudget(double ms, int exactUnits)
        {
            this.ms = ms;
            this.units = exactUnits;
        }

        /// <summary>Starts the frame's allowance afresh.</summary>
        public void Start()
        {
            watch.Restart();
            used = 0;
        }

        /// <summary>Whether the frame's allowance is spent; with ExactFrames each call is one unit.</summary>
        public bool Over() => ExactFrames ? used++ >= units : watch.Elapsed.TotalMilliseconds >= ms;
    }
}
