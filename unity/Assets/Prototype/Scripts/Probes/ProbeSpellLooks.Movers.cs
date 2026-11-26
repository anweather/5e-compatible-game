using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The wave and the boulder cast at an angle (issues #11, #21): each aimed off the
// grid's four ways, on flat dirt, with a crate in the wave's way. Checks where each
// went against the aim, that the crate rode the wave along the aim, and that the
// wave left water behind it. MoversProbe (movers) runs these alone.
public partial class SpellLooksProbe
{
    protected IEnumerator Angled()
    {
        yield return AngledWave(Spot(14, 14));
        yield return AngledBoulder(Spot(14, 14));
    }

    IEnumerator AngledWave(Vector2Int o)
    {
        Patch(o, 14, 14, TerrainIds.Dirt);
        var (aimX, aimY) = (5, 3);
        var from = new Vector2Int(o.x + 2, o.y + 3);
        var way = (grid.CellCenter(aimX, aimY) - grid.CellCenter(0, 0)).normalized;
        yield return Frame(o, 14, 14, V(-2f, 9f, -13f), 8.5f);
        var crate = NearestCrate(Mid(o, 14, 14));
        var start = grid.CellCenter(from.x, from.y) + way * 5f;
        if (crate != null) Drop(crate, new Vector3(start.x, grid.GroundAt(start) + 0.5f, start.z));
        yield return Wait(0.6f);
        var crateFrom = crate != null ? crate.transform.position : Vector3.zero;
        caster.Cast(new CastCommand(SpellIds.Wave, from.x, from.y, 1, 1, aimX, aimY));
        yield return Wait(0.8f);
        var wave = World.Waves.FirstOrDefault();
        Check("slant_wave_rolls", wave != null, $"{World.Waves.Count} waves");
        Vector2Int last = from;
        yield return Picture("wave-slant-rolling");
        for (float t = 0f; t < 6f && World.Waves.Count > 0; t += Time.deltaTime)
        {
            if (wave != null) last = new Vector2Int(wave.X, wave.Y);
            yield return null;
        }
        yield return Picture("wave-slant-break");
        float off = OffAim(last - from, aimX, aimY);
        Metric("wave_off_aim_degrees", off);
        Check("wave_follows_the_aim", (last - from).sqrMagnitude >= 16 && off < 8f, $"from {from} to {last}, {off:0.0} degrees off");
        if (crate != null)
        {
            var went = crate.transform.position - crateFrom;
            went.y = 0f;
            Metric("wave_slant_carried_m", went.magnitude);
            Metric("crate_off_aim_degrees", Vector3.Angle(went, way));
            Check("slant_wave_carries", went.magnitude > 1.5f && Vector3.Angle(went, way) < 20f, $"the crate went {went.magnitude:0.0} m, {Vector3.Angle(went, way):0} degrees off the aim");
        }
        int wet = Enumerable.Range(0, 196).Count(i => World.At(o.x + i % 14, o.y + i / 14).Water > 0);
        Metric("wave_wet_cells", wet);
        Check("wave_leaves_water", wet > 6, $"{wet} wet cells where it passed");
        yield return Wait(1f);
        yield return Picture("wave-slant-after");
    }

    IEnumerator AngledBoulder(Vector2Int o)
    {
        Patch(o, 14, 14, TerrainIds.Dirt);
        var (aimX, aimY) = (4, 5);
        var from = new Vector2Int(o.x + 3, o.y + 2);
        yield return Frame(o, 14, 14, V(-2f, 9f, -13f), 8.5f);
        caster.Cast(new CastCommand(SpellIds.RollingBoulder, from.x, from.y, 1, 1, aimX, aimY));
        yield return Wait(0.9f);
        var boulder = World.Boulders.FirstOrDefault();
        Check("slant_boulder_rolls", boulder != null, $"{World.Boulders.Count} boulders");
        yield return Picture("boulder-slant-rolling");
        Vector2Int last = from;
        for (float t = 0f; t < 6f && World.Boulders.Count > 0; t += Time.deltaTime)
        {
            if (boulder != null) last = new Vector2Int(boulder.X, boulder.Y);
            yield return null;
        }
        float off = OffAim(last - from, aimX, aimY);
        Metric("boulder_off_aim_degrees", off);
        Check("boulder_follows_the_aim", (last - from).sqrMagnitude >= 16 && off < 8f, $"from {from} to {last}, {off:0.0} degrees off");
        yield return Wait(0.8f);
        yield return Picture("boulder-slant-rest");
    }

    static float OffAim(Vector2Int went, int aimX, int aimY) => Vector2.Angle(went, new Vector2(aimX, aimY));
}

/// <summary>The wave and the boulder at an angle alone.</summary>
public class MoversProbe : SpellLooksProbe
{
    protected override IEnumerator Run()
    {
        yield return Angled();
    }
}
