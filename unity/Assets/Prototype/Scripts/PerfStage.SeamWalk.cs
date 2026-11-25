using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// seamwalk (docs/design/hills.md): a walk from the spawn a chunk a leg, by -perf-walk's legs ("e8,s8,w8,n8" by default:
// east 8 chunks, south 8, west 8, north 8, round and round until the run ends), so on the hills, whose spawn is half a
// chunk from the east and south seams, it crosses both seams and back. The cells are taken unfolded (past the east seam x
// runs on past W); the wizard walks on in the view's own coordinates. Every frame's time is kept beside where the wizard
// was, and as the run ends a "PERF SEAM" line per seam crossing gives the frames within SeamWindowSeconds of it: their mean
// and the worst, with where it fell.
public partial class PerfStage
{
    const string DefaultWalk = "e8,s8,w8,n8";
    const float SeamWindowSeconds = 3f;

    readonly List<(float at, float ms, Vector2Int cell)> seamFrames = new();
    readonly List<(float at, string seam, Vector2Int cell)> crossings = new();

    IEnumerator SeamWalk()
    {
        var spawn = grid.PlayerStart;
        var legs = (System.Environment.GetCommandLineArgs().SkipWhile(a => a != "-perf-walk").Skip(1).FirstOrDefault() ?? DefaultWalk)
            .Split(',').Select(l => (way: l[0], n: int.Parse(l.Substring(1), CultureInfo.InvariantCulture))).ToArray();
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        Debug.Log($"PERF SEAMWALK from={spawn.x},{spawn.y} legs={string.Join(",", legs.Select(l => l.way + l.n.ToString(CultureInfo.InvariantCulture)))} map={World.W}x{World.H} wrap={World.WrapX}");
        StartCoroutine(WatchSeams());
        while (!Go) yield return null;
        int x = spawn.x, y = spawn.y, walked = 0;
        while (enabled)
            foreach (var (way, n) in legs)
                for (int k = 0; k < n && enabled; k++)
                {
                    x += way switch { 'e' => ChunkCells, 'w' => -ChunkCells, _ => 0 };
                    y += way switch { 's' => ChunkCells, 'n' => -ChunkCells, _ => 0 };
                    yield return WalkTo(CellGround(x, y), inputs);
                    PerfCounts.Add("walk.legs");
                    walked++;
                }
        inputs.move = Vector2.zero;
        inputs.sprint = false;
        Debug.Log($"PERF SEAMWALK done legs={walked} at_s={Time.realtimeSinceStartup:0}");
    }

    // Each frame's time and the wizard's folded cell; a crossing where the folded cell jumps by more than half the map.
    IEnumerator WatchSeams()
    {
        Vector2Int last = default;
        bool first = true;
        while (enabled)
        {
            var cell = grid.CellAt(wizard.transform.position);
            cell = new Vector2Int(World.FoldX(cell.x), World.FoldY(cell.y));
            if (Go) seamFrames.Add((Time.realtimeSinceStartup, Time.unscaledDeltaTime * 1000f, cell));
            if (!first && Mathf.Abs(cell.x - last.x) > World.W / 2) crossings.Add((Time.realtimeSinceStartup, cell.x < last.x ? "east" : "west", cell));
            if (!first && Mathf.Abs(cell.y - last.y) > World.H / 2) crossings.Add((Time.realtimeSinceStartup, cell.y < last.y ? "south" : "north", cell));
            last = cell;
            first = false;
            yield return null;
        }
    }

    /// <summary>The "PERF SEAM" lines: per crossing, the frames round it (logged as the run ends).</summary>
    public void LogSeams()
    {
        if (Scenario is not ("seamwalk" or "roadwalk")) return;
        Debug.Log($"PERF SEAM crossings={crossings.Count} frames={seamFrames.Count}");
        foreach (var (at, seam, cell) in crossings)
        {
            var near = seamFrames.Where(f => Mathf.Abs(f.at - at) <= SeamWindowSeconds).ToList();
            if (near.Count == 0) continue;
            var worst = near.OrderByDescending(f => f.ms).First();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "PERF SEAM seam={0} at_s={1:0.0} cell={2},{3} frames={4} mean_ms={5:0.00} worst_ms={6:0.00} worst_at_s={7:+0.00;-0.00} worst_cell={8},{9}",
                seam, at, cell.x, cell.y, near.Count, near.Average(f => f.ms), worst.ms, worst.at - at, worst.cell.x, worst.cell.y));
        }
    }
}
