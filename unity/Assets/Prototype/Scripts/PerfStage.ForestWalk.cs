using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Chem;
using UnityEngine;

// The forest walk (docs/design/hills.md, "Forest"): roadwalk with -perf-forest. On a hills map with woods, through the
// densest wood (HillsForest.Heart's chunk) from west to east along its middle row, 900 m either side of it, and back, for
// as long as the run lasts; each waypoint a few cells on, on a cell with no tree round it (the path keeps to the gaps
// between the trees, as a player would). The path is worked out on a worker before the run. As the run ends a "PERF
// FORESTWALK" line says how far it went and how many trees stood round the camera on average.
public partial class PerfStage
{
    const int ForestHalfCells = 600, ForestStepCells = 4, ForestSway = 6;
    long forestFrames, forestNear, forestAll;

    IEnumerator ForestWalk()
    {
        var loaded = LoadedMaps.Get(ChemGrid.MapSource);
        var hills = loaded.Hills;
        if (hills?.Forest == null)
        {
            Debug.LogError("PERF FORESTWALK: this map has no woods (a hills map's \"forest\")");
            yield break;
        }
        var gen = loaded.Gen;
        var job = Task.Run(() => ForestPath(gen, hills));
        while (!job.IsCompleted) yield return null;
        var (heart, path) = job.Result;
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        Debug.Log($"PERF FORESTWALK heart={heart.cx},{heart.cy} points={path.Count} start={path[0].x},{path[0].y} end={path[path.Count - 1].x},{path[path.Count - 1].y}");
        PlaceCell(World.FoldX(path[0].x), World.FoldY(path[0].y));
        yield return null;
        while (grid.ChunksLeftToDraw > 0) yield return null;
        PlaceCell(World.FoldX(path[0].x), World.FoldY(path[0].y));
        Ready = true;
        StartCoroutine(WatchSeams());
        StartCoroutine(WatchTrees());
        while (!Go) yield return null;
        var last = path[0];
        int k = 0, way = 1;
        while (enabled)
        {
            // There and back again.
            if (k + way < 0 || k + way >= path.Count) way = -way;
            k += way;
            roadCells += Mathf.Max(Mathf.Abs(path[k].x - last.x), Mathf.Abs(path[k].y - last.y));
            last = path[k];
            yield return WalkTo(CellGround(path[k].x, path[k].y), inputs);
            PerfCounts.Add("walk.legs");
        }
        inputs.move = Vector2.zero;
        inputs.sprint = false;
    }

    // The densest wood's chunk and a path along its middle row, ForestHalfCells either side, keeping to the gaps.
    static ((int cx, int cy) heart, List<(int x, int y)> path) ForestPath(ChunkGen gen, HillsLand hills)
    {
        var heart = hills.Forest.Heart(hills.At);
        int hx = heart.cx * ChunkGen.Size + ChunkGen.Size / 2, hy = heart.cy * ChunkGen.Size + ChunkGen.Size / 2;
        bool Clear(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (gen.HillsGroundAt(x + dx, y + dy) == 'T') return false;
            return true;
        }
        var path = new List<(int x, int y)>();
        int off = 0;
        for (int x = hx - ForestHalfCells; x <= hx + ForestHalfCells; x++)
        {
            // The sway nearest the last one with no tree round it (else straight on: the walk jumps or is put past it).
            for (int d = 0; d <= ForestSway; d++)
            {
                if (Mathf.Abs(off + d) <= ForestSway && Clear(x, hy + off + d))
                {
                    off += d;
                    break;
                }
                if (Mathf.Abs(off - d) <= ForestSway && Clear(x, hy + off - d))
                {
                    off -= d;
                    break;
                }
            }
            if ((x - hx) % ForestStepCells == 0) path.Add((x, hy + off));
        }
        return (heart, path);
    }

    // Every frame once the run is going: the trees round the camera (TreeImpostors' counts for the tree budget).
    IEnumerator WatchTrees()
    {
        while (enabled)
        {
            if (Go)
            {
                forestFrames++;
                forestNear += TreeImpostors.NearAround;
                forestAll += TreeImpostors.AllAround;
                if (forestFrames % 600 == 0)
                {
                    var cell = grid.CellAt(wizard.transform.position);
                    Debug.Log($"PERF FORESTWALK at={cell.x},{cell.y} trees_near={TreeImpostors.NearAround} trees_around={TreeImpostors.AllAround} prefabs={TreeImpostors.NearDrawn} prefabs_made={TreeImpostors.NearMade} impostors={TreeImpostors.Drawn} calls={TreeImpostors.Calls} held={TreeImpostors.Instance?.Held} prefabs_within={TreeImpostors.PrefabsWithin:0}");
                }
            }
            yield return null;
        }
    }

    /// <summary>The "PERF FORESTWALK" line: how far the walk went, and the trees round the camera on average.</summary>
    void LogForestWalk()
    {
        long n = System.Math.Max(1, forestFrames);
        Debug.Log(string.Format(CultureInfo.InvariantCulture, "PERF FORESTWALK done cells={0} km={1:0.00} trees_near={2} trees_around={3} prefabs_within={4:0} impostors_within={5:0}",
            roadCells, roadCells * 1.5 / 1000, forestNear / n, forestAll / n, TreeImpostors.PrefabsWithin, TreeImpostors.ImpostorsWithin));
    }
}
