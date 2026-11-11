using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

// The probe's playlog scenario: the frame-rate log (PlayLog) in a folder of its own. Old logs
// trimmed, lines a second, no garbage from a frame, a hitch and a mark with its picture, a summary.
public class PlayLogProbe : ProbeScenario
{
    const int FramesCounted = 600, OldLogs = 14;

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
        yield return Wait(6.2f);

        float made = -1f;
        yield return GarbagePerCall(() => log.Frame(16.7f), FramesCounted, m => made = m);
        Metric("frame_bytes", made);
        Check("frame_makes_no_garbage", made >= 0f && made < MostGarbage, $"{made:0.00} bytes a frame, over {FramesCounted} a frame of the game");

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
        // Written at the first second, once the window has its size: it said 640 x 480 for 1280 x 720 (#217).
        Check("header_says_the_window", lines.Length > 0 && lines[0].EndsWith($", {Screen.width}x{Screen.height}", System.StringComparison.Ordinal), $"{lines.FirstOrDefault()} (the window {Screen.width}x{Screen.height})");
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
        yield return Readout();
    }

    // The frame readout (#217): on, it says the last second's mean, p95 and worst; it costs well under 0.1 ms a frame.
    IEnumerator Readout()
    {
        var readout = FrameReadout.Instance;
        Check("readout_there", readout != null, "the readout's object");
        if (readout == null) yield break;
        bool was = readout.Shown;
        readout.Show(true);
        yield return Wait(1.5f);
        Check("readout_says_the_frames", readout.MeanMs > 0f && readout.P95Ms >= readout.MeanMs * 0.5f && readout.WorstMs >= readout.P95Ms,
            $"mean {readout.MeanMs:0.0}, p95 {readout.P95Ms:0.0}, worst {readout.WorstMs:0.0} ms");
        var update = typeof(FrameReadout).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int Calls = 2000;
        for (int i = 0; i < Calls; i++) update.Invoke(readout, null);
        double each = clock.Elapsed.TotalMilliseconds / Calls;
        Metric("readout_update_ms", each);
        Check("readout_is_cheap", each < 0.1, $"{each:0.0000} ms an Update (the limit 0.1)");
        readout.Show(was);
    }
}
