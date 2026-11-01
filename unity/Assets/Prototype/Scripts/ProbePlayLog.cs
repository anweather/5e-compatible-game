using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

// The probe's playlog scenario: the frame-rate log (PlayLog) in a folder of its own. Old logs
// trimmed, lines a second, no garbage from a frame, a hitch and a mark with its picture, a summary.
public class PlayLogProbe : ProbeScenario
{
    const int FramesCounted = 3000, OldLogs = 14;

    protected override IEnumerator Run()
    {
        var log = PlayLog.Instance;
        Check("always_there", log != null, "the log's object");
        if (log == null) yield break;
        string folder = Path.Combine(Application.temporaryCachePath, "play-log-probe");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < OldLogs; i++) File.WriteAllText(Path.Combine(folder, $"play-log-2020-01-{i + 10}-000000.csv"), "old");
        File.WriteAllText(Path.Combine(folder, "play-log-2020-01-10-000000-mark-1.jpg"), "old");

        log.Begin(folder);
        string path = log.FilePath;
        yield return Wait(2.4f);

        long before = System.GC.GetTotalMemory(false);
        for (int i = 0; i < FramesCounted; i++) log.Frame(16.7f);
        long made = System.GC.GetTotalMemory(false) - before;
        Metric("frame_bytes", made);
        Check("frame_makes_no_garbage", made == 0, $"{made} bytes over {FramesCounted} frames");

        Thread.Sleep(130);
        yield return null;
        yield return null;
        log.Mark();
        yield return Wait(1.3f);
        log.End();
        yield return Wait(0.5f);

        Check("file_written", File.Exists(path), path);
        if (!File.Exists(path)) yield break;
        var lines = File.ReadAllLines(path);
        Metric("lines", lines.Length);
        Check("header", lines.Length > 2 && lines[1].StartsWith("kind,seconds", System.StringComparison.Ordinal), lines.FirstOrDefault());
        Check("a_line_a_second", lines.Count(l => l.StartsWith("second,", System.StringComparison.Ordinal)) >= 3, $"{lines.Length} lines");
        Check("hitch_line", lines.Any(l => l.StartsWith("hitch,", System.StringComparison.Ordinal)), "over 100 ms");
        Check("mark_line", lines.Any(l => l.StartsWith("mark,", System.StringComparison.Ordinal) && l.Contains("felt bad")), "F8");
        Check("mark_picture", Directory.GetFiles(folder, "*-mark-1.jpg").Any(f => new FileInfo(f).Length > 1000), string.Join(" ", Directory.GetFiles(folder, "*.jpg").Select(Path.GetFileName)));
        var summary = lines.LastOrDefault();
        Check("summary_last", summary != null && summary.StartsWith("summary,", System.StringComparison.Ordinal) && summary.Contains("1% low") && summary.Contains(path), summary);
        Check("keeps_the_last_ten", Directory.GetFiles(folder, "play-log-*.csv").Length == 10 && !File.Exists(Path.Combine(folder, "play-log-2020-01-10-000000-mark-1.jpg")),
            $"{Directory.GetFiles(folder, "play-log-*.csv").Length} logs left");
        Check("every_second_says_the_columns", lines.Where(l => l.StartsWith("second,", System.StringComparison.Ordinal)).All(l => l.Split(',').Length >= 15), lines.FirstOrDefault(l => l.StartsWith("second,", System.StringComparison.Ordinal)));
        Directory.Delete(folder, true);
    }
}
