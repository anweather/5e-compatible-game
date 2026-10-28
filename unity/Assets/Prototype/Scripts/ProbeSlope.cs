using System.Collections;
using UnityEngine;

// The probe's slope scenario (playtest #31): Create Water on the steepest dry
// hillside near the start, then how far down the hill the water has run after
// a while, and a picture from the side. The hill is the valley's baked ground,
// found through ValleyGround.BaseHeight, so it runs with or without the fix:
// the before and after pictures come from the same script.
public class SlopeProbe : ProbeScenario
{
    // Measure: how far round the cast cell the water is looked for (a stream runs a cell a tick).
    const int Reach = 6, Search = 90, Measure = 40;

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
        PlaceCell(best.x - down.x * 3, best.y - down.y * 3);
        // From high on one side, over the hill and the way down it (a low camera sat inside the hill).
        var far = grid.CellCenter(best.x + down.x * Reach * 3, best.y + down.y * Reach * 3);
        // A cell's centre is at the core's height; the valley's ground stands over it.
        at.y = grid.GroundAt(at);
        far.y = grid.GroundAt(far);
        var look = (at + far) / 2f;
        look.y = grid.GroundAt(look);
        // From straight above, nearly: trees round a hillside hid it from the side.
        Cut(look + (at - far).normalized * 6f + Vector3.up * 20f, look);
        caster.Cast(SpellIds.CreateWater, best.x, best.y, dir);
        yield return Wait(12f);

        // Where the water went: its centre's distance down the hill from the cast cell, in cells
        // (spread evenly it stays near 0; running downhill it moves down), and the lowest wet ground.
        float top = ground.BaseHeight(best.x, best.y), lowest = top;
        double units = 0, along = 0;
        for (int y = best.y - Measure; y <= best.y + Measure; y++)
            for (int x = best.x - Measure; x <= best.x + Measure; x++)
            {
                var c = World.InBounds(x, y) ? World.At(x, y) : null;
                if (c == null || c.Water == 0) continue;
                units += c.Water;
                along += c.Water * ((x - best.x) * down.x + (y - best.y) * down.y);
                lowest = Mathf.Min(lowest, ground.BaseHeight(x, y));
            }
        float centre = units > 0 ? (float)(along / units) : 0f;
        Metric("hill_fall_m", fall);
        Metric("water_units", units);
        Metric("ran_down_m", top - lowest);
        Metric("centre_downhill_cells", centre);
        Check("water_runs_downhill", centre > 1.5f, $"the water's centre is {centre:0.0} cells down the hill from where it was cast; its lowest {top - lowest:0.0} m below");
        // The hillsides are wooded with the terrains' own trees: hidden for the picture, then back.
        foreach (var t in Terrain.activeTerrains) t.drawTreesAndFoliage = false;
        yield return Wait(0.3f);
        Shot("slope");
        yield return null;
        yield return null;
        // Close, from up the hill: where it was cast and the first cells it ran down.
        Cut(at + (at - far).normalized * 5f + Vector3.up * 4f, at + (far - at).normalized * 4f);
        yield return Wait(0.3f);
        Shot("slope-close");
        yield return null;
        yield return null;
        foreach (var t in Terrain.activeTerrains) t.drawTreesAndFoliage = true;
    }

    // The dry, open cell near the start whose ground falls furthest over Reach cells in one direction.
    (Vector2Int at, int dir, float fall) Steepest(ValleyGround ground, Vector2Int start)
    {
        var best = (at: start, dir: 0, fall: 0f);
        for (int y = start.y - Search; y <= start.y + Search; y += 2)
            for (int x = start.x - Search; x <= start.x + Search; x += 2)
                if (Dry(x, y)) best = SteepestFrom(ground, x, y, best);
        return best;
    }

    // The best so far, or this cell if one of its directions falls further (a hillside, not a cliff:
    // over 6 m is a wall of the river's cut or the coast).
    (Vector2Int at, int dir, float fall) SteepestFrom(ValleyGround ground, int x, int y, (Vector2Int at, int dir, float fall) best)
    {
        for (int d = 0; d < 4; d++)
        {
            int tx = x + DirX(d) * Reach, ty = y + DirY(d) * Reach;
            float fall = ground.BaseHeight(x, y) - ground.BaseHeight(tx, ty);
            if (fall > best.fall && fall <= 6f && Dry(tx, ty)) best = (new Vector2Int(x, y), d, fall);
        }
        return best;
    }

    bool Dry(int x, int y)
    {
        if (!World.InBounds(x, y)) return false;
        var c = World.At(x, y);
        // Open ground: the woods' cells hide the water from the camera.
        return c != null && c.Water == 0 && (c.T == "grass" || c.T == "dirt") && !World.IsBlocking(c);
    }

    // N, E, S, W as the core's directions (north is -y).
    static readonly int[] StepX = { 0, 1, 0, -1 }, StepY = { -1, 0, 1, 0 };
    static int DirX(int d) => StepX[d];
    static int DirY(int d) => StepY[d];
}
