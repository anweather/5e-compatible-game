using System.Collections;
using UnityEngine;

// The probe's river scenario (docs/FAST_LOOP.md): the river from the falls
// into the lake is chemistry water (river terrain refills itself, a spring),
// six squares across in a channel cut deep enough to swim in, with a bridge
// over it on the way to the cabin on its east bank (ValleyShape, ValleyBridge).
// Measures the river across at three places down its course, drops the wizard
// into its middle, walks the wizard over the bridge from one bank to the other,
// then digs the ground beside the river with Lower Earth, charged. Checks the
// river is wide, deep and full, the wizard swims in it, crosses the bridge dry
// and ends on the far bank, the hole fills from the river, and the river is
// full again after.
public class RiverProbe : ProbeScenario
{
    // Points the river's course bends through (ValleyShape.River), down from the foothills; it runs
    // roughly north to south there, so a row of squares crosses it.
    static readonly Vector3[] OnCourse = { new(128f, 0f, 170f), new(142f, 0f, 95f), new(120f, 0f, 30f) };
    const int MinWidth = 5;
    const float MinDepth = 1.8f, WalkSeconds = 25f;

    protected override IEnumerator Run()
    {
        var surface = wizard.GetComponent<SurfaceEffects>();
        var deepest = MeasureRiver();
        if (deepest.HasValue) yield return Swim(deepest.Value, surface);
        yield return CrossBridge(surface);
        yield return Dig();
    }

    // The river's width in squares and the water's depth over the drawn ground at its middle, at each
    // point of OnCourse. Returns the deepest middle square, or null if the river wasn't found.
    Vector2Int? MeasureRiver()
    {
        int narrowest = int.MaxValue;
        float shallowest = float.MaxValue, deepestDepth = 0f;
        Vector2Int? deepest = null;
        for (int i = 0; i < OnCourse.Length; i++)
        {
            var (middle, width) = Across(grid.CellAt(OnCourse[i]));
            float depth = width > 0 ? grid.WaterSurfaceY(middle.x, middle.y) - TerrainGround(grid.CellCenter(middle.x, middle.y)) : 0f;
            Metric($"river_width_squares_{i}", width);
            Metric($"river_depth_m_{i}", depth);
            narrowest = Mathf.Min(narrowest, width);
            shallowest = Mathf.Min(shallowest, depth);
            if (depth <= deepestDepth) continue;
            deepestDepth = depth;
            deepest = middle;
        }
        Check("river_wide", narrowest >= MinWidth, $"narrowest {narrowest} squares across (at least {MinWidth}) at {OnCourse.Length} places down the course");
        Check("river_deep", shallowest > MinDepth, $"shallowest middle {shallowest:0.00} m of water over the bed (over {MinDepth} m)");
        return deepest;
    }

    // The run of river squares across the row through a square (from the river square nearest it in
    // that row): its middle square and how many squares it is.
    (Vector2Int middle, int width) Across(Vector2Int at)
    {
        int x0 = int.MinValue;
        for (int d = 0; d <= 12 && x0 == int.MinValue; d++)
        {
            if (IsRiver(at.x - d, at.y)) x0 = at.x - d;
            else if (IsRiver(at.x + d, at.y)) x0 = at.x + d;
        }
        if (x0 == int.MinValue) return (at, 0);
        int lo = x0, hi = x0;
        while (IsRiver(lo - 1, at.y)) lo--;
        while (IsRiver(hi + 1, at.y)) hi++;
        return (new Vector2Int((lo + hi) / 2, at.y), hi - lo + 1);
    }

    bool IsRiver(int x, int y) => World.InBounds(x, y) && World.At(x, y).T == TerrainIds.River;

    // Drops the wizard into the river's middle: deep enough there to float rather than stand on the bed.
    IEnumerator Swim(Vector2Int middle, SurfaceEffects surface)
    {
        var at = grid.CellCenter(middle.x, middle.y);
        float top = grid.WaterSurfaceY(middle.x, middle.y);
        Teleport(new Vector3(at.x, top + 0.5f, at.z));
        Cut(at + V(-9f, top + 5f, -9f), at + V(0f, top, 0f));
        yield return Wait(3f);
        Check("swims_in_river", surface.Swimming, $"SurfaceEffects.Swimming, feet {top - wizard.transform.position.y:0.00} m under the surface");
        Shot("swimming");
    }

    // Walks the wizard from the ground past the bridge's west ramp to past its east ramp.
    IEnumerator CrossBridge(SurfaceEffects surface)
    {
        var bridge = GameObject.Find("Bridge");
        var west = bridge != null ? bridge.transform.Find("West landing") : null;
        var east = bridge != null ? bridge.transform.Find("East landing") : null;
        bool built = bridge != null && west != null && east != null;
        Check("bridge_built", built && bridge.GetComponentsInChildren<Collider>().Length >= 9, "the Bridge with its walkways, sides and landings");
        if (!built) yield break;

        var deck = bridge.transform.position;
        var under = grid.CellAt(deck);
        float clear = deck.y - grid.WaterSurfaceY(under.x, under.y);
        Metric("bridge_deck_over_water_m", clear);
        Check("bridge_over_river", IsRiver(under.x, under.y) && clear > 1.2f, $"the deck's middle {clear:0.00} m over the water of {World.At(under.x, under.y).T}");

        Teleport(west.position + Vector3.up * 0.1f);
        Cut(deck + bridge.transform.right * 16f + V(0f, 6f, 0f), deck);
        yield return Wait(1f);
        bool wet = false, shot = false;
        float highest = float.MinValue, took = 0f;
        var walk = puppet.WalkTo(east.position);
        for (; took < WalkSeconds && walk.MoveNext(); took += Time.deltaTime)
        {
            wet |= surface.Swimming;
            highest = Mathf.Max(highest, wizard.transform.position.y);
            if (!shot && Flat(wizard.transform.position - deck).magnitude < 1.5f)
            {
                shot = true;
                Shot("on-the-bridge");
            }
            yield return walk.Current;
        }
        puppet.Move(Vector2.zero);
        yield return Wait(0.5f);

        var end = wizard.transform.position;
        float left = Flat(end - east.position).magnitude;
        Metric("bridge_walk_s", took);
        Metric("bridge_highest_over_deck_m", highest - deck.y);
        Check("bridge_dry", !wet, "SurfaceEffects.Swimming stayed false all the way across");
        Check("bridge_walked_over", highest > deck.y - 0.3f, $"highest {highest - deck.y:0.00} m against the deck's top");
        Check("bridge_crossed", left < 1.5f && Vector3.Dot(end - deck, bridge.transform.forward) > 0f,
            $"{left:0.0} m from the east landing after {took:0.0} s");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Digs from the river's edge east of it to a dry square three squares out, and checks the water comes.
    IEnumerator Dig()
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
                    if (IsRiver(x, y)) return new Vector2Int(x, y);
        return null;
    }
}
