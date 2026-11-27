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

    public StepTimes(string startup = null) => this.startup = startup;

    public void Mark(string step)
    {
        if (startup != null) StartupTimeline.Mark($"{startup}: {step}", 50);
        line.Append(line.Length > 0 ? " " : "").Append(step).Append('=')
            .Append(watch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
        watch.Restart();
    }

    public override string ToString() => line.ToString();
}
