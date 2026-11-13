using System.Diagnostics;
using System.Globalization;
using System.Text;

// A start-up's steps timed for one log line ("heightfield=412 trees=3 ..."): what a world's build costs, by step, so a
// big map's start shows which step follows the map's size (J-086). Mark ends a step and starts the next.
public sealed class StepTimes
{
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly StringBuilder line = new();
    readonly string startup;

    /// <param name="startup">If named, each step of 50 ms or more is also a line on the start-up timeline (StartupTimeline).</param>
    public StepTimes(string startup = null) => this.startup = startup;

    /// <summary>The step just done, timed since the last mark (or since this was made).</summary>
    public void Mark(string step)
    {
        if (startup != null) StartupTimeline.Mark($"{startup}: {step}", 50);
        line.Append(line.Length > 0 ? " " : "").Append(step).Append('=')
            .Append(watch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
        watch.Restart();
    }

    public override string ToString() => line.ToString();
}
