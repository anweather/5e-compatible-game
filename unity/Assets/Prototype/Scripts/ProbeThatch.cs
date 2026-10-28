using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's thatch scenario (Andrew, 2026-10-28): thatch and wood burn
// differently. A Fire Bolt on a wall of a thatch-roofed timber house: how long
// the thatch takes to catch, how long it burns, and how long the log walls
// burn (materials.json flammability, fuel and heat; ValleyChemistry.Roofs).
public class ThatchProbe : ProbeScenario
{
    // When every wall cell had burnt out (Time.time, -1 not yet), and the most alight at once.
    float allOut = -1f;
    int mostAlight;

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var valley = FindAnyObjectByType<ValleyChemistry>();
        var house = FindObjectsByType<VillageHouse>(FindObjectsSortMode.None).FirstOrDefault(h => h.walls == WallKind.Timber && h.cover == RoofCover.Thatch);
        Check("house_found", house != null && valley != null, house != null ? house.name : "no thatch-roofed timber house");
        if (house == null || valley == null) yield break;
        var cells = RoofsProbe.HouseCells(grid, World, house).Where(c => World.At(c.x, c.y).T == TerrainIds.LogWall).ToList();
        var wall = cells[0];
        Teleport(house.transform.position - house.transform.forward * 7f + Vector3.up * 0.1f);
        var t = house.transform;
        Cut(t.position - t.forward * 10f + t.right * 4f + Vector3.up * 11f, t.position + Vector3.up * 1f);
        float cast = Time.time;
        caster.Cast(SpellIds.FireBolt, wall.x, wall.y, 1);
        yield return Watch(cells, cast);
        var (caught, gone) = valley.RoofTimes(house.transform);
        Metric("thatch_catch_s", caught < 0 ? -1 : caught - cast);
        Metric("thatch_burn_s", caught < 0 || gone < 0 ? -1 : gone - caught);
        Metric("wood_burn_s", allOut < 0 ? -1 : allOut - cast);
        Metric("walls_alight_at_most", mostAlight);
        Check("thatch_catches_fast", caught >= 0 && caught - cast < 3f, $"the thatch caught {caught - cast:0.0} s after the bolt");
        Check("thatch_burns_seconds", gone >= 0 && gone - caught >= 2f && gone - caught <= 6f, $"and was gone {gone - caught:0.0} s later");
        Check("wood_burns_long", allOut < 0 || allOut - cast > 20f, allOut < 0 ? "the log walls still burning after 110 s" : $"the log walls burned {allOut - cast:0} s");
        Check("thatch_lights_the_walls", mostAlight > 1, $"{mostAlight} wall cells alight at once");
        yield return Wait(0.3f);
        Shot("thatch-after");
        yield return null;
        yield return null;
    }

    // Watches the walls burn, for up to 110 s: a picture as the thatch flares.
    IEnumerator Watch(System.Collections.Generic.List<Vector2Int> cells, float cast)
    {
        while (Time.time - cast < 110f && allOut < 0f)
        {
            yield return Wait(0.25f);
            mostAlight = Mathf.Max(mostAlight, cells.Count(c => World.At(c.x, c.y).Fire > 0));
            if (cells.All(c => World.At(c.x, c.y).T != TerrainIds.LogWall)) allOut = Time.time;
            if (Mathf.Abs(Time.time - cast - 3f) < 0.13f) Shot("thatch-flaring");
        }
    }
}
