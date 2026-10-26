using System.Collections;
using UnityEngine;

// A short film of the lake, for the trailer page (Prototype > Record
// Showcase, DemoRecorder):
//
//   1. Swimming: the wizard walks off the lake's north shore into deep water
//      and swims (SurfaceEffects), then kicks up out of it.
//   2. Ice: back on the shore, a charged Ray of Frost freezes a path across
//      the lake, Sleet Storm freezes a wide circle of it, and the wizard walks
//      out onto the ice.
public class WaterShowcase : ShowcaseScript
{
    Vector2Int shore, deep;

    protected override IEnumerator Play()
    {
        if (!FindLake()) yield break;
        yield return Switch(At(12f));
        yield return Swim();
        yield return Ice();
    }

    // The deepest lake square, and the dry square north of it on the shore.
    bool FindLake()
    {
        float best = 0f;
        for (int y = 0; y < World.H; y++)
            for (int x = 0; x < World.W; x++)
            {
                // Deep enough to swim, and the shore north of it near: a beach, not the far cliff.
                if (!IsLake(x, y)) continue;
                int n = WaterNorth(x, y);
                if (n < 6 || n > 12) continue;
                float d = grid.WaterSurfaceY(x, y) - TerrainGround(grid.CellCenter(x, y));
                if (d <= best) continue;
                best = d;
                deep = new Vector2Int(x, y);
                shore = new Vector2Int(x, y - n - 1);
            }
        return best > 2.2f;
    }

    bool IsLake(int x, int y)
    {
        var c = World.At(x, y);
        return c.Water > 0 && c.T == TerrainIds.Pit && grid.valley != null && grid.valley.IsLake(c, x, y);
    }

    // Wet squares straight north of (x, y), up to 16.
    int WaterNorth(int x, int y)
    {
        int n = 0;
        while (n < 16 && World.InBounds(x, y - n - 1) && World.At(x, y - n - 1).Water > 0) n++;
        return n;
    }

    IEnumerator Swim()
    {
        Chapter("Swimming");
        PlaceCell(shore.x, shore.y - 1);
        var water = grid.CellCenter(deep.x, deep.y);
        water.y = grid.WaterSurfaceY(deep.x, deep.y);
        puppet.Face(water);
        var me = wizard.transform.position;
        // From the shore to one side, low over the water.
        Cut(me + V(7f, 3f, 2f), (me + water) / 2f);
        yield return Wait(0.8f);
        var walk = puppet.WalkTo(water);
        for (float t = 0f; t < 9f && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
        var flight = Fly(W(wizard.transform.position + V(5f, 2.5f, 4f), wizard.transform.position, 2.5f));
        yield return new WaitUntil(() => flight.Done);
        wizard.GetComponent<SurfaceEffects>().Kick();
        yield return Wait(2.5f);
    }

    IEnumerator Ice()
    {
        Chapter("Ice");
        PlaceCell(shore.x, shore.y);
        var out1 = grid.CellCenter(deep.x, deep.y + 4);
        out1.y = grid.WaterSurfaceY(deep.x, deep.y);
        puppet.Face(out1);
        var me = wizard.transform.position;
        Cut(me + V(-6f, 6f, 6f), out1);
        yield return Wait(0.8f);
        caster.Cast(new Chem.CastCommand(SpellIds.RayOfFrost, shore.x, shore.y + 1, 2, 3));
        yield return Wait(2f);
        caster.Cast(SpellIds.SleetStorm, deep.x + 3, deep.y + 2, 2);
        var flight = Fly(W(me + V(-9f, 9f, 3f), out1, 3f));
        yield return new WaitUntil(() => flight.Done);
        var walk = puppet.WalkTo(grid.CellCenter(shore.x, shore.y + 6));
        for (float t = 0f; t < 4f && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
        yield return Wait(1f);
    }
}
