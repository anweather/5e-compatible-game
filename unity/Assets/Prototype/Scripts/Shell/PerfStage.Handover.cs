using System.Collections;
using UnityEngine;

// The handover scenario (-perf-scenario handover, a hills map; docs/design/hills.md "One air"): the stills for
// tools/handover.py, the boat game's I16 test of where one tier of the land gives way to the next. For each target chunk
// and each hand-over (the chunk terrains to the known meshes at sight, the known meshes to the far land at the known
// ring), the wizard stands in the chunk one past the hand-over from the target ("far": the outer tier draws it) and then
// one short of it ("near": the inner tier does), and a still of each from the same eye, HandoverEyeUp metres over the
// far stand, looking at the target's ground: the rings follow the wizard, so only the tier drawing the target changes
// between the two, not the view or the air. The target's place on the screen goes in the log (PERF HANDOVER), so the
// tool can set the two pictures' colour there side by side. The weather and the hour are the run's (-perf-weather, -perf-hour).
public partial class PerfStage
{
    const float HandoverEyeUp = 20f, HandoverSettle = 2.5f;

    static readonly (string name, int cx, int cy, int dx, int dy)[] HandoverTargets =
    {
        ("grass-east", 100, 85, -1, 0),
        ("grass-west", 10, 85, 1, 0),
        ("grass-north", 100, 80, 0, -1),
        ("crest", 54, 29, 0, 1),
    };

    IEnumerator Handover()
    {
        while (FarRing.Instance == null || !FarRing.Instance.IsReady || !FarRing.Instance.FarLandShown) yield return null;
        var far = FarRing.Instance;
        var (drawn, known) = far.Dials;
        foreach (var (name, cx, cy, dx, dy) in HandoverTargets)
        {
            int tx = cx * ChunkCells + ChunkCells / 2, ty = cy * ChunkCells + ChunkCells / 2;
            foreach (var (tier, at) in new[] { ("terrain-known", drawn), ("known-far", known) })
            {
                int ex = tx + dx * (at + 1) * ChunkCells, ey = ty + dy * (at + 1) * ChunkCells;
                foreach (var (side, n) in new[] { ("far", at + 1), ("near", at) })
                    yield return HandoverShot($"handover-{name}-{tier}-{side}", tx, ty, tx + dx * n * ChunkCells, ty + dy * n * ChunkCells, ex, ey, far);
            }
        }
        Ready = true;
    }

    IEnumerator HandoverShot(string shot, int tx, int ty, int wx, int wy, int ex, int ey, FarRing far)
    {
        PlaceCell(wx, wy);
        yield return null;
        float until = Time.realtimeSinceStartup + 60f;
        while ((grid.ChunksLeftToDraw > 0 || !far.Settled) && Time.realtimeSinceStartup < until) yield return null;
        PlaceCell(wx, wy);
        var target = grid.CellCenter(tx, ty);
        target.y = far.HillsLandAt(tx, ty) ?? grid.GroundAt(target);
        var eye = grid.CellCenter(ex, ey);
        eye.y = (far.HillsLandAt(ex, ey) ?? grid.GroundAt(eye)) + HandoverEyeUp;
        Cut(eye, target);
        yield return new WaitForSecondsRealtime(HandoverSettle);
        var p = Camera.main.WorldToScreenPoint(target);
        Debug.Log($"PERF HANDOVER shot={shot} x={p.x:0} y={p.y:0} w={Screen.width} h={Screen.height} metres={Vector3.Distance(eye, target):0}");
        yield return PerfScenario.Shot(shot);
        yield return new WaitForSecondsRealtime(0.5f);
    }
}
