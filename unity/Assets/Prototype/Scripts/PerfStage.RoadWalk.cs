using System.Collections;
using System.Globalization;
using System.Linq;
using Chem;
using UnityEngine;

// roadwalk (docs/design/hills.md, "Roads"): on a map with generated roads, a walk from the spawn along the main road it
// stands on (the one along its row of world squares, else its column), east (south) and on round the torus for as long as
// the run lasts, a waypoint at each point of the road's line (10 to 20 cells apart). The seams are watched as the seam walk watches them,
// and as the run ends a "PERF ROADWALK" line says how far it went and how many of its frames stood on the road.
// -perf-river walks down the main river's bank instead (RiverWalk), -perf-forest through the densest wood (ForestWalk). -perf-road row,N (or column,N) walks that main road instead, and -perf-from X,Y starts the wizard there (the line's
// point nearest it), once the chunks round it are drawn: the pass walk of docs/design/alpine.md (-perf-road column,5
// -perf-from 3300,500, up the foothills, over the north pass and down into the meadow).
public partial class PerfStage
{
    int roadFrames, roadFramesOn, roadCells;

    IEnumerator RoadWalk()
    {
        var roads = LoadedMaps.Get(ChemGrid.MapSource).Hills?.Roads;
        if (roads == null)
        {
            Debug.LogError("PERF ROADWALK: this map has no generated roads (a hills map's \"roads\")");
            yield break;
        }
        if (Asked("-perf-river"))
        {
            yield return RiverWalk();
            yield break;
        }
        if (Asked("-perf-forest"))
        {
            yield return ForestWalk();
            yield break;
        }
        var spawn = grid.PlayerStart;
        var from0 = Pair("-perf-from");
        if (from0 is { } f) spawn = f;
        int sx = spawn.x / WorldPlan.SquareCells, sy = spawn.y / WorldPlan.SquareCells;
        bool alongRow = roads.MainRows.Contains(sy) || !roads.MainColumns.Contains(sx);
        int index = alongRow ? sy : sx;
        if (RoadAsked() is { } asked) (alongRow, index) = asked;
        var path = roads.Route(alongRow, index);
        int from = 0;
        long best = long.MaxValue;
        for (int p = 0; p < path.Count; p++)
        {
            long dx = World.DeltaX(path[p].x, spawn.x), dy = World.DeltaY(path[p].y, spawn.y), d = dx * dx + dy * dy;
            if (d < best) (best, from) = (d, p);
        }
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        Debug.Log($"PERF ROADWALK from={spawn.x},{spawn.y} {(alongRow ? "row" : "column")}={index} line={path.Count} points start={path[from].x},{path[from].y}");
        if (from0 != null)
        {
            PlaceCell(path[from].x, path[from].y);
            yield return null;
            while (grid.ChunksLeftToDraw > 0) yield return null;
            PlaceCell(path[from].x, path[from].y);
            Ready = true;
        }
        StartCoroutine(WatchSeams());
        StartCoroutine(WatchRoad());
        while (!Go) yield return null;
        int wx = alongRow ? World.W : 0, wy = alongRow ? 0 : World.H, k = from, lap = 0;
        (int x, int y) last = path[from];
        while (enabled)
        {
            if (++k >= path.Count)
            {
                k = 0;
                lap++;
            }
            var (x, y) = path[k];
            x += lap * wx;
            y += lap * wy;
            roadCells += Mathf.Max(Mathf.Abs(x - last.x), Mathf.Abs(y - last.y));
            last = (x, y);
            yield return WalkTo(CellGround(x, y), inputs);
            PerfCounts.Add("walk.legs");
        }
        inputs.move = Vector2.zero;
        inputs.sprint = false;
    }

    IEnumerator RiverWalk()
    {
        var hills = LoadedMaps.Get(ChemGrid.MapSource).Hills;
        var rivers = hills?.Rivers;
        if (rivers == null)
        {
            Debug.LogError("PERF RIVERWALK: this map has no rivers (a hills map's \"rivers\")");
            yield break;
        }
        var main = rivers.Rivers.First(r => !r.Joins);
        var massif = hills.Massif;
        var path = new System.Collections.Generic.List<(int x, int y)>();
        var gen = LoadedMaps.Get(ChemGrid.MapSource).Gen;
        bool started = false, meadow = false;
        for (int k = 0; k + 1 < main.X.Length; k++)
        {
            float x = main.X[k] / 65536f, y = main.Y[k] / 65536f, dx = main.X[k + 1] / 65536f - x, dy = main.Y[k + 1] / 65536f - y;
            float len = Mathf.Max(0.01f, Mathf.Sqrt(dx * dx + dy * dy)), side = main.Half[k] + 4;
            int bx = Mathf.RoundToInt(x - dy / len * side), by = Mathf.RoundToInt(y + dx / len * side);
            bool ring = massif != null && massif.Touches(bx, by) && massif.Ring(massif.Radius(bx, by)) > 0;
            if (!ring) meadow = true;
            if (!started && meadow && ring) started = true;
            gen.HillsCellAt(World.FoldX(bx), World.FoldY(by), out char kind, out _);
            if (kind is 'r' or 'w') continue;
            if (started && (path.Count == 0 || Mathf.Max(Mathf.Abs(bx - path[path.Count - 1].x), Mathf.Abs(by - path[path.Count - 1].y)) >= 6)) path.Add((bx, by));
        }
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        Debug.Log($"PERF RIVERWALK points={path.Count} start={path[0].x},{path[0].y} end={path[path.Count - 1].x},{path[path.Count - 1].y}");
        PlaceCell(World.FoldX(path[0].x), World.FoldY(path[0].y));
        yield return null;
        while (grid.ChunksLeftToDraw > 0) yield return null;
        PlaceCell(World.FoldX(path[0].x), World.FoldY(path[0].y));
        Ready = true;
        StartCoroutine(WatchSeams());
        while (!Go) yield return null;
        var last = path[0];
        for (int k = 1; k < path.Count && enabled; k++)
        {
            roadCells += Mathf.Max(Mathf.Abs(path[k].x - last.x), Mathf.Abs(path[k].y - last.y));
            last = path[k];
            yield return WalkTo(CellGround(path[k].x, path[k].y), inputs);
            PerfCounts.Add("walk.legs");
        }
        Debug.Log(string.Format(CultureInfo.InvariantCulture, "PERF RIVERWALK reached the end lake={0}", path.Count > 0 && last == path[path.Count - 1]));
        inputs.move = Vector2.zero;
        inputs.sprint = false;
    }

    static (bool alongRow, int index)? RoadAsked()
    {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, "-perf-road");
        if (at < 0 || at + 1 >= args.Length) return null;
        var parts = args[at + 1].Split(',');
        return parts.Length == 2 && int.TryParse(parts[1], out int n) ? (parts[0] == "row", n) : null;
    }

    IEnumerator WatchRoad()
    {
        var gen = LoadedMaps.Get(ChemGrid.MapSource).Gen;
        while (enabled)
        {
            if (Go)
            {
                var cell = grid.CellAt(wizard.transform.position);
                gen.HillsLandAt(World.FoldX(cell.x), World.FoldY(cell.y), out bool road);
                roadFrames++;
                if (road) roadFramesOn++;
            }
            yield return null;
        }
    }

    public void LogRoadWalk()
    {
        if (Scenario != "roadwalk") return;
        if (Asked("-perf-forest"))
        {
            LogForestWalk();
            return;
        }
        if (Asked("-perf-river"))
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "PERF RIVERWALK done cells={0} km={1:0.00}", roadCells, roadCells * 1.5 / 1000));
            return;
        }
        Debug.Log(string.Format(CultureInfo.InvariantCulture, "PERF ROADWALK done cells={0} km={1:0.00} frames={2} on_road={3:0.0}%",
            roadCells, roadCells * 1.5 / 1000, roadFrames, 100.0 * roadFramesOn / Mathf.Max(1, roadFrames)));
    }
}
