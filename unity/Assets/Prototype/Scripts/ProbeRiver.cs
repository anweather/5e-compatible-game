using System.Collections;
using UnityEngine;

// The probe's river scenario (docs/FAST_LOOP.md): the river from the foothills
// into the lake is chemistry water (river terrain refills itself, a spring).
// The wizard goes to it east of the yard, finds a river square, and digs the
// ground beside it with Lower Earth, charged. Checks the river is there and
// full, the hole fills from it, and the river is full again after.
public class RiverProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        // Near the river's course at world (120, 30), on the grid.
        var near = grid.CellAt(new Vector3(120f, 0f, 30f));
        var river = FindRiver(near, 12);
        Check("river_found", river.HasValue, river.HasValue ? $"river square {river.Value}" : $"no river square within 12 of {near}");
        if (!river.HasValue) yield break;
        var r = river.Value;
        Check("river_full", World.At(r.x, r.y).Water >= 4, $"{World.At(r.x, r.y).Water} units");

        // The dry square east of it, three squares out, and the wizard west of that.
        var dry = new Vector2Int(r.x + 3, r.y);
        while (World.At(dry.x - 1, dry.y).T == TerrainIds.River) dry.x++;
        var yard = grid.yardCells;
        Place(dry.x + 3 - yard.x, dry.y - yard.y);
        var at = grid.CellCenter(dry.x, dry.y);
        at.y = grid.GroundAt(at);
        Cut(at + V(6f, 7f, -8f), at);
        yield return Wait(1f);
        // Dig a trench from the river's edge to the dry square: Lower Earth, charged, along the gap.
        for (int x = dry.x - 2; x <= dry.x; x += 1) World.Cast(SpellIds.MoldEarth, x, dry.y, 1, 2);
        yield return Wait(4f);
        int wet = World.At(dry.x, dry.y).Water;
        Metric("dug_square_water", wet);
        Check("dug_square_fills_from_river", wet > 0, $"{wet} units in the dug square");
        Check("river_stays_full", World.At(r.x, r.y).Water >= 4, $"{World.At(r.x, r.y).Water} units");
        Shot("river");
    }

    Vector2Int? FindRiver(Vector2Int around, int reach)
    {
        for (int d = 0; d <= reach; d++)
            for (int y = around.y - d; y <= around.y + d; y++)
                for (int x = around.x - d; x <= around.x + d; x++)
                    if (World.InBounds(x, y) && World.At(x, y).T == TerrainIds.River) return new Vector2Int(x, y);
        return null;
    }
}
