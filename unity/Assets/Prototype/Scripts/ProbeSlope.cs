using System.Collections;
using UnityEngine;

// The probe's slope scenario (playtest #31): Create Water on the steepest dry
// hillside near the start, then how far down the hill the water has run after
// a while, and a picture from the side. The hill is the valley's baked ground,
// found through ValleyGround.BaseHeight, so it runs with or without the fix:
// the before and after pictures come from the same script.
public class SlopeProbe : ProbeScenario
{
    const int Reach = 6, Search = 90;

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var valley = FindAnyObjectByType<ValleyChemistry>();
        var ground = valley != null ? valley.Ground : null;
        if (ground == null)
        {
            Check("hillside_found", false, "no valley ground");
            yield break;
        }
        var start = grid.PlayerStart;
        var (best, dir, fall) = Steepest(ground, start);
        Check("hillside_found", fall > 1.5f, $"a {fall:0.0} m fall over {Reach} cells at {best}");
        if (fall <= 1.5f) yield break;

        var down = new Vector2Int(DirX(dir), DirY(dir));
        var at = grid.CellCenter(best.x, best.y);
        var below = grid.CellCenter(best.x + down.x * Reach, best.y + down.y * Reach);
        PlaceCell(best.x - down.x * 3, best.y - down.y * 3);
        var side = Vector3.Cross(below - at, Vector3.up).normalized;
        Cut((at + below) / 2f + side * 9f + Vector3.up * 5f, (at + below) / 2f);
        caster.Cast(SpellIds.CreateWater, best.x, best.y, dir);
        yield return Wait(12f);

        // How far down the hill the water ran: the lowest wet cell's ground under the cast cell's.
        float top = ground.BaseHeight(best.x, best.y), lowest = top;
        int wet = 0;
        for (int y = best.y - Reach * 2; y <= best.y + Reach * 2; y++)
            for (int x = best.x - Reach * 2; x <= best.x + Reach * 2; x++)
            {
                var c = World.InBounds(x, y) ? World.At(x, y) : null;
                if (c == null || c.Water == 0) continue;
                wet++;
                lowest = Mathf.Min(lowest, ground.BaseHeight(x, y));
            }
        Metric("hill_fall_m", fall);
        Metric("wet_cells", wet);
        Metric("ran_down_m", top - lowest);
        Check("water_runs_downhill", top - lowest > 1f, $"the water reached {top - lowest:0.0} m below where it was cast ({wet} wet cells)");
        yield return Wait(0.3f);
        Shot("slope");
        yield return null;
        yield return null;
    }

    // The dry, open cell near the start whose ground falls furthest over Reach cells in one direction.
    (Vector2Int at, int dir, float fall) Steepest(ValleyGround ground, Vector2Int start)
    {
        var best = start;
        int bestDir = 0;
        float bestFall = 0f;
        for (int y = start.y - Search; y <= start.y + Search; y += 2)
            for (int x = start.x - Search; x <= start.x + Search; x += 2)
            {
                if (!Dry(x, y)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int tx = x + DirX(d) * Reach, ty = y + DirY(d) * Reach;
                    if (!Dry(tx, ty)) continue;
                    float fall = ground.BaseHeight(x, y) - ground.BaseHeight(tx, ty);
                    // A hillside, not a cliff (a wall of the river's cut or the coast).
                    if (fall <= bestFall || fall > 6f) continue;
                    best = new Vector2Int(x, y);
                    bestDir = d;
                    bestFall = fall;
                }
            }
        return (best, bestDir, bestFall);
    }

    bool Dry(int x, int y)
    {
        if (!World.InBounds(x, y)) return false;
        var c = World.At(x, y);
        return c != null && c.Water == 0 && !World.IsBlocking(c) && World.TerrainOf(c).Spring == 0;
    }

    // N, E, S, W as the core's directions (north is -y).
    static readonly int[] StepX = { 0, 1, 0, -1 }, StepY = { -1, 0, 1, 0 };
    static int DirX(int d) => StepX[d];
    static int DirY(int d) => StepY[d];
}
